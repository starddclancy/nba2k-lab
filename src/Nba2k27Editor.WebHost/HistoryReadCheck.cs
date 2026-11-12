using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;

namespace Nba2k27Editor.WebHost;

internal sealed partial class Bridge
{
    internal byte[] HistoryBytes(string key)
    {
        var snapshot = _history!.Read(key);
        return Session().Memory.ReadBytes(snapshot.Address, snapshot.Bytes.Length);
    }
}

internal static class HistoryReadCheck
{
    internal static int Run(string? path, bool write)
    {
        using var bridge = new Bridge();
        var report = new JsonObject { ["format"] = "nba2k27-history-bridge-check/1", ["status"] = "failed" };
        string? pending = null;
        JsonNode Call(string command, JsonObject payload)
        {
            var response = JsonNode.Parse(bridge.Handle(new JsonObject
            {
                ["id"] = command, ["command"] = command, ["payload"] = payload,
            }.ToJsonString()))!;
            if (response["ok"]?.GetValue<bool>() != true)
            {
                throw new InvalidOperationException(response["error"]?.GetValue<string>());
            }
            return response["data"]!;
        }
        try
        {
            report["session"] = Call("connect", new()).DeepClone();
            var trials = new JsonArray();
            report["trials"] = trials;
            var rejected = new JsonArray();
            report["rejections"] = rejected;
            void Reject(string label, string command, JsonObject payload)
            {
                int writesBefore = bridge.TotalWriteAttempts;
                var reply = JsonNode.Parse(bridge.Handle(new JsonObject
                    { ["id"] = label, ["command"] = command, ["payload"] = payload }.ToJsonString()))!;
                bool passed = reply["ok"]?.GetValue<bool>() == false && bridge.TotalWriteAttempts == writesBefore;
                rejected.Add(new JsonObject { ["case"] = label, ["rejected"] = !reply["ok"]!.GetValue<bool>(),
                    ["writes"] = bridge.TotalWriteAttempts - writesBefore, ["error"] = reply["error"]?.DeepClone() });
                if (!passed) { throw new InvalidOperationException($"History rejection failed: {label}"); }
            }
            var examples = new JsonArray();
            report["reads"] = examples;
            var regions = new Dictionary<string, byte[]>();
            foreach (var (kind, field) in new[] { ("league", "DATA"), ("totals", "CHAMPIONSHIPS"),
                ("season", "TOTALPOINTS"), ("franchise", "STARTYEAR"), ("jersey", "RETIREDYEAR") })
            {
                var list = Call("historyList", new() { ["kind"] = kind, ["teamIndex"] = 0 }).AsArray();
                JsonNode item = kind == "league"
                    ? list.First(i => i!["category"]!.GetValue<string>() == HistorySession.Categories[3])!
                    : list.First()!;
                string key = item["key"]!.GetValue<string>();
                var bytes = bridge.HistoryBytes(key);
                regions[key] = bytes;
                var read = Call("historyRead", new() { ["key"] = key });
                examples.Add(read.DeepClone());
                var cell = read["fields"]!.AsArray().Single(f => f!["key"]!.GetValue<string>() == field)!;
                var before = cell["value"]!.DeepClone();
                var next = write ? JsonValue.Create(before.GetValue<double>() + 1)! : before.DeepClone();
                Reject($"invalid-token-{kind}", "historyApply", new()
                {
                    ["key"] = key, ["readToken"] = "invalid",
                    ["changes"] = new JsonObject { [field] = before.DeepClone() },
                });
                var applied = Call("historyApply", new()
                {
                    ["key"] = key, ["readToken"] = read["readToken"]!.DeepClone(),
                    ["changes"] = new JsonObject { [field] = next.DeepClone() }, ["acknowledgeShared"] = true,
                });
                if (applied["patched"]!.GetValue<bool>())
                {
                    pending = key;
                }
                var after = Call("historyRead", new() { ["key"] = key });
                var afterValue = after["fields"]!.AsArray().Single(f => f!["key"]!.GetValue<string>() == field)!["value"];
                bool matches = Math.Abs(afterValue!.GetValue<double>() - next.GetValue<double>()) < 0.001;
                var trial = new JsonObject { ["key"] = key, ["field"] = field, ["before"] = before,
                    ["requested"] = next, ["after"] = afterValue.DeepClone(), ["apply"] = applied.DeepClone(),
                    ["patched"] = applied["patched"]!.DeepClone(), ["readbackMatches"] = matches };
                trials.Add(trial);
                if (write)
                {
                    Reject($"consumed-token-{kind}", "historyApply", new()
                    {
                        ["key"] = key, ["readToken"] = read["readToken"]!.DeepClone(),
                        ["changes"] = new JsonObject { [field] = next.DeepClone() },
                    });
                    trial["undo"] = Call("historyUndo", new() { ["key"] = key }).DeepClone();
                    pending = null;
                }
                trial["bytesRestored"] = bridge.HistoryBytes(key).SequenceEqual(bytes);
                if (!matches || !trial["bytesRestored"]!.GetValue<bool>()
                    || write != trial["patched"]!.GetValue<bool>())
                {
                    throw new InvalidOperationException("Bridge history roundtrip mismatch");
                }
            }
            var genericTeam = Call("readTeam", new() { ["index"] = 0 });
            report["genericTeamExcludesIndependentTables"] = genericTeam["sections"]!.AsArray().All(section =>
                section!["name"]!.GetValue<string>() is not ("Team History" or "Team Season Stats" or "Team Stats Edit" or "Team Latest Record"));
            Reject("generic-team-cross-table-write", "applyTeam", new() { ["index"] = 0,
                ["changes"] = new JsonObject { ["Team Season Stats/Offense/TOTALPOINTS"] = 1 } });
            var sharedRead = Call("historyRead", new() { ["key"] = "season:0:0" });
            report["sharedSample"] = sharedRead.DeepClone();
            if (sharedRead["sharedTeams"]!.AsArray().Count > 0)
            {
                var sharedValue = sharedRead["fields"]!.AsArray().Single(f => f!["key"]!.GetValue<string>() == "TOTALPOINTS")!["value"]!;
                Reject("shared-season-needs-acknowledgement", "historyApply", new()
                {
                    ["key"] = "season:0:0", ["readToken"] = sharedRead["readToken"]!.DeepClone(),
                    ["changes"] = new JsonObject { ["TOTALPOINTS"] = sharedValue.DeepClone() },
                });
                report["sharedSameValueAcknowledged"] = Call("historyApply", new()
                {
                    ["key"] = "season:0:0", ["readToken"] = sharedRead["readToken"]!.DeepClone(),
                    ["acknowledgeShared"] = true,
                    ["changes"] = new JsonObject { ["TOTALPOINTS"] = sharedValue.DeepClone() },
                }).DeepClone();
            }
            else
            {
                report["sharedSampleStatus"] = "current record not shared";
            }
            report["finalBytesEqual"] = regions.All(region => bridge.HistoryBytes(region.Key).SequenceEqual(region.Value));
            report["writes"] = bridge.TotalWriteAttempts;
            if (!report["finalBytesEqual"]!.GetValue<bool>() || !write && bridge.TotalWriteAttempts != 0
                || !report["genericTeamExcludesIndependentTables"]!.GetValue<bool>())
            {
                throw new InvalidOperationException("Bridge invariant failed");
            }
            report["status"] = "ok";
            Program.WriteReport(report, path);
            return 0;
        }
        catch (Exception exc)
        {
            report["error"] = exc.ToString();
            if (pending is not null)
            {
                try { report["cleanupUndo"] = Call("historyUndo", new() { ["key"] = pending }).DeepClone(); }
                catch (Exception cleanup) { report["cleanupError"] = cleanup.Message; }
            }
            report["writes"] = bridge.TotalWriteAttempts;
            Program.WriteReport(report, path);
            return 1;
        }
    }
}
