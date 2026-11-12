using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Gs;

namespace Nba2k27Editor.Cli;

/// <summary>Live general-GS read and apply/undo verification against the running game.</summary>
public static class GsCommand
{
    public static int Run(Dictionary<string, string> options)
    {
        string mode = options.GetValueOrDefault("mode", "read");
        string? field = options.GetValueOrDefault("field");
        int? value = options.TryGetValue("value", out var rawValue)
            ? int.Parse(rawValue, CultureInfo.InvariantCulture) : null;
        double holdSeconds = options.TryGetValue("hold-seconds", out var rawHold)
            ? double.Parse(rawHold, CultureInfo.InvariantCulture) : 2;
        string? outPath = options.GetValueOrDefault("out");
        string? dataDir = options.GetValueOrDefault("data-dir");

        using var session = new NativeGeneralGsSession(dataDir);
        var snapshot = session.Connect();
        var baseline = snapshot.Values;
        Console.WriteLine(
            $"connected pid {snapshot.Epoch.Pid}, root 0x{snapshot.Root:X}, fields {GsFieldIds.Ids.Count}, "
            + $"values {baseline.Count}");

        if (mode == "read")
        {
            var report = Observation("baseline", snapshot, baseline, 0);
            report["values_match_python"] = null;
            report["status"] = "passed";
            report["menu_verified"] = false;
            report["game_settings_saved"] = false;
            Write(outPath, report);
            Console.WriteLine($"values {baseline.Count}, blocks 2 x {NativeGsConstants.BlockSize} bytes, writes 0");
            return 0;
        }
        if (mode == "set")
        {
            if (field is null || value is null)
            {
                throw new ArgumentException("set mode needs --field and --value");
            }
            int current = baseline[field];
            var written = session.Apply(snapshot, new Dictionary<string, int> { [field] = value.Value });
            if (written is null)
            {
                throw new InvalidOperationException("the planned GS edit would not change any byte");
            }
            int setWrites = session.WriteAttempts;
            // Read back through a fresh connection: the value must be visible without sharing writer state.
            var readback = Observe();
            var report = Observation("applied", written, written.Values, 0);
            report["format"] = "nba2k27-csharp-general-gs-set/1";
            report["field"] = field;
            report["value_before"] = current;
            report["value_target"] = value.Value;
            report["apply_writes"] = setWrites;
            report["verifier_writes"] = readback.Writes;
            report["verifier_field_value"] = readback.Values[field];
            report["verifier_matches_target"] = readback.Values[field] == value.Value;
            report["verifier_other_fields_unchanged"] = readback.Values
                .Where(pair => pair.Key != field)
                .All(pair => baseline.TryGetValue(pair.Key, out int old) && old == pair.Value);
            report["game_settings_saved"] = false;
            report["menu_verified"] = false;
            report["status"] = report["verifier_matches_target"]!.GetValue<bool>()
                && report["verifier_other_fields_unchanged"]!.GetValue<bool>()
                && readback.Writes == 0
                    ? "applied"
                    : "needs_review";
            Write(outPath, report);
            Console.WriteLine($"{field}: {current} -> {value.Value} "
                + $"(verifier {readback.Values[field]}, writer {setWrites} writes, "
                + $"verifier {readback.Writes} writes, status {report["status"]})");
            return report["status"]!.GetValue<string>() == "applied" ? 0 : 1;
        }
        if (mode != "roundtrip")
        {
            throw new ArgumentException($"unsupported mode: {mode}");
        }
        if (field is null)
        {
            throw new ArgumentException("roundtrip mode needs --field");
        }

        int before = baseline[field];
        int target = value ?? (before < 100 ? before + 1 : before - 1);
        var applied = session.Apply(snapshot, new Dictionary<string, int> { [field] = target });
        if (applied is null)
        {
            throw new InvalidOperationException("the planned GS edit would not change any byte");
        }
        var appliedValues = applied.Values;
        int applyWrites = session.WriteAttempts;
        Console.WriteLine($"applied {field}: {before} -> {target}");

        // Independent observer: its own connection, its own read, zero writes.
        var observer = Observe();
        Thread.Sleep(TimeSpan.FromSeconds(holdSeconds));

        var restored = session.Undo();
        var restoredValues = restored.Values;
        var verifier = Observe();

        var roundtrip = Observation("baseline", snapshot, baseline, 0);
        roundtrip["format"] = "nba2k27-csharp-general-gs-roundtrip/1";
        roundtrip["field"] = field;
        roundtrip["value_before"] = before;
        roundtrip["value_target"] = target;
        roundtrip["apply_writes"] = applyWrites;
        roundtrip["applied_values"] = Nodes(appliedValues);
        roundtrip["observer_writes"] = observer.Writes;
        roundtrip["observer_field_value"] = observer.Values[field];
        roundtrip["observer_applied_matches"] = observer.Values[field] == target;
        roundtrip["observer_values_match_applied"] = SameValues(observer.Values, appliedValues);
        roundtrip["undo_writes"] = session.WriteAttempts - applyWrites;
        roundtrip["verifier_writes"] = verifier.Writes;
        roundtrip["verifier_field_value"] = verifier.Values[field];
        roundtrip["restored_values"] = Nodes(restoredValues);
        roundtrip["values_restored"] = SameValues(restoredValues, baseline);
        roundtrip["verifier_values_match_baseline"] = SameValues(verifier.Values, baseline);
        roundtrip["blocks_restored"] = verifier.Blocks[0].SequenceEqual(snapshot.Blocks[0])
            && verifier.Blocks[1].SequenceEqual(snapshot.Blocks[1]);
        roundtrip["game_settings_saved"] = false;
        roundtrip["menu_verified"] = false;
        roundtrip["status"] = roundtrip["values_restored"]!.GetValue<bool>()
            && roundtrip["blocks_restored"]!.GetValue<bool>()
            && roundtrip["observer_applied_matches"]!.GetValue<bool>()
            && observer.Writes == 0 && verifier.Writes == 0
                ? "restored"
                : "needs_review";
        Write(outPath, roundtrip);
        Console.WriteLine(
            $"{field}: {before} -> {target} (observer {observer.Values[field]}), "
            + $"writer {session.WriteAttempts} writes, observer {observer.Writes}, verifier {verifier.Writes}, "
            + $"status {roundtrip["status"]}");
        return roundtrip["status"]!.GetValue<string>() == "restored" ? 0 : 1;
    }

