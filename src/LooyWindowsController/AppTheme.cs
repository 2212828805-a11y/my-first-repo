using System.Drawing.Drawing2D;

namespace Looy.WindowsController;

internal static class AppTheme
{
    public static readonly Color Canvas = Color.FromArgb(244, 240, 233);
    public static readonly Color Surface = Color.FromArgb(255, 252, 247);
    public static readonly Color SurfaceMuted = Color.FromArgb(247, 242, 234);
    public static readonly Color Ink = Color.FromArgb(38, 35, 31);
    public static readonly Color Muted = Color.FromArgb(117, 109, 99);
    public static readonly Color Border = Color.FromArgb(222, 213, 201);
    public static readonly Color Accent = Color.FromArgb(44, 40, 35);
    public static readonly Color Gold = Color.FromArgb(194, 151, 94);
    public static readonly Color Success = Color.FromArgb(52, 126, 92);
    public static readonly Color Warning = Color.FromArgb(181, 117, 34);
    public static readonly Color Danger = Color.FromArgb(177, 75, 72);
    public static readonly Color LogSurface = Color.FromArgb(38, 34, 30);

    public static void StyleButton(Button button, ButtonKind kind = ButtonKind.Secondary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        button.FlatAppearance.BorderSize = kind == ButtonKind.Secondary ? 1 : 0;
        button.FlatAppearance.BorderColor = Border;
        button.BackColor = kind switch
        {
            ButtonKind.Primary => Accent,
            ButtonKind.Danger => Danger,
            _ => Surface
        };
        button.ForeColor = kind == ButtonKind.Secondary ? Ink : Color.White;
        button.FlatAppearance.MouseOverBackColor = kind switch
        {
            ButtonKind.Primary => Color.FromArgb(61, 56, 49),
            ButtonKind.Danger => Color.FromArgb(194, 88, 84),
            _ => SurfaceMuted
        };
        button.FlatAppearance.MouseDownBackColor = kind switch
        {
            ButtonKind.Primary => Color.FromArgb(29, 27, 24),
            ButtonKind.Danger => Color.FromArgb(150, 58, 56),
            _ => Color.FromArgb(236, 229, 219)
        };
    }

    public static void StyleTextBox(TextBox box)
    {
        box.BackColor = Color.White;
        box.ForeColor = Ink;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Microsoft YaHei UI", 10F);
    }

    public static Label SectionTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
        ForeColor = Ink,
        Margin = new Padding(0, 0, 0, 8)
    };
}

internal enum ButtonKind
{
    Primary,
    Secondary,
    Danger
}

internal sealed class WarmTabControl : TabControl
{
    public WarmTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(150, 42);
        Padding = new Point(18, 6);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnDrawItem(DrawItemEventArgs eventArgs)
    {
        var selected = eventArgs.Index == SelectedIndex;
        var bounds = GetTabRect(eventArgs.Index);
        using var background = new SolidBrush(selected ? AppTheme.Surface : AppTheme.Canvas);
        using var foreground = new SolidBrush(selected ? AppTheme.Ink : AppTheme.Muted);
        eventArgs.Graphics.FillRectangle(background, bounds);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            TabPages[eventArgs.Index].Text,
            Font,
            bounds,
            foreground.Color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (selected)
        {
            using var accent = new SolidBrush(AppTheme.Gold);
            eventArgs.Graphics.FillRectangle(accent, bounds.Left + 24, bounds.Bottom - 3, bounds.Width - 48, 3);
        }
    }
}

internal sealed class BrandMarkControl : Control
{
    public BrandMarkControl()
    {
        Size = new Size(56, 56);
        MinimumSize = Size;
        MaximumSize = Size;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRectangle(new Rectangle(1, 1, Width - 2, Height - 2), 16);
        using var background = new SolidBrush(AppTheme.Accent);
        eventArgs.Graphics.FillPath(background, path);

        using var signal = new Pen(AppTheme.Surface, 3.2F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        eventArgs.Graphics.DrawArc(signal, 12, 13, 32, 32, 205, 130);
        eventArgs.Graphics.DrawArc(signal, 18, 19, 20, 20, 205, 130);
        using var dot = new SolidBrush(AppTheme.Gold);
        eventArgs.Graphics.FillEllipse(dot, 24, 24, 8, 8);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
