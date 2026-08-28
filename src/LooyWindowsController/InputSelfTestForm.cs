namespace Looy.WindowsController;

internal sealed class InputSelfTestForm : Form
{
    private const string TestText = "路遥-KEYBOARD-OK";

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

        Text = "键盘与鼠标真实检测";
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

        layout.Controls.Add(AppTheme.SectionTitle("检测真实键盘与鼠标输入"), 0, 0);
        layout.Controls.Add(new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = AppTheme.Muted,
            Text = "检测会在下方输入框键入一段测试文字，并把鼠标移动到测试区域完成一次单击。不会打开其他应用，也不会读取你的内容。"
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

            _status.Text = keyboardPassed && mousePassed
                ? "检测通过：键盘和鼠标均可正常使用"
                : "检测未通过：请查看下方具体结果";
            _status.ForeColor = keyboardPassed && mousePassed ? AppTheme.Success : AppTheme.Warning;
            _details.Text = string.Join(
                Environment.NewLine,
                $"键盘：{(keyboardPassed ? "通过" : "未通过")}　{keyboardResult.Message}",
                $"鼠标移动：{(moveResult.Success ? "通过" : "未通过")}　{moveResult.Message}",
                $"鼠标单击：{(mousePassed ? "通过" : "未通过")}　{clickResult.Message}",
                string.Empty,
                keyboardPassed && mousePassed
                    ? "现在可以连接路遥并直接使用键盘、鼠标和应用内操作。"
                    : "若程序提示目标窗口权限更高，请回到“授权管理”点击“管理员模式重启”，然后再次检测。锁屏、UAC 安全窗口和部分远程桌面会阻止输入。"
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
