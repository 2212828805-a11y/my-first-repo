using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Looy.WindowsController;

internal sealed class McpEndpointClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly Func<IReadOnlyList<ToolDefinition>> _getTools;
    private readonly ToolExecutor _executor;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private ClientWebSocket? _socket;
    private volatile bool _manualStop = true;

    public McpEndpointClient(Func<IReadOnlyList<ToolDefinition>> getTools, ToolExecutor executor)
    {
        _getTools = getTools;
        _executor = executor;
    }

    public event Action<string>? Log;
    public event Action<EndpointConnectionState, string>? StateChanged;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public void Start(Uri endpoint)
    {
        if (_runTask is { IsCompleted: false })
        {
            throw new InvalidOperationException("MCP 客户端已经在运行。");
        }

        _manualStop = false;
        _runCancellation = new CancellationTokenSource();
        _runTask = Task.Run(() => RunReconnectLoopAsync(endpoint, _runCancellation.Token));
    }

    public async Task StopAsync()
    {
        _manualStop = true;
        var cancellation = _runCancellation;
        cancellation?.Cancel();
        var socket = _socket;
        if (socket is { State: WebSocketState.Open })
        {
            try
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "User disconnected",
                    CancellationToken.None);
            }
            catch
            {
                socket.Abort();
            }
        }

        if (_runTask is not null)
        {
            try
            {
                await _runTask.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // Cancellation and close races are expected here.
            }
        }

        SetState(EndpointConnectionState.Stopped, "已停止");
    }

    public async Task NotifyToolsChangedAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        try
        {
            await SendJsonAsync(
                new Dictionary<string, object?>
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "notifications/tools/list_changed"
                },
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException)
        {
            LogMessage("授权已保存；当前连接正在切换，重连后会自动同步工具列表。");
        }
    }

    private async Task RunReconnectLoopAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        var delaySeconds = 2;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SetState(
                    delaySeconds == 2 ? EndpointConnectionState.Connecting : EndpointConnectionState.Reconnecting,
                    delaySeconds == 2 ? "正在连接" : "正在重新连接");

                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                _socket = socket;
                LogMessage($"正在连接：{RedactEndpoint(endpoint)}");
                await socket.ConnectAsync(endpoint, cancellationToken);
                delaySeconds = 2;
                SetState(EndpointConnectionState.Connected, "已连接");
                LogMessage("MCP 接入点连接成功，正在等待初始化请求。");

                await ReceiveLoopAsync(socket, cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    LogMessage("MCP 连接已断开，将自动重连。");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogMessage($"MCP 连接失败：{exception.Message}");
            }
            finally
            {
                _socket = null;
            }

            if (_manualStop || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            SetState(EndpointConnectionState.Reconnecting, $"{delaySeconds} 秒后重连");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            delaySeconds = Math.Min(delaySeconds * 2, 20);
        }

        SetState(EndpointConnectionState.Disconnected, "未连接");
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var messageStream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
                messageStream.Write(buffer, 0, result.Count);
                if (messageStream.Length > 2 * 1024 * 1024)
                {
                    throw new InvalidOperationException("MCP 消息超过 2 MB 安全限制。");
                }
            } while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            var json = Encoding.UTF8.GetString(messageStream.ToArray());
            await HandleMessageAsync(json, cancellationToken);
        }
    }

    private async Task HandleMessageAsync(string json, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            LogMessage("忽略了无法解析的 MCP 消息。");
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var hasId = root.TryGetProperty("id", out var idElement);
            object? id = hasId ? idElement.Clone() : null;
            if (!root.TryGetProperty("method", out var methodElement)
                || methodElement.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var method = methodElement.GetString() ?? string.Empty;
            switch (method)
            {
                case "initialize":
                    if (hasId)
                    {
                        await SendResultAsync(
                            id,
                            new Dictionary<string, object?>
                            {
                                ["protocolVersion"] = "2024-11-05",
                                ["capabilities"] = new Dictionary<string, object?>
                                {
                                    ["tools"] = new Dictionary<string, object?> { ["listChanged"] = true }
                                },
                                ["serverInfo"] = new Dictionary<string, object?>
                                {
                                    ["name"] = "LOOY Windows Controller",
                                    ["version"] = "0.7.1"
                                }
                            },
                            cancellationToken);
                        LogMessage("MCP 初始化完成。");
                    }
                    break;

                case "notifications/initialized":
                    LogMessage("小智已读取控制器能力。");
                    break;

                case "tools/list":
                    if (hasId)
                    {
                        var tools = _getTools();
                        await SendResultAsync(
                            id,
                            new Dictionary<string, object?> { ["tools"] = tools },
                            cancellationToken);
                        LogMessage($"已向小智注册 {tools.Count} 个工具。");
                    }
                    break;

                case "tools/call":
                    if (hasId)
                    {
                        await HandleToolCallAsync(root, id, cancellationToken);
                    }
                    break;

                case "ping":
                    if (hasId)
                    {
                        await SendResultAsync(id, new Dictionary<string, object?>(), cancellationToken);
                    }
                    break;

                default:
                    if (hasId)
                    {
                        await SendErrorAsync(id, -32601, $"不支持的方法：{method}", cancellationToken);
                    }
                    break;
            }
        }
    }

    private async Task HandleToolCallAsync(
        JsonElement root,
        object? id,
        CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("params", out var parameters)
            || parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String)
        {
            await SendErrorAsync(id, -32602, "tools/call 参数不完整。", cancellationToken);
            return;
        }

        var toolName = nameElement.GetString() ?? string.Empty;
        JsonElement arguments;
        if (parameters.TryGetProperty("arguments", out var argumentsElement))
        {
            arguments = argumentsElement.Clone();
        }
        else
        {
            using var emptyArguments = JsonDocument.Parse("{}");
            arguments = emptyArguments.RootElement.Clone();
        }
        LogMessage($"收到工具调用：{toolName}");

        var executionResult = await _executor(toolName, arguments, cancellationToken);
        var content = new[]
        {
            new Dictionary<string, object?>
            {
                ["type"] = "text",
                ["text"] = executionResult.Message
            }
        };
        await SendResultAsync(
            id,
            new Dictionary<string, object?>
            {
                ["content"] = content,
                ["isError"] = !executionResult.Success
            },
            cancellationToken);
        LogMessage($"工具调用{(executionResult.Success ? "完成" : "被拒绝")}：{executionResult.Message}");
    }

    private Task SendResultAsync(object? id, object result, CancellationToken cancellationToken)
    {
        return SendJsonAsync(
            new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["result"] = result
            },
            cancellationToken);
    }

    private Task SendErrorAsync(
        object? id,
        int code,
        string message,
        CancellationToken cancellationToken)
    {
        return SendJsonAsync(
            new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = new Dictionary<string, object?>
                {
                    ["code"] = code,
                    ["message"] = message
                }
            },
            cancellationToken);
    }

    private async Task SendJsonAsync(object payload, CancellationToken cancellationToken)
    {
        var socket = _socket;
        if (socket is not { State: WebSocketState.Open })
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void LogMessage(string message) => Log?.Invoke(message);

    private void SetState(EndpointConnectionState state, string message) => StateChanged?.Invoke(state, message);

    private static string RedactEndpoint(Uri endpoint)
    {
        return endpoint.GetLeftPart(UriPartial.Path) + (string.IsNullOrEmpty(endpoint.Query) ? string.Empty : "?token=***");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _runCancellation?.Dispose();
        _sendLock.Dispose();
    }
}
