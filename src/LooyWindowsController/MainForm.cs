using System.ComponentModel;
using System.Diagnostics;
using System.Net;

namespace Looy.WindowsController;

internal sealed class MainForm : Form
{
    private readonly SettingsStore _settingsStore;
    private readonly ControllerSettings _settings;
    private readonly BindingList<AppEntry> _apps;
    private readonly WindowsController _windowsController;
    private readonly McpEndpointClient _mcpClient;
    private readonly DeviceLicenseClient _licenseClient;
    private readonly System.Windows.Forms.Timer _licenseTimer = new();

    private readonly TextBox _endpointBox = new();
    private readonly CheckBox _showEndpointBox = new() { Text = "显示连接地址", AutoSize = true };
    private readonly CheckBox _rememberEndpointBox = new() { Text = "在本机安全保存", AutoSize = true };
    private readonly CheckBox _autoStartBox = new() { Text = "开机自动启动", AutoSize = true };
    private readonly CheckBox _autoConnectBox = new() { Text = "启动后自动连接", AutoSize = true };
    private readonly Button _connectButton = new() { Text = "连接路遥", Width = 126, Height = 38 };
    private readonly Button _disconnectButton = new() { Text = "暂时断开", Width = 106, Height = 38, Enabled = false };
    private readonly Button _emergencyButton = new() { Text = "立即停用", Width = 106, Height = 38 };
    private readonly Label _statusLabel = new() { Text = "● 等待连接", AutoSize = true };
    private readonly Label _licenseStatusLabel = new() { Text = "● 授权有效", AutoSize = true };
    private readonly RichTextBox _logBox = new();
    private readonly DataGridView _appsGrid = new();
    private readonly Dictionary<string, CheckBox> _permissionBoxes = new();
    private readonly HashSet<string> _sessionPermissions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Button _inputAccessButton = new() { Text = "授权键盘与鼠标", Width = 148, Height = 38 };
    private readonly Label _inputAccessStateLabel = new() { AutoSize = true };
    private readonly Label _systemInputAccessLabel = new() { AutoSize = true };
    private readonly Button _elevateButton = new() { Text = "管理员模式重启", Width = 148, Height = 36 };
    private readonly Button _inputTestButton = new() { Text = "检测键鼠与识屏", Width = 148, Height = 36 };
    private bool _initializing = true;
    private bool _emergencyStopped;
    private bool _closingAfterStop;
    private bool _licenseCheckRunning;
    private bool _licenseBlocked;

    public MainForm(DeviceLicenseClient licenseClient)
    {
        _licenseClient = licenseClient;
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _apps = new BindingList<AppEntry>(_settings.Apps.Select(app => app.Clone()).ToList());
        _windowsController = new WindowsController(
            IsPermissionEnabled,
            RequestSensitivePermissionAsync,
            () => _apps.Select(app => app.Clone()).ToArray(),
            _settingsStore,
            WriteLog);
        _mcpClient = new McpEndpointClient(
            () => ToolCatalog.Build(IsPermissionEnabled),
            _windowsController.ExecuteAsync);
        _mcpClient.Log += WriteLog;
        _mcpClient.StateChanged += UpdateConnectionState;

        BuildWindow();
        ApplySettingsToUi();
        WireEvents();
        _licenseTimer.Interval = checked(_licenseClient.NextCheckSeconds * 1000);
        _initializing = false;
        WriteLog("路遥智控 0.7.5 已启动。连接密钥和设备私钥不会显示在运行记录中。");
        WriteLog($"设备授权：{_licenseClient.StatusText}（{_licenseClient.DeviceIdHint}）。");
    }

