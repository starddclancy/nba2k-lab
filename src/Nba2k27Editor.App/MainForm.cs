using System.Globalization;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;
using Nba2k27Editor.Core.Roster;

namespace Nba2k27Editor.App;

/// <summary>Player editor window: roster search on the left, the six field sections on the right.</summary>
public sealed class MainForm : Form
{
    public static readonly string[] Sections = PlayerSession.Sections;

    private readonly PlayerSession _session = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _source = new();
    private readonly ListBox _players = new();
    private readonly TabControl _tabs = new();
    private readonly ToolStripStatusLabel _status = new();
    private readonly ToolStripButton _connect = new("连接游戏");
    private readonly ToolStripButton _refresh = new("刷新");
    private readonly ToolStripButton _apply = new("应用修改");
    private readonly ToolStripButton _undo = new("撤销");
    private readonly ToolStripButton _export = new("导出球员");
    private readonly ToolStripButton _import = new("导入预设");
    private readonly ToolStripButton _roster = new("调队/交易");
    private readonly ToolStripButton _gs = new("GS 菜单");

    private readonly Dictionary<string, DataGridView> _grids = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, FieldSpec>> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _original = new(StringComparer.Ordinal);

    private List<RecordListItem> _items = new();
    private List<RecordListItem> _draftItems = new();
    private PlayerSnapshot? _snapshot;