    private sealed record ObservationResult(
        int Writes, Dictionary<string, int> Values, IReadOnlyList<byte[]> Blocks, long Root);

    /// <summary>Read through a fresh connection so the observation cannot share writer state.</summary>
    private static ObservationResult Observe()
    {
        using var reader = new NativeGeneralGsReader();
        var snapshot = reader.Read();
        return new ObservationResult(reader.WriteAttempts, snapshot.Values, snapshot.Blocks, snapshot.Root);
    }

    private static JsonObject Observation(
        string stage, GeneralGsSnapshot snapshot, Dictionary<string, int> values, int writes) =>
        new()
        {
            ["format"] = "nba2k27-csharp-general-gs-observation/1",
            ["stage"] = stage,
            ["observed"] = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            ["fields"] = GsFieldIds.Ids.Count,
            ["value_count"] = values.Count,
            ["epoch"] = snapshot.Epoch.ToJsonArray(),
            ["root"] = snapshot.Root,
            ["allocation_identity"] = AllocationNode(snapshot.Allocation),
            ["values"] = Nodes(values),
            ["blocks_hex"] = new JsonArray(
                snapshot.Blocks.Select(block => (JsonNode?)JsonValue.Create(Convert.ToHexString(block).ToLowerInvariant())).ToArray()),
            ["observer_writes"] = writes,
        };

    private static bool SameValues(Dictionary<string, int> left, Dictionary<string, int> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out int value) && value == pair.Value);

    private static JsonObject Nodes(Dictionary<string, int> values)
    {
        var node = new JsonObject();
        foreach (var key in values.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            node[key] = values[key];
        }
        return node;
    }

    private static JsonArray AllocationNode(
        IReadOnlyList<(long Start, long Length, long Allocation, uint AllocationProtect, uint State, uint Protect, uint Type)> allocation)
    {
        var array = new JsonArray();
        foreach (var row in allocation)
        {
            array.Add(new JsonArray(
                JsonValue.Create(row.Start), JsonValue.Create(row.Length), JsonValue.Create(row.Allocation),
                JsonValue.Create((long)row.AllocationProtect), JsonValue.Create((long)row.State),
                JsonValue.Create((long)row.Protect), JsonValue.Create((long)row.Type)));
        }
        return array;
    }

    private static void Write(string? path, JsonObject report)
    {
        if (path is null)
        {
            Console.WriteLine(report.ToJsonString(JournalJson.Options));
            return;
        }
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, report.ToJsonString(JournalJson.Options), new UTF8Encoding(false));
        Console.WriteLine($"report: {Path.GetFullPath(path)}");
    }
}
