namespace Looy.WindowsController;

internal static class ToolCatalog
{
    public static IReadOnlyList<ToolDefinition> Build(Func<string, bool> permissionEnabled)
    {
        var tools = new List<ToolDefinition>();

        if (permissionEnabled(PermissionKeys.SystemStatus))
        {
            tools.Add(Tool(
                "windows.system_status",
                "读取当前 Windows 电脑的名称、系统版本、当前时间和运行状态。只读操作。",
                Properties()));
        }

        if (permissionEnabled(PermissionKeys.Applications))
        {
            tools.Add(Tool(
                "windows.list_apps",
                "列出用户在路遥智控中允许打开的应用及其别名。打开应用前优先调用本工具。",
                Properties()));
            tools.Add(Tool(
                "windows.open_app",
                "按白名单别名打开一个 Windows 应用。只能打开用户明确允许的应用。",
                Properties(
                    Required("app", "string", "应用别名，例如 notepad、edge、wechat。"))));
            tools.Add(Tool(
                "windows.close_app",
                "请求白名单中的桌面应用正常关闭，不会强制结束进程。",
                Properties(
                    Required("app", "string", "应用别名。"))));
            tools.Add(Tool(
                "windows.app_action",
                "对指定白名单应用执行动作。支持更稳健的窗口激活、应用内搜索、微信发送消息，以及网易云等媒体应用的播放暂停和切歌。所有输入前都会再次确认目标应用仍在前台。搜索和微信消息要求用户开启键盘权限，媒体动作要求开启媒体权限。",
                Properties(
                    Required("app", "string", "应用别名，例如 wechat、netease_music。"),
                    RequiredEnum("action", "应用动作。", "activate", "search", "send_message", "play_pause", "previous", "next"),
                    Optional("query", "string", "search 动作的搜索关键词，其他动作省略。"),
                    Optional("recipient", "string", "send_message 动作的微信联系人名称。"),
                    Optional("message", "string", "send_message 动作要发送的消息，最长 1000 个字符。"))));
            tools.Add(Tool(
                "windows.diagnose_apps",
                "只读检查白名单应用的配置路径、自动发现路径、运行进程和可用动作。不会读取聊天内容或 MCP Token。",
                Properties()));
        }

        if (permissionEnabled(PermissionKeys.Web))
        {
            tools.Add(Tool(
                "windows.open_url",
                "使用默认浏览器打开 http 或 https 网页。不要用于本地文件或自定义协议。",
                Properties(
                    Required("url", "string", "完整的 http 或 https 地址。"))));
            tools.Add(Tool(
                "windows.web_search",
                "使用默认浏览器搜索关键词。适合用户说‘搜索……’或‘帮我查……’。",
                Properties(
                    Required("query", "string", "要搜索的内容。"),
                    OptionalEnum("engine", "搜索引擎，默认 baidu。", "baidu", "bing"))));
        }

        if (permissionEnabled(PermissionKeys.Keyboard))
        {
            tools.Add(Tool(
                "windows.type_text",
                "向当前获得焦点的输入框键入文字。调用前必须确认正确窗口和输入框已获得焦点。",
                Properties(
                    Required("text", "string", "要输入的文字，最长 4000 个字符。"))));
            tools.Add(Tool(
                "windows.hotkey",
                "在当前窗口按下键盘快捷键，例如 ctrl+l、ctrl+c、ctrl+v、alt+tab。",
                Properties(
                    Required("keys", "string", "使用加号连接的快捷键，例如 ctrl+shift+s。"))));
        }

        if (permissionEnabled(PermissionKeys.Mouse))
        {
            tools.Add(Tool(
                "windows.move_mouse",
                "把鼠标移动到屏幕绝对坐标。坐标必须位于当前虚拟屏幕范围内。",
                Properties(
                    Required("x", "integer", "目标横坐标。"),
                    Required("y", "integer", "目标纵坐标。"))));
            tools.Add(Tool(
                "windows.click",
                "点击鼠标。可省略坐标以点击当前位置；默认单击左键。",
                Properties(
                    Optional("x", "integer", "可选横坐标。"),
                    Optional("y", "integer", "可选纵坐标。"),
                    OptionalEnum("button", "鼠标按键。", "left", "right", "middle"),
                    OptionalInteger("clicks", "点击次数，只允许 1 或 2。", 1, 2))));
            tools.Add(Tool(
                "windows.scroll",
                "在当前鼠标位置滚动页面。正数向上，负数向下，范围 -20 到 20。",
                Properties(
                    RequiredInteger("amount", "滚动格数。", -20, 20))));
        }

        if (permissionEnabled(PermissionKeys.Media))
        {
            tools.Add(Tool(
                "windows.media_control",
                "控制系统音量和媒体播放，支持增大音量、减小音量、静音、播放暂停、上一首、下一首。",
                Properties(
                    RequiredEnum(
                        "action",
                        "媒体动作。",
                        "volume_up",
                        "volume_down",
                        "mute",
                        "play_pause",
                        "previous",
                        "next"),
                    OptionalInteger("steps", "调节音量的步数，默认 2。", 1, 10))));
        }

        if (permissionEnabled(PermissionKeys.Screenshot))
        {
            tools.Add(Tool(
                "windows.screenshot",
                "截取全部屏幕并保存到本机 LOOY 数据目录，返回图片本地路径。",
                Properties()));
        }

        return tools;
    }

    private static ToolDefinition Tool(string name, string description, object schema) => new()
    {
        Name = name,
        Description = description,
        InputSchema = schema
    };

    private static object Properties(params PropertySpec[] specs)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();
        foreach (var spec in specs)
        {
            var definition = new Dictionary<string, object>
            {
                ["type"] = spec.Type,
                ["description"] = spec.Description
            };
            if (spec.EnumValues is { Length: > 0 })
            {
                definition["enum"] = spec.EnumValues;
            }
            if (spec.Minimum is not null)
            {
                definition["minimum"] = spec.Minimum.Value;
            }
            if (spec.Maximum is not null)
            {
                definition["maximum"] = spec.Maximum.Value;
            }

            properties[spec.Name] = definition;
            if (spec.IsRequired)
            {
                required.Add(spec.Name);
            }
        }

        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false
        };
    }

    private static PropertySpec Required(string name, string type, string description) =>
        new(name, type, description, true, null, null, null);

    private static PropertySpec Optional(string name, string type, string description) =>
        new(name, type, description, false, null, null, null);

    private static PropertySpec RequiredInteger(string name, string description, int minimum, int maximum) =>
        new(name, "integer", description, true, null, minimum, maximum);

    private static PropertySpec OptionalInteger(string name, string description, int minimum, int maximum) =>
        new(name, "integer", description, false, null, minimum, maximum);

    private static PropertySpec RequiredEnum(string name, string description, params string[] values) =>
        new(name, "string", description, true, values, null, null);

    private static PropertySpec OptionalEnum(string name, string description, params string[] values) =>
        new(name, "string", description, false, values, null, null);

    private sealed record PropertySpec(
        string Name,
        string Type,
        string Description,
        bool IsRequired,
        string[]? EnumValues,
        int? Minimum,
        int? Maximum);
}
