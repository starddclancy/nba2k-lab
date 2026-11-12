using System.Text.Json.Nodes;
using System.Globalization;
using System.Text.Json;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Match;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.WebHost;

internal sealed partial class Bridge
{
    private (long Address, int Count, SessionEpoch Epoch)? _gameTable;
    private ScheduleEditor? _schedule;

    /// <summary>
    /// Read-only: the current season's schedule and box scores from the game's own game table
    /// (SeasonGames). Teams and players are matched by UNIQUEID (team +3872, player +296).
    /// </summary>
    private JsonNode SeasonGamesRead(bool diagnostics = false)
    {
        PlayerSession session = Session();
        session.Guard();
        if (session.Memory is not Win32Memory memory)
        {
            throw new InvalidOperationException("赛程需要连接实际游戏进程");
        }
        _gameTable = ResolveGameTable(session);
        var games = SeasonGames.Read(memory, _gameTable.Value.Address, _gameTable.Value.Count);
        var teamByUid = new Dictionary<int, RecordListItem>();
        var leagueUids = new HashSet<int>();
        var teamIdentities = new JsonArray();
        var currentIndex = session.Model.FieldByName("Teams", "CURRENTINDEX")
            ?? throw new InvalidOperationException("球队当前联盟索引字段不可用");
        foreach (var team in TeamItems())
        {
            int uid = BitConverter.ToUInt16(memory.ReadBytes(team.Address + 3872, 2));
            teamByUid.TryAdd(uid, team);
            int leagueIndex = Convert.ToInt32(session.Model.ReadField(currentIndex, team).RawValue);
            if (leagueIndex is >= 0 and < 255) leagueUids.Add(uid);
            if (diagnostics)
            {
                var identity = new JsonObject { ["index"] = team.Index, ["label"] = team.Label, ["uid"] = uid };
                foreach (string name in new[] { "ORIGINALID", "BASETEAMID", "COMMONID", "ORIGINALTYPE", "CURRENTINDEX", "HISTORICYEAR", "CURRENTYEARSTATID" })
                {
                    var field = session.Model.FieldByName("Teams", name);
                    if (field is null) continue;
                    try { identity[name] = ToJson(session.Model.ReadField(field, team).RawValue); }
                    catch (InvalidOperationException) { }
                }
                teamIdentities.Add(identity);
            }
        }
        var playerByUid = new Dictionary<int, RecordListItem>();
        foreach (var item in session.Items)
        {
            try { playerByUid.TryAdd(BitConverter.ToUInt16(memory.ReadBytes(item.Address + 296, 2)), item); }
            catch (InvalidOperationException) { }
        }
        session.Guard();
        JsonNode Lines(IReadOnlyList<GameLine> lines) => new JsonArray(lines.Select(line =>
        {
            playerByUid.TryGetValue(line.PlayerUid, out var player);
            return (JsonNode?)new JsonArray(player?.Index ?? -1, player?.Label ?? $"#{line.PlayerUid}", line.Minutes, line.PlusMinus,
                line.Points, line.FgMade, line.FgAttempted, line.ThreeMade, line.ThreeAttempted, line.FtMade, line.FtAttempted,
                line.OffensiveRebounds, line.DefensiveRebounds, line.Assists, line.Steals, line.Blocks, line.Turnovers, line.Fouls);
        }).ToArray());
        var rows = new JsonArray();
        var otherRows = new JsonArray();
        int unmappedGames = 0;
        foreach (var game in games)
        {
            teamByUid.TryGetValue(game.HomeUid, out var home);
            teamByUid.TryGetValue(game.AwayUid, out var away);
            if (home is null || away is null) unmappedGames++;
            bool league = leagueUids.Contains(game.HomeUid) && leagueUids.Contains(game.AwayUid);
            (league ? rows : otherRows).Add(new JsonObject
            {
                ["id"] = game.Id, ["date"] = game.Date.ToString("yyyy-MM-dd"), ["slot"] = (int)(game.DateCode & 0xFFFF),
                ["played"] = game.Played, ["home"] = home?.Index ?? -game.HomeUid - 1,
                ["away"] = away?.Index ?? -game.AwayUid - 1,
                ["homeUid"] = game.HomeUid, ["awayUid"] = game.AwayUid,
                ["homeLabel"] = home?.Label ?? $"球队 #{game.HomeUid}（名称未读取）",
                ["awayLabel"] = away?.Label ?? $"球队 #{game.AwayUid}（名称未读取）",
                ["teamsResolved"] = home is not null && away is not null,
                ["homePoints"] = game.Played ? game.HomePoints : null, ["awayPoints"] = game.Played ? game.AwayPoints : null,
                ["homeQuarters"] = game.Played ? new JsonArray(game.HomeQuarters.Select(q => (JsonNode?)q).ToArray()) : null,
                ["awayQuarters"] = game.Played ? new JsonArray(game.AwayQuarters.Select(q => (JsonNode?)q).ToArray()) : null,
                ["homeLines"] = game.Played ? Lines(game.Home) : null, ["awayLines"] = game.Played ? Lines(game.Away) : null,
            });
        }
        var result = new JsonObject
        {
            ["format"] = "nba2k27-season-games/1",
            ["columns"] = new JsonArray("player", "name", "min", "pm", "pts", "fgm", "fga", "tpm", "tpa", "ftm", "fta", "oreb", "dreb", "ast", "stl", "blk", "tov", "pf"),
            ["games"] = rows, ["otherGames"] = otherRows, ["unmappedGameCount"] = unmappedGames,
            ["leagueTeamCount"] = leagueUids.Count, ["table"] = $"0x{_gameTable!.Value.Address:X}",
            ["tableSource"] = "registered-native-manager",
            ["tableCount"] = _gameTable.Value.Count, ["writes"] = 0,
            ["writesEnabled"] = _scheduleWrites, ["undoDepth"] = _schedule?.UndoDepth ?? 0,
            ["writeReason"] = _scheduleWrites ? null : "赛程改期尚未完成实机验收，当前只读；须经确认后启动写入模式。",
        };
        if (diagnostics)
        {
            result["teamIdentities"] = teamIdentities;
            result["unmappedTeams"] = new JsonArray(games.SelectMany(game => new[] { game.HomeUid, game.AwayUid })
                .Where(uid => !teamByUid.ContainsKey(uid)).GroupBy(uid => uid)
                .OrderByDescending(group => group.Count()).Select(group => (JsonNode?)new JsonObject
                {
                    ["uid"] = group.Key, ["games"] = group.Count(),
                }).ToArray());
        }
        return result;
    }