    private void BuildWindow()
    {
        Text = "路遥智控 · LOOY v0.7.5";
        Width = 1040;
        Height = 760;
        MinimumSize = new Size(900, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = AppTheme.Canvas;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(24, 20, 24, 24),
            BackColor = AppTheme.Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titlePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = AppTheme.Canvas,
            Margin = new Padding(0, 0, 0, 14)
        };
        titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 310));
        var brandMark = new BrandMarkControl { Anchor = AnchorStyles.Left };
        titlePanel.Controls.Add(brandMark, 0, 0);

        var titleCopy = new Panel { Dock = DockStyle.Fill };
        var title = new Label
        {
            Text = "路遥智控",
            Font = new Font("Microsoft YaHei UI", 21F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 4),
            ForeColor = AppTheme.Ink
        };
        var subtitle = new Label
        {
            Text = "让每一次电脑操作，都发生在你清楚授权的范围里",
            AutoSize = true,
            Location = new Point(2, 48),
            ForeColor = AppTheme.Muted
        };
        titleCopy.Controls.Add(title);
        titleCopy.Controls.Add(subtitle);
        titlePanel.Controls.Add(titleCopy, 1, 0);

        var statePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 18, 2, 0)
        };
        _statusLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        _statusLabel.ForeColor = AppTheme.Muted;
        _statusLabel.BackColor = AppTheme.Surface;
        _statusLabel.Padding = new Padding(14, 9, 14, 9);
        statePanel.Controls.Add(_statusLabel);
        _licenseStatusLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        _licenseStatusLabel.ForeColor = AppTheme.Success;
        _licenseStatusLabel.BackColor = AppTheme.Surface;
        _licenseStatusLabel.Padding = new Padding(14, 9, 14, 9);
        _licenseStatusLabel.Text = $"● {_licenseClient.StatusText}";
        statePanel.Controls.Add(_licenseStatusLabel);
        titlePanel.Controls.Add(statePanel, 2, 0);
        root.Controls.Add(titlePanel, 0, 0);

        var tabs = new WarmTabControl
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Canvas
        };
        tabs.TabPages.Add(CreateConnectionTab());
        tabs.TabPages.Add(CreatePermissionsTab());
        tabs.TabPages.Add(CreateAppsTab());
        tabs.TabPages.Add(CreateLogsTab());
        root.Controls.Add(tabs, 0, 1);
        Controls.Add(root);

        AppTheme.StyleButton(_connectButton, ButtonKind.Primary);
        AppTheme.StyleButton(_disconnectButton);
        AppTheme.StyleButton(_emergencyButton, ButtonKind.Danger);
        AppTheme.StyleButton(_inputAccessButton, ButtonKind.Primary);
        AppTheme.StyleButton(_elevateButton);
        AppTheme.StyleButton(_inputTestButton);
        AppTheme.StyleTextBox(_endpointBox);
    }

    private TabPage CreateConnectionTab()
    {
        var page = new TabPage("连接中心") { Padding = new Padding(26, 24, 26, 24), BackColor = AppTheme.Surface };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            BackColor = AppTheme.Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(AppTheme.SectionTitle("连接这台电脑"), 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "连接地址",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = AppTheme.Ink
        }, 0, 1);

        _endpointBox.Dock = DockStyle.Fill;
        _endpointBox.UseSystemPasswordChar = true;
        _endpointBox.PlaceholderText = "粘贴以 wss:// 开头的安全连接地址";
        _endpointBox.Margin = new Padding(0, 5, 0, 9);
        layout.Controls.Add(_endpointBox, 0, 2);

        var endpointOptions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = AppTheme.Surface
        };
        StyleOption(_showEndpointBox);
        StyleOption(_rememberEndpointBox);
        endpointOptions.Controls.Add(_showEndpointBox);
        endpointOptions.Controls.Add(Spacer(18));
        endpointOptions.Controls.Add(_rememberEndpointBox);
        layout.Controls.Add(endpointOptions, 0, 3);

        var launchOptions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = AppTheme.Surface
        };
        StyleOption(_autoStartBox);
        StyleOption(_autoConnectBox);
        launchOptions.Controls.Add(_autoStartBox);
        launchOptions.Controls.Add(Spacer(18));
        launchOptions.Controls.Add(_autoConnectBox);
        layout.Controls.Add(launchOptions, 0, 4);

        var actionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = AppTheme.Surface,
            Padding = new Padding(0, 7, 0, 7)
        };
        actionPanel.Controls.Add(_connectButton);
        actionPanel.Controls.Add(_disconnectButton);
        actionPanel.Controls.Add(Spacer(12));
        actionPanel.Controls.Add(_emergencyButton);
        layout.Controls.Add(actionPanel, 0, 5);

        var securityPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.SurfaceMuted,
            Padding = new Padding(16, 14, 16, 12),
            Margin = new Padding(0, 6, 0, 4)
        };
        securityPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "连接地址就是这台电脑的控制密钥，请勿截图或转发。公网连接请始终使用 wss://。\n路遥不会获取管理员权限，也不会替你确认系统安全弹窗。",
            ForeColor = AppTheme.Muted,
            AutoSize = false
        });
        layout.Controls.Add(securityPanel, 0, 6);

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = AppTheme.Muted,
            Padding = new Padding(0, 14, 0, 0),
            Text = "连接后只会开放你在“授权管理”中启用的能力。点击“立即停用”会关闭全部控制权限并断开连接。"
        }, 0, 7);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreatePermissionsTab()
    {
        var page = new TabPage("授权管理") { Padding = new Padding(26, 24, 26, 24), BackColor = AppTheme.Surface };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = AppTheme.Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(AppTheme.SectionTitle("决定路遥可以做什么"), 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "授权随时可以撤回。键盘、鼠标、屏幕文字识别和截图等敏感能力默认保持关闭。",
            AutoSize = true,
            ForeColor = AppTheme.Muted,
            Anchor = AnchorStyles.Left
        }, 0, 1);

        var inputAccessPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = AppTheme.SurfaceMuted,
            Padding = new Padding(14, 10, 12, 10),
            Margin = new Padding(0, 0, 0, 8)
        };
        inputAccessPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputAccessPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
        var inputCopy = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.SurfaceMuted };
        inputCopy.Controls.Add(new Label
        {
            Text = "键盘与鼠标控制",
            AutoSize = true,
            Location = new Point(0, 1),
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = AppTheme.Ink
        });
        _inputAccessStateLabel.Location = new Point(0, 28);
        _inputAccessStateLabel.ForeColor = AppTheme.Muted;
        inputCopy.Controls.Add(_inputAccessStateLabel);
        _systemInputAccessLabel.Location = new Point(0, 54);
        _systemInputAccessLabel.ForeColor = AppTheme.Muted;
        inputCopy.Controls.Add(_systemInputAccessLabel);
        inputAccessPanel.Controls.Add(inputCopy, 0, 0);
        var inputButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = AppTheme.SurfaceMuted,
            Padding = new Padding(8, 0, 0, 0)
        };
        inputButtons.Controls.Add(_inputAccessButton);
        inputButtons.Controls.Add(_elevateButton);
        inputButtons.Controls.Add(_inputTestButton);
        inputAccessPanel.Controls.Add(inputButtons, 1, 0);
        layout.Controls.Add(inputAccessPanel, 0, 2);

        var permissionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 4, 8, 4),
            BackColor = AppTheme.Surface
        };
        AddPermission(permissionPanel, PermissionKeys.SystemStatus, "读取电脑状态", "读取电脑名称、系统版本和时间；不修改系统。", true);
        AddPermission(permissionPanel, PermissionKeys.Applications, "打开和关闭可用应用", "只能操作“应用管理”中已启用的应用。", true);
        AddPermission(permissionPanel, PermissionKeys.Web, "打开网页和搜索", "只允许 http/https 地址，不允许打开本地文件。", true);
        AddPermission(permissionPanel, PermissionKeys.Media, "音量与媒体控制", "调节音量、静音、播放暂停和切歌。", true);
        AddPermission(permissionPanel, PermissionKeys.SystemControl, "系统设置与电源操作", "主题、壁纸和经二次确认的锁定/关机/重启；默认关闭。", false);
        AddPermission(permissionPanel, PermissionKeys.Clipboard, "读取剪贴板文字", "剪贴板可能包含密码或隐私；只有明确要求时才开启。", false);
        AddPermission(permissionPanel, PermissionKeys.Keyboard, "键盘输入和快捷键", "能够向当前窗口输入文字并发送快捷键。", false);
        AddPermission(permissionPanel, PermissionKeys.Mouse, "鼠标移动、点击和滚动", "能够操作当前桌面，请谨慎开启。", false);
        AddPermission(permissionPanel, PermissionKeys.ScreenRecognition, "识别前台窗口文字", "截图只在本机内存中识别且不保存；识别出的文字会返回给当前连接的路遥。", false);
        AddPermission(permissionPanel, PermissionKeys.Screenshot, "截取屏幕", "截图可能包含聊天、账号或其他隐私信息。", false);
        layout.Controls.Add(permissionPanel, 0, 3);

        var note = new Label
        {
            Text = "授权变更会立即同步到当前连接；已经开始的单次操作不会被中途改变。",
            AutoSize = true,
            ForeColor = AppTheme.Muted,
            Anchor = AnchorStyles.Left
        };
        layout.Controls.Add(note, 0, 4);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateAppsTab()
    {
        var page = new TabPage("应用管理") { Padding = new Padding(26, 24, 26, 24), BackColor = AppTheme.Surface };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = AppTheme.Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.Controls.Add(AppTheme.SectionTitle("管理可被控制的应用"), 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "只有这里已启用的应用可以被路遥操作。双击应用可编辑，路径也可以自动检测。",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = AppTheme.Muted
        }, 0, 1);

        _appsGrid.Dock = DockStyle.Fill;
        _appsGrid.AutoGenerateColumns = false;
        _appsGrid.AllowUserToAddRows = false;
        _appsGrid.AllowUserToDeleteRows = false;
        _appsGrid.MultiSelect = false;
        _appsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _appsGrid.RowHeadersVisible = false;
        _appsGrid.BackgroundColor = AppTheme.Surface;
        _appsGrid.BorderStyle = BorderStyle.None;
        _appsGrid.GridColor = AppTheme.Border;
        _appsGrid.EnableHeadersVisualStyles = false;
        _appsGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _appsGrid.ColumnHeadersHeight = 40;
        _appsGrid.ColumnHeadersDefaultCellStyle.BackColor = AppTheme.Accent;
        _appsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _appsGrid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
        _appsGrid.DefaultCellStyle.BackColor = AppTheme.Surface;
        _appsGrid.DefaultCellStyle.ForeColor = AppTheme.Ink;
        _appsGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 222, 209);
        _appsGrid.DefaultCellStyle.SelectionForeColor = AppTheme.Ink;
        _appsGrid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
        _appsGrid.RowTemplate.Height = 38;
        _appsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(AppEntry.Enabled),
            HeaderText = "启用",
            Width = 60
        });
        _appsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(AppEntry.Alias),
            HeaderText = "英文别名",
            Width = 155,
            ReadOnly = true
        });
        _appsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(AppEntry.DisplayName),
            HeaderText = "应用名称",
            Width = 180,
            ReadOnly = true
        });
        _appsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(AppEntry.Target),
            HeaderText = "程序路径或协议",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true
        });
        _appsGrid.DataSource = _apps;
        layout.Controls.Add(_appsGrid, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0),
            BackColor = AppTheme.Surface
        };
        var addButton = new Button { Text = "添加应用", Width = 104, Height = 36 };
        var editButton = new Button { Text = "编辑选中项", Width = 116, Height = 36 };
        var deleteButton = new Button { Text = "移除选中项", Width = 116, Height = 36 };
        var detectButton = new Button { Text = "自动检测路径", Width = 126, Height = 36 };
        AppTheme.StyleButton(addButton, ButtonKind.Primary);
        AppTheme.StyleButton(editButton);
        AppTheme.StyleButton(deleteButton);
        AppTheme.StyleButton(detectButton);
        addButton.Click += (_, _) => AddApp();
        editButton.Click += (_, _) => EditSelectedApp();
        deleteButton.Click += (_, _) => DeleteSelectedApp();
        detectButton.Click += (_, _) => AutoDetectAppPaths();
        buttons.Controls.Add(addButton);
        buttons.Controls.Add(editButton);
        buttons.Controls.Add(deleteButton);
        buttons.Controls.Add(detectButton);
        layout.Controls.Add(buttons, 0, 3);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateLogsTab()
    {
        var page = new TabPage("运行记录") { Padding = new Padding(26, 24, 26, 24), BackColor = AppTheme.Surface };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = AppTheme.Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.Controls.Add(AppTheme.SectionTitle("查看最近发生的操作"), 0, 0);

        _logBox.Dock = DockStyle.Fill;
        _logBox.ReadOnly = true;
        _logBox.BackColor = AppTheme.LogSurface;
        _logBox.ForeColor = Color.FromArgb(237, 230, 219);
        _logBox.Font = new Font("Consolas", 9F);
        _logBox.BorderStyle = BorderStyle.None;
        layout.Controls.Add(_logBox, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 12, 0, 0),
            BackColor = AppTheme.Surface
        };
        var clearButton = new Button { Text = "清空记录", Width = 104, Height = 36 };
        var exportButton = new Button { Text = "导出诊断", Width = 110, Height = 36 };
        AppTheme.StyleButton(clearButton);
        AppTheme.StyleButton(exportButton);
        clearButton.Click += (_, _) => _logBox.Clear();
        buttons.Controls.Add(clearButton);
        exportButton.Click += (_, _) => ExportAppDiagnostics();
        buttons.Controls.Add(exportButton);
        layout.Controls.Add(buttons, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private void AddPermission(
        Control parent,
        string key,
        string title,
        string description,
        bool recommended)
    {
        var checkBox = new CheckBox
        {
            AutoSize = false,
            Width = 760,
            Height = 56,
            Text = $"{title}{(recommended ? "（建议开启）" : string.Empty)}\r\n    {description}",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            ForeColor = AppTheme.Ink,
            BackColor = AppTheme.SurfaceMuted,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 0, 8)
        };
        checkBox.CheckedChanged += PermissionBox_CheckedChanged;
        _permissionBoxes[key] = checkBox;
        parent.Controls.Add(checkBox);
    }

    private void ApplySettingsToUi()
    {
        _endpointBox.Text = _settings.Endpoint;
        _rememberEndpointBox.Checked = _settings.RememberEndpoint;
        _autoStartBox.Checked = _settings.AutoStart;
        _autoConnectBox.Checked = _settings.AutoConnect;
        foreach (var pair in _permissionBoxes)
        {
            pair.Value.Checked = _settings.Permissions.TryGetValue(pair.Key, out var enabled) && enabled;
        }
        UpdateInputAccessState();
    }

    private void WireEvents()
    {
        _showEndpointBox.CheckedChanged += (_, _) =>
            _endpointBox.UseSystemPasswordChar = !_showEndpointBox.Checked;
        _connectButton.Click += ConnectButton_Click;
        _disconnectButton.Click += async (_, _) => await DisconnectAsync();
        _emergencyButton.Click += EmergencyButton_Click;
        _rememberEndpointBox.CheckedChanged += (_, _) => SaveSettingsSafe();
        _autoConnectBox.CheckedChanged += (_, _) => SaveSettingsSafe();
        _autoStartBox.CheckedChanged += AutoStartBox_CheckedChanged;
        _endpointBox.Leave += (_, _) => SaveSettingsSafe();
        _appsGrid.CellValueChanged += (_, _) => AppsChanged();
        _appsGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_appsGrid.IsCurrentCellDirty)
            {
                _appsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _appsGrid.CellDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex >= 0)
            {
                EditSelectedApp();
            }
        };
        _inputAccessButton.Click += InputAccessButton_Click;
        _elevateButton.Click += ElevateButton_Click;
        _inputTestButton.Click += InputTestButton_Click;
        _licenseTimer.Tick += LicenseTimer_Tick;
        Shown += MainForm_Shown;
        FormClosing += MainForm_FormClosing;
    }

    private void MainForm_Shown(object? sender, EventArgs eventArgs)
    {
        if (Environment.GetCommandLineArgs().Any(argument => argument.Equals("--autostart", StringComparison.OrdinalIgnoreCase)))
        {
            WindowState = FormWindowState.Minimized;
        }

        if (_settings.AutoConnect && !string.IsNullOrWhiteSpace(_settings.Endpoint))
        {
            ConnectToEndpoint();
        }

        if (Environment.GetCommandLineArgs().Any(argument => argument.Equals("--elevated-restart", StringComparison.OrdinalIgnoreCase)))
        {
            WriteLog("管理员输入模式已开启。UAC 安全窗口仍需要你本人确认。");
        }

        _licenseTimer.Start();
    }

    private async void LicenseTimer_Tick(object? sender, EventArgs eventArgs)
    {
        if (_licenseCheckRunning || _licenseBlocked || IsDisposed || Disposing)
        {
            return;
        }

        _licenseCheckRunning = true;
        _licenseTimer.Stop();
        try
        {
            var result = await _licenseClient.CheckAsync(allowOfflineGrace: true);
            UpdateLicenseStatus(result);
            if (!result.Allowed)
            {
                await StopForLicenseFailureAsync(result.Message);
            }
        }
        catch (Exception exception)
        {
            var message = $"严格在线授权复核失败：{exception.Message}";
            WriteLog(message);
            await StopForLicenseFailureAsync(message);
        }
        finally
        {
            _licenseCheckRunning = false;
            if (!_licenseBlocked && !IsDisposed && !Disposing)
            {
                _licenseTimer.Interval = checked(_licenseClient.NextCheckSeconds * 1000);
                _licenseTimer.Start();
            }
        }
    }

    private void UpdateLicenseStatus(DeviceLicenseCheckResult result)
    {
        _licenseStatusLabel.Text = result.Allowed
            ? result.UsedOfflineGrace ? "● 离线宽限" : $"● {_licenseClient.StatusText}"
            : "● 授权不可用";
        _licenseStatusLabel.ForeColor = result.Allowed
            ? result.UsedOfflineGrace ? AppTheme.Warning : AppTheme.Success
            : AppTheme.Danger;
        if (result.UsedOfflineGrace)
        {
            WriteLog(result.Message);
        }
    }

    private async Task StopForLicenseFailureAsync(string message)
    {
        if (_licenseBlocked)
        {
            return;
        }
        _licenseBlocked = true;
        _emergencyStopped = true;
        _connectButton.Enabled = false;
        _licenseTimer.Stop();
        await _mcpClient.StopAsync();
        MessageBox.Show(
            $"设备授权已停止：\n\n{message}\n\n应用将关闭；处理完成后可重新打开校验。",
            "设备授权不可用",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        Close();
    }

    private void ConnectButton_Click(object? sender, EventArgs eventArgs) => ConnectToEndpoint();

    private async void InputAccessButton_Click(object? sender, EventArgs eventArgs)
    {
        var approved = ShowInputAuthorizationDialog(
            null,
            "允许路遥在你确认后操作当前窗口");
        if (approved)
        {
            await _mcpClient.NotifyToolsChangedAsync();
        }
    }

    private async void InputTestButton_Click(object? sender, EventArgs eventArgs)
    {
        if (!IsPermissionEnabled(PermissionKeys.Keyboard)
            || !IsPermissionEnabled(PermissionKeys.Mouse))
        {
            var approved = ShowInputAuthorizationDialog(
                null,
                "在路遥智控窗口内检测键盘输入、鼠标移动和单击");
            if (!approved
                || !IsPermissionEnabled(PermissionKeys.Keyboard)
                || !IsPermissionEnabled(PermissionKeys.Mouse))
            {
                return;
            }
            await _mcpClient.NotifyToolsChangedAsync();
        }

        using var dialog = new InputSelfTestForm(_windowsController);
        dialog.ShowDialog(this);
    }

    private async void ElevateButton_Click(object? sender, EventArgs eventArgs)
    {
        if (WindowsInputAccess.IsElevated)
        {
            MessageBox.Show(
                "路遥智控已经在管理员输入模式中运行。UAC 安全确认窗口仍必须由你本人操作。",
                "已开启管理员输入模式",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var answer = MessageBox.Show(
            "如果微信、QQ、网易云或其他目标应用是以管理员身份运行，普通程序无法向它输入键盘内容。\n\n"
            + "点击“是”后，Windows 会显示管理员确认；确认后路遥智控将重启。重启后如果使用的是“仅本次连接”授权，请再授权一次键盘或鼠标。\n\n"
            + "管理员模式权限更高，请只在你能看到电脑屏幕时开启。是否继续？",
            "切换管理员输入模式",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        SaveSettingsSafe();
        try
        {
            WindowsInputAccess.RestartElevated();
        }
        catch (OperationCanceledException exception)
        {
            MessageBox.Show(exception.Message, "未切换权限", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"无法切换管理员输入模式：\n\n{exception.Message}",
                "重启失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        _elevateButton.Enabled = false;
        WriteLog("Windows 已允许管理员模式，正在关闭当前普通模式窗口。");
        await _mcpClient.StopAsync();
        await _mcpClient.DisposeAsync();
        _closingAfterStop = true;
        Close();
    }

    private Task<bool> RequestSensitivePermissionAsync(
        string permission,
        string reason,
        CancellationToken cancellationToken)
    {
        return permission == PermissionKeys.ScreenRecognition
            ? RequestScreenRecognitionPermissionAsync(reason, cancellationToken)
            : RequestInputPermissionAsync(permission, reason, cancellationToken);
    }

    private async Task<bool> RequestScreenRecognitionPermissionAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        if (IsPermissionEnabled(PermissionKeys.ScreenRecognition))
        {
            return true;
        }
        if (IsDisposed || Disposing || cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        void ShowDialogOnUi()
        {
            if (IsDisposed || Disposing || cancellationToken.IsCancellationRequested)
            {
                completion.TrySetResult(false);
                return;
            }

            var answer = MessageBox.Show(
                this,
                $"本次请求：{reason}\n\n"
                + "路遥智控会在本机内存中截取当前前台窗口，并使用 Windows OCR 读取可见文字。截图不会保存到磁盘，也不会上传；识别出的文字会返回给当前连接的路遥，用于按编号选择并点击。\n\n"
                + "本授权仅在这次连接中有效，断开连接后自动撤回。是否允许？",
                "屏幕文字识别授权",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                WriteLog("用户取消了屏幕文字识别授权。");
                completion.TrySetResult(false);
                return;
            }

            _emergencyStopped = false;
            _sessionPermissions.Add(PermissionKeys.ScreenRecognition);
            WriteLog("已授权本次连接识别前台窗口文字；截图只在本机内存中处理，不会保存。");
            completion.TrySetResult(true);
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(ShowDialogOnUi);
            }
            catch
            {
                completion.TrySetResult(false);
            }
        }
        else
        {
            ShowDialogOnUi();
        }

        try
        {
            return await completion.Task;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> RequestInputPermissionAsync(
        string permission,
        string reason,
        CancellationToken cancellationToken)
    {
        if (IsPermissionEnabled(permission))
        {
            return true;
        }
        if (IsDisposed || Disposing || cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        void ShowDialogOnUi()
        {
            if (IsDisposed || Disposing || cancellationToken.IsCancellationRequested)
            {
                completion.TrySetResult(false);
                return;
            }
            completion.TrySetResult(ShowInputAuthorizationDialog(permission, reason));
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(ShowDialogOnUi);
            }
            catch
            {
                completion.TrySetResult(false);
            }
        }
        else
        {
            ShowDialogOnUi();
        }

        try
        {
            return await completion.Task;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private bool ShowInputAuthorizationDialog(string? requiredPermission, string reason)
    {
        using var dialog = new InputAuthorizationForm(requiredPermission, reason);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            WriteLog("用户取消了键盘与鼠标授权。");
            return false;
        }

        _emergencyStopped = false;
        var selected = dialog.SelectedPermissions;
        if (dialog.Persistence == InputAuthorizationPersistence.Session)
        {
            foreach (var permission in selected)
            {
                _sessionPermissions.Add(permission);
            }
            WriteLog("已授权本次连接使用键盘或鼠标；断开连接后自动撤回。");
        }
        else if (dialog.Persistence == InputAuthorizationPersistence.Always)
        {
            var wasInitializing = _initializing;
            _initializing = true;
            try
            {
                foreach (var permission in selected)
                {
                    _settings.Permissions[permission] = true;
                    if (_permissionBoxes.TryGetValue(permission, out var permissionBox))
                    {
                        permissionBox.Checked = true;
                    }
                }
            }
            finally
            {
                _initializing = wasInitializing;
            }
            SaveSettingsSafe();
            WriteLog("已保存键盘与鼠标授权；可在“授权管理”中随时撤回。");
        }

        UpdateInputAccessState();
        return requiredPermission is null || IsPermissionEnabled(requiredPermission);
    }

    private void UpdateInputAccessState()
    {
        var keyboard = IsPermissionEnabled(PermissionKeys.Keyboard);
        var mouse = IsPermissionEnabled(PermissionKeys.Mouse);
        _inputAccessStateLabel.Text = (keyboard, mouse) switch
        {
            (true, true) => "键盘和鼠标已授权",
            (true, false) => "键盘已授权，鼠标未授权",
            (false, true) => "鼠标已授权，键盘未授权",
            _ => "尚未授权；首次调用时也会弹窗询问"
        };
        _inputAccessStateLabel.ForeColor = keyboard || mouse ? AppTheme.Success : AppTheme.Muted;
        _inputAccessButton.Text = keyboard && mouse ? "调整授权" : "授权键盘与鼠标";
        _systemInputAccessLabel.Text = WindowsInputAccess.StatusText;
        _systemInputAccessLabel.ForeColor = WindowsInputAccess.IsElevated ? AppTheme.Success : AppTheme.Warning;
        _elevateButton.Text = WindowsInputAccess.IsElevated ? "管理员模式已开启" : "管理员模式重启";
        _elevateButton.Enabled = !WindowsInputAccess.IsElevated;
    }

    private void ConnectToEndpoint()
    {
        var endpointText = _endpointBox.Text.Trim();
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != "ws" && endpoint.Scheme != "wss"))
        {
            MessageBox.Show(
                "请输入完整的 ws:// 或 wss:// MCP 接入点地址。",
                "地址无效",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _endpointBox.Focus();
            return;
        }

        if (endpoint.Scheme == "ws" && !IsPrivateOrLocalHost(endpoint.Host))
        {
            var answer = MessageBox.Show(
                "这是未加密的公网 ws:// 地址，MCP Token 可能被网络中的其他人看到。\n\n仍要继续连接吗？",
                "连接安全提醒",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                return;
            }
        }

        try
        {
            _emergencyStopped = false;
            _settings.Endpoint = endpointText;
            SaveSettingsSafe();
            _mcpClient.Start(endpoint);
            _connectButton.Enabled = false;
            _disconnectButton.Enabled = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "无法连接", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DisconnectAsync()
    {
        _disconnectButton.Enabled = false;
        await _mcpClient.StopAsync();
        _windowsController.ClearTransientState();
        _sessionPermissions.Clear();
        UpdateInputAccessState();
        _connectButton.Enabled = true;
    }

    private async void EmergencyButton_Click(object? sender, EventArgs eventArgs)
    {
        var wasInitializing = _initializing;
        _initializing = true;
        try
        {
            foreach (var pair in _permissionBoxes)
            {
                pair.Value.Checked = pair.Key == PermissionKeys.SystemStatus;
                _settings.Permissions[pair.Key] = pair.Value.Checked;
            }
        }
        finally
        {
            _initializing = wasInitializing;
        }
        _emergencyStopped = true;
        _sessionPermissions.Clear();
        SaveSettingsSafe();
        UpdateInputAccessState();
        WriteLog("已触发紧急停止：控制权限全部关闭，MCP 连接正在断开。");
        await DisconnectAsync();
        MessageBox.Show(
            "已断开连接，并关闭所有控制权限。\n重新使用前请在“授权管理”中逐项开启。",
            "紧急停止完成",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async void PermissionBox_CheckedChanged(object? sender, EventArgs eventArgs)
    {
        if (_initializing)
        {
            return;
        }

        _emergencyStopped = false;
        if (sender is CheckBox changedBox && !changedBox.Checked)
        {
            var revoked = _permissionBoxes.FirstOrDefault(pair => ReferenceEquals(pair.Value, changedBox)).Key;
            if (!string.IsNullOrWhiteSpace(revoked))
            {
                _sessionPermissions.Remove(revoked);
            }
        }
        foreach (var pair in _permissionBoxes)
        {
            _settings.Permissions[pair.Key] = pair.Value.Checked;
        }
        SaveSettingsSafe();
        UpdateInputAccessState();
        await _mcpClient.NotifyToolsChangedAsync();
    }

    private void AutoStartBox_CheckedChanged(object? sender, EventArgs eventArgs)
    {
        if (_initializing)
        {
            return;
        }

        try
        {
            StartupManager.SetEnabled(_autoStartBox.Checked);
            WriteLog(_autoStartBox.Checked ? "已启用开机自动启动。" : "已关闭开机自动启动。");
            SaveSettingsSafe();
        }
        catch (Exception exception)
        {
            _autoStartBox.Checked = false;
            MessageBox.Show(exception.Message, "开机启动设置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void AddApp()
    {
        using var editor = new AppEditorForm();
        if (editor.ShowDialog(this) != DialogResult.OK || editor.Result is null)
        {
            return;
        }

        if (_apps.Any(app => app.Alias.Equals(editor.Result.Alias, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("这个英文别名已经存在。", "无法添加", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _apps.Add(editor.Result);
        AppsChanged();
    }

    private void EditSelectedApp()
    {
        if (_appsGrid.CurrentRow?.DataBoundItem is not AppEntry selected)
        {
            return;
        }

        using var editor = new AppEditorForm(selected);
        if (editor.ShowDialog(this) != DialogResult.OK || editor.Result is null)
        {
            return;
        }

        if (_apps.Any(app => !ReferenceEquals(app, selected)
                             && app.Alias.Equals(editor.Result.Alias, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("这个英文别名已经存在。", "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        selected.Alias = editor.Result.Alias;
        selected.DisplayName = editor.Result.DisplayName;
        selected.Target = editor.Result.Target;
        selected.Enabled = editor.Result.Enabled;
        _apps.ResetBindings();
        AppsChanged();
    }

    private void DeleteSelectedApp()
    {
        if (_appsGrid.CurrentRow?.DataBoundItem is not AppEntry selected)
        {
            return;
        }
        var answer = MessageBox.Show(
            $"确定从白名单删除“{selected.DisplayName}”吗？",
            "删除应用",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            return;
        }
        _apps.Remove(selected);
        AppsChanged();
    }

    private void AutoDetectAppPaths()
    {
        var detected = new List<string>();
        var missing = new List<string>();
        foreach (var app in _apps)
        {
            var resolved = InstalledAppResolver.TryResolvePath(app);
            if (string.IsNullOrWhiteSpace(resolved) || InstalledAppResolver.IsProtocol(resolved))
            {
                if (!InstalledAppResolver.IsProtocol(app.Target))
                {
                    missing.Add(app.DisplayName);
                }
                continue;
            }

            app.Target = resolved;
            detected.Add(app.DisplayName);
        }
        _apps.ResetBindings();
        AppsChanged();

        var message = $"已检测到 {detected.Count} 个应用路径。";
        if (missing.Count > 0)
        {
            message += $"\n\n仍未找到：{string.Join("、", missing)}。可双击对应行手动选择实际路径。";
        }
        MessageBox.Show(message, "应用路径检测完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportAppDiagnostics()
    {
        var result = _windowsController.ExportDiagnosticReport();
        MessageBox.Show(
            result.Message,
            result.Success ? "诊断报告已导出" : "导出失败",
            MessageBoxButtons.OK,
            result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (result.Success)
        {
            Process.Start(new ProcessStartInfo(_settingsStore.DiagnosticsDirectory) { UseShellExecute = true });
        }
    }

    private void AppsChanged()
    {
        if (_initializing)
        {
            return;
        }
        _settings.Apps = _apps.Select(app => app.Clone()).ToList();
        SaveSettingsSafe();
    }

    private bool IsPermissionEnabled(string key)
    {
        return !_emergencyStopped
               && ((_settings.Permissions.TryGetValue(key, out var enabled) && enabled)
                   || _sessionPermissions.Contains(key));
    }

    private void SaveSettingsSafe()
    {
        if (_initializing)
        {
            return;
        }

        try
        {
            _settings.Endpoint = _endpointBox.Text.Trim();
            _settings.RememberEndpoint = _rememberEndpointBox.Checked;
            _settings.AutoStart = _autoStartBox.Checked;
            _settings.AutoConnect = _autoConnectBox.Checked;
            _settings.Apps = _apps.Select(app => app.Clone()).ToList();
            foreach (var pair in _permissionBoxes)
            {
                _settings.Permissions[pair.Key] = pair.Value.Checked;
            }
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            WriteLog($"保存设置失败：{exception.Message}");
        }
    }

    private void UpdateConnectionState(EndpointConnectionState state, string message)
    {
        RunOnUi(() =>
        {
            _statusLabel.Text = $"● {message}";
            _statusLabel.ForeColor = state switch
            {
                EndpointConnectionState.Connected => AppTheme.Success,
                EndpointConnectionState.Connecting or EndpointConnectionState.Reconnecting => AppTheme.Warning,
                _ => AppTheme.Muted
            };
            var running = state is EndpointConnectionState.Connecting
                or EndpointConnectionState.Connected
                or EndpointConnectionState.Reconnecting;
            _connectButton.Enabled = !running;
            _disconnectButton.Enabled = running;
        });
    }

    private void WriteLog(string message)
    {
        RunOnUi(() =>
        {
            _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        });
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch
            {
                // The form may be closing.
            }
            return;
        }
        action();
    }

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        _licenseTimer.Stop();
        if (_closingAfterStop)
        {
            _licenseTimer.Dispose();
            return;
        }

        eventArgs.Cancel = true;
        SaveSettingsSafe();
        await _mcpClient.StopAsync();
        await _mcpClient.DisposeAsync();
        _licenseTimer.Dispose();
        _closingAfterStop = true;
        Close();
    }

    private static Control Spacer(int width) => new Panel { Width = width, Height = 1 };

    private static void StyleOption(CheckBox option)
    {
        option.ForeColor = AppTheme.Ink;
        option.BackColor = AppTheme.Surface;
        option.FlatStyle = FlatStyle.Flat;
    }

    private static bool IsPrivateOrLocalHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }
        var bytes = address.GetAddressBytes();
        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
               && (bytes[0] == 10
                   || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                   || (bytes[0] == 192 && bytes[1] == 168));
    }
}
