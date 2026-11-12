using System.Globalization;
using Nba2k27Editor.Core.Gs;

namespace Nba2k27Editor.App;

/// <summary>
/// Native GS menus. Tab one is the general 70-field GS table with journaled edits and presets;
/// tab two is the five observed menu slider pairs, where only the verified CPU three-point
/// values 56 and 57 may be written.
/// </summary>
public sealed class GsForm : Form
{
    private readonly NativeGeneralGsSession _session = new();
    private readonly NativeMenuGsSession _legacy = new();
    private readonly TabControl _tabs = new();
    private readonly DataGridView _grid = new();
    private readonly DataGridView _legacyGrid = new();
    private readonly ToolStripStatusLabel _status = new();
    private readonly Label _legacyNote = new();
    private readonly Label _legacyState = new();
    private readonly ComboBox _legacyValue = new();
    private readonly Button _connect = new() { Text = "连接" };
    private readonly Button _apply = new() { Text = "应用修改" };
    private readonly Button _undo = new() { Text = "撤销" };
    private readonly Button _load = new() { Text = "导入预设" };
    private readonly Button _save = new() { Text = "导出预设" };
    private readonly Button _legacyConnect = new() { Text = "连接" };
    private readonly Button _legacyApply = new() { Text = "写入 56/57" };
    private readonly Button _legacyUndo = new() { Text = "撤销" };

    private GeneralGsSnapshot? _snapshot;
    private LegacyGsSnapshot? _legacySnapshot;
    private readonly Dictionary<int, GsField> _rows = new();
    private readonly Dictionary<string, string> _original = new(StringComparer.Ordinal);

