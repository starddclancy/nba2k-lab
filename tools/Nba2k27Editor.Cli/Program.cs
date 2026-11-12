using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;
using Nba2k27Editor.Core.Roster;

namespace Nba2k27Editor.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("usage: dump --player <name> --out <file.json>");
                return 2;
            }
            var options = Parse(args.Skip(1).ToArray());
            return args[0] switch
            {
                "dump" => Dump(options),
                "stats" => Stats(options),
                "stats-roundtrip" => StatisticsRoundTrip.Run(options),
                "history-probe" => HistoryCommand.Probe(options),
                "history-check" => HistoryCommand.Check(options),
                "avatar-ids" => AvatarIds(options),
                "ratings-dump" => RatingsDump(options),
                "trade-probe" => TradeProbe(options),
                "draft-picks" => DraftPicksDump(options),
                "trade-plan" => TradePlanDry(options),
                "face-probe" => FaceProbe(options),
                "library-dry" => LibraryDry(options),
                "package-plan" => PackagePlanDry(options),
                "team-colors" => TeamColors(options),
                "schedule-probe" => ScheduleProbe(options),
                "schedule-rows" => ScheduleRows(options),
                "team-seasons" => TeamSeasonsProbe(options),
                "form-scan" => FormScan(options),
                "form-decoded" => FormDecoded(options),
                "league-season" => LeagueSeasonProbe(options),
                "restore-player-journal" => RestorePlayerJournal(options),
                "player-types" => PlayerTypes(options),
                "team-history-fields" => TeamHistoryFields(options),
                "stat-manager-probe" => StatManagerProbe.Run(options),
                "retired-jerseys" => StatManagerProbe.Jerseys(options),
                "find-u32" => StatManagerProbe.FindValue(options),
                "box-probe" => StatManagerProbe.Box(options),
                "jersey-history" => StatManagerProbe.JerseyHistory(options),
                "game-headers" => StatManagerProbe.Games(options),
                "find-pointer" => StatManagerProbe.FindPointer(options),
                "game-record" => StatManagerProbe.GameRecord(options),
                "team-pairs" => StatManagerProbe.TeamPairs(options),
                "game-list" => StatManagerProbe.GameList(options),
                "pointer-chain" => StatManagerProbe.Chain(options),
                "games-check" => StatManagerProbe.GamesCheck(options),
                "games-candidates" => StatManagerProbe.Candidates(options),
                "games-root-probe" => StatManagerProbe.NativeRoot(options),
                "parents" => ParentProbe.Run(options),
                "list" => ListPlayers(options),
                "teams" => ListTeams(options),
                "find" => Find(options),
                "draft" => Draft(options),
                "prospects" => Prospects(options),
                "scan" => Scan(options),
                "locate" => Locate(options),
                "probe" => Probe(),
                "roundtrip" => RoundTrip.Run(options),
                "set" => Set(options),
                "roster-move" => RosterMoveCommand.Run(options),
                "gs" => GsCommand.Run(options),
                "gs-legacy" => LegacyGsCommand.Run(options),
                "gs-preset" => GsPresetCommand.Run(options),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception exc)
        {
            Console.Error.WriteLine(exc);
            return 1;
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"unknown command: {command}");
        return 2;
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"unexpected argument: {args[index]}");
            }
            var key = args[index][2..];
            // A flag with no value is stored as "true"; existing commands always pass a value,
            // so their parsing is unchanged.
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[key] = "true";
                continue;
            }
            options[key] = args[++index];
        }
        return options;
    }

    private static int Dump(Dictionary<string, string> options)
    {
        options.TryGetValue("player", out var playerName);
        if ((string.IsNullOrEmpty(playerName) && !options.ContainsKey("index"))
            || !options.TryGetValue("out", out var outPath))
        {
            Console.Error.WriteLine("dump requires --player or --index, and --out");
            return 2;
        }
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            int? index = options.TryGetValue("index", out var indexText)
                ? int.Parse(indexText, CultureInfo.InvariantCulture) : null;
            var matches = items.Where(item => (index is null || item.Index == index)
                && (string.IsNullOrEmpty(playerName) || item.Label == playerName)).ToList();
            if (matches.Count != 1)
            {
                Console.Error.WriteLine($"player selection must match exactly once: {playerName} index={index} ({matches.Count})");
                return 3;
            }
            var snapshot = session.ReadPlayer(matches[0]);
            var document = new JsonObject
            {
                ["version"] = AppVersion.Value,
                ["module_base"] = Hex(session.Memory.BaseAddress),
                ["pid"] = session.Memory.Pid,
                ["stride"] = session.Model.DomainStride("Players"),
                ["player_count"] = items.Count,
                ["spec_count"] = session.Specs.Count,
                ["epoch"] = new JsonArray(snapshot.Epoch.ToJsonArray().Select(value => JsonValue.Create(value)).ToArray()),
                ["first_items"] = ItemsNode(items.Take(10)),
                ["last_items"] = ItemsNode(items.Skip(Math.Max(0, items.Count - 5))),
                ["player"] = new JsonObject
                {
                    ["label"] = snapshot.Item.Label,
                    ["index"] = snapshot.Item.Index,
                    ["address"] = Hex(snapshot.Item.Address),
                    ["source"] = snapshot.Source,
                    ["identity"] = Convert.ToHexString(snapshot.Identity).ToLowerInvariant(),
                    ["fields"] = FieldsNode(snapshot.Values),
                },
                ["errors"] = ErrorsNode(snapshot.Errors),
                ["draft_status"] = session.RosterIndex.DraftStatus,
                ["teams"] = session.RosterIndex.Teams.Count,
                ["team_members_total"] = session.RosterIndex.Teams.Sum(team => team.Players.Count),
                ["team_sample"] = TeamSample(session.RosterIndex),
                ["warnings"] = new JsonArray(session.RosterIndex.Warnings.Select(text => (JsonNode?)JsonValue.Create(text)).ToArray()),
                ["writes"] = 0,
            };
            File.WriteAllText(outPath, document.ToJsonString(Json.Options), new UTF8Encoding(false));
            Console.WriteLine($"wrote {outPath}: {snapshot.Values.Count} fields, {items.Count} players");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }

    private static int Stats(Dictionary<string, string> options)
    {
        int index = int.Parse(options["index"], CultureInfo.InvariantCulture);
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var item = session.Items.Single(player => player.Index == index);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var read = PlayerStatistics.Read(session, item);
            var table = PlayerStatistics.Table(session.Model);
            var document = new JsonObject
            {
                ["pid"] = session.Memory.Pid,
                ["name"] = item.Label,
                ["index"] = index,
                ["table"] = Hex(table.Address),
                ["count"] = table.Count,
                ["elapsedMs"] = timer.Elapsed.TotalMilliseconds,
                ["seasons"] = new JsonArray(read.Seasons.Select(season => (JsonNode?)new JsonObject
                {
                    ["selector"] = season.Selector,
                    ["id"] = season.Id,
                    ["address"] = Hex(season.Address),
                    ["fields"] = FieldsNode(read.Snapshot.Values.Where(pair => season.Keys.Contains(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value)),
                }).ToArray()),
                ["direct"] = FieldsNode(read.Snapshot.Values.Where(pair => !session.Specs[pair.Key].IsSeasonBound)
                    .ToDictionary(pair => pair.Key, pair => pair.Value)),
                ["warnings"] = new JsonArray(read.Warnings.Select(text => (JsonNode?)JsonValue.Create(text)).ToArray()),
                ["errors"] = ErrorsNode(read.Snapshot.Errors),
                ["writes"] = session.Memory is Win32Memory native ? native.Writes : 0,
            };
            File.WriteAllText(options["out"], document.ToJsonString(Json.Options), new UTF8Encoding(false));
            Console.WriteLine($"{item.Label}: {read.Seasons.Count} seasons, {read.Snapshot.Values.Count} fields, "
                + $"{read.Snapshot.Errors.Count} errors, {timer.Elapsed.TotalMilliseconds:F0} ms; {options["out"]}");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>
    /// Read-only lookup: where does a name live in the Players table, and is it on a team?
    /// This answers "I cannot find this player in the game" without guessing.
    /// </summary>
    private static int Find(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("name", out var name) || name.Length == 0)
        {
            Console.Error.WriteLine("find requires --name <text>");
            return 2;
        }
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            var matches = items
                .Where(item => item.Label.Contains(name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var mover = new RosterMover(session);
            var teams = mover.TeamsByAddress();
            bool showSlots = options.ContainsKey("slots");
            var rows = new JsonArray();
            Console.WriteLine($"pid {session.Memory.Pid}, roster {items.Count}, teams {session.RosterIndex.Teams.Count}, "
                + $"matches for \"{name}\": {matches.Count}");
            foreach (var item in matches.Take(50))
            {
                string placement;
                string? teamIndex = null;
                try
                {
                    var place = mover.Placement(item);
                    placement = $"{place.TeamLabel} slot {place.Slot}";
                    teamIndex = $"[team {place.TeamIndex}]";
                    if (showSlots && teams.TryGetValue(place.TeamAddress, out var team))
                    {
                        var slots = mover.TeamSlots(team);
                        var labels = slots.Entries
                            .Select((entry, index) => slots.Values[index] == 0
                                ? $"{entry.Slot}:<empty>"
                                : $"{entry.Slot}:{items.FirstOrDefault(candidate => candidate.Address == slots.Values[index])?.Label ?? "?"}")
                            .ToList();
                        placement += "  " + string.Join(" | ", labels);
                    }
                }
                catch (RosterMoveException exc)
                {
                    placement = exc.Message;
                }
                Console.WriteLine($"  index {item.Index,5}  {Hex(item.Address)}  {item.Label}  -> {placement}");
                rows.Add(new JsonObject
                {
                    ["index"] = item.Index,
                    ["address"] = item.Address,
                    ["label"] = item.Label,
                    ["team_index"] = teamIndex,
                    ["placement"] = placement,
                });
            }
            if (options.TryGetValue("out", out var outPath))
            {
                var document = new JsonObject
                {
                    ["format"] = "nba2k27-editor-find/1",
                    ["pid"] = session.Memory.Pid,
                    ["roster"] = items.Count,
                    ["teams"] = session.RosterIndex.Teams.Count,
                    ["query"] = name,
                    ["matches"] = rows,
                };
                File.WriteAllText(
                    outPath,
                    document.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n",
                    new UTF8Encoding(false));
            }
            return 0;
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>
    /// Read-only diagnosis of the draft class root: shows the recovered roots, the pointer slot
    /// value and the runtime container/count, so "not loaded" can be told apart from "wrong root".
    /// </summary>
    /// <summary>
    /// Read-only UTF-16 search over the game's private heap. Used to locate runtime tables that
    /// the static pointer slots do not currently expose (for example a loaded draft class).
    /// </summary>
    /// <summary>
    /// Reverse-engineers the table container from one known record address: walks the candidate
    /// record indexes backwards and keeps the containers whose leading count is plausible.
    /// </summary>
    private static int Locate(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("record", out var recordText))
        {
            Console.Error.WriteLine("locate requires --record <address>");
            return 2;
        }
        long record = ParseAddress(recordText);
        int stride = options.TryGetValue("stride", out var strideText) ? int.Parse(strideText, CultureInfo.InvariantCulture) : 2360;
        int bodyOffset = options.TryGetValue("body", out var bodyText) ? int.Parse(bodyText, CultureInfo.InvariantCulture) : 8;
        int capacity = options.TryGetValue("capacity", out var capText) ? int.Parse(capText, CultureInfo.InvariantCulture) : 150;
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var memory = session.Memory as Win32Memory;
            if (memory is null)
            {
                Console.Error.WriteLine("locate requires the live Win32 memory backend");
                return 3;
            }
            Console.WriteLine($"record {Hex(record)}, stride {stride}, body {bodyOffset}, capacity {capacity}");
            string NameAt(long address)
            {
                try
                {
                    byte[] raw = memory.ReadBytes(address, 80);
                    string text = Encoding.Unicode.GetString(raw).TrimEnd('\0');
                    bool ok = text.Length > 0 && text.All(ch => ch == '\0' || (ch >= ' ' && ch < '\uFFFE'));
                    return ok ? text : "";
                }
                catch (InvalidOperationException)
                {
                    return "";
                }
            }

            string self = NameAt(record);
            Console.WriteLine($"  name at record: \"{self}\"");
            // Walk the stride in both directions while records still look like names, so the
            // table extent is derived from data instead of assumed.
            long first = record;
            int before = 0;
            while (before < capacity)
            {
                string previous = NameAt(first - stride);
                if (previous.Length == 0)
                {
                    break;
                }
                first -= stride;
                before++;
            }
            long last = record;
            int after = 0;
            while (after < capacity)
            {
                string next = NameAt(last + stride);
                if (next.Length == 0)
                {
                    break;
                }
                last += stride;
                after++;
            }
            Console.WriteLine($"  contiguous records: first {Hex(first)} .. last {Hex(last)}, "
                + $"index of probe {before}, total {before + after + 1}");
            Console.WriteLine($"  first name \"{NameAt(first)}\", last name \"{NameAt(last)}\"");
            long body = first;
            long container = body - bodyOffset;
            try
            {
                int count = BitConverter.ToInt32(memory.ReadBytes(container, 4), 0);
                Console.WriteLine($"  inferred container {Hex(container)} leading int32 {count}");
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine($"  inferred container {Hex(container)} unreadable");
            }
            foreach (long delta in new long[] { -8, -16, -24, 8, 16 })
            {
                try
                {
                    int value = BitConverter.ToInt32(memory.ReadBytes(container + delta, 4), 0);
                    Console.WriteLine($"    int32 at container{delta:+#;-#;0} = {value}");
                }
                catch (InvalidOperationException)
                {
                    Console.WriteLine($"    int32 at container{delta:+#;-#;0} unreadable");
                }
            }
            for (int index = 0; index < capacity; index++)
            {
                long candidate = record - bodyOffset - (long)index * stride;
                if (candidate <= 0)
                {
                    break;
                }
                int count;
                try
                {
                    count = BitConverter.ToInt32(memory.ReadBytes(candidate, 4), 0);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                if (count < 1 || count > capacity || index >= count)
                {
                    continue;
                }
                string firstName = NameAt(candidate + bodyOffset);
                Console.WriteLine($"  candidate container {Hex(candidate)} count {count} index {index} "
                    + $"first_record_name=\"{firstName}\"");
            }
            return 0;
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>
    /// Applies one field and leaves it applied (no undo), journaling the patches so the change
    /// can still be traced or reverted later.
    /// </summary>
    private static int Set(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("field", out var fieldKey)
            || !options.TryGetValue("value", out var valueText)
            || !options.TryGetValue("out", out var outPath))
        {
            Console.Error.WriteLine("set requires --field, --value and --out, plus --player or --index");
            return 2;
        }
        int? indexFilter = options.TryGetValue("index", out var indexText)
            ? int.Parse(indexText, CultureInfo.InvariantCulture)
            : null;
        options.TryGetValue("player", out var playerName);
        if (indexFilter is null && string.IsNullOrEmpty(playerName))
        {
            Console.Error.WriteLine("set requires --player or --index");
            return 2;
        }
        object value = double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric)
            ? numeric
            : valueText;
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            var matches = indexFilter is int wanted
                ? items.Where(item => item.Index == wanted).ToList()
                : items.Where(item => item.Label == playerName).ToList();
            if (matches.Count != 1)
            {
                Console.Error.WriteLine($"selection must match exactly one record: {matches.Count}");
                return 3;
            }
            var target = matches[0];
            var snapshot = session.ReadPlayer(target);
            if (!snapshot.Values.TryGetValue(fieldKey, out var before))
            {
                Console.Error.WriteLine($"field was not read: {fieldKey}");
                return 4;
            }
            var report = new JsonObject
            {
                ["format"] = "nba2k27-editor-set/1",
                ["version"] = AppVersion.Value,
                ["time"] = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                ["player"] = target.Label,
                ["index"] = target.Index,
                ["address"] = Hex(target.Address),
                ["field"] = fieldKey,
                ["display_before"] = Program.ValueNode(before.Display),
                ["raw_before"] = Program.ValueNode(before.Raw),
                ["requested"] = valueText,
            };
            var transaction = session.Apply(snapshot, new Dictionary<string, object?> { [fieldKey] = value });
            if (transaction is null)
            {
                Console.Error.WriteLine("no patch rows were produced; nothing was written");
                return 5;
            }
            report["patches"] = transaction.Patches.Count;
            report["journal"] = transaction.JournalPath;
            report["transaction_status"] = transaction.Status;
            report["writer_writes"] = session.Memory is Win32Memory writer ? writer.Writes : 0;
            var rows = new JsonArray();
            foreach (var row in transaction.Patches)
            {
                rows.Add(new JsonObject
                {
                    ["address"] = Hex(row.Address),
                    ["before"] = row.Before,
                    ["after"] = row.After,
                    ["mask"] = row.Mask,
                });
            }
            report["patch_rows"] = rows;
            // Independent read-back so the applied value is confirmed by a second connection.
            var verifier = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
            try
            {
                var verifierItems = verifier.Connect();
                var verified = verifierItems.FirstOrDefault(item =>
                    item.Address == target.Address && item.Index == target.Index);
                if (verified is null)
                {
                    report["status"] = "verifier_cannot_find_player";
                }
                else
                {
                    var after = verifier.ReadPlayer(verified);
                    report["applied_display"] = Program.ValueNode(after.Values[fieldKey].Display);
                    report["applied_raw"] = Program.ValueNode(after.Values[fieldKey].Raw);
                    report["verifier_writes"] = verifier.Memory is Win32Memory observer ? observer.Writes : 0;
                    report["status"] = "applied";
                }
            }
            finally
            {
                verifier.Close();
            }
            File.WriteAllText(outPath, report.ToJsonString(Program.JsonOptions), new UTF8Encoding(false));
            Console.WriteLine($"{target.Label} {fieldKey}: {Conversions.ToDouble(before.Display):g} -> "
                + $"{report["applied_display"]} (patches {transaction.Patches.Count}, journal {transaction.JournalPath})");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }

    private static long ParseAddress(string text)
    {
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(text, CultureInfo.InvariantCulture);
    }

    private static int Scan(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("text", out var text) || text.Length < 3)
        {
            Console.Error.WriteLine("scan requires --text <at least 3 characters>");
            return 2;
        }
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var memory = session.Memory as Win32Memory;
            if (memory is null)
            {
                Console.Error.WriteLine("scan requires the live Win32 memory backend");
                return 3;
            }
            if (options.TryGetValue("at", out var atText))
            {
                long at = atText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? long.Parse(atText[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    : long.Parse(atText, CultureInfo.InvariantCulture);
                Console.WriteLine($"region for {Hex(at)}:");
                foreach (var region in memory.PrivateRegions())
                {
                    if (at >= region.Base && at < region.Base + region.Size)
                    {
                        Console.WriteLine($"  base {Hex(region.Base)} size {region.Size} offset {Hex(at - region.Base)}");
                        break;
                    }
                }
                try
                {
                    Console.WriteLine($"  bytes: {HexPreview(memory.ReadBytes(at, 64))}");
                }
                catch (InvalidOperationException exc)
                {
                    Console.WriteLine($"  read failed: {exc.Message}");
                }
            }
            long? near = null;
            if (options.TryGetValue("near", out var nearText))
            {
                near = nearText.Equals("auto", StringComparison.OrdinalIgnoreCase)
                    ? session.Connect()[0].Address
                    : (nearText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? long.Parse(nearText[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                        : long.Parse(nearText, CultureInfo.InvariantCulture));
            }
            long maxBytes = options.TryGetValue("max-mb", out var maxText)
                ? long.Parse(maxText, CultureInfo.InvariantCulture) * 1024L * 1024L
                : 768L * 1024 * 1024;
            var hits = memory.FindUtf16(text, 32, maxBytes, near);
            var rows = new JsonArray();
            Console.WriteLine($"pid {memory.Pid}, base {Hex(memory.BaseAddress)}, hits {hits.Count} for \"{text}\"");
            if (options.ContainsKey("stats"))
            {
                var regions = memory.PrivateRegions();
                long total = 0;
                foreach (var region in regions)
                {
                    total += region.Size;
                }
                Console.WriteLine($"regions {regions.Count}, total {total / (1024 * 1024)} MB");
                int readable = 0;
                int failed = 0;
                foreach (var region in regions.Take(8))
                {
                    try
                    {
                        memory.ReadBytes(region.Base, 16);
                        readable++;
                    }
                    catch (InvalidOperationException)
                    {
                        failed++;
                    }
                    Console.WriteLine($"  {Hex(region.Base)} size {region.Size / (1024 * 1024)} MB");
                }
                Console.WriteLine($"probe of first 8 regions: readable {readable}, failed {failed}");
            }
            foreach (long hit in hits)
            {
                string before;
                string after;
                try
                {
                    before = HexPreview(memory.ReadBytes(Math.Max(0, hit - 64), 64));
                }
                catch (InvalidOperationException)
                {
                    before = "<unreadable>";
                }
                try
                {
                    after = HexPreview(memory.ReadBytes(hit, 128));
                }
                catch (InvalidOperationException)
                {
                    after = "<unreadable>";
                }
                Console.WriteLine($"  {Hex(hit)}  before={before}  at={after}");
                rows.Add(new JsonObject
                {
                    ["address"] = Hex(hit),
                    ["offset_in_module"] = hit - memory.BaseAddress,
                    ["bytes_before"] = before,
                    ["bytes_at"] = after,
                });
            }
            if (options.TryGetValue("out", out var outPath))
            {
                var document = new JsonObject
                {
                    ["format"] = "nba2k27-editor-memory-scan/1",
                    ["pid"] = memory.Pid,
                    ["module_base"] = Hex(memory.BaseAddress),
                    ["text"] = text,
                    ["writes"] = memory.Writes,
                    ["hits"] = rows,
                };
                File.WriteAllText(
                    outPath,
                    document.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n",
                    new UTF8Encoding(false));
            }
            return 0;
        }
        finally
        {
            session.Close();
        }
    }

    private static string HexPreview(byte[] data)
    {
        var builder = new StringBuilder();
        foreach (byte value in data)
        {
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }

    private static int Prospects(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            session.Guard();
            var catalog = ProspectBrowser.Read(session.Model, session.Items);
            session.Guard();
            var rows = new JsonArray();
            foreach (var row in catalog.Records.Where(row => row.IsProspect))
            {
                rows.Add(new JsonObject
                {
                    ["index"] = row.Item.Index, ["name"] = row.Item.Label,
                    ["prospectIndex"] = row.ProspectIndex, ["draftYear"] = row.DraftYear,
                    ["round"] = row.Round, ["pick"] = row.Pick, ["birthYear"] = row.BirthYear,
                    ["active"] = row.Active, ["playerType"] = row.PlayerType,
                });
            }
            var groups = new JsonArray();
            foreach (var group in catalog.Records.GroupBy(row => (row.ProspectIndex == 255, row.Round,
                row.Pick, row.Active, row.PlayerType)).OrderByDescending(group => group.Count()))
            {
                groups.Add(new JsonObject { ["key"] = group.Key.ToString(), ["count"] = group.Count() });
            }
            var report = new JsonObject
            {
                ["version"] = AppVersion.Value, ["pid"] = session.Memory.Pid,
                ["roster"] = session.Items.Count, ["records"] = catalog.Records.Count,
                ["errors"] = System.Text.Json.JsonSerializer.SerializeToNode(catalog.Errors),
                ["groups"] = groups, ["items"] = rows,
                ["writes"] = session.Memory is Win32Memory memory ? memory.Writes : 0,
            };
            if (options.TryGetValue("out", out var path))
            {
                File.WriteAllText(path, report.ToJsonString(Json.Options), new UTF8Encoding(false));
            }
            Console.WriteLine($"Players draft candidates: {rows.Count}; read errors: {catalog.Errors.Count}");
            Console.WriteLine(groups.ToJsonString(Json.Options));
            return 0;
        }
        finally
        {
            session.Close();
        }
    }

    private static int Draft(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            var model = session.Model;
            var report = new JsonObject
            {
                ["format"] = "nba2k27-editor-draft-probe/1",
                ["pid"] = session.Memory.Pid,
                ["module_base"] = Hex(session.Memory.BaseAddress),
                ["roster"] = items.Count,
                ["draft_players"] = session.RosterIndex.DraftItems.Count,
                ["draft_status"] = session.RosterIndex.DraftStatus,
            };
            var roots = new JsonObject();
            foreach (var pair in model.RecoveredRoots)
            {
                roots[pair.Key] = pair.Value;
            }
            report["recovered_roots"] = roots;
            Console.WriteLine($"pid {session.Memory.Pid}, draft_players {session.RosterIndex.DraftItems.Count}");
            Console.WriteLine($"status: {session.RosterIndex.DraftStatus}");
            foreach (var pair in model.RecoveredRoots)
            {
                Console.WriteLine($"  root {pair.Key} = {Hex(pair.Value)}");
            }
            try
            {
                var source = RosterBrowser.DraftSource(model);
                report["root"] = Hex(source.Root);
                long container = BitConverter.ToInt64(session.Memory.ReadBytes(source.Root, 8), 0);
                report["container"] = Hex(container);
                Console.WriteLine($"  root slot {Hex(source.Root)} -> container {Hex(container)}");
                if (container != 0)
                {
                    int count = BitConverter.ToInt32(session.Memory.ReadBytes(container, 4), 0);
                    report["count"] = count;
                    Console.WriteLine($"  container count {count}");
                }
            }
            catch (Exception exc)
            {
                report["error"] = exc.Message;
                Console.WriteLine($"  source error: {exc.Message}");
            }
            if (options.TryGetValue("out", out var outPath))
            {
                File.WriteAllText(
                    outPath,
                    report.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n",
                    new UTF8Encoding(false));
            }
            return 0;
        }
        finally
        {
            session.Close();
        }
    }

    private static int Probe()
    {
        var session = new PlayerSession("NBA2K27.exe");
        try
        {
            if (!session.Model.RecoverBasePointers())
            {
                Console.WriteLine($"recover failed: {session.Model.LastStatus}");
            }
            Console.WriteLine($"module base {Hex(session.Memory.BaseAddress)} pointer size {session.Memory.PointerSize}");
            foreach (var key in new[] { "GameDataStore", "DraftClass", "AITuning", "Staff", "RosterData" })
            {
                try
                {
                    var entry = session.Model.BasePointerEntry(key);
                    long baseAddress = session.Model.BaseAddressForKey(key);
                    string stride;
                    try
                    {
                        stride = session.Model.DomainStride(key).ToString(CultureInfo.InvariantCulture);
                    }
                    catch (KeyNotFoundException)
                    {
                        stride = "-";
                    }
                    Console.WriteLine($"{key,-14} entry={entry.ToJsonString()} base={Hex(baseAddress)} stride={stride}");
                }
                catch (Exception exc)
                {
                    Console.WriteLine($"{key,-14} error: {exc.Message}");
                }
            }
            return 0;
        }
        finally
        {
            session.Close();
        }
    }

    private static int ListPlayers(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            Console.WriteLine($"{items.Count} players, stride {session.Model.DomainStride("Players")}");
            foreach (var item in items.Take(5))
            {
                Console.WriteLine($"  {item.Index} {Hex(item.Address)} {item.Label}");
            }
            Console.WriteLine($"draft: {session.RosterIndex.DraftStatus}");
            Console.WriteLine($"teams: {session.RosterIndex.Teams.Count}");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>
    /// Read-only dump of every player's decoded attributes plus the overall the game already
    /// cached, for fitting the builder's overall formula offline. Never triggers a recalculation.
    /// </summary>
    private static int RatingsDump(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("out", out string? outPath))
        {
            Console.Error.WriteLine("ratings-dump requires --out <file.json>");
            return 2;
        }
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            string[] groups = { "Attributes/Offense/", "Attributes/Defense/", "Attributes/Athleticism/",
                "Attributes/Mental/", "Attributes/Misc/" };
            string[] keys = session.Specs.Keys.Where(key => groups.Any(key.StartsWith)
                && !key.Contains("CACHED", StringComparison.Ordinal) && !key.EndsWith("OVR", StringComparison.Ordinal))
                .Append("Vitals/Vitals/POSITION").Append("Vitals/Body/HEIGHT").Append("Vitals/Body/WEIGHT")
                .Where(session.Specs.ContainsKey).Distinct().OrderBy(key => key, StringComparer.Ordinal).ToArray();
            var rows = new JsonArray();
            int skipped = 0;
            foreach (RecordListItem item in items)
            {
                int? overall = PlayerSummary.ReadOverall(session, item);
                if (overall is null)
                {
                    skipped++;
                    continue;
                }
                PlayerSnapshot snapshot = session.ReadPlayer(item, keys);
                var values = new JsonObject();
                foreach (string key in keys)
                {
                    if (snapshot.Values.TryGetValue(key, out FieldValue? value))
                    {
                        values[key] = Program.ValueNode(value.Display);
                    }
                }
                rows.Add(new JsonObject { ["index"] = item.Index, ["name"] = item.Label, ["overall"] = overall, ["values"] = values });
            }
            var report = new JsonObject
            {
                ["format"] = "nba2k27-ratings-dump/1",
                ["pid"] = session.Memory.Pid,
                ["records"] = items.Count,
                ["withCachedOverall"] = rows.Count,
                ["skippedNoCachedOverall"] = skipped,
                ["fields"] = new JsonArray(keys.Select(key => (JsonNode?)JsonValue.Create(key)).ToArray()),
                ["writes"] = 0,
                ["players"] = rows,
            };
            File.WriteAllText(outPath, report.ToJsonString(Json.Options), new UTF8Encoding(false));
            Console.WriteLine($"wrote {outPath}: {rows.Count} players with cached overall, {skipped} skipped, {keys.Length} fields, writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>
    /// Read-only survey for trade linkage: draft pick ownership (DraftData), team payroll and
    /// contract fields of a few players. Never writes.
    /// </summary>
    private static int TradeProbe(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("out", out string? outPath))
        {
            Console.Error.WriteLine("trade-probe requires --out <file.json>");
            return 2;
        }
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            var model = session.Model;
            var teams = model.ScanRecords("Teams");
            var teamByAddress = new Dictionary<long, RecordListItem>();
            foreach (var team in teams) { teamByAddress.TryAdd(team.Address, team); }
            string TeamName(long address) => address == 0 ? "(none)"
                : teamByAddress.TryGetValue(address, out var team) ? team.Label : $"? 0x{address:X}";
            var report = new JsonObject { ["format"] = "nba2k27-trade-probe/1", ["pid"] = session.Memory.Pid, ["writes"] = 0 };

            long roster = model.ResolveBasePointerEntry(model.BasePointerEntry("RosterData"), stopAtNull: true);
            var config = model.BasePointerEntry("DraftData");
            int count = BitConverter.ToInt32(model.Memory.ReadBytes(roster + Conversions.ToInt(FieldIo.NodeText(config["count_offset"])), 4));
            long table = BitConverter.ToInt64(model.Memory.ReadBytes(roster + Conversions.ToInt(FieldIo.NodeText(config["pointer_offset"])), 8));
            var playerByAddress = new Dictionary<long, RecordListItem>();
            foreach (var item in items) { playerByAddress.TryAdd(item.Address, item); }
            var picks = new JsonArray();
            var owned = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int traded = 0, assigned = 0;
            for (int index = 0; index < Math.Min(count, 2000); index++)
            {
                byte[] row = model.Memory.ReadBytes(table + index * 32L, 32);
                long current = BitConverter.ToInt64(row, 0), original = BitConverter.ToInt64(row, 8);
                long player = BitConverter.ToInt64(row, 16);
                ulong meta = BitConverter.ToUInt64(row, 24);
                traded += current != original ? 1 : 0;
                assigned += player != 0 ? 1 : 0;
                owned[TeamName(current)] = owned.GetValueOrDefault(TeamName(current)) + 1;
                if (true)
                {
                    picks.Add(new JsonObject
                    {
                        ["index"] = index, ["current"] = TeamName(current), ["original"] = TeamName(original),
                        ["player"] = player == 0 ? null : playerByAddress.TryGetValue(player, out var who) ? who.Label : $"? 0x{player:X}",
                        ["meta"] = $"0x{meta:X16}",
                        ["playerDraft"] = player != 0 && playerByAddress.TryGetValue(player, out var drafted)
                            ? string.Join("/", new[] { "YEARDRAFTEDINTERNAL", "ROUNDDRAFTED", "DRAFTPICKNUMBER", "SHUFFLEDDRAFTYEAR" }.Select(name =>
                            {
                                var entry = model.FieldByName("Players", name);
                                try { return entry is null ? "-" : Convert.ToString(model.ReadField(entry, drafted).RawValue, CultureInfo.InvariantCulture); }
                                catch (Exception) { return "!"; }
                            }))
                            : null,
                    });
                }
            }
            var ownedNode = new JsonObject();
            foreach (var pair in owned) { ownedNode[pair.Key] = pair.Value; }
            report["draftData"] = new JsonObject
            {
                ["rosterData"] = $"0x{roster:X}", ["table"] = $"0x{table:X}", ["count"] = count,
                ["tradedPicks"] = traded, ["assignedPlayers"] = assigned, ["ownedPerTeam"] = ownedNode, ["rows"] = picks,
            };

            var payroll = new JsonArray();
            foreach (var team in teams.Take(32))
            {
                var row = new JsonObject { ["team"] = team.Label };
                foreach (string name in new[] { "PLAYERPAYROLL", "SALARY", "STAFFPAYROLL" })
                {
                    var entry = model.FieldByName("Teams", name);
                    try { row[name] = entry is null ? null : Program.ValueNode(model.ReadField(entry, team).RawValue); }
                    catch (Exception exc) { row[name] = "error: " + exc.Message; }
                }
                payroll.Add(row);
            }
            // Does the team SALARY field equal the sum of its contracted players' current salary?
            var contractEntry = model.FieldByName("Players", "CONTRACTTEAMINTERNAL");
            var currentEntry = model.FieldByName("Players", "CURRENTTEAMINTERNAL");
            var salaryEntry = model.FieldByName("Players", "SALARYYEAR1");
            var byContract = new Dictionary<long, long>();
            var byCurrent = new Dictionary<long, long>();
            foreach (var item in items)
            {
                try
                {
                    long salary = Convert.ToInt64(model.ReadField(salaryEntry!, item).RawValue);
                    long contractTeam = Convert.ToInt64(model.ReadField(contractEntry!, item).RawValue);
                    long currentTeam = Convert.ToInt64(model.ReadField(currentEntry!, item).RawValue);
                    byContract[contractTeam] = byContract.GetValueOrDefault(contractTeam) + salary;
                    byCurrent[currentTeam] = byCurrent.GetValueOrDefault(currentTeam) + salary;
                }
                catch (Exception)
                {
                }
            }
            foreach (var node in payroll)
            {
                var row = (JsonObject)node!;
                var team = teams.First(item => item.Label == row["team"]!.GetValue<string>());
                row["sumByContractTeam"] = byContract.GetValueOrDefault(team.Address);
                row["sumByCurrentTeam"] = byCurrent.GetValueOrDefault(team.Address);
            }
            report["teamPayroll"] = payroll;

            string[] contractNames = { "CONTRACTTEAMINTERNAL", "CURRENTTEAMINTERNAL", "CONTRACTYEAR1", "CONTRACTYEAR2",
                "SALARYYEAR1", "SALARYYEAR2", "YEARSLEFT", "CONTRACTLENGTH", "BIRDYEARS", "NOTRADE", "OPTION", "FASTATUS", "LASTYEARWAGE" };
            var contracts = new JsonArray();
            foreach (var item in items.Take(400).Where((_, index) => index % 40 == 0))
            {
                var row = new JsonObject { ["player"] = item.Label };
                foreach (string name in contractNames)
                {
                    var entry = model.FieldByName("Players", name);
                    try
                    {
                        object? raw = entry is null ? null : model.ReadField(entry, item).RawValue;
                        row[name] = name.EndsWith("INTERNAL", StringComparison.Ordinal) && raw is not null
                            ? TeamName(Convert.ToInt64(raw)) : Program.ValueNode(raw);
                    }
                    catch (Exception exc) { row[name] = "error: " + exc.Message; }
                }
                contracts.Add(row);
            }
            report["contracts"] = contracts;
            File.WriteAllText(outPath, report.ToJsonString(Json.Options), new UTF8Encoding(false));
            Console.WriteLine($"wrote {outPath}: {count} draft picks ({traded} traded, {assigned} assigned), {payroll.Count} teams, writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only listing of decoded draft picks, as the trade panel shows them.</summary>
    private static int DraftPicksDump(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            var teams = new Dictionary<long, string>();
            foreach (var team in session.Model.ScanRecords("Teams")) { teams.TryAdd(team.Address, team.Label); }
            var views = Nba2k27Editor.Core.Roster.DraftPickTable.Describe(session.Model, items);
            var lines = new List<string>();
            foreach (var view in views)
            {
                var pick = view.Pick;
                lines.Add(string.Join("\t", pick.Index, view.Year?.ToString(CultureInfo.InvariantCulture) ?? "?", pick.Round, pick.Order,
                    teams.GetValueOrDefault(pick.Owner, "?"), teams.GetValueOrDefault(pick.Original, "?"),
                    view.PlayerLabel ?? "", view.HasTerms ? "terms" : ""));
            }
            string text = "index\tyear\tround\torder\towner\toriginal\tplayer\tterms\n" + string.Join("\n", lines) + "\n";
            if (options.TryGetValue("out", out string? outPath))
            {
                File.WriteAllText(outPath, text, new UTF8Encoding(false));
            }
            Console.WriteLine($"{views.Count} owned picks, writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Plan (never apply) a trade with picks and payroll sync; prints the linked changes.</summary>
    private static int TradePlanDry(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            var mover = new Nba2k27Editor.Core.Roster.RosterMover(session);
            RecordListItem Find(string name) => items.First(item => item.Label == name);
            int[] Picks(string key) => options.TryGetValue(key, out string? text) && text.Length > 0
                ? text.Split(',').Select(int.Parse).ToArray() : Array.Empty<int>();
            int before = session.Memory is Nba2k27Editor.Core.Memory.Win32Memory live ? live.Writes : -1;
            var plan = mover.PlanTrade(Find(options["first"]), Find(options["second"]),
                Picks("first-picks"), Picks("second-picks"), syncPayroll: true);
            int after = session.Memory is Nba2k27Editor.Core.Memory.Win32Memory live2 ? live2.Writes : -1;
            Console.WriteLine(plan.Summary);
            Console.WriteLine($"patch bytes: {plan.Rows.Count}; writes during planning: {after - before}");
            foreach (string line in plan.Details)
            {
                Console.WriteLine("  " + line);
            }
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: face-related IDs and flags for every player, to find the face asset key.</summary>
    private static int FaceProbe(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            string[] names = { "GRAPHICID", "PHOTOID", "UNIQUEID", "SIGNATUREID", "ISSCANNEDHEAD", "HEADPRESET",
                "SKULLPRESET", "LOADASCREATEDPLAYERIFARTISMISSING", "ISGAMEDATAHACK", "ISMODIFIED", "EYESPRESET", "NOSEPRESET",
                "SKINTONE", "HAIRTYPE", "BEARDTYPE", "BIRTHYEAR", "HEADPOINTER", "APPEARANCEPOINTER" };
            var entries = names.Select(name => (name, entry: session.Model.FieldByName("Players", name))).ToList();
            var lines = new List<string> { "index\tname\t" + string.Join("\t", names) };
            foreach (var item in items)
            {
                var cells = new List<string> { item.Index.ToString(CultureInfo.InvariantCulture), item.Label };
                foreach (var (name, entry) in entries)
                {
                    try { cells.Add(entry is null ? "-" : Convert.ToString(session.Model.ReadField(entry, item).RawValue, CultureInfo.InvariantCulture) ?? ""); }
                    catch (Exception) { cells.Add("!"); }
                }
                lines.Add(string.Join("\t", cells));
            }
            File.WriteAllText(options["out"], string.Join("\n", lines) + "\n", new UTF8Encoding(false));
            Console.WriteLine($"{items.Count} players, writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>
    /// Read-only: build a face entry from one player and list what applying it to another would
    /// change. Writes only the entry file under --dir, never the game.
    /// </summary>
    private static int LibraryDry(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            var from = items.First(item => item.Label == options["from"]);
            var to = items.First(item => item.Label == options["to"]);
            var source = session.ReadPlayer(from);
            var values = source.Values.Where(pair => pair.Value.Writable)
                .Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, JsonValue.Create(Convert.ToString(pair.Value.Raw, CultureInfo.InvariantCulture))));
            var now = DateTimeOffset.Now;
            var document = PlayerLibrary.Create(PlayerLibrary.NewId(now), from.Label + " face", "face", from.Label, "", values, now);
            PlayerLibrary.Save(options["dir"], document);
            var target = session.ReadPlayer(to);
            int differ = 0, total = 0;
            foreach (var pair in (JsonObject)document["values"]!)
            {
                if (!target.Values.TryGetValue(pair.Key, out var current) || !current.Writable)
                {
                    continue;
                }
                total++;
                string after = pair.Value!.GetValue<string>();
                string before = Convert.ToString(current.Raw, CultureInfo.InvariantCulture) ?? "";
                if (before != after)
                {
                    differ++;
                    if (differ <= 12)
                    {
                        Console.WriteLine($"  {pair.Key}: {before} -> {after}");
                    }
                }
            }
            Console.WriteLine($"face entry {document["id"]}: {document["fieldCount"]} fields; {to.Label} differs in {differ}/{total}; writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Plan (never apply) a team-to-team package. --first/--second are comma-separated names.</summary>
    private static int PackagePlanDry(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            var mover = new Nba2k27Editor.Core.Roster.RosterMover(session);
            RecordListItem[] Players(string key) => options.TryGetValue(key, out string? text) && text.Length > 0
                ? text.Split(',').Select(name => items.First(item => item.Label == name.Trim())).ToArray() : Array.Empty<RecordListItem>();
            int[] Picks(string key) => options.TryGetValue(key, out string? text) && text.Length > 0
                ? text.Split(',').Select(int.Parse).ToArray() : Array.Empty<int>();
            if (options.ContainsKey("list-team")) { foreach (var item in items.Where(item => { try { return mover.Placement(item).TeamLabel == options["list-team"]; } catch (Exception) { return false; } })) { Console.WriteLine(item.Label); } return 0; }
            var first = Players("first");
            var second = Players("second");
            var teams = session.Model.ScanRecords("Teams", 512);
            RecordListItem TeamOf(RecordListItem player) => teams.First(team => team.Address == mover.Placement(player).TeamAddress);
            int before = session.Memory is Nba2k27Editor.Core.Memory.Win32Memory live ? live.Writes : -1;
            var plan = mover.PlanPackage(TeamOf(first[0]), TeamOf(second[0]), first, second, Picks("first-picks"), Picks("second-picks"), syncPayroll: true);
            int after = session.Memory is Nba2k27Editor.Core.Memory.Win32Memory live2 ? live2.Writes : -1;
            Console.WriteLine(plan.Summary);
            Console.WriteLine($"patch bytes: {plan.Rows.Count}; players moved: {plan.Players.Count}; writes during planning: {after - before}");
            foreach (string line in plan.Details) { Console.WriteLine("  " + line); }
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only dump of each team's TEAMCOLOR1..3 raw bytes.</summary>
    private static int TeamColors(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var entries = session.Model.GroupedFields("Teams").Where(entry => entry.NormalizedName is "TEAMCOLOR1" or "TEAMCOLOR2" or "TEAMCOLOR3").ToArray();
            foreach (var team in session.Model.ScanRecords("Teams", 512).Take(options.ContainsKey("all") ? 512 : 8))
            {
                var parts = entries.Select(entry => { try { var read = session.Model.ReadField(entry, team); return $"{entry.NormalizedName}={read.DisplayValue}"; } catch (Exception exc) { return $"{entry.NormalizedName}!{exc.GetType().Name}"; } });
                Console.WriteLine($"{team.Index}\t{team.Label}\t{string.Join("\t", parts)}");
            }
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: dump schedule / stat manager table headers and the first rows as hex.</summary>
    private static int ScheduleProbe(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var model = session.Model;
            var teams = model.ScanRecords("Teams", 512).ToDictionary(team => team.Address, team => team.Label);
            foreach (string key in options.GetValueOrDefault("keys", "NBAScheduleData,PlayerStatManagerData,PlayerSeasonStats,GameDataStore").Split(','))
            {
                var config = model.BasePointerEntry(key);
                long baseAddress = model.ResolveBasePointerEntry(config, stopAtNull: true);
                Console.WriteLine($"== {key} base=0x{baseAddress:X}");
                if (baseAddress == 0 || config["count_offset"] is null)
                {
                    continue;
                }
                int count = BitConverter.ToInt32(model.Memory.ReadBytes(baseAddress + Conversions.ToInt(FieldIo.NodeText(config["count_offset"])), 4));
                long table = BitConverter.ToInt64(model.Memory.ReadBytes(baseAddress + Conversions.ToInt(FieldIo.NodeText(config["pointer_offset"])), 8));
                int stride = int.Parse(options.GetValueOrDefault($"stride-{key}", key == "NBAScheduleData" ? "32" : "64"), CultureInfo.InvariantCulture);
                Console.WriteLine($"   count={count} table=0x{table:X} stride={stride}");
                int rows = Math.Min(count, int.Parse(options.GetValueOrDefault("rows", "6"), CultureInfo.InvariantCulture));
                if (table == 0 || rows <= 0)
                {
                    continue;
                }
                byte[] data = model.Memory.ReadBytes(table, rows * stride);
                for (int row = 0; row < rows; row++)
                {
                    var hex = Convert.ToHexString(data, row * stride, stride);
                    var refs = new List<string>();
                    for (int at = 0; at + 8 <= stride; at += 8)
                    {
                        long value = BitConverter.ToInt64(data, row * stride + at);
                        if (teams.TryGetValue(value, out string? label)) refs.Add($"+{at}:{label}");
                    }
                    Console.WriteLine($"   [{row}] {hex} {string.Join(" ", refs)}");
                }
            }
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: RosterData NBASCHEDULEDATA rows (32 bytes) with team references decoded.</summary>
    private static int ScheduleRows(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var model = session.Model;
            long roster = model.ResolveBasePointerEntry(model.BasePointerEntry("RosterData"), stopAtNull: true);
            var rosterItem = new RecordListItem("RosterData", 0, roster, "RosterData");
            object? Field(string name)
            {
                var entry = model.GroupedFields("RosterData").First(item => item.NormalizedName == name);
                return model.ReadField(entry, rosterItem).RawValue;
            }
            long count = Convert.ToInt64(Field("NUMBEROFSCHEDULEDNBAGAMES"), CultureInfo.InvariantCulture);
            long table = Convert.ToInt64(Field("NBASCHEDULEDATA"), CultureInfo.InvariantCulture);
            Console.WriteLine($"roster=0x{roster:X} games={count} table=0x{table:X}");
            var teams = model.ScanRecords("Teams", 512).ToDictionary(team => team.Address, team => team.Label);
            int rows = (int)Math.Min(count, int.Parse(options.GetValueOrDefault("rows", "12"), CultureInfo.InvariantCulture));
            if (table == 0 || rows <= 0)
            {
                return 0;
            }
            byte[] data = model.Memory.ReadBytes(table, rows * 32);
            for (int row = 0; row < rows; row++)
            {
                var refs = new List<string>();
                for (int at = 0; at + 8 <= 32; at += 8)
                {
                    long value = BitConverter.ToInt64(data, row * 32 + at);
                    if (teams.TryGetValue(value, out string? label)) refs.Add($"+{at}:{label}");
                }
                Console.WriteLine($"[{row}] {Convert.ToHexString(data, row * 32, 32)} {string.Join(" ", refs)}");
            }
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: the team season lines the analytics page charts.</summary>
    private static int TeamSeasonsProbe(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            int team = int.Parse(options.GetValueOrDefault("team", "0"), CultureInfo.InvariantCulture);
            var rows = new HistorySession(session).TeamSeasons(team);
            Console.WriteLine($"team {team}: {rows.Count} seasons");
            foreach (var row in rows)
            {
                Console.WriteLine($"  slot={row["slot"]} id={row["id"]} year={row["COMPACTYEAR"]} W-L={row["WINS"]}-{row["LOSSES"]} pts={row["TOTALPOINTS"]} allowed={row["TOTALPOINTSALLOWED"]} 3PM={row["THREEPOINTSHOTSMADE"]}/{row["THREEPOINTSHOTSATTEMPTED"]} poss={row["POSSESSIONS"]}");
            }
            Console.WriteLine("writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: per-player GameScore history (last 5 games) and team streak / last-10 fields.</summary>
    private static int FormScan(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            string[] keys = session.Specs.Keys.Where(key => key.StartsWith("Stats/History/GAMESCOREHISTORY", StringComparison.Ordinal)).OrderBy(key => key).ToArray();
            Console.WriteLine($"history keys: {keys.Length}");
            int nonzero = 0;
            var samples = new List<string>();
            foreach (var item in items)
            {
                var snapshot = session.ReadPlayer(item, keys);
                var values = keys.Select(key => Convert.ToString(snapshot.Values[key], CultureInfo.InvariantCulture) ?? "").ToArray();
                if (values.Any(value => value != "0" && value != "0.0" && value != ""))
                {
                    nonzero++;
                    if (samples.Count < 8) samples.Add($"{item.Label}: " + string.Join(" ", keys.Select((key, i) => $"{key.Replace("Stats/History/GAMESCOREHISTORY", "")}={values[i]}")));
                }
            }
            Console.WriteLine($"players with GameScore history: {nonzero}/{items.Count}");
            samples.ForEach(Console.WriteLine);
            var model = session.Model;
            var teams = model.ScanRecords("Teams", 512).Take(int.Parse(options.GetValueOrDefault("teams", "6"), CultureInfo.InvariantCulture)).ToArray();
            string[] teamNames = { "WINLOSSSTREAK", "LAST10", "LAST10HOME", "LAST10AWAY", "LAST10HOMECOUNT", "LAST10AWAYCOUNT", "HOMEWINLOSSSTREAK", "AWAYWINLOSSSTREAK",
                "LONGESTWINSTREAK", "LONGESTLOSSSTREAK", "SEASONHOMEWINS", "SEASONHOMELOSSES", "PREVIOUSWINLOSSSTREAK" };
            var entries = model.GroupedFields("Teams").Where(entry => teamNames.Contains(entry.NormalizedName)).ToArray();
            foreach (var team in teams)
            {
                var parts = entries.Select(entry =>
                {
                    try { return $"{entry.NormalizedName}={model.ReadField(entry, team).RawValue}"; }
                    catch (Exception error) { return $"{entry.NormalizedName}=!{error.GetType().Name}"; }
                });
                Console.WriteLine($"{team.Label}: {string.Join(" ", parts)}");
            }
            Console.WriteLine("writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: decoded recent form (same decoder as the bridge) for a few players and teams.</summary>
    private static int FormDecoded(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            string[] keys = session.Specs.Keys.Where(key => key.StartsWith("Stats/History/GAMESCOREHISTORY", StringComparison.Ordinal)).ToArray();
            int shown = 0, withGames = 0;
            foreach (var item in items)
            {
                var snapshot = session.ReadPlayer(item, keys);
                long Raw(string name) => Convert.ToInt64(snapshot.Values[$"Stats/History/{name}"].Raw ?? 0L, CultureInfo.InvariantCulture);
                var games = RecentForm.Games(Enumerable.Range(0, 5).Select(i => Raw($"GAMESCOREHISTORYDATE{i}")).ToArray(),
                    Enumerable.Range(0, 5).Select(i => Raw($"GAMESCOREHISTORYMINUTES{i}")).ToArray(),
                    Enumerable.Range(0, 5).Select(i => Raw($"GAMESCOREHISTORYVALUE{i}")).ToArray());
                if (games.Count > 0) withGames++;
                if (games.Count > 0 && shown++ < 6)
                    Console.WriteLine($"{item.Label}: " + string.Join("  ", games.Select(g => $"{g.Date:yyyy-MM-dd} {g.Minutes}min GmSc {g.GameScore}")));
            }
            Console.WriteLine($"players with recent games: {withGames}/{items.Count}");
            var model = session.Model;
            var entries = model.GroupedFields("Teams").GroupBy(entry => entry.NormalizedName).ToDictionary(group => group.Key, group => group.First());
            foreach (var team in model.ScanRecords("Teams", 512).Take(4))
            {
                long Field(string name) => entries.TryGetValue(name, out var entry) && model.ReadField(entry, team).RawValue is { } raw ? Convert.ToInt64(raw, CultureInfo.InvariantCulture) : 0;
                var form = RecentForm.Team(Field);
                Console.WriteLine($"{team.Label}: last10 {form.Last10} ({form.Wins}-{form.Losses}) streak {form.Streak} home {form.HomeWins}-{form.HomeLosses} longestW {form.LongestWin}");
            }
            Console.WriteLine("writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: time the bulk current-season read and print the top 5 in points and threes made.</summary>
    private static int LeagueSeasonProbe(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var specs = session.Specs.Values.Where(spec => spec.IsSeasonBound
                && FieldIo.NodeText(spec.Payload["selected_record_source"]?["selector_field"]) == "CURRENTYEARSTATID").ToArray();
            FieldSpec Spec(string name) => specs.First(spec => spec.Key.EndsWith("/" + name, StringComparison.Ordinal));
            var wanted = new[] { "GAMESPLAYED", "POINTS", "3POINTERSMADE", "COMPACTYEAR" }.Select(Spec).ToArray();
            var model = session.Model;
            var selector = model.FieldByName("Players", "CURRENTYEARSTATID")!;
            var (table, count) = PlayerStatistics.Table(model);
            var all = specs.Select(spec => (spec, payload: model.FieldVersionPayload(spec.Entry.Field))).ToArray();
            int fast = 0;
            foreach (var item in items)
            {
                int id = Conversions.ToInt(model.ReadFieldAt("Players", selector.Section, selector.Field, item.Address).RawValue);
                if (id <= 0 || id == 65535 || id >= count) continue;
                foreach (var (spec, payload) in all)
                {
                    try { FieldIo.ReadAuthoredValue(model.Memory, table + id * 64L + FieldIo.FieldOffset(payload), payload); } catch (InvalidOperationException) { }
                }
                fast++;
            }
            Console.WriteLine($"fast path: {fast} players x {all.Length} fields in {watch.ElapsedMilliseconds}ms");
            watch.Restart();
            var rows = new List<(string Name, double[] Values)>();
            foreach (var item in items)
            {
                try
                {
                    rows.Add((item.Label, wanted.Select(spec => Convert.ToDouble(session.Model.ReadField(spec.Entry, item).DisplayValue, CultureInfo.InvariantCulture)).ToArray()));
                }
                catch (Exception exc) when (exc is InvalidOperationException or ArgumentException or FormatException or InvalidCastException) { }
            }
            Console.WriteLine($"specs={specs.Length} rows={rows.Count}/{items.Count} {watch.ElapsedMilliseconds}ms for 4 columns");
            var years = rows.GroupBy(row => row.Values[3]).OrderByDescending(group => group.Count()).Take(3).Select(group => $"{group.Key}:{group.Count()}");
            Console.WriteLine("compact years: " + string.Join(", ", years));
            var qualified = rows.Where(row => row.Values[0] >= 20).ToList();
            Console.WriteLine("PPG top5: " + string.Join("; ", qualified.OrderByDescending(row => row.Values[1] / row.Values[0]).Take(5).Select(row => $"{row.Name} {row.Values[1] / row.Values[0]:F1}")));
            Console.WriteLine("3PM top5: " + string.Join("; ", qualified.OrderByDescending(row => row.Values[2]).Take(5).Select(row => $"{row.Name} {row.Values[2]}")));
            Console.WriteLine("writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>
    /// Undo one player-edit journal through the normal guarded edit path: same PID, same player
    /// identity, same field address, and the field still holds the journal's "after" raw value.
    /// Writes the "before" raw value, reads it back and writes a restore record. Requires --confirm.
    /// </summary>
    private static int RestorePlayerJournal(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("journal", out var journalPath) || !options.ContainsKey("confirm"))
        {
            Console.Error.WriteLine("restore-player-journal requires --journal <file> and --confirm");
            return 2;
        }
        var journal = JsonNode.Parse(File.ReadAllText(journalPath))!.AsObject();
        if (journal["format"]?.GetValue<string>() != "nba2k27-player-edit/1" || journal["players"]!.AsArray().Count != 1)
        {
            Console.Error.WriteLine("unsupported journal");
            return 3;
        }
        var player = journal["players"]![0]!.AsObject();
        var change = player["changes"]!.AsArray().Single()!.AsObject();
        string field = change["field"]!.GetValue<string>();
        long before = change["before"]!.GetValue<long>();
        long after = change["after"]!.GetValue<long>();
        long fieldAddress = player["field_addresses"]![field]!.GetValue<long>();
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            int pid = journal["epoch"]![0]!.GetValue<int>();
            var current = items.SingleOrDefault(item => item.Index == player["index"]!.GetValue<int>());
            if (session.Model.Memory is not Win32Memory memory || current is null || current.Label != player["player"]!.GetValue<string>()
                || current.Address != player["address"]!.GetValue<long>() || session.Epoch.Pid != pid)
            {
                Console.Error.WriteLine($"target mismatch: pid or player changed (now {current?.Label} pid {session.Epoch.Pid})");
                return 4;
            }
            var snapshot = session.ReadPlayer(current, new[] { field });
            var value = snapshot.Values[field];
            if (value.Address != fieldAddress || Convert.ToInt64(value.Raw, CultureInfo.InvariantCulture) != after)
            {
                Console.Error.WriteLine($"field no longer holds the journal value: raw {value.Raw} at 0x{value.Address:X}; nothing written");
                return 5;
            }
            var spec = session.Specs[field];
            var overlay = new Overlay(session.Memory);
            FieldIo.WriteAuthoredValue(overlay, value.Address, spec.Payload, before);
            var patches = overlay.Patches();
            if (patches.Count != 1)
            {
                Console.Error.WriteLine($"expected 1 patch, got {patches.Count}; nothing written");
                return 6;
            }
            var patch = patches[0];
            memory.WriteBytes(patch.Address, new[] { (byte)patch.After });
            var check = session.ReadPlayer(current, new[] { field }).Values[field];
            var record = new JsonObject
            {
                ["format"] = "nba2k27-manual-restore/1",
                ["time"] = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                ["pid"] = pid,
                ["reason"] = options.GetValueOrDefault("reason", "undo journal"),
                ["journal"] = Path.GetFileName(journalPath),
                ["player"] = current.Label,
                ["rows"] = new JsonArray(new JsonObject
                {
                    ["field"] = field, ["address"] = patch.Address, ["byte_before"] = patch.Before, ["byte_after"] = patch.After,
                    ["raw_before_restore"] = after, ["raw_after_restore"] = Program.ValueNode(check.Raw), ["display"] = Program.ValueNode(check.Display),
                }),
                ["writes"] = memory.Writes,
                ["verified"] = Convert.ToInt64(check.Raw, CultureInfo.InvariantCulture) == before,
            };
            string outPath = Path.Combine(Path.GetDirectoryName(journalPath)!, $"{DateTime.Now:yyyyMMdd}-restore-{Path.GetFileNameWithoutExtension(journalPath).Split('-').Last()}.json");
            File.WriteAllText(outPath, record.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"restored {field}: raw {after} -> {check.Raw} (display {check.Display}) writes={memory.Writes} verified={record["verified"]} record={outPath}");
            return record["verified"]!.GetValue<bool>() ? 0 : 7;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: the game's own player type fields for every rated player, with counts.</summary>
    private static int PlayerTypes(Dictionary<string, string> options)
    {
        string outPath = options.GetValueOrDefault("out", "player-types.json");
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            string[] keys = { "Vitals/Type/PLAYERTYPE", "Vitals/Type/OFFENSIVEPLAYERTYPE", "Attributes/Progression/PROGRESSIONPLAYERTYPE",
                "Vitals/Type/ORGROLE", "Vitals/Vitals/POSITION" };
            keys = keys.Where(session.Specs.ContainsKey).ToArray();
            var rows = new JsonArray();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (RecordListItem item in items)
            {
                int? overall = PlayerSummary.ReadOverall(session, item);
                if (overall is null)
                {
                    continue;
                }
                PlayerSnapshot snapshot = session.ReadPlayer(item, keys);
                var row = new JsonObject { ["index"] = item.Index, ["name"] = item.Label, ["overall"] = overall };
                foreach (string key in keys)
                {
                    row[key.Split('/').Last()] = snapshot.Values.TryGetValue(key, out var value) ? Program.ValueNode(value.Display) : null;
                }
                string pair = $"{row["PLAYERTYPE"]}|{row["OFFENSIVEPLAYERTYPE"]}";
                counts[pair] = counts.GetValueOrDefault(pair) + 1;
                rows.Add(row);
            }
            var report = new JsonObject
            {
                ["players"] = rows.Count,
                ["counts"] = new JsonObject(counts.OrderByDescending(pair => pair.Value).Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value))),
                ["rows"] = rows,
                ["writes"] = session.Memory is Win32Memory memory ? memory.Writes : 0,
            };
            File.WriteAllText(outPath, report.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"wrote {outPath}: {rows.Count} players, writes={report["writes"]}");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: team-record history fields (Team Stats group) with offsets and decoded values.</summary>
    private static int TeamHistoryFields(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var entries = session.Model.GroupedFields("Teams").Where(entry => entry.Section == "Stats" || entry.Section == "Team Stats").ToArray();
            foreach (var entry in entries.Take(40))
            {
                Console.WriteLine($"field {entry.Section}/{entry.Group}/{entry.NormalizedName} payload={entry.Field.ToJsonString()[..Math.Min(220, entry.Field.ToJsonString().Length)]}");
            }
            foreach (var team in session.Model.ScanRecords("Teams", 512).Take(6))
            {
                var parts = entries.Select(entry => { try { var read = session.Model.ReadField(entry, team); return $"{entry.NormalizedName}={read.DisplayValue}"; } catch (Exception exc) { return $"{entry.NormalizedName}!{exc.GetType().Name}"; } });
                Console.WriteLine($"{team.Index}\t{team.Label}\t{string.Join(" ", parts)}");
            }
            Console.WriteLine($"writes={(session.Memory is Win32Memory memory ? memory.Writes : 0)}");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    private static int AvatarIds(Dictionary<string, string> options)    {
        if (!options.TryGetValue("out", out string? outPath))
        {
            Console.Error.WriteLine("avatar-ids requires --out <file.json>");
            return 2;
        }
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            FieldEntry field = session.Model.FieldByName("Players", "PHOTOID")
                ?? throw new InvalidOperationException("The active player layout has no PHOTOID field");
            var rows = new JsonArray();
            int matched = 0;
            foreach (RecordListItem item in items)
            {
                try
                {
                    int photoId = Convert.ToInt32(
                        session.Model.ReadField(field, item).RawValue, CultureInfo.InvariantCulture);
                    bool found = photoId > 0;
                    matched += found ? 1 : 0;
                    // Birth date lets the UI match other records of the same person (photo borrowing).
                    PlayerSummary summary = PlayerSummary.Read(session, item);
                    rows.Add(new JsonObject
                    {
                        ["index"] = item.Index,
                        ["name"] = item.Label,
                        ["photoId"] = photoId,
                        ["resource"] = found ? $"player_images/chr_g{photoId}.iff" : null,
                        ["summary"] = new JsonObject
                        {
                            ["birthYear"] = summary.BirthYear,
                            ["birthMonth"] = summary.BirthMonth,
                            ["birthDay"] = summary.BirthDay,
                        },
                    });
                }
                catch (Exception)
                {
                    rows.Add(new JsonObject
                    {
                        ["index"] = item.Index,
                        ["name"] = item.Label,
                        ["photoId"] = null,
                        ["resource"] = null,
                    });
                }
            }
            var report = new JsonObject
            {
                ["format"] = "nba2k27-player-avatar-ids/1",
                ["pid"] = session.Memory.Pid,
                ["total"] = rows.Count,
                ["matched"] = matched,
                ["writes"] = 0,
                ["players"] = rows,
            };
            File.WriteAllText(outPath, report.ToJsonString(Json.Options), new UTF8Encoding(false));
            Console.WriteLine($"wrote {outPath}: {rows.Count} records, {matched} matching portraits, writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }

    private static int ListTeams(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var teams = session.Model.ScanRecords("Teams");
            var fields = session.Model.GroupedFields("Teams").Where(entry =>
                entry.NormalizedName is "HISTORICYEAR" or "CLASSICTEAMYEAR" or "BASELOGONAME"
                    or "LOGO1" or "LOGO2" or "TEAMTYPE").ToList();
            var rows = new JsonArray();
            foreach (var team in teams)
            {
                var row = new JsonObject { ["index"] = team.Index, ["label"] = team.Label };
                foreach (var field in fields)
                {
                    try
                    {
                        row[field.NormalizedName] = ValueNode(session.Model.ReadField(field, team).RawValue);
                    }
                    catch (Exception exc)
                    {
                        row[field.NormalizedName + "_error"] = exc.Message;
                    }
                }
                rows.Add(row);
            }
            var report = new JsonObject
            {
                ["pid"] = session.Memory.Pid,
                ["context"] = System.Text.Json.JsonSerializer.SerializeToNode(PlayerAgeContext.Read(session.Memory)),
                ["teams"] = rows,
                ["writes"] = session.Memory is Win32Memory memory ? memory.Writes : 0,
            };
            if (options.TryGetValue("out", out var path))
            {
                File.WriteAllText(path, report.ToJsonString(Json.Options), new UTF8Encoding(false));
            }
            Console.WriteLine(report.ToJsonString(Json.Options));
            return 0;
        }
        finally
        {
            session.Close();
        }
    }

    private static JsonArray ItemsNode(IEnumerable<RecordListItem> items)
    {
        var array = new JsonArray();
        foreach (var item in items)
        {
            array.Add(new JsonObject
            {
                ["index"] = item.Index,
                ["address"] = Hex(item.Address),
                ["label"] = item.Label,
            });
        }
        return array;
    }

    private static JsonArray TeamSample(Core.Roster.RosterIndex index)
    {
        var array = new JsonArray();
        foreach (var team in index.Teams.Take(5))
        {
            array.Add(new JsonObject
            {
                ["index"] = team.Index,
                ["label"] = team.Label,
                ["members"] = team.Players.Count,
                ["first"] = team.Players.Count > 0 ? team.Players[0].Label : null,
            });
        }
        return array;
    }

    private static JsonObject FieldsNode(IReadOnlyDictionary<string, FieldValue> values)
    {
        var fields = new JsonObject();
        foreach (var pair in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            fields[pair.Key] = new JsonObject
            {
                ["raw"] = ValueNode(pair.Value.Raw),
                ["value"] = ValueNode(pair.Value.Display),
                ["address"] = Hex(pair.Value.Address),
                ["writable"] = pair.Value.Writable,
            };
        }
        return fields;
    }

    private static JsonObject ErrorsNode(IReadOnlyDictionary<string, string> errors)
    {
        var node = new JsonObject();
        foreach (var pair in errors.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            node[pair.Key] = pair.Value;
        }
        return node;
    }

    internal static JsonNode? ValueNode(object? value)
    {
        return value switch
        {
            null => null,
            string text => JsonValue.Create(text),
            bool flag => JsonValue.Create(flag),
            byte[] bytes => JsonValue.Create(Convert.ToHexString(bytes).ToLowerInvariant()),
            ValueTuple<double, double> pair => new JsonArray(JsonValue.Create(pair.Item1), JsonValue.Create(pair.Item2)),
            sbyte or byte or short or ushort or int or uint or long or ulong => JsonValue.Create(Convert.ToInt64(value)),
            float single => JsonValue.Create(Math.Round(single, 6)),
            double number => JsonValue.Create(Math.Round(number, 6)),
            decimal money => JsonValue.Create((double)money),
            _ => JsonValue.Create(value.ToString()),
        };
    }

    internal static System.Text.Json.JsonSerializerOptions JsonOptions => Json.Options;

    internal static string Hex(long value) => "0x" + value.ToString("x", CultureInfo.InvariantCulture);
}

internal static class Json
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };
}
