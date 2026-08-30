using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Looy.WindowsController;

internal sealed class DeviceLicenseClient : IDisposable
{
    internal const string ServiceBaseUrl = "https://looy-public-gateway.2212828805.workers.dev";
    internal const string BackupServiceBaseUrl = "https://looy-admin-console.honest-crown-2664.chatgpt.site";
    internal const string ServiceStatusUrl = ServiceBaseUrl + "/api/v1/info";
    internal const string BackupServiceStatusUrl = BackupServiceBaseUrl + "/api/v1/info";
    internal const string PrivacyUrl = BackupServiceBaseUrl + "/privacy";
    internal const string AdminUrl = BackupServiceBaseUrl + "/admin";
    internal const string ConsentVersion = "2026-08-30-v3";

    private const int DefaultOfflineGraceSeconds = 0;
    private const int DefaultNextCheckSeconds = 5;
    private const int EndpointTimeoutSeconds = 6;

    private static readonly Uri[] ServiceEndpoints =
    [
        CreateServiceUri(ServiceBaseUrl),
        CreateServiceUri(BackupServiceBaseUrl)
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly string _statePath;
    private DeviceLicenseState _state;
    private bool _disposed;

    public DeviceLicenseClient()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LOOY",
            "WindowsController");
        Directory.CreateDirectory(dataDirectory);
        _statePath = Path.Combine(dataDirectory, "device-license.json");
        _state = LoadOrCreateState();

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(4),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            UseProxy = true
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"LooyWindowsController/{AppVersion}");
    }

    public string DeviceId => _state.DeviceId;
    public string DeviceIdHint => _state.DeviceId.Length >= 14
        ? $"{_state.DeviceId[..8]}…{_state.DeviceId[^6..]}"
        : _state.DeviceId;
    public int NextCheckSeconds => Math.Clamp(
        _state.NextCheckSeconds <= 0 ? DefaultNextCheckSeconds : _state.NextCheckSeconds,
        5,
        60 * 60);
    public string StatusText => StatusDisplayName(_state.Status);
    public DateTimeOffset? LastValidatedAt => _state.LastValidatedAt;
    public DateTimeOffset? LicenseExpiresAt => _state.LicenseExpiresAt;
    public bool HasStoredLicense => !string.IsNullOrWhiteSpace(_state.ProtectedToken);
    public bool ConsentIsCurrent => string.Equals(
        _state.AcceptedConsentVersion,
        ConsentVersion,
        StringComparison.Ordinal);

    public async Task<DeviceLicenseCheckResult> ActivateAsync(
        string activationCode,
        bool consentAccepted,
        CancellationToken cancellationToken = default)
    {
        if (!consentAccepted)
        {
            return DeviceLicenseCheckResult.Denied(
                "consent_required",
                "请先阅读并勾选同意隐私与数据说明。",
                requiresActivation: true);
        }

        var normalizedCode = NormalizeActivationCode(activationCode);
        if (string.IsNullOrWhiteSpace(normalizedCode) || normalizedCode.Length > 64)
        {
            return DeviceLicenseCheckResult.Denied(
                "invalid_code",
                "请输入管理后台生成的完整激活码。",
                requiresActivation: true);
        }

        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            _state.AcceptedConsentVersion = ConsentVersion;
            SaveState();
            object CreateRequest()
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var nonce = CreateNonce();
                var payload = BuildSignaturePayload(
                    "activate",
                    _state.DeviceId,
                    timestamp,
                    nonce,
                    AppVersion,
                    normalizedCode);
                return new
                {
                    activationCode = normalizedCode,
                    deviceId = _state.DeviceId,
                    publicKeySpki = _state.PublicKeySpki,
                    timestamp,
                    nonce,
                    appVersion = AppVersion,
                    consentVersion = ConsentVersion,
                    signature = SignPayload(payload)
                };
            }

            var apiResult = await PostAsync("/api/v1/activate", CreateRequest, cancellationToken);
            return ApplyApiResult(apiResult, requiresActivationOnAuthFailure: true);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<DeviceLicenseCheckResult> AcceptUpdatedConsentAsync(
        bool consentAccepted,
        CancellationToken cancellationToken = default)
    {
        if (!consentAccepted)
        {
            return DeviceLicenseCheckResult.Denied(
                "consent_required",
                "请先阅读并同意最新用户协议与敏感权限说明。",
                requiresActivation: true);
        }
        if (!HasStoredLicense)
        {
            return DeviceLicenseCheckResult.Denied(
                "activation_required",
                "这台电脑尚未绑定，请输入激活码。",
                requiresActivation: true);
        }

        ThrowIfDisposed();
        _state.AcceptedConsentVersion = ConsentVersion;
        SaveState();
        return await CheckAsync(allowOfflineGrace: false, cancellationToken);
    }

    public async Task<DeviceLicenseCheckResult> CheckAsync(
        bool allowOfflineGrace,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(_state.ProtectedToken))
            {
                return DeviceLicenseCheckResult.Denied(
                    "activation_required",
                    "这台电脑尚未绑定，请输入激活码。",
                    requiresActivation: true);
            }
            if (!ConsentIsCurrent)
            {
                return DeviceLicenseCheckResult.Denied(
                    "consent_required",
                    "用户协议与敏感权限说明已更新，请重新阅读并明确同意。",
                    requiresActivation: true);
            }

            string token;
            try
            {
                token = DpapiProtector.Unprotect(_state.ProtectedToken);
            }
            catch
            {
                return DeviceLicenseCheckResult.Denied(
                    "local_token_unavailable",
                    "本机授权凭证无法读取，请重新输入激活码。",
                    requiresActivation: true);
            }

            object CreateRequest()
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var nonce = CreateNonce();
                var payload = BuildSignaturePayload(
                    "heartbeat",
                    _state.DeviceId,
                    timestamp,
                    nonce,
                    AppVersion);
                return new
                {
                    deviceId = _state.DeviceId,
                    token,
                    timestamp,
                    nonce,
                    appVersion = AppVersion,
                    consentVersion = ConsentVersion,
                    signature = SignPayload(payload)
                };
            }

            LicenseHttpResult apiResult;
            try
            {
                apiResult = await PostAsync("/api/v1/heartbeat", CreateRequest, cancellationToken);
            }
            catch (Exception exception) when (
                allowOfflineGrace &&
                exception is HttpRequestException or TaskCanceledException)
            {
                return EvaluateOfflineGrace(networkUnavailable: true);
            }

            if ((int)apiResult.StatusCode >= 500 && allowOfflineGrace)
            {
                return EvaluateOfflineGrace(networkUnavailable: true);
            }
            return ApplyApiResult(apiResult, requiresActivationOnAuthFailure: true);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public static bool RunComponentSelfTest()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Base64UrlEncode(key.ExportSubjectPublicKeyInfo());
        var deviceId = ComputeDeviceId(publicKey);
        var nonce = CreateNonce();
        var payload = BuildSignaturePayload(
            "activate",
            deviceId,
            1_787_990_000_000,
            nonce,
            "0.7.7",
            "LY-ABCDE-FGHIJ-KLMNO");
        var signature = key.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return ConsentVersion == "2026-08-30-v3"
               && DefaultOfflineGraceSeconds == 0
               && DefaultNextCheckSeconds == 5
               && ServiceBaseUrl == "https://looy-public-gateway.2212828805.workers.dev"
               && BackupServiceBaseUrl == "https://looy-admin-console.honest-crown-2664.chatgpt.site"
               && ServiceStatusUrl == ServiceBaseUrl + "/api/v1/info"
               && BackupServiceStatusUrl == BackupServiceBaseUrl + "/api/v1/info"
               && PrivacyUrl == BackupServiceBaseUrl + "/privacy"
               && AdminUrl == BackupServiceBaseUrl + "/admin"
               && ServiceEndpoints.Length == 2
               && ServiceEndpoints.Select(endpoint => endpoint.GetLeftPart(UriPartial.Authority)).Distinct().Count() == 2
               && ShouldTryNextEndpoint(new LicenseHttpResult(
                   HttpStatusCode.Forbidden,
                   null,
                   null,
                   "授权网关请求失败（HTTP 403）。",
                   StructuredServiceResponse: false))
               && !ShouldTryNextEndpoint(new LicenseHttpResult(
                   HttpStatusCode.Forbidden,
                   null,
                   "invalid_code",
                   "激活码无效",
                   StructuredServiceResponse: true))
               && deviceId.Length == 64
               && deviceId.All(character => char.IsAsciiHexDigit(character) && !char.IsUpper(character))
               && nonce.Length >= 16
               && key.VerifyData(
                   payload,
                   signature,
                   HashAlgorithmName.SHA256,
                   DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
               && Encoding.UTF8.GetString(payload).EndsWith("\nLY-ABCDE-FGHIJ-KLMNO", StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _httpClient.Dispose();
        _operationGate.Dispose();
    }

    private DeviceLicenseCheckResult ApplyApiResult(
        LicenseHttpResult result,
        bool requiresActivationOnAuthFailure)
    {
        if (result.Response is null)
        {
            var requiresActivation = string.Equals(
                                         result.ErrorCode,
                                         "consent_required",
                                         StringComparison.Ordinal)
                                     || (requiresActivationOnAuthFailure
                                         && result.StatusCode is HttpStatusCode.Unauthorized
                                             or HttpStatusCode.NotFound);
            return DeviceLicenseCheckResult.Denied(
                result.ErrorCode ?? "service_error",
                result.Message ?? "授权服务暂时无法完成校验。",
                requiresActivation);
        }

        var response = result.Response;
        _state.Status = response.Status ?? "unknown";
        _state.BillingEnabled = response.BillingEnabled;
        _state.OfflineGraceSeconds = response.OfflineGraceSeconds >= 0
            ? response.OfflineGraceSeconds
            : DefaultOfflineGraceSeconds;
        _state.NextCheckSeconds = response.NextCheckSeconds > 0
            ? response.NextCheckSeconds
            : DefaultNextCheckSeconds;
        _state.LicenseExpiresAt = response.LicenseExpiresAt;
        _state.GraceEndsAt = response.GraceEndsAt;
        _state.TokenExpiresAt = response.TokenExpiresAt;
        _state.LastValidatedAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(response.Token))
        {
            _state.ProtectedToken = DpapiProtector.Protect(response.Token);
        }
        SaveState();

        if (response.Allowed)
        {
            return DeviceLicenseCheckResult.Authorized(
                response.Status ?? "active",
                response.Status == "grace" ? "授权处于宽限期。" : "设备授权有效。");
        }
        return DeviceLicenseCheckResult.Denied(
            response.Status ?? "not_allowed",
            StatusBlockedMessage(response.Status),
            requiresActivation: false);
    }

    private DeviceLicenseCheckResult EvaluateOfflineGrace(bool networkUnavailable = false)
    {
        var now = DateTimeOffset.UtcNow;
        var graceSeconds = _state.OfflineGraceSeconds > 0
            ? _state.OfflineGraceSeconds
            : DefaultOfflineGraceSeconds;
        if (graceSeconds <= 0)
        {
            return DeviceLicenseCheckResult.Denied(
                "online_check_required",
                networkUnavailable
                    ? "主线路和备用线路均未完成授权校验；这不是服务器返回的封禁或激活码无效。请切换手机热点或关闭仅对浏览器生效的代理后，点击“重新校验”。严格在线授权期间应用不会在未校验状态下继续运行。"
                    : "严格在线授权校验失败；当前版本不提供离线宽限，请恢复网络后重新打开应用。",
                requiresActivation: false);
        }

        var lastValidation = _state.LastValidatedAt;
        var previouslyAllowed = _state.Status is "active" or "grace";
        var tokenValid = !_state.TokenExpiresAt.HasValue || _state.TokenExpiresAt.Value > now;
        var withinOfflineGrace = lastValidation.HasValue
                                 && now <= lastValidation.Value.AddSeconds(graceSeconds);
        var withinPaidPeriod = !_state.BillingEnabled
                               || !_state.LicenseExpiresAt.HasValue
                               || now <= (_state.GraceEndsAt ?? _state.LicenseExpiresAt.Value);

        if (previouslyAllowed && tokenValid && withinOfflineGrace && withinPaidPeriod)
        {
            var remaining = lastValidation!.Value.AddSeconds(graceSeconds) - now;
            return DeviceLicenseCheckResult.AuthorizedOffline(
                _state.Status,
                $"暂时无法连接授权服务，已使用离线宽限（剩余约 {Math.Max(1, (int)Math.Ceiling(remaining.TotalHours))} 小时）。");
        }
        return DeviceLicenseCheckResult.Denied(
            "online_check_required",
            "无法连接授权服务，且离线宽限已结束。请联网后重试。",
            requiresActivation: false);
    }

    private async Task<LicenseHttpResult> PostAsync(
        string path,
        Func<object> requestFactory,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        LicenseHttpResult? lastRetryableFailure = null;
        foreach (var endpoint in OrderedServiceEndpoints())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(TimeSpan.FromSeconds(EndpointTimeoutSeconds));
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    new Uri(endpoint, path.TrimStart('/')))
                {
                    Version = HttpVersion.Version11,
                    VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
                    Content = JsonContent.Create(requestFactory(), options: JsonOptions)
                };
                request.Headers.Accept.ParseAdd("application/json");
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    attempt.Token);
                var result = await ReadResultAsync(response, attempt.Token);
                if (!ShouldTryNextEndpoint(result))
                {
                    if (result.Response is not null)
                    {
                        RememberWorkingEndpoint(endpoint);
                    }
                    return result;
                }

                lastRetryableFailure = result;
                failures.Add($"{endpoint.Host}: HTTP {(int)result.StatusCode}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                failures.Add($"{endpoint.Host}: 连接超时");
            }
            catch (HttpRequestException exception)
            {
                failures.Add($"{endpoint.Host}: {CompactNetworkError(exception.Message)}");
            }
        }

        if (lastRetryableFailure is not null)
        {
            return new LicenseHttpResult(
                HttpStatusCode.ServiceUnavailable,
                null,
                "gateway_unavailable",
                $"授权主线路和备用线路均未通过（{string.Join("；", failures)}）。这不是设备封禁或激活码无效。",
                StructuredServiceResponse: true);
        }

        throw new HttpRequestException(
            $"授权主线路和备用线路均无法连接（{string.Join("；", failures)}）。");
    }

    private static async Task<LicenseHttpResult> ReadResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (content.Length > 64 * 1024)
        {
            return new LicenseHttpResult(
                response.StatusCode,
                null,
                "invalid_response",
                "授权服务返回内容异常。",
                StructuredServiceResponse: false);
        }

        LicenseApiResponse? payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<LicenseApiResponse>(content, JsonOptions);
        }
        catch (JsonException)
        {
            // A structured error is returned below.
        }

        var structuredServiceResponse = payload is not null
                                        && (payload.Ok
                                            || !string.IsNullOrWhiteSpace(payload.Error)
                                            || !string.IsNullOrWhiteSpace(payload.Message));
        if (!response.IsSuccessStatusCode || payload?.Ok != true)
        {
            return new LicenseHttpResult(
                response.StatusCode,
                null,
                payload?.Error,
                payload?.Message ?? $"授权网关请求失败（HTTP {(int)response.StatusCode}）。",
                structuredServiceResponse);
        }
        return new LicenseHttpResult(
            response.StatusCode,
            payload,
            null,
            null,
            StructuredServiceResponse: true);
    }

    private static bool ShouldTryNextEndpoint(LicenseHttpResult result) =>
        (int)result.StatusCode >= 500
        || result.StatusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
        || !result.StructuredServiceResponse;

    private IEnumerable<Uri> OrderedServiceEndpoints()
    {
        var preferred = _state.PreferredServiceBaseUrl;
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var preferredEndpoint = ServiceEndpoints.FirstOrDefault(endpoint =>
                string.Equals(
                    endpoint.GetLeftPart(UriPartial.Authority),
                    preferred,
                    StringComparison.OrdinalIgnoreCase));
            if (preferredEndpoint is not null)
            {
                yield return preferredEndpoint;
            }
        }

        foreach (var endpoint in ServiceEndpoints)
        {
            if (!string.Equals(
                    endpoint.GetLeftPart(UriPartial.Authority),
                    preferred,
                    StringComparison.OrdinalIgnoreCase))
            {
                yield return endpoint;
            }
        }
    }

    private void RememberWorkingEndpoint(Uri endpoint)
    {
        var value = endpoint.GetLeftPart(UriPartial.Authority);
        if (string.Equals(_state.PreferredServiceBaseUrl, value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _state.PreferredServiceBaseUrl = value;
        SaveState();
    }

    private static Uri CreateServiceUri(string value) =>
        new(value.TrimEnd('/') + "/", UriKind.Absolute);

    private static string CompactNetworkError(string value)
    {
        var compact = string.Join(" ", value.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return compact.Length <= 120 ? compact : compact[..120] + "…";
    }

    private DeviceLicenseState LoadOrCreateState()
    {
        if (File.Exists(_statePath))
        {
            try
            {
                var json = File.ReadAllText(_statePath);
                var state = JsonSerializer.Deserialize<DeviceLicenseState>(json, JsonOptions);
                if (state is not null && ValidateIdentity(state))
                {
                    return state;
                }
                throw new InvalidDataException("设备身份字段不完整");
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "本机设备授权数据无法读取。请保留 device-license.json 并联系管理员处理，避免重复占用设备名额。",
                    exception);
            }
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKeySpki = Base64UrlEncode(key.ExportSubjectPublicKeyInfo());
        var stateToCreate = new DeviceLicenseState
        {
            PublicKeySpki = publicKeySpki,
            DeviceId = ComputeDeviceId(publicKeySpki),
            ProtectedPrivateKeyPkcs8 = DpapiProtector.Protect(
                Convert.ToBase64String(key.ExportPkcs8PrivateKey())),
            OfflineGraceSeconds = DefaultOfflineGraceSeconds,
            NextCheckSeconds = DefaultNextCheckSeconds
        };
        _state = stateToCreate;
        SaveState();
        return stateToCreate;
    }

    private static bool ValidateIdentity(DeviceLicenseState state)
    {
        if (string.IsNullOrWhiteSpace(state.ProtectedPrivateKeyPkcs8)
            || string.IsNullOrWhiteSpace(state.PublicKeySpki)
            || state.DeviceId.Length != 64
            || state.DeviceId.Any(character => !char.IsAsciiHexDigit(character) || char.IsUpper(character))
            || ComputeDeviceId(state.PublicKeySpki) != state.DeviceId)
        {
            return false;
        }

        var privateKey = Convert.FromBase64String(
            DpapiProtector.Unprotect(state.ProtectedPrivateKeyPkcs8));
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(privateKey, out var bytesRead);
        return bytesRead == privateKey.Length
               && Base64UrlEncode(key.ExportSubjectPublicKeyInfo()) == state.PublicKeySpki;
    }

    private string SignPayload(byte[] payload)
    {
        var privateKey = Convert.FromBase64String(
            DpapiProtector.Unprotect(_state.ProtectedPrivateKeyPkcs8));
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(privateKey, out var bytesRead);
        if (bytesRead != privateKey.Length)
        {
            throw new InvalidOperationException("本机设备密钥格式不正确。");
        }
        return Base64UrlEncode(
            key.SignData(
                payload,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    private void SaveState()
    {
        var directory = Path.GetDirectoryName(_statePath)
                        ?? throw new InvalidOperationException("设备授权目录无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = _statePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_state, JsonOptions));
        File.Move(temporaryPath, _statePath, true);
    }

    private static string AppVersion
    {
        get
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            return string.IsNullOrWhiteSpace(informational)
                ? "0.7.1"
                : informational.Split('+')[0];
        }
    }

    private static string NormalizeActivationCode(string value) =>
        new(value
            .Trim()
            .ToUpperInvariant()
            .Where(character => !char.IsWhiteSpace(character))
            .ToArray());

    private static byte[] BuildSignaturePayload(
        string action,
        string deviceId,
        long timestamp,
        string nonce,
        string appVersion,
        string? activationCode = null)
    {
        var fields = new List<string>
        {
            action,
            deviceId,
            timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture),
            nonce,
            appVersion
        };
        if (action == "activate")
        {
            fields.Add(NormalizeActivationCode(activationCode ?? string.Empty));
        }
        return Encoding.UTF8.GetBytes(string.Join("\n", fields));
    }

    private static string ComputeDeviceId(string publicKeySpki)
    {
        var digest = SHA256.HashData(Base64UrlDecode(publicKeySpki));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static string CreateNonce() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(24));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight((normalized.Length + 3) / 4 * 4, '=');
        return Convert.FromBase64String(normalized);
    }

    private static string StatusDisplayName(string? status) => status switch
    {
        "active" => "授权有效",
        "grace" => "授权宽限期",
        "banned" => "设备已封禁",
        "disabled" => "激活码已停用",
        "payment_required" => "授权已到期",
        _ => "等待设备绑定"
    };

    private static string StatusBlockedMessage(string? status) => status switch
    {
        "banned" => "这台设备已被管理员封禁。",
        "disabled" => "绑定使用的激活码已停用。",
        "payment_required" => "授权已到期，请联系管理员续期。",
        _ => "当前设备授权不可用，请联系管理员。"
    };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class DeviceLicenseState
    {
        public string DeviceId { get; set; } = string.Empty;
        public string PublicKeySpki { get; set; } = string.Empty;
        public string ProtectedPrivateKeyPkcs8 { get; set; } = string.Empty;
        public string ProtectedToken { get; set; } = string.Empty;
        public string Status { get; set; } = "unbound";
        public bool BillingEnabled { get; set; }
        public DateTimeOffset? LicenseExpiresAt { get; set; }
        public DateTimeOffset? GraceEndsAt { get; set; }
        public DateTimeOffset? TokenExpiresAt { get; set; }
        public DateTimeOffset? LastValidatedAt { get; set; }
        public int OfflineGraceSeconds { get; set; } = DefaultOfflineGraceSeconds;
        public int NextCheckSeconds { get; set; } = DefaultNextCheckSeconds;
        public string? AcceptedConsentVersion { get; set; }
        public string? PreferredServiceBaseUrl { get; set; }
    }

    private sealed class LicenseApiResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("allowed")]
        public bool Allowed { get; set; }

        [JsonPropertyName("billingEnabled")]
        public bool BillingEnabled { get; set; }

        [JsonPropertyName("licenseExpiresAt")]
        public DateTimeOffset? LicenseExpiresAt { get; set; }

        [JsonPropertyName("graceEndsAt")]
        public DateTimeOffset? GraceEndsAt { get; set; }

        [JsonPropertyName("offlineGraceSeconds")]
        public int OfflineGraceSeconds { get; set; }

        [JsonPropertyName("nextCheckSeconds")]
        public int NextCheckSeconds { get; set; }

        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("tokenExpiresAt")]
        public DateTimeOffset? TokenExpiresAt { get; set; }
    }

    private sealed record LicenseHttpResult(
        HttpStatusCode StatusCode,
        LicenseApiResponse? Response,
        string? ErrorCode,
        string? Message,
        bool StructuredServiceResponse);
}

internal sealed record DeviceLicenseCheckResult(
    bool Allowed,
    bool UsedOfflineGrace,
    bool RequiresActivation,
    string Status,
    string Message)
{
    public static DeviceLicenseCheckResult Authorized(string status, string message) =>
        new(true, false, false, status, message);

    public static DeviceLicenseCheckResult AuthorizedOffline(string status, string message) =>
        new(true, true, false, status, message);

    public static DeviceLicenseCheckResult Denied(
        string status,
        string message,
        bool requiresActivation) =>
        new(false, false, requiresActivation, status, message);
}
