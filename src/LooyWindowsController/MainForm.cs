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

    private readonly TextBox _endpointBox = new();
    private readonly CheckBox _showEndpointBox = new() { Text = "显示地址", AutoSize = true };
    private readonly CheckBox _rememberEndpointBox = new() { Text = "在本机加密保存 MCP 地址", AutoSize = true };
    private readonly CheckBox _autoStartBox = new() { Text = "开机自动启动", AutoSize = true };
    private readonly CheckBox _autoConnectBox = new() { Text = "启动后自动连接", AutoSize = true };
    private readonly Button _connectButton = new() { Text = "连接小智", Width = 120, Height = 36 };
    private readonly Button _disconnectButton = new() { Text = "断开连接", Width = 110, Height = 36, Enabled = false };
    private readonly Button _emergencyButton = new() { Text = "紧急停止", Width = 110, Height = 36 };
    private readonly Label _statusLabel = new() { Text = "● 未连接", AutoSize = true };
    private readonly RichTextBox _logBox = new();
    private readonly DataGridView _appsGrid = new();
    private readonly Dictionary<string, CheckBox> _permissionBoxes = new();
    private bool _initializing = true;
    private bool _emergencyStopped;
    private bool _closingAfterStop;

    public MainForm()
    {
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _apps = new BindingList<AppEntry>(_settings.Apps.Select(app => app.Clone()).ToList());
        _windowsController = new WindowsController(
            IsPermissionEnabled,
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
        _initializing = false;
        WriteLog("路遥电脑控制器已启动。MCP Token 不会显示在日志中。");
    }

    private void BuildWindow()
    {
        Text = "路遥电脑控制器 · LOOY";
        Width = 940;
        Height = 700;
        MinimumSize = new Size(820, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(246, 247, 249);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titlePanel = new Panel { Dock = DockStyle.Fill };
        var title = new Label
        {
            Text = "路遥电脑控制器",
            Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(2, 4),
            ForeColor = Color.FromArgb(35, 38, 45)
        };
        var subtitle = new Label
        {
            Text = "连接小智 MCP 接入点，在你授权的范围内控制这台 Windows 电脑",
            AutoSize = true,
            Location = new Point(5, 46),
            ForeColor = Color.FromArgb(100, 105, 115)
        };
        titlePanel.Controls.Add(title);
        titlePanel.Controls.Add(subtitle);
        root.Controls.Add(titlePanel, 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateConnectionTab());
        tabs.TabPages.Add(CreatePermissionsTab());
        tabs.TabPages.Add(CreateAppsTab());
        tabs.TabPages.Add(CreateLogsTab());
        root.Controls.Add(tabs, 0, 1);
        Controls.Add(root);

        _emergencyButton.BackColor = Color.FromArgb(190, 45, 55);
        _emergencyButton.ForeColor = Color.White;
        _emergencyButton.FlatStyle = FlatStyle.Flat;
        _emergencyButton.FlatAppearance.BorderSize = 0;
        _connectButton.BackColor = Color.FromArgb(55, 105, 220);
        _connectButton.ForeColor = Color.White;
        _connectButton.FlatStyle = FlatStyle.Flat;
        _connectButton.FlatAppearance.BorderSize = 0;
    }

    private TabPage CreateConnectionTab()
    {
        var page = new TabPage("连接") { Padding = new Padding(20), BackColor = Color.White };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = "MCP 接入点地址",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 0);

        _endpointBox.Dock = DockStyle.Fill;
        _endpointBox.UseSystemPasswordChar = true;
        _endpointBox.PlaceholderText = "wss://服务器/mcp_endpoint/mcp/?token=...";
        _endpointBox.Margin = new Padding(0, 4, 0, 6);
        layout.Controls.Add(_endpointBox, 0, 1);

        var endpointOptions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        endpointOptions.Controls.Add(_showEndpointBox);
        endpointOptions.Controls.Add(Spacer(18));
        endpointOptions.Controls.Add(_rememberEndpointBox);
        layout.Controls.Add(endpointOptions, 0, 2);

        var launchOptions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        launchOptions.Controls.Add(_autoStartBox);
        launchOptions.Controls.Add(Spacer(18));
        launchOptions.Controls.Add(_autoConnectBox);
        layout.Controls.Add(launchOptions, 0, 3);

        var actionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        actionPanel.Controls.Add(_connectButton);
        actionPanel.Controls.Add(_disconnectButton);
        actionPanel.Controls.Add(Spacer(12));
        actionPanel.Controls.Add(_emergencyButton);
        _statusLabel.Margin = new Padding(18, 11, 0, 0);
        _statusLabel.Font = new Font(Font, FontStyle.Bold);
        actionPanel.Controls.Add(_statusLabel);
        layout.Controls.Add(actionPanel, 0, 4);

        var securityPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(245, 248, 255),
            Padding = new Padding(14)
        };
        securityPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "安全提示：MCP 地址相当于控制密钥，请勿截图公开。公网连接建议使用 wss://。\n程序默认不会获得管理员权限，也不会自动点击 UAC 确认窗口。",
            ForeColor = Color.FromArgb(55, 75, 115),
            AutoSize = false
        });
        layout.Controls.Add(securityPanel, 0, 5);

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Padding = new Padding(0, 10, 0, 0),
            Text = "连接成功后，小智会自动读取“权限”页面中已经开启的工具。\n如果紧急停止，所有控制权限会立即关闭并断开连接。"
        }, 0, 6);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreatePermissionsTab()
    {
        var page = new TabPage("权限") { Padding = new Padding(20), BackColor = Color.White };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.Controls.Add(new Label
        {
            Text = "只开启你确实需要的能力。鼠标、键盘和截图默认关闭。",
            AutoSize = true,
            ForeColor = Color.FromArgb(80, 85, 95),
            Anchor = AnchorStyles.Left
        }, 0, 0);

        var permissionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8)
        };
        AddPermission(permissionPanel, PermissionKeys.SystemStatus, "读取电脑状态", "读取电脑名称、系统版本和时间；不修改系统。", true);
        AddPermission(permissionPanel, PermissionKeys.Applications, "打开和关闭白名单应用", "只能操作“应用白名单”中已启用的应用。", true);
        AddPermission(permissionPanel, PermissionKeys.Web, "打开网页和搜索", "只允许 http/https 地址，不允许打开本地文件。", true);
        AddPermission(permissionPanel, PermissionKeys.Media, "音量与媒体控制", "调节音量、静音、播放暂停和切歌。", true);
        AddPermission(permissionPanel, PermissionKeys.Keyboard, "键盘输入和快捷键", "能够向当前窗口输入文字并发送快捷键。", false);
        AddPermission(permissionPanel, PermissionKeys.Mouse, "鼠标移动、点击和滚动", "能够操作当前桌面，请谨慎开启。", false);
        AddPermission(permissionPanel, PermissionKeys.Screenshot, "截取屏幕", "截图可能包含聊天、账号或其他隐私信息。", false);
        layout.Controls.Add(permissionPanel, 0, 1);

        var note = new Label
        {
            Text = "修改权限会立即通知已连接的小智；正在执行的单次操作不会被中途更改。",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Anchor = AnchorStyles.Left
        };
        layout.Controls.Add(note, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateAppsTab()
    {
        var page = new TabPage("应用白名单") { Padding = new Padding(14), BackColor = Color.White };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.Controls.Add(new Label
        {
            Text = "小智只能操作这里列出且已勾选的应用。可自动检测第三方应用路径，双击一行可编辑。",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Color.FromArgb(80, 85, 95)
        }, 0, 0);

        _appsGrid.Dock = DockStyle.Fill;
        _appsGrid.AutoGenerateColumns = false;
        _appsGrid.AllowUserToAddRows = false;
        _appsGrid.AllowUserToDeleteRows = false;
        _appsGrid.MultiSelect = false;
        _appsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _appsGrid.RowHeadersVisible = false;
        _appsGrid.BackgroundColor = Color.White;
        _appsGrid.BorderStyle = BorderStyle.Fixed3D;
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
        layout.Controls.Add(_appsGrid, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var addButton = new Button { Text = "添加应用", Width = 100, Height = 32 };
        var editButton = new Button { Text = "编辑选中项", Width = 110, Height = 32 };
        var deleteButton = new Button { Text = "删除选中项", Width = 110, Height = 32 };
        var detectButton = new Button { Text = "自动检测路径", Width = 120, Height = 32 };
        addButton.Click += (_, _) => AddApp();
        editButton.Click += (_, _) => EditSelectedApp();
        deleteButton.Click += (_, _) => DeleteSelectedApp();
        detectButton.Click += (_, _) => AutoDetectAppPaths();
        buttons.Controls.Add(addButton);
        buttons.Controls.Add(editButton);
        buttons.Controls.Add(deleteButton);
        buttons.Controls.Add(detectButton);
        layout.Controls.Add(buttons, 0, 2);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateLogsTab()
    {
        var page = new TabPage("调用记录") { Padding = new Padding(14), BackColor = Color.White };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        _logBox.Dock = DockStyle.Fill;
        _logBox.ReadOnly = true;
        _logBox.BackColor = Color.FromArgb(26, 29, 35);
        _logBox.ForeColor = Color.FromArgb(220, 225, 232);
        _logBox.Font = new Font("Consolas", 9F);
        _logBox.BorderStyle = BorderStyle.None;
        layout.Controls.Add(_logBox, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };
        var clearButton = new Button { Text = "清空记录", Width = 100, Height = 30 };
        var exportButton = new Button { Text = "导出应用诊断", Width = 120, Height = 30 };
        clearButton.Click += (_, _) => _logBox.Clear();
        buttons.Controls.Add(clearButton);
        exportButton.Click += (_, _) => ExportAppDiagnostics();
        buttons.Controls.Add(exportButton);
        layout.Controls.Add(buttons, 0, 1);
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
            Padding = new Padding(4, 0, 0, 0)
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
    }

    private void ConnectButton_Click(object? sender, EventArgs eventArgs) => ConnectToEndpoint();

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
        _connectButton.Enabled = true;
    }

    private async void EmergencyButton_Click(object? sender, EventArgs eventArgs)
    {
        _emergencyStopped = true;
        foreach (var pair in _permissionBoxes)
        {
            pair.Value.Checked = pair.Key == PermissionKeys.SystemStatus;
        }
        SaveSettingsSafe();
        WriteLog("已触发紧急停止：控制权限全部关闭，MCP 连接正在断开。");
        await DisconnectAsync();
        MessageBox.Show(
            "已断开 MCP 连接，并关闭所有控制权限。\n重新使用前请在“权限”页面逐项开启。",
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
        foreach (var pair in _permissionBoxes)
        {
            _settings.Permissions[pair.Key] = pair.Value.Checked;
        }
        SaveSettingsSafe();
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
               && _settings.Permissions.TryGetValue(key, out var enabled)
               && enabled;
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
                EndpointConnectionState.Connected => Color.FromArgb(25, 145, 85),
                EndpointConnectionState.Connecting or EndpointConnectionState.Reconnecting => Color.FromArgb(210, 130, 20),
                _ => Color.FromArgb(115, 120, 130)
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
        if (_closingAfterStop)
        {
            return;
        }

        eventArgs.Cancel = true;
        SaveSettingsSafe();
        await _mcpClient.StopAsync();
        await _mcpClient.DisposeAsync();
        _closingAfterStop = true;
        Close();
    }

    private static Control Spacer(int width) => new Panel { Width = width, Height = 1 };

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
