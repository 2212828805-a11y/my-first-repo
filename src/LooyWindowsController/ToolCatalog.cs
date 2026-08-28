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
                "按白名单别名打开一个 Windows 桌面应用。只能打开用户明确允许的应用。用户只要求打开网易云音乐时，应使用 windows.netease_music_task，而不是浏览器或网页搜索。",
                Properties(
                    Required("app", "string", "应用别名，例如 notepad、edge、wechat。"))));
            tools.Add(Tool(
                "windows.close_app",
                "请求白名单中的桌面应用正常关闭，不会强制结束进程。",
                Properties(
                    Required("app", "string", "应用别名。"))));
            tools.Add(Tool(
                "windows.netease_music_task",
                "网易云音乐桌面客户端专用连续任务。用户提到打开网易云、在网易云搜索、播放第几个搜索结果、暂停或切歌时，必须优先调用本工具，禁止改用 windows.web_search。open 只打开桌面应用；search 会在一次调用中打开/激活应用并输入搜索词；search_and_play 会连续完成打开、搜索、本机识屏并双击第 N 个歌曲结果。",
                Properties(
                    RequiredEnum("action", "网易云任务。", "open", "search", "search_and_play", "play_pause", "previous", "next"),
                    Optional("query", "string", "search 或 search_and_play 的歌曲、歌手或搜索关键词。"),
                    OptionalInteger("result_number", "search_and_play 要播放搜索结果中的第几个，默认 1，范围 1 到 20。", 1, 20))));
            tools.Add(Tool(
                "windows.app_action",
                "对指定白名单应用执行动作。支持窗口激活、应用内搜索、微信或 QQ 发消息、记事本新建与写入，以及媒体播放控制。网易云相关请求应改用 windows.netease_music_task，以保证打开、搜索和播放动作连续完成。键盘未授权时会先在电脑上弹出授权窗口；输入前会确认目标应用仍在前台。",
                Properties(
                    Required("app", "string", "应用别名，例如 wechat、qq、netease_music、notepad。"),
                    RequiredEnum("action", "应用动作。", "activate", "search", "send_message", "new_document", "write_text", "new_and_write", "play_pause", "previous", "next"),
                    Optional("query", "string", "search 动作的搜索关键词，其他动作省略。"),
                    Optional("recipient", "string", "send_message 动作的微信或 QQ 联系人名称。"),
                    Optional("message", "string", "send_message 动作要发送的消息，最长 1000 个字符。"),
                    Optional("text", "string", "write_text 或 new_and_write 动作写入记事本的内容。"))));
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
                "只用于用户明确要求网页或浏览器搜索。不得用于打开网易云音乐或在网易云客户端搜索；网易云请求必须调用 windows.netease_music_task。仅当用户明确说“用浏览器/网页搜索网易云”时，才把 force_browser 设为 true。",
                Properties(
                    Required("query", "string", "要搜索的内容。"),
                    OptionalEnum("engine", "搜索引擎，默认 baidu。", "baidu", "bing", "google"),
                    Optional("force_browser", "boolean", "用户明确要求用浏览器搜索网易云时设为 true；否则省略。"))));
        }

        tools.Add(Tool(
            "windows.type_text",
            "向当前获得焦点的输入框键入文字。未授权时会先在电脑上弹出键盘授权窗口。调用前必须确认正确窗口。",
            Properties(
                Required("text", "string", "要输入的文字，最长 4000 个字符。"))));
        tools.Add(Tool(
            "windows.hotkey",
            "在当前窗口按下键盘快捷键。未授权时会先弹出授权窗口。",
            Properties(
                Required("keys", "string", "使用加号连接的快捷键，例如 ctrl+shift+s。"))));

        tools.Add(Tool(
            "windows.cursor_position",
            "读取当前鼠标指针坐标。未授权时会先在电脑上弹出鼠标授权窗口。",
            Properties()));
        tools.Add(Tool(
            "windows.move_mouse",
            "把鼠标移动到屏幕绝对坐标。未授权时会先弹出授权窗口。",
            Properties(
                Required("x", "integer", "目标横坐标。"),
                Required("y", "integer", "目标纵坐标。"))));
        tools.Add(Tool(
            "windows.click",
            "点击鼠标。可省略坐标以点击当前位置；默认单击左键。未授权时会先弹出授权窗口。",
            Properties(
                Optional("x", "integer", "可选横坐标。"),
                Optional("y", "integer", "可选纵坐标。"),
                OptionalEnum("button", "鼠标按键。", "left", "right", "middle"),
                OptionalInteger("clicks", "点击次数，只允许 1 或 2。", 1, 2))));
        tools.Add(Tool(
            "windows.scroll",
            "在当前鼠标位置滚动页面。正数向上，负数向下，范围 -20 到 20。未授权时会先弹出授权窗口。",
            Properties(
                RequiredInteger("amount", "滚动格数。", -20, 20))));

        tools.Add(Tool(
            "windows.inspect_screen",
            "在本机截取并 OCR 识别当前前台窗口，返回带编号的可见文字和短期快照 ID；截图只在内存中处理，不保存到磁盘，但识别出的文字会返回给当前连接的路遥。网页或网易云搜索完成后，必须先调用本工具读取结果，禁止猜测坐标。未授权时会在电脑上弹窗询问。",
            Properties(
                OptionalInteger("max_items", "最多返回的可见文字条目，默认 60。", 10, 80))));
        tools.Add(Tool(
            "windows.open_screen_text",
            "识别当前前台画面并按可见文字打开软件或项目，一次调用完成 OCR、定位和点击。适用于“打开屏幕上的微信/某某软件”等请求，不执行路径、命令或后台启动。桌面图标默认双击，开始菜单等菜单项默认单击；同名结果不唯一时必须提供 occurrence，禁止猜测。需要屏幕文字识别和鼠标授权。",
            Properties(
                Required("text", "string", "屏幕上可见的软件或项目名称，例如 微信、网易云音乐。"),
                OptionalInteger("occurrence", "同名结果从上到下、从左到右的第几个；只有出现多个匹配项时才提供。", 1, 20),
                OptionalInteger("clicks", "可显式指定 1=单击、2=双击；省略时程序根据桌面或菜单自动选择。", 1, 2))));
        tools.Add(Tool(
            "windows.click_screen_item",
            "点击 windows.inspect_screen 返回的某个文字编号。调用前必须使用同一快照 ID，选择标题等唯一文字，不能把用户说的“第几个结果”直接当成本参数。抖音视频标题通常单击（clicks=1），网易云歌曲标题通常双击（clicks=2）。点击前会重新识别并核对文字、窗口与位置；页面变化时会拒绝点击。",
            Properties(
                Required("snapshot_id", "string", "windows.inspect_screen 返回的 8 位快照 ID。"),
                RequiredInteger("index", "该快照列表中方括号里的文字编号。", 1, 80),
                OptionalInteger("clicks", "左键点击次数；视频使用 1，音乐使用 2，默认 1。", 1, 2))));

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
