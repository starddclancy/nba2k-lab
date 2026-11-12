using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Model;
using Nba2k27Editor.Core.Roster;

namespace Nba2k27Editor.App;

/// <summary>Move or trade players between teams; every change is journaled and undoable.</summary>
public sealed class RosterMoveForm : Form
{
    private readonly RosterMover _mover;
    private readonly TextBox _searchA = new();
    private readonly ListBox _playersA = new();
    private readonly TextBox _searchB = new();
    private readonly ListBox _playersB = new();
    private readonly ComboBox _teams = new();
    private readonly Label _info = new();
    private readonly Button _move = new() { Text = "执行调队" };
    private readonly Button _trade = new() { Text = "交易两名球员" };
    private readonly Button _undo = new() { Text = "撤销" };

    private List<RecordListItem> _items = new();

    public RosterMoveForm(PlayerSession session)
    {
        _mover = new RosterMover(session);
        _items = session.Items.ToList();
        Text = "调队 / 交易";
        Width = 980;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9F);
        Result = "未执行";

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 490 };

        var left = new GroupBox { Text = "球员 A", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _searchA.Dock = DockStyle.Top;
        _searchA.PlaceholderText = "搜索球员 A";
        _searchA.TextChanged += (_, _) => Fill(_searchA, _playersA);
        _playersA.Dock = DockStyle.Fill;
        _playersA.IntegralHeight = false;
        _playersA.SelectedIndexChanged += (_, _) => ShowPlacement(_playersA, "A");
        left.Controls.Add(_playersA);
        left.Controls.Add(_searchA);
        split.Panel1.Controls.Add(left);

        var right = new GroupBox { Text = "目标球队 / 球员 B", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _teams.Dock = DockStyle.Top;
        _teams.DropDownStyle = ComboBoxStyle.DropDownList;
        _searchB.Dock = DockStyle.Top;
        _searchB.PlaceholderText = "搜索球员 B（用于交易）";
        _searchB.TextChanged += (_, _) => Fill(_searchB, _playersB);
        _playersB.Dock = DockStyle.Fill;
        _playersB.IntegralHeight = false;
        _playersB.SelectedIndexChanged += (_, _) => ShowPlacement(_playersB, "B");
        right.Controls.Add(_playersB);
        right.Controls.Add(_searchB);
        right.Controls.Add(_teams);
        split.Panel2.Controls.Add(right);
        Controls.Add(split);
        split.BringToFront();

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 92, Padding = new Padding(8) };
        _info.Dock = DockStyle.Top;
        _info.Height = 34;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40 };
        _move.Click += (_, _) => DoMove();
        _trade.Click += (_, _) => DoTrade();
        _undo.Click += (_, _) => DoUndo();
        buttons.Controls.AddRange(new Control[] { _move, _trade, _undo });
        bar.Controls.Add(buttons);
        bar.Controls.Add(_info);
        Controls.Add(bar);
        bar.BringToFront();

        FillTeams();
        Fill(_searchA, _playersA);
        Fill(_searchB, _playersB);
        _info.Text = $"球员 {_items.Count} 人，球队 {_mover.TeamOptions().Count} 支";
    }

    public string Result { get; private set; }

    private void FillTeams()
    {
        _teams.Items.Clear();
        foreach (var (team, slots) in _mover.TeamOptions())
        {
            _teams.Items.Add(new TeamChoice(team, slots.Used, slots.OpenSlots.ToList()));
        }
        _teams.SelectedIndex = _teams.Items.Count > 0 ? 0 : -1;
    }

    private sealed record TeamChoice(RecordListItem Team, int Used, List<int> Open)
    {
        public override string ToString() =>
            $"{Team.Label}（已用 {Used}/15，空位 {string.Join(',', Open)}）";
    }

    private void Fill(TextBox search, ListBox list)
    {
        string needle = search.Text.Trim();
        var matches = needle.Length == 0
            ? _items
            : _items.Where(item => item.Label.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach (var item in matches)
            {
                list.Items.Add(item.Label);
            }
            list.Tag = matches;
        }
        finally
        {
            list.EndUpdate();
        }
    }

    private RecordListItem? Selected(ListBox list) =>
        list.Tag is List<RecordListItem> matches && list.SelectedIndex >= 0
            ? matches[list.SelectedIndex]
            : null;

    private void ShowPlacement(ListBox list, string side)
    {
        var player = Selected(list);
        if (player is null)
        {
            return;
        }
        try
        {
            var placement = _mover.Placement(player);
            _info.Text = $"{side}: {player.Label} — {placement.TeamLabel} 第 {placement.Slot} 位";
        }
        catch (Exception exc)
        {
            _info.Text = $"{side}: {player.Label} — {exc.Message}";
        }
    }

    private void DoMove()
    {
        var player = Selected(_playersA);
        if (player is null || _teams.SelectedItem is not TeamChoice choice)
        {
            _info.Text = "请选择球员 A 和目标球队";
            return;
        }
        try
        {
            UseWaitCursor = true;
            var plan = _mover.PlanMove(player, choice.Team);
            var transaction = _mover.Apply(plan);
            Result = $"已调队：{plan.Summary}（{transaction.Patches.Count} 字节）";
            _info.Text = Result;
        }
        catch (Exception exc)
        {
            _info.Text = $"调队失败：{exc.Message}";
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void DoTrade()
    {
        var first = Selected(_playersA);
        var second = Selected(_playersB);
        if (first is null || second is null)
        {
            _info.Text = "请选择两名球员";
            return;
        }
        try
        {
            UseWaitCursor = true;
            var plan = _mover.PlanTrade(first, second);
            var transaction = _mover.Apply(plan);
            Result = $"已交易：{plan.Summary}（{transaction.Patches.Count} 字节）";
            _info.Text = Result;
        }
        catch (Exception exc)
        {
            _info.Text = $"交易失败：{exc.Message}";
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void DoUndo()
    {
        try
        {
            UseWaitCursor = true;
            var transaction = _mover.Undo();
            Result = $"已撤销：{transaction.Summary}";
            _info.Text = Result;
            FillTeams();
        }
        catch (Exception exc)
        {
            _info.Text = $"撤销失败：{exc.Message}";
        }
        finally
        {
            UseWaitCursor = false;
        }
    }
}
