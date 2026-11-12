using System.Text.Json.Nodes;
using Nba2k27Editor.Core;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Model;
using Nba2k27Editor.Core.Roster;

namespace Nba2k27Editor.WebHost;

internal sealed partial class Bridge
{
    private static readonly string[] RankingNames =
    {
        "LEAGUEOVRRATINGRANKING", "LEAGUEOVRRATINGRANKINGBYPOSITION", "CONFERENCEOVRRATINGRANKING",
        "LEAGUEPERSCORERANKING", "LEAGUEPERSCORERANKINGBYPOSITION", "CONFERENCEPERSCORERANKING",
        "OFFENSERATINGRANKING", "DEFENSERATINGRANKING",
    };

    private static readonly string[] TeamFormNames =
    {
        "LAST10", "LAST10HOME", "LAST10AWAY", "LAST10HOMECOUNT", "LAST10AWAYCOUNT", "WINLOSSSTREAK", "HOMEWINLOSSSTREAK",
        "AWAYWINLOSSSTREAK", "PREVIOUSWINLOSSSTREAK", "LONGESTWINSTREAK", "LONGESTLOSSSTREAK", "SEASONHOMEWINS", "SEASONHOMELOSSES",
    };

    /// <summary>Read-only: the player's last five games (date, minutes, Game Score) and league rankings.</summary>
    private JsonNode PlayerForm(int index, string source)
    {
        PlayerSession session = Session();
        RecordListItem item = Item(index, source);
        string Key(string name) => $"Stats/History/{name}";
        var keys = Enumerable.Range(0, 5).SelectMany(slot => new[] { $"GAMESCOREHISTORYDATE{slot}", $"GAMESCOREHISTORYMINUTES{slot}", $"GAMESCOREHISTORYVALUE{slot}" })
            .Concat(RankingNames).Select(Key).Where(session.Specs.ContainsKey).ToArray();
        var snapshot = session.ReadPlayer(item, keys);
        long Raw(string name) => snapshot.Values.TryGetValue(Key(name), out var value) ? Convert.ToInt64(value.Raw ?? 0L, System.Globalization.CultureInfo.InvariantCulture) : 0;
        var games = RecentForm.Games(
            Enumerable.Range(0, 5).Select(slot => Raw($"GAMESCOREHISTORYDATE{slot}")).ToArray(),
            Enumerable.Range(0, 5).Select(slot => Raw($"GAMESCOREHISTORYMINUTES{slot}")).ToArray(),
            Enumerable.Range(0, 5).Select(slot => Raw($"GAMESCOREHISTORYVALUE{slot}")).ToArray());
        var rankings = new JsonObject();
        foreach (string name in RankingNames)
        {
            if (session.Specs.ContainsKey(Key(name)))
            {
                rankings[name] = Raw(name);
            }
        }
        return new JsonObject
        {
            ["index"] = item.Index,
            ["name"] = item.Label,
            ["games"] = new JsonArray(games.Select(game => (JsonNode?)new JsonObject
            {
                ["slot"] = game.Slot, ["date"] = game.Date.ToString("yyyy-MM-dd"), ["minutes"] = game.Minutes, ["gameScore"] = game.GameScore,
            }).ToArray()),
            ["rankings"] = rankings,
            ["writes"] = 0,
        };
    }

    /// <summary>Read-only: last-10 results, streaks and home record for one team, or for every team.</summary>
    private JsonNode TeamFormRead(int? teamIndex)
    {
        PlayerSession session = Session();
        var entries = session.Model.GroupedFields("Teams").Where(entry => TeamFormNames.Contains(entry.NormalizedName))
            .GroupBy(entry => entry.NormalizedName).ToDictionary(group => group.Key, group => group.First());
        var result = new JsonArray();
        foreach (RecordListItem team in (teamIndex is null ? LeagueTeams() : TeamItems())
            .Where(team => teamIndex is null || team.Index == teamIndex))
        {
            long Field(string name)
            {
                if (!entries.TryGetValue(name, out var entry)) return 0;
                object? raw = session.Model.ReadField(entry, team).RawValue;
                return raw is null ? 0 : Convert.ToInt64(raw, System.Globalization.CultureInfo.InvariantCulture);
            }
            TeamForm form = RecentForm.Team(Field);
            result.Add(new JsonObject
            {
                ["index"] = team.Index, ["label"] = team.Label,
                ["last10"] = form.Last10, ["wins"] = form.Wins, ["losses"] = form.Losses,
                ["last10Home"] = form.Last10Home, ["last10Away"] = form.Last10Away,
                ["streak"] = form.Streak, ["homeStreak"] = form.HomeStreak, ["awayStreak"] = form.AwayStreak,
                ["previousStreak"] = form.PreviousStreak, ["longestWin"] = form.LongestWin, ["longestLoss"] = form.LongestLoss,
                ["homeWins"] = form.HomeWins, ["homeLosses"] = form.HomeLosses,
            });
        }
        session.Guard();
        return result;
    }

