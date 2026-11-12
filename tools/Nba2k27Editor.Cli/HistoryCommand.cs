using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Cli;

internal static class HistoryCommand
{
    internal static int Check(Dictionary<string, string> options)
    {
        var session = new PlayerSession(dataDirectory: options.GetValueOrDefault("data-dir"));
        var report = new JsonObject { ["status"] = "failed", ["format"] = "nba2k27-history-roundtrip/1" };
        var trials = new JsonArray();
        report["trials"] = trials;
        var regions = new Dictionary<long, byte[]>();
        HistorySession? history = null;
        string? active = null;
        try
        {
            session.Connect();
            history = new HistorySession(session);
            report["pid"] = session.Memory.Pid;
            var league = history.Catalog("league");
            report["leagueRecords"] = league.Count;
            report["types"] = new JsonArray(league.GroupBy(i => i.Category).Select(g => (JsonNode?)new JsonObject
                { ["category"] = g.Key, ["count"] = g.Count(), ["minYear"] = g.Min(i => i.Year), ["maxYear"] = g.Max(i => i.Year) }).ToArray());
            var totals = history.Catalog("totals", 0);
            var seasons = history.Catalog("season", 0);
            var franchise = history.Catalog("franchise", 0);
            report["teamSeasons"] = JsonSerializerNode(seasons);
            report["franchise"] = JsonSerializerNode(franchise);
            var chosen = new[]
            {
                (league.First(i => i.Category == HistorySession.Categories[3]).Key, "DATA"),
                (league.First(i => i.Category == HistorySession.Categories[1]).Key, "SCORE2"),
                (league.First(i => i.Category == HistorySession.Categories[1]).Key, "TEAM"),
                (totals[0].Key, "CHAMPIONSHIPS"), (seasons[0].Key, "TOTALPOINTS"),
                (franchise.First(i => i.Summary.Split('–')[0] != i.Summary.Split('–')[1]).Key, "STARTYEAR"),
            };
            foreach (var (key, field) in chosen)
            {
                var read = history.Read(key);
                regions.TryAdd(read.Address, read.Bytes);
                var trial = new JsonObject { ["key"] = key, ["label"] = read.Item.Label, ["field"] = field,
                    ["address"] = $"0x{read.Address:X}", ["before"] = Program.ValueNode(read.Values[field]),
                    ["sharedTeams"] = JsonSerializerNode(read.SharedTeams) };
                trials.Add(trial);
                if (options.ContainsKey("write"))
                {
                    object next = field == "TEAM"
                        ? HistorySession.TeamOption(read.Teams.First(t => HistorySession.TeamOption(t) != (string)read.Values[field]))
                        : Conversions.ToDouble(read.Values[field]) + (field == "SCORE2" && Conversions.ToDouble(read.Values[field]) >= 7 ? -1 : 1);
                    trial["requested"] = Program.ValueNode(next);
                    int writes = ((Nba2k27Editor.Core.Memory.Win32Memory)session.Memory).Writes;
                    var edit = history.Apply(key, read.Token, new Dictionary<string, object?> { [field] = next }, true)
                        ?? throw new InvalidOperationException("No changed bytes");
                    active = key;
                    trial["journal"] = Path.GetFileName(edit.JournalPath);
                    trial["patched"] = edit.Patches.Count > 0;
                    trial["patches"] = edit.Patches.Count;
                    var after = history.Read(key);
                    trial["after"] = Program.ValueNode(after.Values[field]);
                    trial["readbackMatches"] = field == "TEAM" ? Equals(after.Values[field], next)
                        : Math.Abs(Conversions.ToDouble(after.Values[field]) - Conversions.ToDouble(next)) < 0.001;
                    trial["writes"] = ((Nba2k27Editor.Core.Memory.Win32Memory)session.Memory).Writes - writes;
                    history.Undo(key);
                    active = null;
                    trial["restored"] = Program.ValueNode(history.Read(key).Values[field]);
                    trial["undoWrites"] = ((Nba2k27Editor.Core.Memory.Win32Memory)session.Memory).Writes
                        - writes - trial["writes"]!.GetValue<int>();
                    trial["bytesRestored"] = session.Memory.ReadBytes(read.Address, read.Bytes.Length).SequenceEqual(read.Bytes);
                    if (!trial["readbackMatches"]!.GetValue<bool>() || !trial["bytesRestored"]!.GetValue<bool>())
                    {
                        throw new InvalidOperationException("History roundtrip mismatch");
                    }
                }
            }
            report["finalBytesEqual"] = regions.All(p => session.Memory.ReadBytes(p.Key, p.Value.Length).SequenceEqual(p.Value));
            report["comparedBytes"] = regions.Values.Sum(b => b.Length);
            report["writes"] = ((Nba2k27Editor.Core.Memory.Win32Memory)session.Memory).Writes;
            report["status"] = report["finalBytesEqual"]!.GetValue<bool>() ? "ok" : "failed";
            return report["status"]!.GetValue<string>() == "ok" ? 0 : 1;
        }
        catch (Exception exc)
        {
            report["error"] = exc.ToString();
            return 1;
        }
        finally
        {
            if (active is not null && history is not null)
            {
                try { history.Undo(active); report["cleanupUndo"] = true; }
                catch (Exception exc) { report["cleanupError"] = exc.Message; }
            }
            File.WriteAllText(options["out"], report.ToJsonString(Json.Options));
            session.Close();
            Console.WriteLine(options["out"]);
        }
    }

