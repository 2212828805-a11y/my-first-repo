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
    private readonly CheckBox _sensitivePermissionBox = new();
    private readonly Button _activateButton = new() { Text = "绑定这台电脑" };
    private readonly Button _retryButton = new() { Text = "重新校验" };
    private readonly Button _exitButton = new() { Text = "退出应用" };
    private bool _busy;
    private bool _agreementViewed;

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
        Height = 720;
        MinimumSize = new Size(650, 720);
        MaximumSize = new Size(650, 720);
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = "激活码（首次绑定必填；已绑定设备重新同意无需填写）",
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
        _consentBox.Text = $"我已阅读并同意《用户协议与隐私说明》（{DeviceLicenseClient.ConsentVersion}）。";
        _consentBox.ForeColor = AppTheme.Ink;
        _consentBox.FlatStyle = FlatStyle.Flat;
        layout.Controls.Add(_consentBox, 0, 2);

        _sensitivePermissionBox.Dock = DockStyle.Fill;
        _sensitivePermissionBox.Text = "我特别同意：应用可按控制指令识别前台画面，并模拟键盘、鼠标和切换应用；识别出的可见文字与执行结果可能返回给我配置的 MCP 控制连接。聊天发送仍需后续单独“确认发送”。";
        _sensitivePermissionBox.ForeColor = AppTheme.Ink;
        _sensitivePermissionBox.FlatStyle = FlatStyle.Flat;
        layout.Controls.Add(_sensitivePermissionBox, 0, 3);

        var privacyLink = new LinkLabel
        {
            Text = "打开完整协议（必读，应用内可离线查看）",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            LinkColor = AppTheme.Accent,
            ActiveLinkColor = AppTheme.Gold
        };
        privacyLink.LinkClicked += (_, _) => ShowAgreement();
        layout.Controls.Add(privacyLink, 0, 4);

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
        if (!_agreementViewed || !_consentBox.Checked)
        {
            ShowFailure("请先打开完整协议，阅读后点击“我已阅读并同意”，并勾选协议同意。", showActivation: true);
            return;
        }
        if (!_sensitivePermissionBox.Checked)
        {
            ShowFailure("请单独勾选敏感权限特别授权；不同意时不会启用屏幕识别与键盘鼠标控制。", showActivation: true);
            return;
        }

        SetBusy(true, "正在绑定这台电脑……");
        try
        {
            var result = _licenseClient.HasStoredLicense && !_licenseClient.ConsentIsCurrent
                ? await _licenseClient.AcceptUpdatedConsentAsync(
                    _consentBox.Checked,
                    _lifetime.Token)
                : await _licenseClient.ActivateAsync(
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
        _sensitivePermissionBox.Enabled = !busy;
        _activateButton.Enabled = !busy;
        _retryButton.Enabled = !busy;
        _exitButton.Enabled = !busy;
        if (!string.IsNullOrWhiteSpace(message))
        {
            _statusLabel.Text = message;
            _statusLabel.ForeColor = AppTheme.Warning;
        }
    }

    private void ShowAgreement()
    {
        using var agreement = new UserAgreementForm(DeviceLicenseClient.PrivacyUrl);
        if (agreement.ShowDialog(this) == DialogResult.OK)
        {
            _agreementViewed = true;
            _consentBox.Checked = true;
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


internal sealed class UserAgreementForm : Form
{
    public UserAgreementForm(string onlineUrl)
    {
        Text = "路遥智伴 · 用户协议与敏感权限说明";
        Width = 820;
        Height = 720;
        MinimumSize = new Size(680, 560);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = AppTheme.Canvas;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(24),
            BackColor = AppTheme.Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "用户协议、隐私与敏感权限说明",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold),
            ForeColor = AppTheme.Ink
        }, 0, 0);

        var agreementText = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            DetectUrls = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = AppTheme.Ink,
            Font = new Font("Microsoft YaHei UI", 10F),
            Text = BuildAgreementText(),
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        root.Controls.Add(agreementText, 0, 1);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "特别提醒：同意协议不等于自动开启所有控制能力。激活页还会要求你单独勾选敏感权限；键盘和鼠标首次使用时仍会再次弹窗，可选择仅本次、始终允许或拒绝。",
            ForeColor = AppTheme.Warning,
            Padding = new Padding(0, 12, 0, 6)
        }, 0, 2);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = AppTheme.Surface
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));

        var onlineLink = new LinkLabel
        {
            Text = "在浏览器查看在线版本",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            LinkColor = AppTheme.Accent,
            ActiveLinkColor = AppTheme.Gold
        };
        onlineLink.LinkClicked += (_, _) => OpenOnlineUrl(onlineUrl);
        footer.Controls.Add(onlineLink, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var agreeButton = new Button
        {
            Text = "我已阅读并同意",
            Width = 150,
            Height = 36,
            DialogResult = DialogResult.OK
        };
        var declineButton = new Button
        {
            Text = "不同意并返回",
            Width = 126,
            Height = 36,
            DialogResult = DialogResult.Cancel
        };
        AppTheme.StyleButton(agreeButton, ButtonKind.Primary);
        AppTheme.StyleButton(declineButton);
        buttons.Controls.Add(agreeButton);
        buttons.Controls.Add(declineButton);
        footer.Controls.Add(buttons, 1, 0);
        root.Controls.Add(footer, 0, 3);

        Controls.Add(root);
        AcceptButton = agreeButton;
        CancelButton = declineButton;
    }

    private static string BuildAgreementText() => $"""
生效版本：{DeviceLicenseClient.ConsentVersion}
产品：路遥智伴
创作人：阿雾（抖音账号：54530767529）

一、服务与适用范围
路遥智伴是在用户自有或已获合法授权的 Windows 电脑上运行的辅助控制应用。应用可以连接由用户配置的 MCP 控制端，根据收到的指令打开白名单软件、识别前台画面文字，并执行已授权的键盘、鼠标、媒体或系统操作。禁止用于访问无权控制的设备、账号或数据。

二、激活码与管理后台
激活码绑定本机生成的独立公钥标识。管理员可单独启用、封禁设备，设置到期时间、永久授权或收费状态。管理后台控制的是本应用能否继续使用，不能仅凭激活码直接查看屏幕、读取文件或远程操作键盘鼠标。0.7.4 及以上版本采用严格在线授权：保持联网时，管理员封禁通常会在约 5 秒内生效并关闭应用；不再提供离线使用宽限，无法联网校验时应用会停止。设备已经断网时无法接收状态，恢复联网后会在首次校验时停用。

三、设备授权服务处理的数据
激活与心跳会提交随机设备公钥标识、应用版本、授权状态、最后在线时间和同意版本。公网网关会提供公网 IP，以及由 IP 推断的国家、省级地区和城市级近似位置；这不是 GPS 精确定位。数据用于设备绑定、授权校验、封禁、收费控制、安全风控和故障支持。

四、敏感权限特别说明
• 屏幕捕获与 OCR：按指令截取前台窗口或屏幕并识别可见文字，画面可能含账号、聊天或其他敏感信息。
• 键盘与鼠标自动化：向当前窗口输入文字和快捷键，并移动、点击或滚动鼠标；界面变化可能造成误操作。
• 应用与系统操作：切换或启动白名单应用、控制媒体，并在单独确认后执行消息发送或高影响系统动作。
• 联网控制连接：与用户配置的 MCP 地址连接，接收工具调用，并返回识别文字和执行结果。
• 本机保存与管理员模式：保存加密设置、设备凭据和必要日志；需要控制高权限程序时，仅由用户主动选择管理员模式重启，UAC 仍须用户本人确认。

五、屏幕文字与控制连接的数据流向
屏幕 OCR 在本机完成，临时截图通常只在内存中处理；“保存截图”工具只有在该功能被启用并被调用时才写入本机数据目录。为了让当前 MCP 控制端判断下一步，识别出的可见文字、窗口信息和工具执行结果可能通过你配置的连接返回。设备授权后台本身不接收截图、OCR 文字、聊天内容、键盘逐键记录或鼠标轨迹。请只连接可信 MCP 地址，并避免在显示密码、验证码、支付信息或私密聊天时使用识别功能。

六、重要操作保护
QQ、微信消息先准备联系人和草稿，只有用户在后续单独明确发出“确认发送”指令后才发送；确认编号会过期且不能重复使用。关机、重启等高影响操作也需单独确认。自动识别可能因界面变化产生偏差，请在支付、删除、发布、发送或系统设置等操作前核对画面。

七、保存与安全
设备私钥由 Windows 当前用户加密并保存在本机。激活码在后台只保存不可逆摘要，完整激活码仅在创建时显示一次。后台保存设备授权记录及最近一次 IP、城市级近似位置，不建立连续定位轨迹。请妥善保护电脑账号、激活码和 MCP 地址。

八、选择与撤回
你必须分别确认协议同意和敏感权限特别授权才能完成激活。激活后仍可在应用中停用键盘、鼠标等权限、断开 MCP 地址、退出或卸载应用，也可联系管理员封禁设备。撤回会使相应功能不可用。

九、收费与更新
收费规则由管理员后台开关控制；接入实际支付平台前不会自动扣费。数据范围、敏感权限或控制方式发生重要变化时，应用会更新同意版本并要求重新阅读确认，不会静默代替用户同意。

十、联系
对授权、数据处理或权限撤回有疑问，请通过创作人公开渠道联系。
""";

    private static void OpenOnlineUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"无法打开在线协议：{exception.Message}\n\n{url}",
                "打开失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