    public GsForm()
    {
        Text = "GS 菜单（MyNBA 参数）";
        Width = 900;
        Height = 700;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9F);
        Result = "未连接";

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(6) };
        _connect.Click += (_, _) => Connect();
        _apply.Click += (_, _) => ApplyChanges();
        _undo.Click += (_, _) => Undo();
        _load.Click += (_, _) => LoadPreset();
        _save.Click += (_, _) => SavePreset();
        bar.Controls.AddRange(new Control[] { _connect, _apply, _undo, _load, _save });

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "group", HeaderText = "分组", ReadOnly = true, FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "label", HeaderText = "字段", ReadOnly = true, FillWeight = 50 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "user", HeaderText = "用户", FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "cpu", HeaderText = "电脑", FillWeight = 16 });

        var generalPage = new TabPage("通用 GS（70 项）") { Padding = new Padding(4) };
        generalPage.Controls.Add(_grid);

        _tabs.Dock = DockStyle.Fill;
        _tabs.TabPages.Add(generalPage);
        _tabs.TabPages.Add(BuildLegacyPage());

        var strip = new StatusStrip { Dock = DockStyle.Fill, SizingGrip = false };
        _status.Spring = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Text = "未连接";
        strip.Items.Add(_status);

        root.Controls.Add(bar, 0, 0);
        root.Controls.Add(_tabs, 0, 1);
        root.Controls.Add(strip, 0, 2);
        Controls.Add(root);

        _apply.Enabled = false;
        _undo.Enabled = false;
        _load.Enabled = false;
        _save.Enabled = false;
        _legacyApply.Enabled = false;
        _legacyUndo.Enabled = false;
    }

    public string Result { get; private set; }
    public int TabCount => _tabs.TabPages.Count;
    public int LegacyFieldCount => NativeMenuGs.Fields.Length;
    public IReadOnlyList<int> LegacyTestedValues => NativeMenuGs.TestedValues;
    public string LegacyTarget => NativeMenuGs.TargetField;

    private TabPage BuildLegacyPage()
    {
        var page = new TabPage("菜单实测 5 项") { Padding = new Padding(4) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _legacyNote.AutoSize = true;
        _legacyNote.MaximumSize = new Size(840, 0);
        _legacyNote.Padding = new Padding(6, 6, 6, 2);
        // The honest boundary: only the CPU three-point pair has been checked against the menu.
        _legacyNote.Text =
            "这 5 对数值直接读自内存，尚未与当前游戏菜单逐项核对；只有“电脑·三分”的 56 / 57 "
            + "两个值经过菜单验证可以写入，且两个内存块必须只在该处不同。写入后请在游戏内确认，"
            + "不要在未核对的情况下保存联盟设置。";

        _legacyState.AutoSize = true;
        _legacyState.Padding = new Padding(6, 0, 6, 4);
        _legacyState.Text = "未连接";

        _legacyGrid.Dock = DockStyle.Fill;
        _legacyGrid.AllowUserToAddRows = false;
        _legacyGrid.AllowUserToDeleteRows = false;
        _legacyGrid.AllowUserToResizeRows = false;
        _legacyGrid.RowHeadersVisible = false;
        _legacyGrid.ReadOnly = true;
        _legacyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _legacyGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _legacyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "field", HeaderText = "字段", FillWeight = 50 });
        _legacyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "user", HeaderText = "用户", FillWeight = 25 });
        _legacyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "cpu", HeaderText = "电脑", FillWeight = 25 });

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(4) };
        _legacyConnect.Click += (_, _) => ConnectLegacy();
        _legacyApply.Click += (_, _) => ApplyLegacy();
        _legacyUndo.Click += (_, _) => UndoLegacy();
        _legacyValue.DropDownStyle = ComboBoxStyle.DropDownList;
        _legacyValue.Width = 80;
        foreach (int value in NativeMenuGs.TestedValues)
        {
            _legacyValue.Items.Add(value);
        }
        _legacyValue.SelectedIndex = 0;
        bar.Controls.AddRange(new Control[]
        {
            _legacyConnect,
            new Label { Text = "电脑·三分 =", AutoSize = true, Padding = new Padding(8, 6, 0, 0) },
            _legacyValue,
            _legacyApply,
            _legacyUndo,
        });

        layout.Controls.Add(_legacyNote, 0, 0);
        layout.Controls.Add(_legacyState, 0, 1);
        layout.Controls.Add(_legacyGrid, 0, 2);
        layout.Controls.Add(bar, 0, 3);
        page.Controls.Add(layout);
        return page;
    }

    private void Connect()
    {
        try
        {
            UseWaitCursor = true;
            _snapshot = _session.Connect();
            FillGrid();
            _apply.Enabled = true;
            _undo.Enabled = true;
            _load.Enabled = true;
            _save.Enabled = true;
            Result = $"已连接 GS：{_snapshot.Values.Count} 个值";
            _status.Text = Result;
        }
        catch (Exception exc)
        {
            Result = $"GS 连接失败：{exc.Message}";
            _status.Text = Result;
            MessageBox.Show(this, exc.Message, "GS 连接失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void FillGrid()
    {
        if (_snapshot is null)
        {
            return;
        }
        _grid.Rows.Clear();
        _rows.Clear();
        _original.Clear();
        foreach (var field in GsPresets.Catalog)
        {
            string userKey = $"user.{field.Id}";
            string cpuKey = $"cpu.{field.Id}";
            if (!_snapshot.Values.TryGetValue(userKey, out int user)
                || !_snapshot.Values.TryGetValue(cpuKey, out int cpu))
            {
                continue;
            }
            int index = _grid.Rows.Add(GsPresets.Groups[field.Group], field.Label, user, cpu);
            _rows[index] = field;
            _original[userKey] = user.ToString(CultureInfo.InvariantCulture);
            _original[cpuKey] = cpu.ToString(CultureInfo.InvariantCulture);
        }
    }

    private void ApplyChanges()
    {
        if (_snapshot is null)
        {
            return;
        }
        var changes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in _rows)
        {
            var row = _grid.Rows[pair.Key];
            foreach (var (column, side) in new[] { ("user", "user"), ("cpu", "cpu") })
            {
                string key = $"{side}.{pair.Value.Id}";
                string current = Convert.ToString(row.Cells[column].Value, CultureInfo.InvariantCulture) ?? string.Empty;
                if (_original.TryGetValue(key, out string? before) && before == current)
                {
                    continue;
                }
                if (!int.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    _status.Text = $"{pair.Value.Label}（{side}）不是整数：{current}";
                    return;
                }
                changes[key] = value;
            }
        }
        if (changes.Count == 0)
        {
            _status.Text = "没有修改";
            return;
        }
        try
        {
            UseWaitCursor = true;
            var fresh = _session.Apply(_snapshot, changes);
            _snapshot = fresh ?? _snapshot;
            FillGrid();
            Result = fresh is null ? "没有修改" : $"已应用 GS {changes.Count} 项";
            _status.Text = Result;
        }
        catch (Exception exc)
        {
            Result = $"GS 应用失败：{exc.Message}";
            _status.Text = Result;
            MessageBox.Show(this, exc.Message, "GS 应用失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _snapshot = _session.Read();
            FillGrid();
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void Undo()
    {
        try
        {
            UseWaitCursor = true;
            _snapshot = _session.Undo();
            FillGrid();
            Result = "已撤销 GS 修改";
            _status.Text = Result;
        }
        catch (Exception exc)
        {
            Result = $"GS 撤销失败：{exc.Message}";
            _status.Text = Result;
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void ConnectLegacy()
    {
        try
        {
            UseWaitCursor = true;
            _legacySnapshot = _legacy.Connect();
            FillLegacy();
            Result = $"已连接菜单 GS：{_legacySnapshot.Values.Count} 个值，可写={_legacySnapshot.Writable}";
        }
        catch (Exception exc)
        {
            Result = $"菜单 GS 连接失败：{exc.Message}";
            MessageBox.Show(this, exc.Message, "菜单 GS 连接失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
        _status.Text = Result;
    }

    private void FillLegacy()
    {
        if (_legacySnapshot is null)
        {
            return;
        }
        _legacyGrid.Rows.Clear();
        foreach (string field in NativeMenuGs.Fields)
        {
            _legacyGrid.Rows.Add(
                field,
                _legacySnapshot.Values[$"user.{field}"],
                _legacySnapshot.Values[$"cpu.{field}"]);
        }
        _legacyState.Text =
            $"状态：{_legacySnapshot.ObservationStatus}　可写：{(_legacySnapshot.Writable ? "是" : "否")}　"
            + $"当前 电脑·三分：{_legacySnapshot.Values[NativeMenuGs.TargetField]}";
        _legacyApply.Enabled = _legacySnapshot.Writable;
        _legacyUndo.Enabled = _legacy.UndoStack.Count > 0;
        int current = _legacySnapshot.Values[NativeMenuGs.TargetField];
        _legacyValue.SelectedIndex = NativeMenuGs.TestedValues.Contains(current)
            ? Array.IndexOf(NativeMenuGs.TestedValues, current)
            : 0;
    }

    private void ApplyLegacy()
    {
        if (_legacySnapshot is null)
        {
            return;
        }
        if (_legacyValue.SelectedItem is not int value)
        {
            _status.Text = "请选择 56 或 57";
            return;
        }
        try
        {
            UseWaitCursor = true;
            _legacySnapshot = _legacy.Apply(_legacySnapshot, value);
            FillLegacy();
            Result = $"已写入 电脑·三分 = {value}（可撤销）";
        }
        catch (Exception exc)
        {
            Result = $"菜单 GS 写入失败：{exc.Message}";
            MessageBox.Show(this, exc.Message, "菜单 GS 写入失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            try
            {
                _legacySnapshot = _legacy.Read();
                FillLegacy();
            }
            catch (Exception reread)
            {
                _legacyState.Text = $"重新读取失败：{reread.Message}";
            }
        }
        finally
        {
            UseWaitCursor = false;
        }
        _status.Text = Result;
    }

    private void UndoLegacy()
    {
        try
        {
            UseWaitCursor = true;
            _legacySnapshot = _legacy.Undo();
            FillLegacy();
            Result = "已撤销菜单 GS 写入";
        }
        catch (Exception exc)
        {
            Result = $"菜单 GS 撤销失败：{exc.Message}";
            MessageBox.Show(this, exc.Message, "菜单 GS 撤销失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
        _status.Text = Result;
    }

    private void LoadPreset()
    {
        using var dialog = new OpenFileDialog { Filter = "GS 预设 (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            var preset = GsPresets.LoadPreset(dialog.FileName);
            foreach (var pair in preset)
            {
                var (side, fieldId) = GsPresets.SplitKey(pair.Key);
                foreach (var row in _rows)
                {
                    if (row.Value.Id != fieldId)
                    {
                        continue;
                    }
                    _grid.Rows[row.Key].Cells[side].Value = pair.Value;
                }
            }
            Result = $"已载入预设 {preset.Count} 项（未写入，需点“应用修改”）";
            _status.Text = Result;
        }
        catch (Exception exc)
        {
            Result = $"预设读取失败：{exc.Message}";
            _status.Text = Result;
            MessageBox.Show(this, exc.Message, "预设读取失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SavePreset()
    {
        if (_snapshot is null)
        {
            return;
        }
        using var dialog = new SaveFileDialog { Filter = "GS 预设 (*.json)|*.json", FileName = "gs-preset.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            var saved = GsPresets.SavePreset(dialog.FileName, _snapshot.Values, overwrite: true);
            Result = $"已导出预设到 {saved}";
            _status.Text = Result;
        }
        catch (Exception exc)
        {
            Result = $"预设保存失败：{exc.Message}";
            _status.Text = Result;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _session.Dispose();
        _legacy.Dispose();
        base.OnFormClosed(e);
    }
}