    private static JsonNode? JsonSerializerNode<T>(T value) => System.Text.Json.JsonSerializer.SerializeToNode(value);

    internal static int Probe(Dictionary<string, string> options)
    {
        var session = new PlayerSession(dataDirectory: options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var model = session.Model;
            long roster = model.ResolveBasePointerEntry(model.BasePointerEntry("RosterData"), stopAtNull: true);
            var tables = new JsonArray();
            foreach (var (key, stride) in new[] { ("NBAHistory", 168), ("TeamStatsData", 64), ("TeamHistory", 364) })
            {
                var config = model.BasePointerEntry(key);
                long address = model.ResolveBasePointerEntry(config, stopAtNull: true);
                int count = BitConverter.ToInt32(session.Memory.ReadBytes(roster
                    + Conversions.ToInt(FieldIo.NodeText(config["count_offset"])), 4));
                var samples = new JsonArray();
                if (address != 0 && count > 0 && count < 100000)
                {
                    for (int i = 0; i < Math.Min(count, key == "TeamStatsData" ? 12 : 10000); i++)
                    {
                        var row = new JsonObject { ["index"] = i };
                        if (key != "TeamStatsData")
                        {
                            var entries = model.GroupedFields(key == "NBAHistory" ? "NBA History" : "Teams")
                                .Where(e => key == "NBAHistory" || e.Section == "Team History");
                            foreach (var entry in entries)
                            {
                                var payload = model.FieldVersionPayload(entry.Field);
                                row[entry.NormalizedName] = Program.ValueNode(FieldIo.ReadAuthoredValue(
                                    session.Memory, address + i * (long)stride + FieldIo.FieldOffset(payload), payload));
                            }
                        }
                        else
                        {
                            row["bytes"] = Convert.ToHexString(session.Memory.ReadBytes(address + i * 64L, 64));
                        }
                        samples.Add(row);
                    }
                }
                tables.Add(new JsonObject { ["key"] = key, ["address"] = $"0x{address:X}", ["count"] = count,
                    ["stride"] = stride, ["samples"] = samples });
            }
            var teams = new JsonArray();
            foreach (var team in model.ScanRecords("Teams"))
            {
                var row = new JsonObject { ["index"] = team.Index, ["name"] = team.Label, ["address"] = $"0x{team.Address:X}" };
                foreach (var entry in model.GroupedFields("Teams").Where(e => e.NormalizedName.Contains("SIGNATURE")
                    || FieldIo.NodeText(model.FieldVersionPayload(e.Field)["stat_role"]) == "season_id_selector"))
                {
                    try { row[$"{entry.Section}/{entry.NormalizedName}"] = Program.ValueNode(model.ReadField(entry, team).RawValue); }
                    catch (Exception exc) { row[$"{entry.Section}/{entry.NormalizedName}"] = exc.Message; }
                }
                teams.Add(row);
            }
            var report = new JsonObject { ["pid"] = session.Memory.Pid, ["tables"] = tables, ["teams"] = teams,
                ["writes"] = ((Nba2k27Editor.Core.Memory.Win32Memory)session.Memory).Writes };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options["out"]))!);
            File.WriteAllText(options["out"], report.ToJsonString(Json.Options));
            Console.WriteLine(options["out"]);
            return 0;
        }
        finally { session.Close(); }
    }
}
