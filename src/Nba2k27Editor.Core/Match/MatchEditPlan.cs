namespace Nba2k27Editor.Core.Match;

public sealed record MatchEditRequest(int Side, int PlayerId, int Slot, Dictionary<string, long> Changes);
public sealed record MatchEditChange(string Owner, int Side, int? PlayerId, int Slot,
    string Field, long Before, long After, bool Derived);
public sealed record MatchEditPlan(List<MatchEditChange> Changes, int Players, int Periods);

/// <summary>Validate captured counters and calculate related values without any memory access.</summary>
public static class MatchEditPlanner
{
    private static readonly string[] Counters =
    {
        "twoPointMade", "twoPointAttempted", "threePointMade", "threePointAttempted",
        "freeThrowMade", "freeThrowAttempted", "offensiveRebounds", "defensiveRebounds",
        "assists", "steals", "blocks", "turnovers",
    };
    private static readonly string[] Fields = new[] { "points", "rebounds" }.Concat(Counters).ToArray();

    public static MatchEditPlan Prepare(MatchSnapshot snapshot, IReadOnlyList<MatchEditRequest> requests)
    {
        Require(snapshot.Period is >= 1 and <= 20 && snapshot.Teams.Count == 2
            && snapshot.Teams.Select(team => team.Side).Order().SequenceEqual(new[] { 0, 1 }),
            "比赛快照无效，请重新读取");
        Require(snapshot.Issues.Count == 0, "原始统计合计存在差异，不能准备修改");
        Require(requests.Count is > 0 and <= 180, "请选择需要修改的球员和分节");
        Require(requests.Select(request => (request.Side, request.PlayerId, request.Slot))
            .Distinct().Count() == requests.Count, "同一球员同一分节不能重复提交");
        foreach (var request in requests)
        {
            Require(request.Side is 0 or 1 && request.Slot >= 1
                && request.Slot <= Math.Min(snapshot.Period, 5), "只能修改已经开始的分节");
            Require(request.Changes is { Count: > 0 } && request.Changes.Keys.All(Counters.Contains),
                "只能修改投篮、篮板、助攻、抢断、盖帽或失误；得分由投篮计算");
            Require(snapshot.Teams.Single(team => team.Side == request.Side).Players
                .Count(player => player.Id == request.PlayerId) == 1, "球员身份无效或不唯一，请重新读取");
            foreach (long value in request.Changes.Values)
            {
                Require(value is >= 0 and <= ushort.MaxValue, "统计值必须在0至65535之间");
            }
        }

        var changes = new List<MatchEditChange>();
        var players = new HashSet<(int Side, int Id)>();
        var periods = new HashSet<int>();
        foreach (var team in snapshot.Teams.OrderBy(team => team.Side))
        {
            var teamRequests = requests.Where(request => request.Side == team.Side).ToList();
            if (teamRequests.Count == 0)
            {
                continue;
            }
            ValidateStats(team.Stats);
            foreach (var player in team.Players)
            {
                ValidateStats(player.Stats);
            }
            for (int slot = 0; slot <= 5; slot++)
            {
                foreach (string field in Fields)
                {
                    Require(Row(team.Stats, slot)[field]
                        == team.Players.Sum(player => Row(player.Stats, slot)[field]),
                        "原始球队与球员统计不一致，不能准备修改");
                }
            }
            var nextTeam = Copy(team.Stats);
            foreach (var player in team.Players.OrderBy(player => player.Id))
            {
                var edits = teamRequests.Where(request => request.PlayerId == player.Id).ToList();
                if (edits.Count == 0)
                {
                    continue;
                }
                var next = Copy(player.Stats);
                foreach (var edit in edits.OrderBy(edit => edit.Slot))
                {
                    var row = Row(next, edit.Slot);
                    foreach (var (field, value) in edit.Changes)
                    {
                        row[field] = value;
                    }
                    Derive(row);
                    ValidateRow(row);
                    foreach (string field in Fields)
                    {
                        Row(nextTeam, edit.Slot)[field] += row[field] - Row(player.Stats, edit.Slot)[field];
                    }
                }
                Total(next);
                ValidateStats(next);
                int before = changes.Count;
                AddChanges(changes, player.Name, team.Side, player.Id, player.Stats, next,
                    (slot, field) => !edits.Any(edit => edit.Slot == slot && edit.Changes.ContainsKey(field)));
                if (changes.Count > before)
                {
                    players.Add((team.Side, player.Id));
                    foreach (var edit in edits.Where(edit => Fields.Any(field =>
                        Row(player.Stats, edit.Slot)[field] != Row(next, edit.Slot)[field])))
                    {
                        periods.Add(edit.Slot);
                    }
                }
            }
            Total(nextTeam);
            ValidateStats(nextTeam);
            AddChanges(changes, team.Label, team.Side, null, team.Stats, nextTeam, (_, _) => true);
        }
        Require(changes.Count > 0, "没有需要修改的统计值");
        return new(changes, players.Count, periods.Count);
    }

    private static void AddChanges(List<MatchEditChange> changes, string owner, int side, int? playerId,
        MatchStats before, MatchStats after, Func<int, string, bool> derived)
    {
        for (int slot = 0; slot <= 5; slot++)
        {
            foreach (string field in Fields)
            {
                long oldValue = Row(before, slot)[field];
                long newValue = Row(after, slot)[field];
                if (oldValue != newValue)
                {
                    changes.Add(new(owner, side, playerId, slot, field, oldValue, newValue, derived(slot, field)));
                }
            }
        }
    }

    private static MatchStats Copy(MatchStats stats) =>
        new(new(stats.Total), stats.Periods.Select(row => new Dictionary<string, long>(row)).ToList());

    private static Dictionary<string, long> Row(MatchStats stats, int slot) =>
        slot == 0 ? stats.Total : stats.Periods[slot - 1];

    private static void Derive(Dictionary<string, long> row)
    {
        row["points"] = 2 * row["twoPointMade"] + 3 * row["threePointMade"] + row["freeThrowMade"];
        row["rebounds"] = row["offensiveRebounds"] + row["defensiveRebounds"];
    }

    private static void Total(MatchStats stats)
    {
        foreach (string field in Fields)
        {
            stats.Total[field] = stats.Periods.Sum(row => row[field]);
        }
    }

    private static void ValidateStats(MatchStats stats)
    {
        Require(stats.Periods.Count == 5, "比赛分节统计不完整");
        foreach (var row in new[] { stats.Total }.Concat(stats.Periods))
        {
            ValidateRow(row);
        }
        Require(Fields.All(field => stats.Total[field] == stats.Periods.Sum(row => row[field])),
            "原始全场与分节统计不一致，不能准备修改");
    }

    private static void ValidateRow(Dictionary<string, long> row)
    {
        Require(Fields.All(row.ContainsKey), "比赛统计字段不完整");
        Require(Counters.All(field => row[field] is >= 0 and <= ushort.MaxValue)
            && row["points"] is >= 0 and <= uint.MaxValue, "统计合计超出字段范围");
        Require(row["twoPointMade"] <= row["twoPointAttempted"]
            && row["threePointMade"] <= row["threePointAttempted"]
            && row["freeThrowMade"] <= row["freeThrowAttempted"], "命中数不能超过出手数");
        Require(row["points"] == 2 * row["twoPointMade"] + 3 * row["threePointMade"] + row["freeThrowMade"],
            "得分与投篮统计不一致，不能准备修改");
        Require(row["rebounds"] == row["offensiveRebounds"] + row["defensiveRebounds"],
            "篮板与前后场篮板合计不一致");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
