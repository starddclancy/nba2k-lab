using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Cli;

/// <summary>
/// Live write round trip: edit one field, prove it from a second connection, undo, then prove
/// the player record is byte-identical to the pre-edit state from a third connection.
/// </summary>
internal static class RoundTrip
{
    public static int Run(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("field", out var fieldKey)
            || !options.TryGetValue("out", out var outPath))
        {
            Console.Error.WriteLine("roundtrip requires --field and --out, plus --player or --index");
            return 2;
        }
        // Duplicate names exist in the table, so an index selects one exact record.
        int? indexFilter = options.TryGetValue("index", out var indexText)
            ? int.Parse(indexText, CultureInfo.InvariantCulture)
            : null;
        options.TryGetValue("player", out var playerName);
        if (indexFilter is null && string.IsNullOrEmpty(playerName))
        {
            Console.Error.WriteLine("roundtrip requires --player or --index");
            return 2;
        }
        double delta = options.TryGetValue("delta", out var deltaText)
            ? double.Parse(deltaText, CultureInfo.InvariantCulture)
            : 1.0;
        int holdSeconds = options.TryGetValue("hold-seconds", out var holdText)
            ? int.Parse(holdText, CultureInfo.InvariantCulture)
            : 0;
        string? holdUntil = options.GetValueOrDefault("hold-until");
        var dataDirectory = options.GetValueOrDefault("data-dir");
        var report = new JsonObject
        {
            ["format"] = "nba2k27-csharp-write-roundtrip/1",
            ["version"] = AppVersion.Value,
            ["time"] = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            ["player"] = playerName,
            ["field"] = fieldKey,
            ["delta"] = delta,
        };