    private static (long Address, int Count, SessionEpoch Epoch) ResolveGameTable(PlayerSession session)
    {
        long storeRva = session.Model.RecoveredRoots.TryGetValue("GameDataStore", out long recovered)
            ? recovered : Conversions.ToInt(FieldIo.NodeText(session.Model.ActiveConfig()["base_pointers"]!["GameDataStore"]!["address"]));
        // The native game manager is two registered slots before GameDataStore in this executable.
        var found = SeasonGames.Resolve(session.Memory, checked(session.Memory.BaseAddress + storeRva - 16));
        return (found.Address, found.Count, session.Epoch!);
    }

    private ScheduleEditor Schedule()
    {
        var session = Session();
        session.Guard();
        if (_gameTable is null || _gameTable.Value.Epoch != session.Epoch)
        {
            SeasonGamesRead();
        }
        return _schedule ??= new ScheduleEditor(session.Memory, () =>
        {
            session.Guard();
            var table = ResolveGameTable(session);
            _gameTable = table;
            return new ScheduleTable(table.Address, table.Count, JsonSerializer.Serialize(session.Epoch));
        }, (phase, detail) =>
        {
            var checkpoint = new MatchCheckpoint(Path.Combine(session.DataDirectory, "schedule-edits"));
            checkpoint.Record(phase, "schedule", detail);
            checkpoint.Save($"{detail["id"]}.json", new JsonObject
            {
                ["phase"] = phase, ["utc"] = DateTime.UtcNow, ["detail"] = detail.DeepClone(),
            });
        });
    }

    private JsonNode SchedulePrepare(JsonObject payload)
    {
        int id = Required(payload, "gameId").GetValue<int>();
        if (!DateOnly.TryParseExact(Required(payload, "date").GetValue<string>(), "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new InvalidOperationException("日期必须为 YYYY-MM-DD");
        }
        var preview = Schedule().Prepare(id, date);
        return new JsonObject
        {
            ["token"] = preview.Token, ["gameId"] = preview.GameId,
            ["beforeDate"] = preview.BeforeDate.ToString("yyyy-MM-dd"),
            ["afterDate"] = preview.AfterDate.ToString("yyyy-MM-dd"),
            ["patchBytes"] = preview.PatchBytes, ["writes"] = 0, ["writesEnabled"] = _scheduleWrites,
        };
    }

    private void RequireScheduleWrites()
    {
        if (!_scheduleWrites)
        {
            throw new InvalidOperationException("赛程写入未获开启，本次没有写入游戏");
        }
    }

    private JsonNode ScheduleApply(JsonObject payload)
    {
        RequireScheduleWrites();
        Schedule().Apply(Required(payload, "token").GetValue<string>());
        return new JsonObject { ["undoDepth"] = _schedule!.UndoDepth, ["writes"] = TotalWriteAttempts };
    }

    private JsonNode ScheduleUndo()
    {
        RequireScheduleWrites();
        Schedule().Undo();
        return new JsonObject { ["undoDepth"] = _schedule!.UndoDepth, ["writes"] = TotalWriteAttempts };
    }
}
