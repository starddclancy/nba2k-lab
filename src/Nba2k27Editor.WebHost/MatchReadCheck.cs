using System.Text.Json.Nodes;

namespace Nba2k27Editor.WebHost;

internal static class MatchReadCheck
{
    internal static int Run(string? path)
    {
        using var bridge = new Bridge();
        var report = new JsonObject { ["format"] = "nba2k27-match-bridge-read/1" };
        try
        {
            JsonNode Read()
            {
                var reply = JsonNode.Parse(bridge.Handle(new JsonObject
                {
                    ["id"] = "match", ["command"] = "matchRead", ["payload"] = new JsonObject(),
                }.ToJsonString()))!;
                if (reply["ok"]?.GetValue<bool>() != true)
                {
                    throw new InvalidOperationException(reply["error"]?.GetValue<string>());
                }
                return reply["data"]!.DeepClone();
            }
            report["cold"] = Read();
            report["warm"] = Read();
            report["writes"] = bridge.TotalWriteAttempts;
            bool ok = bridge.TotalWriteAttempts == 0
                && report["cold"]!["writes"]!.GetValue<int>() == 0
                && report["warm"]!["writes"]!.GetValue<int>() == 0
                && report["cold"]!["issues"]!.AsArray().Count == 0
                && report["warm"]!["issues"]!.AsArray().Count == 0;
            report["status"] = ok ? "ok" : "inconsistent";
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
