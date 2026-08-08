namespace Looy.WindowsController;

internal enum InputAuthorizationPersistence
{
    None,
    Session,
    Always
}

internal sealed class InputAuthorizationForm : Form
{
    private readonly CheckBox _keyboardBox = new()
    {
        Text = "允许键盘输入和快捷键",
        AutoSize = true
    };

    private readonly CheckBox _mouseBox = new()
    {
        Text = "允许鼠标移动、点击和滚动",
        AutoSize = true
    };

    public InputAuthorizationPersistence Persistence { get; private set; }

    public IReadOnlyCollection<string> SelectedPermissions
    {
        get
        {
            var selected = new List<string>();
            if (_keyboardBox.Checked)
            {
                selected.Add(PermissionKeys.Keyboard);
            }
            if (_mouseBox.Checked)
            {
                selected.Add(PermissionKeys.Mouse);
            }
            return selected;
        }
    }

    public InputAuthorizationForm(string? requiredPermission, string reason)
    {
        Text = "键盘与鼠标授权";
        Width = 610;
        Height = 440;
        MinimumSize = new Size(610, 440);
        MaximumSize = new Size(610, 440);
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
            RowCount = 6,
            Padding = new Padding(28, 24, 28, 24),
            BackColor = AppTheme.Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        layout.Controls.Add(AppTheme.SectionTitle("路遥需要你的明确授权"), 0, 0);
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = $"本次请求：{reason}\n授权后，路遥可以向当前窗口输入内容或操作鼠标。你可以随时点击“立即停用”撤回授权。",
            ForeColor = AppTheme.Muted,
            AutoSize = false
        }, 0, 1);

        StylePermissionBox(_keyboardBox);
        StylePermissionBox(_mouseBox);
        _keyboardBox.Checked = requiredPermission is null || requiredPermission == PermissionKeys.Keyboard;
        _mouseBox.Checked = requiredPermission is null || requiredPermission == PermissionKeys.Mouse;
        if (requiredPermission == PermissionKeys.Keyboard)
        {
            _keyboardBox.Enabled = false;
        }
        if (requiredPermission == PermissionKeys.Mouse)
        {
            _mouseBox.Enabled = false;
        }
        layout.Controls.Add(_keyboardBox, 0, 2);
        layout.Controls.Add(_mouseBox, 0, 3);

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(0, 12, 0, 0),
            ForeColor = AppTheme.Warning,
            Text = WindowsInputAccess.IsElevated
                ? "当前已是管理员输入模式。请只在你能看到屏幕时授权；UAC 安全窗口仍必须由你本人确认。"
                : "当前是普通输入模式。若目标应用以管理员身份运行，请授权后在主窗口点击“管理员模式重启”。"
        }, 0, 4);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = AppTheme.Surface
        };
        var cancelButton = new Button { Text = "取消", Width = 88, Height = 36, DialogResult = DialogResult.Cancel };
        var alwaysButton = new Button { Text = "始终允许", Width = 104, Height = 36 };
        var sessionButton = new Button { Text = "仅本次连接", Width = 118, Height = 36 };
        AppTheme.StyleButton(cancelButton);
        AppTheme.StyleButton(alwaysButton);
        AppTheme.StyleButton(sessionButton, ButtonKind.Primary);
        sessionButton.Click += (_, _) => Complete(InputAuthorizationPersistence.Session);
        alwaysButton.Click += (_, _) => Complete(InputAuthorizationPersistence.Always);
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(alwaysButton);
        buttons.Controls.Add(sessionButton);
        layout.Controls.Add(buttons, 0, 5);

        Controls.Add(layout);
        CancelButton = cancelButton;
    }

    private void Complete(InputAuthorizationPersistence persistence)
    {
        if (SelectedPermissions.Count == 0)
        {
            MessageBox.Show("请至少选择一项权限。", "尚未选择权限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Persistence = persistence;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static void StylePermissionBox(CheckBox box)
    {
        box.Dock = DockStyle.Fill;
        box.BackColor = AppTheme.SurfaceMuted;
        box.ForeColor = AppTheme.Ink;
        box.FlatStyle = FlatStyle.Flat;
        box.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        box.Padding = new Padding(14, 0, 0, 0);
        box.Margin = new Padding(0, 4, 0, 4);
    }
}
