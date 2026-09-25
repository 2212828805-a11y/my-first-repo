using System.Text.RegularExpressions;

namespace Looy.WindowsController;

internal sealed class AppEditorForm : Form
{
    private static readonly Regex AliasPattern = new("^[a-z0-9][a-z0-9_.-]{0,47}$", RegexOptions.Compiled);
    private readonly TextBox _aliasBox = new();
    private readonly TextBox _displayNameBox = new();
    private readonly TextBox _targetBox = new();
    private readonly CheckBox _enabledBox = new() { Text = "允许路遥使用此应用", AutoSize = true };

    public AppEntry? Result { get; private set; }

    public AppEditorForm(AppEntry? existing = null)
    {
        Text = existing is null ? "添加可用应用" : "编辑可用应用";
        Width = 560;
        Height = 330;
        MinimumSize = new Size(520, 320);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = AppTheme.Canvas;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 6,
            BackColor = AppTheme.Surface,
            Margin = new Padding(18)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        AddField(layout, "英文别名", _aliasBox, 0);
        AddField(layout, "显示名称", _displayNameBox, 1);
        AddField(layout, "程序或协议", _targetBox, 2);
        AppTheme.StyleTextBox(_aliasBox);
        AppTheme.StyleTextBox(_displayNameBox);
        AppTheme.StyleTextBox(_targetBox);
        _enabledBox.ForeColor = AppTheme.Ink;
        _enabledBox.BackColor = AppTheme.Surface;
        _enabledBox.FlatStyle = FlatStyle.Flat;
        layout.Controls.Add(_enabledBox, 1, 3);

        var hint = new Label
        {
            AutoSize = true,
            ForeColor = AppTheme.Muted,
            Text = "示例：notepad.exe、C:\\Apps\\Example.exe、ms-settings:\n英文别名只能使用小写字母、数字、点、横线和下划线。"
        };
        layout.Controls.Add(hint, 1, 4);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var saveButton = new Button { Text = "保存", Width = 90, Height = 32 };
        var cancelButton = new Button { Text = "取消", Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
        AppTheme.StyleButton(saveButton, ButtonKind.Primary);
        AppTheme.StyleButton(cancelButton);
        saveButton.Click += SaveButton_Click;
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);
        layout.Controls.Add(buttons, 0, 5);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        if (existing is not null)
        {
            _aliasBox.Text = existing.Alias;
            _displayNameBox.Text = existing.DisplayName;
            _targetBox.Text = existing.Target;
            _enabledBox.Checked = existing.Enabled;
        }
        else
        {
            _enabledBox.Checked = true;
        }
    }

    private static void AddField(TableLayoutPanel layout, string labelText, Control field, int row)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = AppTheme.Ink
        };
        field.Dock = DockStyle.Fill;
        field.Margin = new Padding(3, 5, 3, 5);
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(field, 1, row);
    }

    private void SaveButton_Click(object? sender, EventArgs eventArgs)
    {
        var alias = _aliasBox.Text.Trim().ToLowerInvariant();
        var displayName = _displayNameBox.Text.Trim();
        var target = _targetBox.Text.Trim();

        if (!AliasPattern.IsMatch(alias))
        {
            MessageBox.Show("英文别名格式不正确。", "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _aliasBox.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(displayName))
        {
            MessageBox.Show("请填写显示名称。", "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _displayNameBox.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(target))
        {
            MessageBox.Show("请填写程序路径、可执行文件名或系统协议。", "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _targetBox.Focus();
            return;
        }

        Result = new AppEntry
        {
            Alias = alias,
            DisplayName = displayName,
            Target = target,
            Enabled = _enabledBox.Checked
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
