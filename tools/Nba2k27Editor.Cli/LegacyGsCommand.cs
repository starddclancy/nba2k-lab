using System.Globalization;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Gs;

namespace Nba2k27Editor.Cli;

/// <summary>Verify the legacy five-pair GS mapping against the live game.</summary>
internal static class LegacyGsCommand
{
    public static int Run(Dictionary<string, string> options)
    {
        string mode = options.GetValueOrDefault("mode", "read");
        string? outPath = options.TryGetValue("out", out string? path) ? path : null;
        return mode switch
        {
            "read" => Read(outPath),
            "restore" => Restore(options, outPath),
            "roundtrip" => RoundTrip(outPath),
            "set" => Set(options, outPath),
            "probe" => Probe(options, outPath),
            _ => Unknown(mode),
        };
    }

    private static int Unknown(string mode)
    {
        Console.Error.WriteLine($"unknown legacy GS mode: {mode}");
        return 2;
    }

    private static int Read(string? outPath)
    {
        using var session = new NativeMenuGsSession();
        var snapshot = session.Connect();
        var report = new JsonObject
        {
            ["format"] = "nba2k27-csharp-native-gs-observation/1",
            ["epoch"] = snapshot.Epoch.ToJsonArray(),
            ["blocks_hex"] = new JsonArray(
                JsonValue.Create(Hex(snapshot.Primary)),
                JsonValue.Create(Hex(snapshot.Secondary))),
            ["values"] = ToJson(snapshot.Values),
            ["secondary_values"] = ToJson(snapshot.SecondaryValues),
            ["observation_status"] = snapshot.ObservationStatus,
            ["observation_note"] = snapshot.ObservationNote,
            ["writable"] = snapshot.Writable,
            ["write_attempts"] = session.WriteAttempts,
            ["menu_verified"] = false,
        };
        Report(report, outPath);
        return 0;
    }

    /// <summary>
    /// Force the single verified float to a chosen tested value, journaled like any other edit.
    /// Used to return the game to its pre-test state and prove the blocks match a known baseline.
    /// </summary>
    private static int Restore(Dictionary<string, string> options, string? outPath)
    {
        if (!options.TryGetValue("value", out string? text)
            || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int target))
        {
            Console.Error.WriteLine("restore requires --value <56|57>");
            return 2;
        }
        using var session = new NativeMenuGsSession();
        var baseline = session.Connect();
        var report = new JsonObject
        {
            ["format"] = "nba2k27-csharp-native-gs-restore/1",
            ["epoch"] = baseline.Epoch.ToJsonArray(),
            ["baseline_hex"] = new JsonArray(
                JsonValue.Create(Hex(baseline.Primary)),
                JsonValue.Create(Hex(baseline.Secondary))),
            ["baseline_value"] = baseline.Values[NativeMenuGs.TargetField],
            ["writable"] = baseline.Writable,
            ["menu_verified"] = false,
            ["game_settings_saved"] = false,
        };
        if (!baseline.Writable)
        {
            report["status"] = "not_writable";
            Report(report, outPath);
            return 1;
        }
        if (baseline.Values[NativeMenuGs.TargetField] == target)
        {
            report["status"] = "already_target";
            report["restored_hex"] = report["baseline_hex"]!.DeepClone();
            report["write_attempts"] = session.WriteAttempts;
            Report(report, outPath);
            return 0;
        }
        var restored = session.Apply(baseline, target);
        report["restored_hex"] = new JsonArray(
            JsonValue.Create(Hex(restored.Primary)),
            JsonValue.Create(Hex(restored.Secondary)));
        report["restored_value"] = restored.Values[NativeMenuGs.TargetField];
        report["write_attempts"] = session.WriteAttempts;

