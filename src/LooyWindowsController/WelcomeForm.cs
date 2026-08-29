using System.Diagnostics;

namespace Looy.WindowsController;

internal sealed class WelcomeForm : Form
{
    internal const string WelcomeText = "感谢您对「路遥智伴」的支持";
    internal const string CreatorText = "创作人：阿雾";
    internal const string DouyinText = "抖音账号：54530767529";

    private const int FadeInMilliseconds = 220;
    private const int DisplayMilliseconds = 3200;
    private const int FadeOutMilliseconds = 260;

    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 16 };
    private readonly Stopwatch _lifetime = new();
    private bool _completed;

    public WelcomeForm()
    {
        Text = "欢迎使用路遥智控";
        Width = 640;
        Height = 470;
        MinimumSize = new Size(640, 470);
        MaximumSize = new Size(640, 470);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ShowInTaskbar = true;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = AppTheme.Canvas;
        Opacity = 0D;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(46, 28, 46, 24),
            BackColor = AppTheme.Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var logoLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = AppTheme.Surface
        };
        logoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        logoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        logoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        logoLayout.Controls.Add(new BrandMarkControl { Anchor = AnchorStyles.None }, 1, 0);
        root.Controls.Add(logoLayout, 0, 0);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = WelcomeText,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold),
            ForeColor = AppTheme.Ink
        }, 0, 1);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "因为有你的喜欢，路遥才能一直陪伴下去。",
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Microsoft YaHei UI", 10F),
            ForeColor = AppTheme.Muted,
            Padding = new Padding(0, 10, 0, 0)
        }, 0, 2);

        var creatorCard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(20, 12, 20, 12),
            Margin = new Padding(36, 0, 36, 14),
            BackColor = AppTheme.SurfaceMuted
        };
        creatorCard.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        creatorCard.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        creatorCard.Controls.Add(CreateInfoLabel(CreatorText, true), 0, 0);
        creatorCard.Controls.Add(CreateInfoLabel(DouyinText, false), 0, 1);
        root.Controls.Add(creatorCard, 0, 3);

        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = AppTheme.Surface
        };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var enterButton = new Button
        {
            Text = "进入应用",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 8)
        };
        AppTheme.StyleButton(enterButton, ButtonKind.Primary);
        enterButton.Click += (_, _) => Complete();
        buttonRow.Controls.Add(enterButton, 1, 0);
        root.Controls.Add(buttonRow, 0, 4);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "即将自动进入路遥智控",
            TextAlign = ContentAlignment.BottomCenter,
            ForeColor = AppTheme.Muted,
            Font = new Font("Microsoft YaHei UI", 8.5F)
        }, 0, 5);

        Controls.Add(root);
        AcceptButton = enterButton;
        _animationTimer.Tick += AnimationTimer_Tick;
    }

    internal static bool RunComponentSelfTest() =>
        WelcomeText == "感谢您对「路遥智伴」的支持"
        && CreatorText == "创作人：阿雾"
        && DouyinText == "抖音账号：54530767529"
        && DisplayMilliseconds >= 2500;

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        _lifetime.Restart();
        _animationTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs eventArgs)
    {
        _animationTimer.Stop();
        _animationTimer.Dispose();
        _lifetime.Stop();
        base.OnFormClosed(eventArgs);
    }

    private static Label CreateInfoLabel(string text, bool bold) => new()
    {
        Dock = DockStyle.Fill,
        Text = text,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Microsoft YaHei UI", 10F, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = AppTheme.Ink
    };

    private void AnimationTimer_Tick(object? sender, EventArgs eventArgs)
    {
        var elapsed = _lifetime.Elapsed.TotalMilliseconds;
        if (elapsed < FadeInMilliseconds)
        {
            Opacity = Math.Clamp(elapsed / FadeInMilliseconds, 0.12D, 1D);
            return;
        }

        if (elapsed < DisplayMilliseconds)
        {
            Opacity = 1D;
            return;
        }

        var fadeOutProgress = (elapsed - DisplayMilliseconds) / FadeOutMilliseconds;
        if (fadeOutProgress < 1D)
        {
            Opacity = Math.Clamp(1D - fadeOutProgress, 0D, 1D);
            return;
        }

        Complete();
    }

    private void Complete()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _animationTimer.Stop();
        DialogResult = DialogResult.OK;
        Close();
    }
}
