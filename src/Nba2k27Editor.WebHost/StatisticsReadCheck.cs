using System.Text.Json.Nodes;

namespace Nba2k27Editor.WebHost;

/// <summary>Live bridge read and same-value submission; no changed value is written.</summary>
internal static class StatisticsReadCheck
{
    internal static int Run(int index, string? path)
    {
        using var bridge = new Bridge();
        var report = new JsonObject { ["format"] = "nba2k27-statistics-read-check/1" };
        try
        {
            JsonNode Call(string command, JsonObject payload)
            {
                var reply = JsonNode.Parse(bridge.Handle(new JsonObject
                {
                    ["id"] = command, ["command"] = command, ["payload"] = payload,
                }.ToJsonString()))!;
                if (reply["ok"]?.GetValue<bool>() != true)
                {
                    throw new InvalidOperationException(reply["error"]?.GetValue<string>());
                }
                return reply["data"]!;
            }
            report["session"] = Call("connect", new()).DeepClone();
            var read = Call("readPlayerStats", new() { ["index"] = index, ["source"] = "roster" });
            report["read"] = read.DeepClone();
            var season = read["seasons"]!.AsArray().First();
            var field = season!["fields"]!.AsArray().Single(field =>
                field!["key"]!.GetValue<string>().EndsWith("/POINTS", StringComparison.Ordinal))!;
            var result = Call("applyPlayer", new()
            {
                ["index"] = index, ["source"] = "roster", ["readToken"] = read["readToken"]!.DeepClone(),
                ["changes"] = new JsonObject { [field["key"]!.GetValue<string>()] = field["value"]!.DeepClone() },
            });
            var after = Call("readPlayerStats", new() { ["index"] = index, ["source"] = "roster" });
            var afterValue = after["seasons"]!.AsArray().First()!["fields"]!.AsArray().Single(cell =>
                cell!["key"]!.GetValue<string>() == field["key"]!.GetValue<string>())!["value"];
            report["sameValueSubmission"] = result.DeepClone();
            report["before"] = field["value"]!.DeepClone();
            report["after"] = afterValue?.DeepClone();
            report["writes"] = bridge.TotalWriteAttempts;
            bool ok = bridge.TotalWriteAttempts == 0 && result["patches"]?.GetValue<int>() == 0
                && JsonNode.DeepEquals(field["value"], afterValue);
            report["status"] = ok ? "ok" : "failed";
            Program.WriteReport(report, path);
            return ok ? 0 : 1;
        }
        catch (Exception exc)
        {
            report["status"] = "failed";
            report["error"] = exc.Message;
            report["writes"] = bridge.TotalWriteAttempts;
            Program.WriteReport(report, path);
            return 1;
        }
    }
}