        using var verifier = new NativeMenuGsSession();
        var verified = verifier.Connect();
        report["verifier_value"] = verified.Values[NativeMenuGs.TargetField];
        report["verifier_writes"] = verifier.WriteAttempts;
        report["verifier_blocks_match_restored"] =
            verified.Primary.SequenceEqual(restored.Primary)
            && verified.Secondary.SequenceEqual(restored.Secondary);
        bool ok = restored.Values[NativeMenuGs.TargetField] == target
            && report["verifier_blocks_match_restored"]!.GetValue<bool>()
            && report["verifier_writes"]!.GetValue<int>() == 0;
        report["status"] = ok ? "restored" : "needs_review";
        Report(report, outPath);
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Flip the single verified float to the other tested value, prove the full-block readback,
    /// then restore and prove the blocks are byte-identical to the baseline.
    /// </summary>
    private static int RoundTrip(string? outPath)
    {
        using var session = new NativeMenuGsSession();
        var baseline = session.Connect();
        var report = new JsonObject
        {
            ["format"] = "nba2k27-csharp-native-gs-roundtrip/1",
            ["epoch"] = baseline.Epoch.ToJsonArray(),
            ["baseline_hex"] = new JsonArray(
                JsonValue.Create(Hex(baseline.Primary)),
                JsonValue.Create(Hex(baseline.Secondary))),
            ["baseline_values"] = ToJson(baseline.Values),
            ["observation_status"] = baseline.ObservationStatus,
            ["writable"] = baseline.Writable,
            ["menu_verified"] = false,
            ["game_settings_saved"] = false,
        };
        if (!baseline.Writable)
        {
            report["status"] = "not_writable";
            report["write_attempts"] = session.WriteAttempts;
            Report(report, outPath);
            return 0;
        }

        int before = baseline.Values[NativeMenuGs.TargetField];
        int after = before == NativeMenuGs.TestedValues[0]
            ? NativeMenuGs.TestedValues[1]
            : NativeMenuGs.TestedValues[0];
        report["field"] = NativeMenuGs.TargetField;
        report["before"] = before;
        report["after"] = after;

        var applied = session.Apply(baseline, after);
        int applyWrites = session.WriteAttempts;
        report["applied_hex"] = new JsonArray(
            JsonValue.Create(Hex(applied.Primary)),
            JsonValue.Create(Hex(applied.Secondary)));
        report["applied_value"] = applied.Values[NativeMenuGs.TargetField];
        report["apply_writes"] = applyWrites;

        // A second connection proves the write is visible outside the writing handle.
        using (var observer = new NativeMenuGsSession())
        {
            var observed = observer.Connect();
            report["observer_value"] = observed.Values[NativeMenuGs.TargetField];
            report["observer_writes"] = observer.WriteAttempts;
            report["observer_matches_applied"] = observed.Values[NativeMenuGs.TargetField] == after;
            report["observer_blocks_match_applied"] =
                observed.Primary.SequenceEqual(applied.Primary)
                && observed.Secondary.SequenceEqual(applied.Secondary);
        }

        var restored = session.Undo();
        report["undo_writes"] = session.WriteAttempts - applyWrites;
        report["restored_hex"] = new JsonArray(
            JsonValue.Create(Hex(restored.Primary)),
            JsonValue.Create(Hex(restored.Secondary)));
        report["restored_value"] = restored.Values[NativeMenuGs.TargetField];
        report["blocks_restored"] =
            restored.Primary.SequenceEqual(baseline.Primary)
            && restored.Secondary.SequenceEqual(baseline.Secondary);

        using (var verifier = new NativeMenuGsSession())
        {
            var verified = verifier.Connect();
            report["verifier_value"] = verified.Values[NativeMenuGs.TargetField];
            report["verifier_writes"] = verifier.WriteAttempts;
            report["verifier_values_match_baseline"] =
                verified.Values[NativeMenuGs.TargetField] == before;
            report["verifier_blocks_match_baseline"] =
                verified.Primary.SequenceEqual(baseline.Primary)
                && verified.Secondary.SequenceEqual(baseline.Secondary);
        }

        bool ok = report["observer_matches_applied"]!.GetValue<bool>()
            && report["observer_blocks_match_applied"]!.GetValue<bool>()
            && report["blocks_restored"]!.GetValue<bool>()
            && report["verifier_values_match_baseline"]!.GetValue<bool>()
            && report["verifier_blocks_match_baseline"]!.GetValue<bool>()
            && report["observer_writes"]!.GetValue<int>() == 0
            && report["verifier_writes"]!.GetValue<int>() == 0;
        report["status"] = ok ? "restored" : "needs_review";
        report["write_attempts"] = session.WriteAttempts;
        Report(report, outPath);
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Write one of the ten in-match sliders and leave it applied. The value itself is not
    /// menu-verified; the block-consistency guard, journal and readback still apply.
    /// </summary>
    private static int Set(Dictionary<string, string> options, string? outPath)
    {
        if (!options.TryGetValue("field", out string? field)
            || !options.TryGetValue("value", out string? text)
            || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int target))
        {
            Console.Error.WriteLine("set requires --field <user|cpu.name> and --value <0-100>");
            return 2;
        }
        using var session = new NativeMenuGsSession();
        var baseline = session.Connect();
        var report = new JsonObject
        {
            ["format"] = "nba2k27-csharp-native-gs-set/1",
            ["epoch"] = baseline.Epoch.ToJsonArray(),
            ["field"] = field,
            ["baseline_hex"] = new JsonArray(
                JsonValue.Create(Hex(baseline.Primary)),
                JsonValue.Create(Hex(baseline.Secondary))),
            ["baseline_values"] = ToJson(baseline.Values),
            ["value_before"] = baseline.Values[field],
            ["value_target"] = target,
            ["writable_field"] = baseline.WritableField(field),
            ["value_menu_verified"] = false,
            ["menu_verified"] = false,
            ["game_settings_saved"] = false,
        };
        if (!baseline.WritableField(field))
        {
            report["status"] = "not_writable";
            report["write_attempts"] = session.WriteAttempts;
            Report(report, outPath);
            return 1;
        }
        if (baseline.Values[field] == target)
        {
            report["status"] = "already_target";
            report["applied_hex"] = report["baseline_hex"]!.DeepClone();
            report["write_attempts"] = session.WriteAttempts;
            Report(report, outPath);
            return 0;
        }
        var applied = session.ApplyField(baseline, field, target);
        report["applied_hex"] = new JsonArray(
            JsonValue.Create(Hex(applied.Primary)),
            JsonValue.Create(Hex(applied.Secondary)));
        report["applied_value"] = applied.Values[field];
        report["apply_writes"] = session.WriteAttempts;

        // A second connection proves the write is visible outside the writing handle.
        using var verifier = new NativeMenuGsSession();
        var verified = verifier.Connect();
        report["verifier_value"] = verified.Values[field];
        report["verifier_writes"] = verifier.WriteAttempts;
        report["verifier_matches_target"] = verified.Values[field] == target;
        report["verifier_blocks_match_applied"] =
            verified.Primary.SequenceEqual(applied.Primary)
            && verified.Secondary.SequenceEqual(applied.Secondary);
        report["verifier_secondary_unchanged"] =
            verified.Secondary.SequenceEqual(baseline.Secondary);
        bool ok = report["verifier_matches_target"]!.GetValue<bool>()
            && report["verifier_blocks_match_applied"]!.GetValue<bool>()
            && report["verifier_secondary_unchanged"]!.GetValue<bool>()
            && report["verifier_writes"]!.GetValue<int>() == 0;
        report["status"] = ok ? "applied" : "needs_review";
        report["journal"] = session.UndoStack.Count > 0 ? session.UndoStack[^1].Path : null;
        Report(report, outPath);
        Console.WriteLine($"{field}: {baseline.Values[field]} -> {target} "
            + $"(verifier {verified.Values[field]}, writer {session.WriteAttempts} writes, "
            + $"verifier {verifier.WriteAttempts} writes, status {report["status"]})");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Read the whole 1296-byte in-match block instead of the five mapped pairs. The second
    /// block sits exactly one block length higher, so the real slot count decides whether the
    /// in-match menu can mirror the general GS field list. <c>--back</c> reads that many bytes
    /// before the block, which tests whether the array continues past the block start.
    /// </summary>
    private static int Probe(Dictionary<string, string> options, string? outPath)
    {
        int back = 0;
        if (options.TryGetValue("back", out string? backText)
            && !int.TryParse(backText, NumberStyles.Integer, CultureInfo.InvariantCulture, out back))
        {
            Console.Error.WriteLine("probe --back requires an integer byte count");
            return 2;
        }
        if (back < 0 || back > NativeGsConstants.PageSize)
        {
            Console.Error.WriteLine("probe --back must be between 0 and 4096");
            return 2;
        }
        using var connection = NativeGsConnection.Open();
        long block0 = connection.Base + NativeGsConstants.LegacyBlockRva0;
        long block1 = connection.Base + NativeGsConstants.LegacyBlockRva1;
        int size = NativeGsConstants.BlockSize;
        byte[] raw0 = connection.Read(block0 - back, size + back);
        byte[] raw1 = connection.Read(block1 - back, size + back);
        var slots = new JsonArray();
        var valid = new JsonArray();
        int differing = 0;
        for (int offset = 0; offset < size; offset++)
        {
            if (raw0[offset] != raw1[offset])
            {
                differing++;
            }
        }
        int firstSlot = -(back / 8);
        int lastSlot = size / 8;
        for (int id = firstSlot; id < lastSlot; id++)
        {
            int offset = back + id * 8;
            float user = BitConverter.ToSingle(raw0, offset);
            float cpu = BitConverter.ToSingle(raw0, offset + 4);
            float userB = BitConverter.ToSingle(raw1, offset);
            float cpuB = BitConverter.ToSingle(raw1, offset + 4);
            bool userOk = IsPercent(user);
            bool cpuOk = IsPercent(cpu);
            var slot = new JsonObject
            {
                ["id"] = id,
                ["general_id"] = id + NativeMenuGs.FirstGeneralId,
                ["user"] = Math.Round(user, 4),
                ["cpu"] = Math.Round(cpu, 4),
                ["user_pct"] = userOk ? (int)MathF.Round(user * 100) : (JsonNode?)null,
                ["cpu_pct"] = cpuOk ? (int)MathF.Round(cpu * 100) : (JsonNode?)null,
                ["both_blocks_equal"] = user == userB && cpu == cpuB,
            };
            slots.Add(slot);
            if (userOk || cpuOk)
            {
                valid.Add(new JsonObject
                {
                    ["id"] = id,
                    ["general_id"] = id + NativeMenuGs.FirstGeneralId,
                    ["offset"] = id * 8,
                    ["user_pct"] = userOk ? (int)MathF.Round(user * 100) : (JsonNode?)null,
                    ["cpu_pct"] = cpuOk ? (int)MathF.Round(cpu * 100) : (JsonNode?)null,
                });
            }
        }
        var report = new JsonObject
        {
            ["format"] = "nba2k27-csharp-native-gs-probe/1",
            ["epoch"] = new JsonArray(connection.Epoch.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            ["block0"] = "0x" + block0.ToString("x", CultureInfo.InvariantCulture),
            ["block1"] = "0x" + block1.ToString("x", CultureInfo.InvariantCulture),
            ["block_size"] = size,
            ["slots"] = size / 8,
            ["differing_bytes"] = differing,
            ["valid_slots"] = valid.Count,
            ["valid"] = valid,
            ["all_slots"] = slots,
            ["write_attempts"] = connection.WriteAttempts,
        };
        Report(report, outPath);
        Console.WriteLine(
            $"block size {size}, {size / 8} slots, {valid.Count} look like percentages, "
            + $"{differing} bytes differ between the two blocks");
        return 0;
    }

    private static bool IsPercent(float number) =>
        float.IsFinite(number) && number >= 0f && number <= 1f
        && Math.Abs(number * 100 - MathF.Round(number * 100)) <= 0.01f;

    private static JsonObject ToJson(Dictionary<string, int> values)
    {
        var node = new JsonObject();
        foreach (string key in NativeMenuGs.Keys)
        {
            node[key] = values[key];
        }
        return node;
    }

    private static string Hex(byte[] raw) => Convert.ToHexString(raw).ToLowerInvariant();

    private static void Report(JsonObject report, string? outPath)
    {
        string json = report.ToJsonString(JournalJson.Options);
        if (outPath is not null)
        {
            File.WriteAllText(outPath, json + "\n", new System.Text.UTF8Encoding(false));
            return;
        }
        Console.Out.Write(json + "\n");
    }
}
