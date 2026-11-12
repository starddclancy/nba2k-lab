using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Match;
using Nba2k27Editor.Core.Memory;

namespace Nba2k27Editor.Core.Editing;

public sealed record ScheduleTable(long Address, int Count, string Epoch);
public sealed record SchedulePreview(string Token, int GameId, DateOnly BeforeDate, DateOnly AfterDate,
    int HomeUid, int AwayUid, int PatchBytes);

/// <summary>
/// Reschedule an unplayed game without changing its identity, participants or time-slot bits.
/// The host owns the explicit live-write gate. This class journals before writing, checks the
/// process/table/record, refuses date conflicts, and restores the full four-byte date on undo.
/// </summary>
public sealed class ScheduleEditor(IMemory memory, Func<ScheduleTable> currentTable,
    Action<string, JsonObject> journal)
{
    private sealed record Plan(SchedulePreview Preview, ScheduleTable Table, SeasonGame Game, byte[] Before,
        MatchCounterPatch Patch);
    private readonly Dictionary<string, Plan> _plans = new();
    private readonly List<(Plan Plan, MatchWriteRecord Record)> _undo = new();
    public int UndoDepth => _undo.Count;

    public SchedulePreview Prepare(int gameId, DateOnly date)
    {
        var table = currentTable();
        var games = SeasonGames.Read(memory, table.Address, table.Count);
        var game = games.SingleOrDefault(g => g.Id == gameId)
            ?? throw new InvalidOperationException("赛程场次不存在或身份不唯一");
        ValidateDate(games, game, date);
        if (game.Date == date)
        {
            throw new InvalidOperationException("比赛日期没有变化");
        }
        byte[] before = memory.ReadBytes(game.Address, SeasonGames.Stride);
        var snapshot = SeasonGames.Decode(before, 0, game.Address);
        if (snapshot.DateCode != game.DateCode || snapshot.Id != game.Id || snapshot.Played
            || snapshot.HomeUid != game.HomeUid || snapshot.AwayUid != game.AwayUid)
        {
            throw new InvalidOperationException("赛程在预览期间变化，请刷新");
        }
        int months = (date.Year - 1960) * 12 + date.Month - 1;
        uint packed = ((uint)months << 21) | ((uint)date.Day << 16) | (game.DateCode & 0xFFFF);
        string token = Guid.NewGuid().ToString("N");
        var preview = new SchedulePreview(token, game.Id, game.Date, date, game.HomeUid, game.AwayUid, 4);
        var patch = new MatchCounterPatch(0, null, 0, "scheduleDate", game.Address, 4,
            Convert.ToHexString(before.AsSpan(0, 4)), Convert.ToHexString(BitConverter.GetBytes(packed)));
        var plan = new Plan(preview, table, game, before, patch);
        Check(plan);
        if (_plans.Count >= 16)
        {
            _plans.Remove(_plans.Keys.First());
        }
        _plans[token] = plan;
        return preview;
    }

    private static void ValidateDate(IReadOnlyList<SeasonGame> games, SeasonGame game, DateOnly date)
    {
        if (game.Played)
        {
            throw new InvalidOperationException("已赛场次不能修改赛程");
        }
        DateOnly lastPlayed = games.Where(g => g.Played).Select(g => g.Date).DefaultIfEmpty(DateOnly.MinValue).Max();
        DateOnly start = games.Min(g => g.Date);
        if (date <= lastPlayed || date < start || date > start.AddMonths(18) || date.Year > 2200)
        {
            throw new InvalidOperationException("新日期必须晚于已赛比赛，且在当前赛季范围内");
        }
        if (games.Any(g => g.Id != game.Id && g.Date == date
            && (g.HomeUid == game.HomeUid || g.AwayUid == game.HomeUid
                || g.HomeUid == game.AwayUid || g.AwayUid == game.AwayUid)))
        {
            throw new InvalidOperationException("同一球队在新日期已有比赛，请选择其他日期");
        }
    }

    private void Check(Plan plan)
    {
        if (currentTable() != plan.Table)
        {
            throw new InvalidOperationException("游戏进程、名单或赛程表已变化，请重新预览");
        }
        var current = memory.ReadBytes(plan.Game.Address, SeasonGames.Stride);
        // Date is the only writable span. Final-state marker, teams and every other byte stay bound.
        if (!current.AsSpan(4).SequenceEqual(plan.Before.AsSpan(4)))
        {
            throw new InvalidOperationException("赛程场次已被游戏更新，操作已停止");
        }
    }

    public void Apply(string token)
    {
        if (!_plans.Remove(token, out var plan))
        {
            throw new InvalidOperationException("赛程预览已失效，请重新预览");
        }
        Check(plan);
        ValidateDate(SeasonGames.Read(memory, plan.Table.Address, plan.Table.Count),
            plan.Game, plan.Preview.AfterDate);
        byte[] Read(long address, int count) { Check(plan); return memory.ReadBytes(address, count); }
        void Write(long address, byte[] bytes) { Check(plan); memory.WriteBytes(address, bytes); }
        Exception? journalFailure = null;
        void Journal(string phase, JsonObject detail)
        {
            // The commit already succeeded at this phase. Retain its undo even if the final log fails.
            try { Record(plan, phase, detail); }
            catch (Exception exc) when (phase == "applied") { journalFailure = exc; }
        }
        var record = MatchWriter.Apply(new[] { plan.Patch }, Read, Write, Journal);
        _undo.Add((plan, record));
        _plans.Clear();
        if (journalFailure is not null)
        {
            throw new IOException("比赛日期已修改，但完成日志写入失败；撤销仍然可用", journalFailure);
        }
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            throw new InvalidOperationException("没有可撤销的赛程修改");
        }
        var (plan, record) = _undo[^1];
        Check(plan);
        ValidateDate(SeasonGames.Read(memory, plan.Table.Address, plan.Table.Count),
            plan.Game, plan.Preview.BeforeDate);
        byte[] Read(long address, int count) { Check(plan); return memory.ReadBytes(address, count); }
        void Write(long address, byte[] bytes) { Check(plan); memory.WriteBytes(address, bytes); }
        Exception? journalFailure = null;
        void Journal(string phase, JsonObject detail)
        {
            try { Record(plan, phase, detail); }
            catch (Exception exc) when (phase == "undone") { journalFailure = exc; }
        }
        MatchWriter.Undo(record, Read, Write, Journal);
        _undo.RemoveAt(_undo.Count - 1);
        _plans.Clear();
        if (journalFailure is not null)
        {
            throw new IOException("比赛日期已恢复，但撤销完成日志写入失败", journalFailure);
        }
    }

    private void Record(Plan plan, string phase, JsonObject detail)
    {
        detail["format"] = "nba2k27-schedule-edit/1";
        detail["gameId"] = plan.Game.Id;
        detail["epoch"] = plan.Table.Epoch;
        detail["table"] = $"0x{plan.Table.Address:X}";
        detail["count"] = plan.Table.Count;
        detail["beforeDate"] = plan.Preview.BeforeDate.ToString("yyyy-MM-dd");
        detail["afterDate"] = plan.Preview.AfterDate.ToString("yyyy-MM-dd");
        detail["beforeRecord"] = Convert.ToHexString(plan.Before);
        journal(phase, detail);
    }
}
