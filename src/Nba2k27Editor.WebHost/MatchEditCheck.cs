using System.Text.Json.Nodes;

namespace Nba2k27Editor.WebHost;

/// <summary>
/// Exercise match command gates without initializing a window or opening the game: forged tokens
/// and an empty undo stack must be rejected before any process access, with writes off and on.
/// </summary>
internal static class MatchEditCheck
{
    internal static int Run(string? path)
    {
        var replies = new JsonObject();
        bool ok = true;
        int reads = 0;
        int writes = 0;
        bool connected = false;
        foreach (bool writesOn in new[] { false, true })
        {
            using var bridge = new Bridge(matchDiagnosticWrites: writesOn);
            var group = new JsonArray();
            foreach (string command in new[] { "matchStatus", "matchPrepare", "matchApply", "matchUndo" })
            {
                var request = new JsonObject
                {
                    ["id"] = command, ["command"] = command,
                    ["payload"] = new JsonObject
                    {
                        ["readToken"] = "forged", ["previewToken"] = "forged", ["edits"] = new JsonArray(),
                    },
                };
                var reply = JsonNode.Parse(bridge.Handle(request.ToJsonString()))!;
                group.Add(reply);
                string error = reply["error"]?.GetValue<string>() ?? "";
                ok &= command switch
                {
                    "matchStatus" => reply["ok"]!.GetValue<bool>()
                        && reply["data"]!["readsEnabled"]!.GetValue<bool>()
                        && reply["data"]!["writesEnabled"]!.GetValue<bool>() == writesOn
                        && reply["data"]!["undoDepth"]!.GetValue<int>() == 0,
                    "matchPrepare" => !reply["ok"]!.GetValue<bool>() && error.Contains("快照已失效"),
                    "matchApply" => !reply["ok"]!.GetValue<bool>()
                        && error.Contains(writesOn ? "预览已失效" : "未开启场内写入"),
                    _ => !reply["ok"]!.GetValue<bool>() && error.Contains("没有可撤销"),
                };
            }
            replies[writesOn ? "writesOn" : "writesOff"] = group;
            reads += bridge.MatchReadRequests;
            writes += bridge.TotalWriteAttempts;
            connected |= bridge.MatchConnectionOpen;
        }
        ok &= reads == 0 && !connected && writes == 0;
        Program.WriteReport(new JsonObject
        {
            ["format"] = "nba2k27-match-command-gates/2",
            ["status"] = ok ? "ok" : "failed",
            ["replies"] = replies,
            ["readRequests"] = reads,
            ["gameConnectionOpen"] = connected,
            ["writes"] = writes,
            ["limitations"] = "Command rejection only; no game memory access, pause-flag or live write validation.",
        }, path);
        return ok ? 0 : 1;
    }
}
