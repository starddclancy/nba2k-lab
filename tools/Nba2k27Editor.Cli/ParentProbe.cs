using System.Text;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Cli;

/// <summary>Read-only audit of authored parent pointers, including legitimately absent objects.</summary>
internal static class ParentProbe
{
    internal static int Run(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("out", out string? output))
        {
            throw new ArgumentException("parents requires --out <file.json>");
        }
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect(buildRosterIndex: false);
            session.Guard();
            var parents = new JsonArray();
            foreach (var group in session.Specs.Values
                .Where(spec => !string.IsNullOrEmpty(FieldIo.NodeText(spec.Payload["parent"])))
                .GroupBy(spec => FieldIo.NodeText(spec.Payload["parent"])))
            {
                var child = group.First();
                var parent = session.Model.ParentPayload("Players", child.Payload)!;
                int loaded = 0;
                int absent = 0;
                int failed = 0;
                var samples = new JsonArray();
                foreach (var item in items)
                {
                    try
                    {
                        long address = FieldIo.FieldAddress(session.Memory, item.Address, parent);
                        long target = Convert.ToInt64(FieldIo.ReadAuthoredValue(session.Memory, address, parent));
                        if (target == 0)
                        {
                            absent++;
                        }
                        else
                        {
                            loaded++;
                            if (samples.Count < 3)
                            {
                                samples.Add(new JsonObject
                                {
                                    ["player"] = item.Label,
                                    ["index"] = item.Index,
                                    ["slot"] = $"0x{address:X}",
                                    ["target"] = $"0x{target:X}",
                                });
                            }
                        }
                    }
                    catch (Exception)
                    {
                        failed++;
                    }
                }
                parents.Add(new JsonObject
                {
                    ["parent"] = group.Key,
                    ["payload"] = parent.DeepClone(),
                    ["child_fields"] = group.Count(),
                    ["nonzero_pointers"] = loaded,
                    ["null_pointers"] = absent,
                    ["pointer_read_failures"] = failed,
                    ["nonzero_samples"] = samples,
                });
            }

            string name = options.GetValueOrDefault("player", "Eric Snow");
            var selected = items.Where(item => item.Label == name).ToList();
            if (selected.Count != 1)
            {
                throw new ArgumentException($"player name must match exactly once: {name} ({selected.Count})");
            }
            var snapshot = session.ReadPlayer(selected[0]);
            var errors = new JsonObject();
            foreach (var pair in snapshot.Errors)
            {
                errors[pair.Key] = pair.Value;
            }
            var absentGroups = new JsonObject();
            foreach (var group in snapshot.Errors
                .Where(pair => pair.Value.StartsWith("Parent object is not loaded:", StringComparison.Ordinal))
                .GroupBy(pair => pair.Key.Split('/')[0]))
            {
                absentGroups[group.Key] = group.Count();
            }
            var headSpec = session.Specs.Values.Single(spec => spec.Name == "ISSCANNEDHEAD");
            var headCounts = new JsonObject { ["scanned"] = 0, ["not_scanned"] = 0, ["unavailable"] = 0 };
            var headSamples = new JsonArray();
            foreach (var item in items)
            {
                var head = PlayerAppearance.ReadHeadScan(session.Model, item);
                headCounts[head.Status] = headCounts[head.Status]!.GetValue<int>() + 1;
                if (headSamples.Count < 8 && (head.Status == "scanned" || item.Label == name))
                {
                    headSamples.Add(new JsonObject
                    {
                        ["player"] = item.Label,
                        ["status"] = head.Status,
                        ["flag"] = head.Flag,
                    });
                }
            }
            var headValue = new JsonObject { ["key"] = headSpec.Key };
            if (snapshot.Values.TryGetValue(headSpec.Key, out var value))
            {
                headValue["raw"] = Convert.ToInt64(value.Raw);
                headValue["address"] = $"0x{value.Address:X}";
            }
            else
            {
                headValue["error"] = snapshot.Errors.GetValueOrDefault(headSpec.Key);
            }
            session.Guard();
            var report = new JsonObject
            {
                ["format"] = "nba2k27-parent-pointer-audit/1",
                ["version"] = AppVersion.Value,
                ["pid"] = session.Memory.Pid,
                ["player_count"] = items.Count,
                ["spec_count"] = session.Specs.Count,
                ["parents"] = parents,
                ["player"] = snapshot.Item.Label,
                ["read_fields"] = snapshot.Values.Count,
                ["error_fields"] = snapshot.Errors.Count,
                ["absent_object_fields_by_section"] = absentGroups,
                ["scanned_head_flag"] = headValue,
                ["head_scan_counts"] = headCounts,
                ["head_scan_samples"] = headSamples,
                ["errors"] = errors,
                ["writes"] = ((Win32Memory)session.Memory).Writes,
                ["note"] = "Nonzero pointers do not prove target validity. Null objects remain unavailable; no allocation or pointer substitution.",
            };
            File.WriteAllText(output, report.ToJsonString(Json.Options), new UTF8Encoding(false));
            Console.WriteLine($"wrote {output}: {snapshot.Values.Count} readable, {snapshot.Errors.Count} unavailable, {((Win32Memory)session.Memory).Writes} writes");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
}