    public MainForm()
    {
        Text = $"NBA2K Lab {AppVersion.Value}";
        Width = 1280;
        Height = 820;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);

        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        _connect.Click += (_, _) => Connect();
        _refresh.Click += (_, _) => ReloadPlayer();
        _apply.Click += (_, _) => ApplyChanges();
        _undo.Click += (_, _) => Undo();
        _export.Click += (_, _) => ExportPlayer();
        _import.Click += (_, _) => ImportPreset();
        _roster.Click += (_, _) => OpenRoster();
        _gs.Click += (_, _) => OpenGs();
        tools.Items.AddRange(new ToolStripItem[]
        {
            _connect, new ToolStripSeparator(), _refresh, _apply, _undo,
            new ToolStripSeparator(), _export, _import, new ToolStripSeparator(), _roster, _gs,
        });
        Controls.Add(tools);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 320,
            Orientation = Orientation.Vertical,
        };
        // Deterministic three row layout: source selector, search box, then the list.
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _source.Dock = DockStyle.Fill;
        _source.DropDownStyle = ComboBoxStyle.DropDownList;
        _source.Items.AddRange(new object[] { "名单", "新秀名单" });
        _source.SelectedIndex = 0;
        _source.SelectedIndexChanged += (_, _) => FillPlayers();
        _search.Dock = DockStyle.Fill;
        _search.PlaceholderText = "搜索球员姓名";
        _search.TextChanged += (_, _) => FillPlayers();
        _players.Dock = DockStyle.Fill;
        _players.IntegralHeight = false;
        // Multi-select drives the batch apply path; a single selection keeps the single-player path.
        _players.SelectionMode = SelectionMode.MultiExtended;
        _players.SelectedIndexChanged += (_, _) => ReloadPlayer();
        left.Controls.Add(_source, 0, 0);
        left.Controls.Add(_search, 0, 1);
        left.Controls.Add(_players, 0, 2);
        split.Panel1.Controls.Add(left);

        _tabs.Dock = DockStyle.Fill;
        foreach (var section in Sections)
        {
            var page = new TabPage(section);
            var grid = BuildGrid(section);
            page.Controls.Add(grid);
            _tabs.TabPages.Add(page);
            _grids[section] = grid;
        }
        split.Panel2.Controls.Add(_tabs);
        Controls.Add(split);
        split.BringToFront();

        var bar = new StatusStrip();
        _status.Text = "未连接";
        _status.Spring = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        bar.Items.Add(_status);
        Controls.Add(bar);
        bar.BringToFront();

        _apply.Enabled = false;
        _undo.Enabled = false;
        _export.Enabled = false;
        _import.Enabled = false;
    }

    public int TabCount => _tabs.TabPages.Count;
    public int PlayerCount => _items.Count;
    public int FieldCount => _session.Specs.Count;
    public bool Connected => _session.Epoch is not null;
    public int WriteCount => _session.Memory is Win32Memory memory ? memory.Writes : 0;

    /// <summary>Read-only live probe for --connect-check: connect, index the roster, read one player.</summary>
    public void ConnectForCheck()
    {
        _items = _session.Connect().ToList();
        _draftItems = _session.RosterIndex.DraftItems.ToList();
        FillPlayers();
        if (_items.Count > 0)
        {
            _snapshot = _session.ReadPlayer(_items[0]);
        }
    }

    public string? SamplePlayer => _snapshot?.Item.Label;
    public int SampleFields => _snapshot?.Values.Count ?? 0;
    public int RosterCount => _items.Count;
    public int DraftCount => _draftItems.Count;
    public int TeamCount => _session.RosterIndex.Teams.Count;
    public string DraftStatus => _session.RosterIndex.DraftStatus;
    public int EpochPid => _session.Epoch?.Pid ?? 0;

    private DataGridView BuildGrid(string section)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            EditMode = DataGridViewEditMode.EditOnEnter,
            BackgroundColor = SystemColors.Window,
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "group",
            HeaderText = "分组",
            ReadOnly = true,
            FillWeight = 22,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "label",
            HeaderText = "字段",
            ReadOnly = true,
            FillWeight = 46,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "value",
            HeaderText = "值",
            FillWeight = 32,
        });
        _rows[section] = new Dictionary<int, FieldSpec>();
        return grid;
    }

    private void Connect()
    {
        try
        {
            UseWaitCursor = true;
            _items = _session.Connect().ToList();
            _draftItems = _session.RosterIndex.DraftItems.ToList();
            _status.Text = $"已连接 pid {_session.Epoch?.Pid}，球员 {_items.Count} 人，新秀 {_draftItems.Count} 人，"
                + $"字段 {_session.Specs.Count} 个";
            FillPlayers();
            _apply.Enabled = true;
            _undo.Enabled = true;
            _export.Enabled = true;
            _import.Enabled = true;
        }
        catch (Exception exc)
        {
            _status.Text = $"连接失败：{exc.Message}";
            MessageBox.Show(this, exc.Message, "连接失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void FillPlayers()
    {
        bool draft = _source.SelectedIndex == 1;
        var source = draft ? _draftItems : _items;
        string needle = _search.Text.Trim();
        var matches = needle.Length == 0
            ? source
            : source.Where(item => item.Label.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        _players.BeginUpdate();
        try
        {
            _players.Items.Clear();
            foreach (var item in matches)
            {
                _players.Items.Add(item.Label);
            }
            _players.Tag = matches;
        }
        finally
        {
            _players.EndUpdate();
        }
        if (draft && source.Count == 0)
        {
            // The draft class is loaded by the game, not by the editor; report the real reason.
            _status.Text = _session.RosterIndex.DraftStatus;
            return;
        }
        string label = draft ? "新秀" : "球员";
        _status.Text = needle.Length == 0
            ? $"{label} {source.Count} 人"
            : $"匹配 {matches.Count} 人（共 {source.Count} 人）";
    }

    private void ReloadPlayer()
    {
        if (_players.Tag is not List<RecordListItem> matches || _players.SelectedIndex < 0)
        {
            return;
        }
        var item = matches[_players.SelectedIndex];
        try
        {
            UseWaitCursor = true;
            _snapshot = _session.ReadPlayer(item);
            _original.Clear();
            foreach (var grid in _grids.Values)
            {
                grid.Rows.Clear();
            }
            foreach (var pair in _rows)
            {
                pair.Value.Clear();
            }
            foreach (var spec in _session.Specs.Values.OrderBy(spec => Array.IndexOf(Sections, spec.Section))
                         .ThenBy(spec => spec.Group, StringComparer.Ordinal)
                         .ThenBy(spec => spec.Label, StringComparer.Ordinal))
            {
                if (!_grids.TryGetValue(spec.Section, out var grid))
                {
                    continue;
                }
                if (!_snapshot.Values.TryGetValue(spec.Key, out var value))
                {
                    continue;
                }
                int index = grid.Rows.Add(spec.Group, spec.Label, Display(value.Display));
                _rows[spec.Section][index] = spec;
                _original[spec.Key] = Display(value.Display);
                grid.Rows[index].Cells["value"].ReadOnly = !value.Writable;
                if (!value.Writable)
                {
                    grid.Rows[index].Cells["value"].Style.ForeColor = SystemColors.GrayText;
                }
                if (_snapshot.Errors.TryGetValue(spec.Key, out string? error))
                {
                    grid.Rows[index].Cells["value"].ToolTipText = error;
                }
            }
            _status.Text = $"已读取 {item.Label}：{_snapshot.Values.Count} 个字段"
                + (_snapshot.Errors.Count > 0 ? $"，{_snapshot.Errors.Count} 个读取失败" : string.Empty);
        }
        catch (Exception exc)
        {
            _status.Text = $"读取失败：{exc.Message}";
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    /// <summary>Selected list entries in list order; empty when nothing is selected.</summary>
    private List<RecordListItem> SelectedItems()
    {
        if (_players.Tag is not List<RecordListItem> matches)
        {
            return new List<RecordListItem>();
        }
        return _players.SelectedIndices
            .Cast<int>()
            .Where(index => index >= 0 && index < matches.Count)
            .OrderBy(index => index)
            .Select(index => matches[index])
            .ToList();
    }

    private static string Display(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        double number => number.ToString("0.####", CultureInfo.InvariantCulture),
        float number => number.ToString("0.####", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private void ApplyChanges()
    {
        if (_snapshot is null)
        {
            return;
        }
        var changes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in _rows)
        {
            var grid = _grids[pair.Key];
            foreach (var row in pair.Value)
            {
                string current = Convert.ToString(grid.Rows[row.Key].Cells["value"].Value, CultureInfo.InvariantCulture)
                    ?? string.Empty;
                if (_original.TryGetValue(row.Value.Key, out string? before) && before == current)
                {
                    continue;
                }
                changes[row.Value.Key] = current;
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
            var selected = SelectedItems()
                .Where(item => item.Domain == _snapshot.Item.Domain)
                .ToList();
            if (selected.Count > 1)
            {
                // Batch keeps roster and draft edits apart; ApplyBatch enforces the same rule.
                var edits = new List<(PlayerSnapshot Snapshot, IReadOnlyDictionary<string, object?> Changed)>();
                foreach (var item in selected)
                {
                    var snapshot = item.Address == _snapshot.Item.Address ? _snapshot : _session.ReadPlayer(item);
                    edits.Add((snapshot, changes));
                }
                var batch = _session.ApplyBatch(edits);
                _status.Text = batch is null
                    ? "没有修改"
                    : $"已批量应用 {edits.Count} 名球员、{changes.Count} 项（{batch.Patches.Count} 字节），可撤销";
            }
            else
            {
                var transaction = _session.Apply(_snapshot, changes);
                _status.Text = transaction is null
                    ? "没有修改"
                    : $"已应用 {changes.Count} 项修改（{transaction.Patches.Count} 字节），可撤销";
            }
            ReloadPlayer();
        }
        catch (Exception exc)
        {
            _status.Text = $"应用失败：{exc.Message}";
            MessageBox.Show(this, exc.Message, "应用失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ReloadPlayer();
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
            var restored = _session.UndoBatch();
            _status.Text = $"已撤销，恢复 {restored.Count} 名球员";
            ReloadPlayer();
        }
        catch (Exception exc)
        {
            _status.Text = $"撤销失败：{exc.Message}";
            MessageBox.Show(this, exc.Message, "撤销失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void ExportPlayer()
    {
        if (_snapshot is null)
        {
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = "球员预设 (*.json)|*.json",
            FileName = $"{_snapshot.Item.Label}.json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            _session.ExportPlayer(_snapshot, dialog.FileName);
            _status.Text = $"已导出到 {dialog.FileName}";
        }
        catch (Exception exc)
        {
            _status.Text = $"导出失败：{exc.Message}";
        }
    }

    private void ImportPreset()
    {
        if (_snapshot is null)
        {
            return;
        }
        using var dialog = new OpenFileDialog { Filter = "球员预设 (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            var preset = _session.ImportPreset(dialog.FileName);
            var changes = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in preset)
            {
                if (_snapshot.Values.TryGetValue(pair.Key, out var value) && value.Writable)
                {
                    changes[pair.Key] = pair.Value;
                }
            }
            if (changes.Count == 0)
            {
                _status.Text = "预设与当前球员没有可应用的差异";
                return;
            }
            var transaction = _session.Apply(_snapshot, changes);
            _status.Text = transaction is null
                ? "预设与当前球员没有差异"
                : $"已应用预设 {changes.Count} 项（{transaction.Patches.Count} 字节）";
            ReloadPlayer();
        }
        catch (Exception exc)
        {
            _status.Text = $"导入失败：{exc.Message}";
            MessageBox.Show(this, exc.Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenRoster()
    {
        try
        {
            using var dialog = new RosterMoveForm(_session);
            dialog.ShowDialog(this);
            _status.Text = dialog.Result;
            ReloadPlayer();
        }
        catch (Exception exc)
        {
            _status.Text = $"调队失败：{exc.Message}";
        }
    }

    private void OpenGs()
    {
        using var dialog = new GsForm();
        dialog.ShowDialog(this);
        _status.Text = dialog.Result;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _session.Close();
        base.OnFormClosed(e);
    }
}
