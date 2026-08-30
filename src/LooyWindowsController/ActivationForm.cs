using System.Diagnostics;

namespace Looy.WindowsController;

internal sealed class ActivationForm : Form
{
    private readonly DeviceLicenseClient _licenseClient;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Label _statusLabel = new();
    private readonly Panel _activationPanel = new();
    private readonly TextBox _activationCodeBox = new();
    private readonly CheckBox _consentBox = new();
    private readonly Button _activateButton = new() { Text = "绑定这台电脑" };
    private readonly Button _retryButton = new() { Text = "重新校验" };
    private readonly Button _exitButton = new() { Text = "退出应用" };
    private bool _busy;

    public ActivationForm(DeviceLicenseClient licenseClient)
    {
        _licenseClient = licenseClient;
        BuildWindow();
        WireEvents();
    }

    private void BuildWindow()
    {
        Text = "路遥智伴 · 设备绑定";
        Width = 650;
        Height = 600;
        MinimumSize = new Size(650, 600);
        MaximumSize = new Size(650, 600);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = AppTheme.Canvas;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(42, 28, 42, 26),
            BackColor = AppTheme.Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        var markRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = AppTheme.Surface
        };
        markRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        markRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        markRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        markRow.Controls.Add(new BrandMarkControl { Anchor = AnchorStyles.None }, 1, 0);
        root.Controls.Add(markRow, 0, 0);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "绑定这台电脑",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold),
            ForeColor = AppTheme.Ink
        }, 0, 1);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = $"设备编号：{_licenseClient.DeviceIdHint}",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = AppTheme.Muted
        }, 0, 2);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Text = "正在校验设备授权……";
        _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        _statusLabel.ForeColor = AppTheme.Warning;
        _statusLabel.BackColor = AppTheme.SurfaceMuted;
        _statusLabel.Padding = new Padding(12, 6, 12, 6);
        root.Controls.Add(_statusLabel, 0, 3);

        BuildActivationPanel();
        root.Controls.Add(_activationPanel, 0, 4);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0),
            BackColor = AppTheme.Surface
        };
        _activateButton.Width = 148;
        _activateButton.Height = 38;
        _retryButton.Width = 112;
        _retryButton.Height = 38;
        _exitButton.Width = 104;
        _exitButton.Height = 38;
        AppTheme.StyleButton(_activateButton, ButtonKind.Primary);
        AppTheme.StyleButton(_retryButton);
        AppTheme.StyleButton(_exitButton);
        buttonRow.Controls.Add(_activateButton);
        buttonRow.Controls.Add(_retryButton);
        buttonRow.Controls.Add(_exitButton);
        root.Controls.Add(buttonRow, 0, 5);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "设备私钥使用 Windows 当前账号加密，仅保存在本机。",
            TextAlign = ContentAlignment.BottomCenter,
            ForeColor = AppTheme.Muted,
            Font = new Font("Microsoft YaHei UI", 8.5F)
        }, 0, 6);

        Controls.Add(root);
        AcceptButton = _activateButton;
    }

    private void BuildActivationPanel()
    {
        _activationPanel.Dock = DockStyle.Fill;
        _activationPanel.BackColor = AppTheme.Surface;
        _activationPanel.Visible = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(24, 14, 24, 4),
            BackColor = AppTheme.Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = "激活码",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = AppTheme.Ink
        }, 0, 0);

        _activationCodeBox.Dock = DockStyle.Fill;
        _activationCodeBox.CharacterCasing = CharacterCasing.Upper;
        _activationCodeBox.PlaceholderText = "LY-XXXXX-XXXXX-XXXXX";
        _activationCodeBox.MaxLength = 64;
        _activationCodeBox.Margin = new Padding(0, 4, 0, 8);
        AppTheme.StyleTextBox(_activationCodeBox);
        layout.Controls.Add(_activationCodeBox, 0, 1);

        _consentBox.Dock = DockStyle.Fill;
        _consentBox.Text = "我已阅读并同意隐私与数据说明，允许上传设备随机标识、应用版本、最后在线时间、公网 IP 与城市级近似位置。";
        _consentBox.ForeColor = AppTheme.Ink;
        _consentBox.FlatStyle = FlatStyle.Flat;
        layout.Controls.Add(_consentBox, 0, 2);

        var privacyLink = new LinkLabel
        {
            Text = "查看隐私与数据说明",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            LinkColor = AppTheme.Accent,
            ActiveLinkColor = AppTheme.Gold
        };
        privacyLink.LinkClicked += (_, _) => OpenUrl(DeviceLicenseClient.PrivacyUrl);
        layout.Controls.Add(privacyLink, 0, 3);

        var adminLink = new LinkLabel
        {
            Text = "管理员可在后台生成激活码",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            LinkColor = AppTheme.Accent,
            ActiveLinkColor = AppTheme.Gold
        };
        adminLink.LinkClicked += (_, _) => OpenUrl(DeviceLicenseClient.AdminUrl);
        layout.Controls.Add(adminLink, 0, 4);
        _activationPanel.Controls.Add(layout);
    }

    private void WireEvents()
    {
        Shown += async (_, _) => await ValidateExistingAsync();
        _activateButton.Click += async (_, _) => await ActivateAsync();
        _retryButton.Click += async (_, _) => await ValidateExistingAsync();
        _exitButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        FormClosing += (_, _) => _lifetime.Cancel();
    }

    private async Task ValidateExistingAsync()
    {
        if (_busy)
        {
            return;
        }
        SetBusy(true, "正在校验设备授权……");
        try
        {
            var result = await _licenseClient.CheckAsync(
                allowOfflineGrace: true,
                _lifetime.Token);
            if (result.Allowed)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            ShowResult(result);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The user closed the form.
        }
        catch (Exception exception)
        {
            ShowFailure($"设备授权校验失败：{exception.Message}", showActivation: true);
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                SetBusy(false);
            }
        }
    }

    private async Task ActivateAsync()
    {
        if (_busy)
        {
            return;
        }
        if (!_consentBox.Checked)
        {
            ShowFailure("请先阅读并勾选同意隐私与数据说明。", showActivation: true);
            return;
        }

        SetBusy(true, "正在绑定这台电脑……");
        try
        {
            var result = await _licenseClient.ActivateAsync(
                _activationCodeBox.Text,
                _consentBox.Checked,
                _lifetime.Token);
            if (result.Allowed)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            ShowResult(result);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The user closed the form.
        }
        catch (HttpRequestException exception)
        {
            ShowFailure(
                $"无法连接公网设备授权服务。\n请先用浏览器打开状态页：{DeviceLicenseClient.ServiceStatusUrl}\n网络错误：{exception.Message}",
                showActivation: true);
        }
        catch (TaskCanceledException)
        {
            ShowFailure(
                $"连接公网设备授权服务超时。\n请先用浏览器打开状态页：{DeviceLicenseClient.ServiceStatusUrl}",
                showActivation: true);
        }
        catch (Exception exception)
        {
            ShowFailure($"绑定失败：{exception.Message}", showActivation: true);
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                SetBusy(false);
            }
        }
    }

    private void ShowResult(DeviceLicenseCheckResult result)
    {
        ShowFailure(result.Message, result.RequiresActivation);
    }

    private void ShowFailure(string message, bool showActivation)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = AppTheme.Danger;
        _activationPanel.Visible = showActivation;
        _activateButton.Visible = showActivation;
        _retryButton.Visible = true;
        if (showActivation)
        {
            _activationCodeBox.Focus();
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        _activationCodeBox.Enabled = !busy;
        _consentBox.Enabled = !busy;
        _activateButton.Enabled = !busy;
        _retryButton.Enabled = !busy;
        _exitButton.Enabled = !busy;
        if (!string.IsNullOrWhiteSpace(message))
        {
            _statusLabel.Text = message;
            _statusLabel.ForeColor = AppTheme.Warning;
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"无法打开网页：{exception.Message}\n\n{url}",
                "打开失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
