using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Match;

namespace Nba2k27Editor.WebHost;

/// <summary>Loopback-only development transport for one captured match and draft previews.</summary>
internal static class BrowserMatchServer
{
    private const string Origin = "http://127.0.0.1:5180";
    private static readonly HashSet<string> Commands = new(StringComparer.Ordinal)
    {
        "matchStatus", "matchRead", "matchPrepare", "matchApply", "matchUndo",
    };

    internal static int Run(string? token, string? reportDirectory, bool allowWrites = false)
    {
        if (!Guid.TryParseExact(token, "N", out _) || reportDirectory is null)
        {
            return 2;
        }
        Directory.CreateDirectory(reportDirectory);
        var checkpoint = new MatchCheckpoint(reportDirectory);
        using var listener = new HttpListener();
        using var bridge = new Bridge(matchDebugReads: true, matchDiagnosticWrites: allowWrites);
        var readTimes = new List<double>();
        int changedSamples = 0;
        listener.Prefixes.Add("http://127.0.0.1:5182/");
        try
        {
            listener.Start();
            Program.WriteReport(new JsonObject
            {
                ["status"] = "listening", ["port"] = 5182, ["origin"] = Origin,
                ["startedUtc"] = DateTime.UtcNow, ["pid"] = Environment.ProcessId,
                ["liveReadLimit"] = null, ["writesEnabled"] = allowWrites,
            }, Path.Combine(reportDirectory, "browser-host.json"));
            while (listener.IsListening)
            {
                var context = listener.GetContext();
                string currentCommand = "";
                try
                {
                    var request = context.Request;
                    var response = context.Response;
                    response.Headers["Cache-Control"] = "no-store";
                    if (!IPAddress.IsLoopback(request.RemoteEndPoint.Address)
                        || request.Url?.Host != "127.0.0.1" || request.Url.AbsolutePath != "/bridge"
                        || request.Headers["Origin"] != Origin)
                    {
                        Reply(context, 403, new JsonObject { ["error"] = "仅允许本机开发页访问" });
                        continue;
                    }
                    response.Headers["Access-Control-Allow-Origin"] = Origin;
                    response.Headers["Vary"] = "Origin";
                    if (request.HttpMethod == "OPTIONS")
                    {
                        response.Headers["Access-Control-Allow-Methods"] = "POST";
                        response.Headers["Access-Control-Allow-Headers"] = "content-type,x-match-session";
                        response.StatusCode = 204;
                        response.Close();
                        continue;
                    }
                    if (request.HttpMethod != "POST" || request.Headers["X-Match-Session"] != token)
                    {
                        Reply(context, 403, new JsonObject { ["error"] = "开发连接凭证无效" });
                        continue;
                    }
                    if (request.ContentLength64 is < 0 or > 65536
                        || request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
                    {
                        Reply(context, 400, new JsonObject { ["error"] = "请求必须为不超过64 KiB的JSON" });
                        continue;
                    }
                    using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                    var root = JsonNode.Parse(reader.ReadToEnd())?.AsObject()
                        ?? throw new InvalidOperationException("请求为空");
                    string command = root["command"]?.GetValue<string>() ?? "";
                    currentCommand = command;
                    if (!Commands.Contains(command))
                    {
                        Reply(context, 400, new JsonObject { ["error"] = "浏览器调试连接仅开放场内快照与修改预览" });
                        continue;
                    }
                    bool live = command == "matchRead";
                    // Live refresh issues a read every few hundred ms; durable per-request files are
                    // kept for prepare/apply/undo, and reads are summarised instead.
                    if (!live)
                    {
                        checkpoint.Save("browser-last-request.json", root);
                        checkpoint.Record("started", command, new JsonObject
                        {
                            ["liveReadRequests"] = bridge.MatchReadRequests, ["writes"] = bridge.TotalWriteAttempts,
                        });
                    }
                    JsonObject reply = JsonNode.Parse(bridge.Handle(root.ToJsonString()))!.AsObject();
                    reply["id"] = root["id"]?.DeepClone();
                    if (live)
                    {
                        if (reply["ok"]?.GetValue<bool>() == true)
                        {
                            readTimes.Add(reply["data"]?["elapsedMs"]?.GetValue<double>() ?? -1);
                            if (bridge.MatchReadRequests == 1)
                            {
                                checkpoint.Save("browser-live-snapshot.json", reply);
                            }
                        }
                        else if (reply["code"]?.GetValue<string>() == "matchChanged")
                        {
                            changedSamples++;
                        }
                        if (bridge.MatchReadRequests % 20 == 1)
                        {
                            checkpoint.Save("browser-live-reads.json", new JsonObject
                            {
                                ["utc"] = DateTime.UtcNow, ["readRequests"] = bridge.MatchReadRequests,
                                ["validSamples"] = readTimes.Count, ["changedSamples"] = changedSamples,
                                ["elapsedMs"] = new JsonArray(readTimes.TakeLast(200)
                                    .Select(value => (JsonNode?)JsonValue.Create(Math.Round(value, 1))).ToArray()),
                                ["writes"] = bridge.TotalWriteAttempts,
                            });
                        }
                    }
                    else
                    {
                        if (command == "matchPrepare")
                        {
                            checkpoint.Save("browser-live-plan.json", reply);
                        }
                        checkpoint.Record("completed", command, new JsonObject
                        {
                            ["ok"] = reply["ok"]?.DeepClone(), ["error"] = reply["error"]?.DeepClone(),
                            ["data"] = command is "matchApply" or "matchUndo" ? reply["data"]?.DeepClone() : null,
                            ["liveReadRequests"] = bridge.MatchReadRequests, ["writes"] = bridge.TotalWriteAttempts,
                        });
                        File.AppendAllText(Path.Combine(reportDirectory, "browser-requests.jsonl"),
                            new JsonObject
                            {
                                ["utc"] = DateTime.UtcNow, ["command"] = command, ["ok"] = reply["ok"]?.DeepClone(),
                                ["liveReadRequests"] = bridge.MatchReadRequests,
                                ["writes"] = bridge.TotalWriteAttempts, ["error"] = reply["error"]?.DeepClone(),
                            }.ToJsonString(Program.JsonOptions) + Environment.NewLine, Encoding.UTF8);
                    }
                    Reply(context, 200, reply);
                }
                catch (Exception exc)
                {
                    try
                    {
                        checkpoint.Record("failed", currentCommand, new JsonObject { ["error"] = exc.Message });
                        Reply(context, 400, new JsonObject { ["error"] = exc.Message });
                    }
                    catch (HttpListenerException)
                    {
                    }
                }
            }
            return 0;
        }
        catch (Exception exc)
        {
            Program.WriteReport(new JsonObject { ["status"] = "failed", ["error"] = exc.Message },
                Path.Combine(reportDirectory, "browser-host.json"));
            return 1;
        }
    }

    private static void Reply(HttpListenerContext context, int status, JsonObject body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body.ToJsonString(Program.JsonOptions));
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes);
        context.Response.Close();
    }
}