        PlayerSession? writer = null;
        PlayerSession? observer = null;
        PlayerSession? verifier = null;
        try
        {
            writer = new PlayerSession("NBA2K27.exe", dataDirectory);
            var items = writer.Connect();
            var matches = indexFilter is int wanted
                ? items.Where(item => item.Index == wanted).ToList()
                : items.Where(item => item.Label == playerName).ToList();
            if (matches.Count != 1)
            {
                Console.Error.WriteLine(
                    $"selection must match exactly one record (player \"{playerName}\", index {indexFilter?.ToString() ?? "-"})"
                    + $": {matches.Count}");
                return 3;
            }
            var target = matches[0];
            report["player"] = target.Label;
            var snapshot = writer.ReadPlayer(target);
            var baselineRaw = snapshot.Values.ToDictionary(pair => pair.Key, pair => pair.Value.Raw);
            var baselineIdentity = Convert.ToHexString(snapshot.Identity).ToLowerInvariant();
            report["index"] = target.Index;
            report["address"] = Program.Hex(target.Address);
            report["identity_before"] = baselineIdentity;
            report["fields_read"] = snapshot.Values.Count;
            report["read_errors"] = snapshot.Errors.Count;

            if (!snapshot.Values.TryGetValue(fieldKey, out var before))
            {
                Console.Error.WriteLine($"field was not read: {fieldKey}");
                return 4;
            }
            if (!before.Writable)
            {
                Console.Error.WriteLine($"field is read-only: {fieldKey}");
                return 4;
            }
            double beforeDisplay = Conversions.ToDouble(before.Display);
            double expectedDisplay = beforeDisplay + delta;
            report["raw_before"] = Program.ValueNode(before.Raw);
            report["display_before"] = Program.ValueNode(before.Display);
            report["display_expected"] = expectedDisplay;
            report["field_address"] = Program.Hex(before.Address);

            int writerWritesBefore = Writes(writer);
            var transaction = writer.Apply(snapshot, new Dictionary<string, object?> { [fieldKey] = expectedDisplay });
            if (transaction is null)
            {
                Console.Error.WriteLine("no patch rows were produced; nothing was written");
                return 5;
            }
            report["patches"] = transaction.Patches.Count;
            report["journal"] = transaction.JournalPath;
            report["writer_writes"] = Writes(writer) - writerWritesBefore;
            report["transaction_status"] = transaction.Status;
            report["patch_rows"] = PatchRows(transaction.Patches);

            observer = new PlayerSession("NBA2K27.exe", dataDirectory);
            var observerItems = observer.Connect();
            var observed = observerItems.FirstOrDefault(item =>
                item.Address == target.Address && item.Index == target.Index);
            if (observed is null)
            {
                Console.Error.WriteLine("the observer connection cannot find the edited player");
                return 6;
            }
            var observerSnapshot = observer.ReadPlayer(observed);
            report["observer_display"] = Program.ValueNode(observerSnapshot.Values[fieldKey].Display);
            report["observer_raw"] = Program.ValueNode(observerSnapshot.Values[fieldKey].Raw);
            report["observer_writes"] = Writes(observer);
            bool observedExpected = Math.Abs(
                Conversions.ToDouble(observerSnapshot.Values[fieldKey].Display) - expectedDisplay) < 1e-6;
            report["observer_matches_expected"] = observedExpected;

            if (holdSeconds > 0 || holdUntil is not null)
            {
                // Manual verification window: keep the written value visible until the caller
                // signals (sentinel file) or the cap elapses, so the modified window stays short.
                var started = DateTime.UtcNow;
                var deadline = started.AddSeconds(holdSeconds > 0 ? holdSeconds : 180);
                while (DateTime.UtcNow < deadline)
                {
                    if (holdUntil is not null && File.Exists(holdUntil))
                    {
                        break;
                    }
                    Thread.Sleep(1000);
                }
                report["held_seconds"] = Math.Round((DateTime.UtcNow - started).TotalSeconds, 1);
            }

            int undoWritesBefore = Writes(writer);
            var undone = writer.UndoBatch();
            report["undo_writes"] = Writes(writer) - undoWritesBefore;
            report["undo_players"] = undone.Count;

            verifier = new PlayerSession("NBA2K27.exe", dataDirectory);
            var verifierItems = verifier.Connect();
            var verified = verifierItems.FirstOrDefault(item =>
                item.Address == target.Address && item.Index == target.Index);
            if (verified is null)
            {
                Console.Error.WriteLine("the verifier connection cannot find the player");
                return 7;
            }
            var verifierSnapshot = verifier.ReadPlayer(verified);
            report["verifier_writes"] = Writes(verifier);
            report["verifier_display"] = Program.ValueNode(verifierSnapshot.Values[fieldKey].Display);
            report["verifier_raw"] = Program.ValueNode(verifierSnapshot.Values[fieldKey].Raw);
            report["identity_after"] = Convert.ToHexString(verifierSnapshot.Identity).ToLowerInvariant();
            report["identity_restored"] = report["identity_after"]!.GetValue<string>() == baselineIdentity;

            var differing = new List<string>();
            foreach (var pair in baselineRaw)
            {
                if (!verifierSnapshot.Values.TryGetValue(pair.Key, out var now))
                {
                    differing.Add(pair.Key);
                    continue;
                }
                if (!PlayerPatchGuard.SameValue(now.Raw, pair.Value))
                {
                    differing.Add(pair.Key);
                }
            }
            report["differing_fields"] = new JsonArray(
                differing.Select(key => (JsonNode?)JsonValue.Create(key)).ToArray());
            report["records_byte_exact_restored"] = differing.Count == 0
                && report["identity_restored"]!.GetValue<bool>();
            report["status"] = report["records_byte_exact_restored"]!.GetValue<bool>() && observedExpected
                ? "restored"
                : "needs_review";

            File.WriteAllText(outPath, report.ToJsonString(Program.JsonOptions), new UTF8Encoding(false));
            Console.WriteLine(
                $"{target.Label} {fieldKey}: {beforeDisplay:g} -> {expectedDisplay:g} "
                + $"(observer {Conversions.ToDouble(observerSnapshot.Values[fieldKey].Display):g}), "
                + $"patches {transaction.Patches.Count}, status {report["status"]}");
            return report["status"]!.GetValue<string>() == "restored" ? 0 : 8;
        }
        finally
        {
            writer?.Close();
            observer?.Close();
            verifier?.Close();
        }
    }

    private static int Writes(PlayerSession session) =>
        session.Memory is Win32Memory memory ? memory.Writes : 0;

    private static JsonArray PatchRows(IReadOnlyList<PatchRow> rows)
    {
        var array = new JsonArray();
        foreach (var row in rows)
        {
            array.Add(new JsonObject
            {
                ["address"] = Program.Hex(row.Address),
                ["before"] = row.Before,
                ["after"] = row.After,
                ["mask"] = row.Mask,
            });
        }
        return array;
    }
}
