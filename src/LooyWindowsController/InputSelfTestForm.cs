namespace Looy.WindowsController;

internal sealed class InputSelfTestForm : Form
{
    private const string TestText = "路遥-KEYBOARD-OK";
    private const string OcrTestText = "LOOY SCREEN 12345";

    private readonly WindowsController _controller;
    private readonly TextBox _keyboardTarget = new();
    private readonly Button _mouseTarget = new()
    {
        Text = "鼠标测试区域",
        Width = 220,
        Height = 64,
        TabStop = false
    };
    private readonly Label _status = new()
    {
        AutoSize = true,
        Text = "正在准备检测……",
        Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold)
    };
    private readonly Label _details = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        ForeColor = AppTheme.Muted
    };
    private readonly Button _closeButton = new()
    {
        Text = "完成",
        Width = 96,
        Height = 36,
        Enabled = false
    };

    private bool _mouseClickObserved;

    public InputSelfTestForm(WindowsController controller)
    {
        _controller = controller;

        Text = "键鼠与屏幕识别真实检测";
        Width = 650;
        Height = 470;
        MinimumSize = new Size(650, 470);
        MaximumSize = new Size(650, 470);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = AppTheme.Canvas;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(28, 24, 28, 24),
            BackColor = AppTheme.Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        layout.Controls.Add(AppTheme.SectionTitle("检测键盘、鼠标与本机屏幕识别"), 0, 0);
        layout.Controls.Add(new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = AppTheme.Muted,
            Text = "检测会在下方输入框键入测试文字、移动鼠标并单击，再用 Windows OCR 读取本检测窗口。不会打开其他应用，截图不保存、不上传。"
        }, 0, 1);

        _keyboardTarget.Dock = DockStyle.Fill;
        _keyboardTarget.ReadOnly = false;
        _keyboardTarget.PlaceholderText = "键盘测试文字会自动出现在这里";
        _keyboardTarget.Margin = new Padding(0, 10, 0, 10);
        AppTheme.StyleTextBox(_keyboardTarget);
        layout.Controls.Add(_keyboardTarget, 0, 2);

        var mousePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.SurfaceMuted
        };
        _mouseTarget.Location = new Point(172, 10);
        AppTheme.StyleButton(_mouseTarget, ButtonKind.Primary);
        _mouseTarget.Click += (_, _) => _mouseClickObserved = true;
        mousePanel.Controls.Add(_mouseTarget);
        layout.Controls.Add(mousePanel, 0, 3);

        _status.ForeColor = AppTheme.Muted;
        _status.Anchor = AnchorStyles.Left;
        layout.Controls.Add(_status, 0, 4);
        layout.Controls.Add(_details, 0, 5);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = AppTheme.Surface
        };
        AppTheme.StyleButton(_closeButton);
        _closeButton.Click += (_, _) => Close();
        buttons.Controls.Add(_closeButton);
        layout.Controls.Add(buttons, 0, 6);

        Controls.Add(layout);
        AcceptButton = _closeButton;
        Shown += async (_, _) => await RunSelfTestAsync();
    }

    private async Task RunSelfTestAsync()
    {
        var originalCursor = Cursor.Position;
        ToolExecutionResult keyboardResult = default;
        ToolExecutionResult moveResult = default;
        ToolExecutionResult clickResult = default;
        var screenPassed = false;
        var screenMessage = "尚未检测";

        try
        {
            _status.Text = "正在检测键盘输入……";
            _keyboardTarget.Clear();
            ActiveControl = _keyboardTarget;
            _keyboardTarget.Focus();
            await Task.Delay(180);
            keyboardResult = _controller.TypeTextForSelfTest(TestText);
            await Task.Delay(220);
            var keyboardPassed = keyboardResult.Success
                                 && _keyboardTarget.Text.Equals(TestText, StringComparison.Ordinal);

            _status.Text = "正在检测鼠标移动与单击……";
            _mouseClickObserved = false;
            var target = _mouseTarget.PointToScreen(
                new Point(_mouseTarget.ClientSize.Width / 2, _mouseTarget.ClientSize.Height / 2));
            moveResult = _controller.MoveMouseForSelfTest(target.X, target.Y);
            await Task.Delay(160);
            clickResult = moveResult.Success
                ? _controller.ClickLeftForSelfTest()
                : ToolExecutionResult.Fail("鼠标未到达测试区域，因此没有执行单击。");
            await Task.Delay(260);
            var mousePassed = moveResult.Success && clickResult.Success && _mouseClickObserved;

            _status.Text = "正在检测本机屏幕文字识别……";
            _details.ForeColor = AppTheme.Ink;
            _details.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            _details.Text = $"屏幕识别测试文字：{OcrTestText}";
            await Task.Delay(220);
            try
            {
                var snapshot = await ScreenRecognitionService.InspectWindowAsync(Handle, 80, CancellationToken.None);
                var recognizedText = string.Concat(snapshot.Items.Select(item => item.Text));
                recognizedText = string.Concat(recognizedText.Where(character => !char.IsWhiteSpace(character)));
                screenPassed = recognizedText.Contains("LOOYSCREEN12345", StringComparison.OrdinalIgnoreCase);
                screenMessage = screenPassed
                    ? $"通过（{snapshot.RecognitionLanguage}，识别 {snapshot.Items.Count} 项）"
                    : $"未识别到测试文字（{snapshot.RecognitionLanguage}，识别 {snapshot.Items.Count} 项）";
            }
            catch (Exception exception)
            {
                screenMessage = exception.Message;
            }

            _details.Font = new Font("Microsoft YaHei UI", 9F);

            _status.Text = keyboardPassed && mousePassed && screenPassed
                ? "检测通过：键盘、鼠标和屏幕识别均正常"
                : "检测未通过：请查看下方具体结果";
            _status.ForeColor = keyboardPassed && mousePassed && screenPassed ? AppTheme.Success : AppTheme.Warning;
            _details.ForeColor = AppTheme.Muted;
            _details.Text = string.Join(
                Environment.NewLine,
                $"键盘：{(keyboardPassed ? "通过" : "未通过")}　{keyboardResult.Message}",
                $"鼠标移动：{(moveResult.Success ? "通过" : "未通过")}　{moveResult.Message}",
                $"鼠标单击：{(mousePassed ? "通过" : "未通过")}　{clickResult.Message}",
                $"屏幕识别：{(screenPassed ? "通过" : "未通过")}　{screenMessage}",
                string.Empty,
                keyboardPassed && mousePassed && screenPassed
                    ? "现在可以连接路遥，并在搜索后识别标题、按编号完成单击或双击。"
                    : "键鼠失败时可尝试“管理员模式重启”；识屏失败时请在 Windows 语言选项中安装当前语言的“基本输入”。锁屏、UAC 安全窗口和部分远程桌面会阻止操作。"
            );
        }
        catch (Exception exception)
        {
            _status.Text = "检测过程中发生错误";
            _status.ForeColor = AppTheme.Warning;
            _details.Text = exception.Message;
        }
        finally
        {
            _controller.MoveMouseForSelfTest(originalCursor.X, originalCursor.Y);
            _closeButton.Enabled = true;
            _closeButton.Focus();
        }
    }
}