    /// <summary>
    /// Read-only: every roster player's current-season record (CURRENTYEARSTATID) in one pass, for
    /// the league leaderboards. Players without a current-season record are skipped. Values are
    /// keyed by the short field name (POINTS, 3POINTERSMADE, ...).
    /// </summary>
    private JsonNode LeagueSeason()
    {
        PlayerSession session = Session();
        session.Guard();
        var specs = session.Specs.Values.Where(spec => spec.IsSeasonBound
            && FieldIo.NodeText(spec.Payload["selected_record_source"]?["selector_field"]) == "CURRENTYEARSTATID"
            && spec.Group is not ("Awards & Honors" or "Awards" or "Internal / Links")).ToArray();
        var names = specs.Select(spec => spec.Key.Split('/').Last()).ToArray();
        var model = session.Model;
        var selector = model.FieldByName("Players", "CURRENTYEARSTATID") ?? throw new InvalidOperationException("统计编号字段不存在");
        var (table, count) = PlayerStatistics.Table(model);
        var payloads = specs.Select(spec => (spec, payload: model.FieldVersionPayload(spec.Entry.Field), offset: FieldIo.FieldOffset(model.FieldVersionPayload(spec.Entry.Field)))).ToArray();
        var rows = new JsonArray();
        int seasonEndYear = SeasonEndYear();
        var year = payloads.Single(item => item.spec.Entry.NormalizedName == "COMPACTYEAR");
        int skipped = 0;
        int wrongSeason = 0;
        foreach (RecordListItem item in LeaguePlayers())
        {
            int id;
            try { id = Conversions.ToInt(model.ReadFieldAt("Players", selector.Section, selector.Field, item.Address).RawValue); }
            catch (Exception exc) when (exc is InvalidOperationException or ArgumentException or FormatException) { skipped++; continue; }
            if (id <= 0 || id == 65535 || id >= count)
            {
                skipped++;
                continue;
            }
            long record = table + id * 64L;
            int compactYear = Conversions.ToInt(FieldIo.ReadAuthoredValue(model.Memory,
                record + year.offset, year.payload));
            if (!LeagueRoster.IsCurrentSeason(compactYear, seasonEndYear))
            {
                skipped++;
                wrongSeason++;
                continue;
            }
            var values = new JsonArray();
            foreach (var (spec, payload, offset) in payloads)
            {
                try
                {
                    if (offset < 0 || offset > 60) { values.Add(null); continue; }
                    var raw = FieldIo.ReadAuthoredValue(model.Memory, record + offset, payload);
                    object? display = FieldIo.RawToDisplayValue(spec.Entry.Section, spec.Entry.Field, payload, raw);
                    values.Add(display is null or string or bool ? null : JsonValue.Create(Convert.ToDouble(display, System.Globalization.CultureInfo.InvariantCulture)));
                }
                catch (Exception exc) when (exc is InvalidOperationException or ArgumentException or FormatException or InvalidCastException)
                {
                    values.Add(null);
                }
            }
            rows.Add(new JsonObject { ["index"] = item.Index, ["values"] = values });
        }        session.Guard();
        return new JsonObject
        {
            ["fields"] = new JsonArray(names.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
            ["rows"] = rows,
            ["skipped"] = skipped,
            ["wrongSeason"] = wrongSeason,
            ["seasonEndYear"] = seasonEndYear,
            ["writes"] = 0,
        };
    }

    /// <summary>Read-only: current league lines matched by the save's season year, across all linked slots.</summary>
    private JsonNode LeagueTeamSeason()
    {
        var history = _history ?? throw new InvalidOperationException("请重新连接游戏");
        var result = new JsonArray();
        int seasonEndYear = SeasonEndYear();
        foreach (RecordListItem team in LeagueTeams())
        {
            var seasons = history.TeamSeasons(team.Index);
            var current = seasons.FirstOrDefault(line =>
                LeagueRoster.IsCurrentSeason(Convert.ToInt32(line.GetValueOrDefault("COMPACTYEAR")), seasonEndYear));
            if (current is null)
            {
                continue;
            }
            var row = new JsonObject { ["index"] = team.Index, ["label"] = team.Label, ["seasonEndYear"] = seasonEndYear };
            foreach (var (key, value) in current)
            {
                row[key] = value is null ? null : JsonValue.Create(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
            }
            result.Add(row);
        }
        return result;
    }
}
