using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Gs;

namespace Nba2k27Editor.WebHost;

/// <summary>Opt-in clicks through the real editor; every successful apply is undone.</summary>
internal static class UiWriteCheck
{
    internal static int Run(string? outPath)
    {
        var report = new JsonObject
        {
            ["format"] = "nba2k27-ui-write-regression/1", ["status"] = "failed",
            ["trials"] = new JsonArray(),
        };
        EditorForm? form = null;
        string? pendingUndo = null;
        Func<int>? byteDifferences = null;
        var reader = new PlayerSession();
        form = new EditorForm(async core =>
        {
            NativeGeneralGsReader? general = null;
            NativeMenuMemory? inMatch = null;
            try
            {
                var requests = new List<JsonObject>();
                core.WebMessageReceived += (_, args) =>
                {
                    if (JsonNode.Parse(args.WebMessageAsJson) is JsonObject request)
                    {
                        requests.Add(request);
                    }
                };
                await Script("window.__writeReplies = {}; chrome.webview.addEventListener('message', "
                    + "e => { if (e.data?.id) window.__writeReplies[e.data.id] = e.data; }); true");
                await Wait("document.querySelectorAll('.roster-player-pane .ant-table-row').length > 1");
                reader.Connect();
                report["pid"] = reader.Memory.Pid;
                var teamPlayers = reader.RosterIndex.Teams[0].Players;
                var before = teamPlayers.ToDictionary(item => item.Index,
                    item => reader.Memory.ReadBytes(item.Address, reader.Model.DomainStride("Players")));
                var teamData = Call("readTeam", new() { ["index"] = 0 });
                string teamSearch = teamData["sections"]!.AsArray().SelectMany(section => section!["fields"]!.AsArray())
                    .Single(field => field!["key"]!.GetValue<string>().EndsWith("/AUDIOCHANTVARIATION", StringComparison.Ordinal))!
                    ["label"]!.GetValue<string>();
                long teamAddress = long.Parse(teamData["address"]!.GetValue<string>()[2..], NumberStyles.HexNumber);
                byte[] teamBytes = reader.Memory.ReadBytes(teamAddress, reader.Model.DomainStride("Teams"));
                general = new NativeGeneralGsReader();
                inMatch = NativeMenuMemory.Open();
                var gsBytes = general.Read().Blocks;
                var matchBytes = inMatch.ReadBlocks();
                byteDifferences = () =>
                {
                    int differences = teamPlayers.Sum(item => Diff(before[item.Index],
                        reader.Memory.ReadBytes(item.Address, before[item.Index].Length)));
                    differences += Diff(teamBytes, reader.Memory.ReadBytes(teamAddress, teamBytes.Length));
                    var gsAfter = general.Read().Blocks;
                    var matchAfter = inMatch.ReadBlocks();
                    return differences + Diff(gsBytes[0], gsAfter[0]) + Diff(gsBytes[1], gsAfter[1])
                        + Diff(matchBytes.Primary, matchAfter.Primary) + Diff(matchBytes.Secondary, matchAfter.Secondary);
                };

                await Script("document.querySelector('.roster-player-pane .ant-table-row').click(); true");
                await Wait("!!document.querySelector('.field-toolbar .ant-typography')");
                await Tab("基本资料");
                await Exercise("player_single", ".detail-host [data-row-key=\"Vitals/Vitals/NUMBER\"] input",
                    ".field-toolbar", "应用", "applyPlayer", "undoPlayer");

                await Script("Array.from(document.querySelectorAll('.roster-player-pane .ant-table-row')).slice(0,2)"
                    + ".forEach(e => e.querySelector('.ant-checkbox-input').click()); true");
                await Button(".roster-player-pane", "批量编辑");
                await Wait("!!document.querySelector('.ant-modal .ant-select:not(.ant-select-disabled)')");
                await Script("document.querySelector('.ant-modal .ant-select-selector').dispatchEvent("
                    + "new MouseEvent('mousedown', {bubbles:true})); true");
                await Input(".ant-modal .ant-select input", "三分投篮");
                await Wait("Array.from(document.querySelectorAll('.ant-select-item-option'))"
                    + ".some(e => (e.getAttribute('title') ?? '').includes('三分投篮'))");
                await Script("Array.from(document.querySelectorAll('.ant-select-item-option'))"
                    + ".find(e => (e.getAttribute('title') ?? '').includes('三分投篮')).click(); true");
                await Wait("document.querySelectorAll('.batch-preview .ant-table-row').length === 2");
                JsonArray preview = JsonNode.Parse(await Script(
                    "Array.from(document.querySelectorAll('.batch-preview .ant-table-row'))"
                    + ".map(e => ({index:Number(e.dataset.rowKey), value:Number(e.cells[1].textContent)}))"))!.AsArray();
                int batchTarget = preview[0]!["value"]!.GetValue<int>() < 99
                    ? preview[0]!["value"]!.GetValue<int>() + 1 : preview[0]!["value"]!.GetValue<int>() - 1;
                await Input(".ant-modal .ant-input-number-input", batchTarget.ToString(CultureInfo.InvariantCulture));
                int startWrites = form!.WriteAttempts;
                JsonNode batchApply = await Mutation("applyBatch", ".ant-modal", "应用到全部");
                pendingUndo = "undoPlayer";
                await Wait("!document.querySelector('.ant-modal')");
                await Wait("document.querySelectorAll('.roster-player-pane .ant-table-row').length > 1"
                    + " && !document.querySelector('.roster-player-pane .ant-btn-loading, .roster-player-pane .ant-spin-spinning')");
                bool batchReadback = preview.All(row =>
                    Convert.ToInt32(reader.ReadPlayer(reader.Items.Single(item =>
                        item.Index == row!["index"]!.GetValue<int>())).Values["Attributes/Offense/3POINT"].Display) == batchTarget);
                await Script("document.querySelector('.roster-player-pane .ant-table-row').click(); true");
                await Wait("!!document.querySelector('.field-toolbar .ant-typography')");
                await Tab("能力值");
                string threeInput = ".detail-host [data-row-key=\"Attributes/Offense/3POINT\"] input";
                await Wait($"document.querySelector({JsonSerializer.Serialize(threeInput)})?.value === '{batchTarget}'");
                JsonNode batchUndo = await Mutation("undoPlayer", ".field-toolbar", "撤销");
                pendingUndo = null;
                await Wait($"document.querySelector({JsonSerializer.Serialize(threeInput)})?.value === "
                    + JsonSerializer.Serialize(preview[0]!["value"]!.ToString()));
                bool batchRestored = preview.All(row =>
                    Convert.ToInt32(reader.ReadPlayer(reader.Items.Single(item =>
                        item.Index == row!["index"]!.GetValue<int>())).Values["Attributes/Offense/3POINT"].Display)
                        == row!["value"]!.GetValue<int>());
                report["trials"]!.AsArray().Add(new JsonObject
                {
                    ["operation"] = "player_batch", ["before"] = preview, ["after"] = batchTarget,
                    ["patched"] = batchApply["patches"]!.GetValue<int>() > 0,
                    ["apply_patches"] = batchApply["patches"]!.DeepClone(),
                    ["undo_completed"] = batchUndo is not null, ["readback_matches"] = batchReadback,
                    ["restored_values_equal"] = batchRestored, ["writes"] = form.WriteAttempts - startWrites,
                });
                Require(batchReadback && batchRestored, "Batch UI readback or undo did not match");

                await Top("球队资料");
                await Wait("!!document.querySelector('.field-toolbar input[placeholder=\"搜索球队资料\"]')");
                await Script("document.querySelector('.field-toolbar .ant-switch').click(); true");
                await Input("input[placeholder=\"搜索球队资料\"]", teamSearch);
                await Wait("document.querySelectorAll('.detail-host .ant-table-row').length === 1");
                await Exercise("team", ".detail-host .ant-table-row input", ".field-toolbar",
                    "应用", "applyTeam", "undoTeam", 255);

                await Top("比赛设置");
                await Wait("document.querySelectorAll('.gs-panel .ant-table-row').length === 70");
                await Exercise("general_gs", ".gs-panel tr[data-row-key=\"inside_shot\"] input",
                    ".gs-panel", "应用改动", "gsApply", "gsUndo", 100);
                await Script("Array.from(document.querySelectorAll('.gs-panel .ant-segmented-item'))"
                    + ".find(e => e.textContent.trim() === '当前比赛设置').click(); true");
                await Wait("document.querySelector('.gs-panel .ant-segmented-item-selected')?.textContent.trim()"
                    + " === '当前比赛设置' && !document.querySelector('.gs-panel .ant-btn-loading')");
                await Exercise("in_match_gs", ".gs-panel tr[data-row-key=\"inside_shot\"] input",
                    ".gs-panel", "应用改动", "inMatchApply", "inMatchUndo", 100);

                int differences = byteDifferences();
                report["final_byte_mismatches"] = differences;
                byteDifferences = null;
                report["status"] = differences == 0 ? "ok" : "failed";
                if (outPath is not null)
                {
                    string screenshot = Path.ChangeExtension(outPath, ".png");
                    using var image = File.Create(screenshot);
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                    report["screenshot"] = Path.GetFileName(screenshot);
                }

                async Task Exercise(string name, string selector, string scope, string applyLabel,
                    string applyCommand, string undoCommand, int maximum = 99)
                {
                    await Wait($"!!document.querySelector({JsonSerializer.Serialize(selector)})");
                    string original = JsonNode.Parse(await Script(
                        $"document.querySelector({JsonSerializer.Serialize(selector)}).value"))!.GetValue<string>();
                    int value = int.Parse(original, CultureInfo.InvariantCulture);
                    int target = value < maximum ? value + 1 : value - 1;
                    int writesBefore = form!.WriteAttempts;
                    await Input(selector, target.ToString(CultureInfo.InvariantCulture));
                    JsonNode applied = await Mutation(applyCommand, scope, applyLabel);
                    pendingUndo = undoCommand;
                    await Wait($"document.querySelector({JsonSerializer.Serialize(selector)})?.value === '{target}'"
                        + " && !document.querySelector('.detail-host .ant-btn-loading, .gs-panel .ant-btn-loading')");
                    int applyWrites = form.WriteAttempts - writesBefore;
                    await Mutation(undoCommand, scope, "撤销");
                    pendingUndo = null;
                    await Wait($"document.querySelector({JsonSerializer.Serialize(selector)})?.value === "
                        + JsonSerializer.Serialize(original));
                    int totalWrites = form.WriteAttempts - writesBefore;
                    Require(applyWrites > 0 && totalWrites == applyWrites * 2, name + " write count did not balance");
                    report["trials"]!.AsArray().Add(new JsonObject
                    {
                        ["operation"] = name, ["before"] = value, ["after"] = target, ["restored"] = value,
                        ["patched"] = applyWrites > 0, ["apply_writes"] = applyWrites,
                        ["undo_writes"] = totalWrites - applyWrites, ["ui_readback_matches"] = true,
                        ["writes_conserved"] = totalWrites == applyWrites * 2,
                        ["reply_received"] = applied is not null,
                    });
                }

                async Task<JsonNode> Mutation(string command, string scope, string label)
                {
                    int count = requests.Count;
                    pendingUndo = command switch
                    {
                        "applyPlayer" or "applyBatch" => "undoPlayer",
                        "applyTeam" => "undoTeam",
                        "gsApply" => "gsUndo",
                        "inMatchApply" => "inMatchUndo",
                        _ => pendingUndo,
                    };
                    await Button(scope, label);
                    await WaitUntil(() => Task.FromResult(requests.Skip(count).Any(request =>
                        request["command"]?.GetValue<string>() == command)));
                    string id = requests.Skip(count).First(request => request["command"]?.GetValue<string>() == command)
                        ["id"]!.GetValue<string>();
                    string expression = "window.__writeReplies[" + JsonSerializer.Serialize(id) + "]";
                    await Wait($"!!({expression})");
                    JsonNode reply = JsonNode.Parse(await Script(expression))!;
                    Require(reply["ok"]?.GetValue<bool>() == true,
                        command + ": " + reply["error"]?.GetValue<string>());
                    return reply["data"]!;
                }

                async Task Button(string scope, string label)
                {
                    string expression = $"Array.from(document.querySelectorAll({JsonSerializer.Serialize(scope + " button")}))"
                        + $".find(e => e.textContent.replace(/\\s/g,'').startsWith({JsonSerializer.Serialize(label.Replace(" ", ""))}) && !e.disabled)";
                    await Wait($"!!({expression})");
                    await Script($"({expression}).click(); true");
                }

                async Task Input(string selector, string value)
                {
                    await Script("(() => { const e = document.querySelector(" + JsonSerializer.Serialize(selector) + ");"
                        + "Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(e,"
                        + JsonSerializer.Serialize(value) + "); e.dispatchEvent(new Event('input', {bubbles:true})); })()");
                    await Task.Delay(150);
                }

                async Task Tab(string label)
                {
                    await Script("Array.from(document.querySelectorAll('.detail-host .ant-tabs-tab'))"
                        + $".find(e => e.textContent.startsWith({JsonSerializer.Serialize(label)})).click(); true");
                    await Task.Delay(150);
                }

                async Task Top(string label)
                {
                    await Script("Array.from(document.querySelectorAll('.app-header .ant-segmented-item'))"
                        + $".find(e => e.textContent.trim() === {JsonSerializer.Serialize(label)}).click(); true");
                }

                Task<string> Script(string script) => core.ExecuteScriptAsync(script);
                async Task Wait(string expression)
                {
                    try
                    {
                        await WaitUntil(async () => JsonNode.Parse(await Script(expression))?.GetValue<bool>() == true);
                    }
                    catch (Exception exc)
                    {
                        throw new InvalidOperationException("UI expected: " + expression, exc);
                    }
                }
                async Task WaitUntil(Func<Task<bool>> ready)
                {
                    for (int attempt = 0; attempt < 100; attempt++)
                    {
                        if (await ready())
                        {
                            return;
                        }
                        await Task.Delay(100);
                    }
                    throw new InvalidOperationException("The editor UI did not reach the expected state");
                }
            }
            catch (Exception exc)
            {
                report["error"] = exc.Message;
                if (outPath is not null)
                {
                    string screenshot = Path.ChangeExtension(outPath, ".failure.png");
                    using var image = File.Create(screenshot);
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                    report["screenshot"] = Path.GetFileName(screenshot);
                }
            }
            finally
            {
                if (pendingUndo is not null)
                {
                    try
                    {
                        Call(pendingUndo);
                        report["recovery_undo"] = true;
                    }
                    catch (Exception exc)
                    {
                        report["recovery_error"] = exc.Message;
                    }
                }
                if (byteDifferences is not null)
                {
                    try
                    {
                        report["final_byte_mismatches"] = byteDifferences();
                    }
                    catch (Exception exc)
                    {
                        report["restore_check_error"] = exc.Message;
                    }
                }
                report["writes"] = form!.WriteAttempts;
                general?.Dispose();
                inMatch?.Dispose();
                reader.Close();
                form.Close();
            }
        });
        Application.Run(form);
        Program.WriteReport(report, outPath);
        return report["status"]?.GetValue<string>() == "ok" ? 0 : 1;

        JsonNode Call(string command, JsonObject? payload = null)
        {
            JsonNode response = JsonNode.Parse(form!.CommandBridge.Handle(new JsonObject
            {
                ["id"] = "ui-observer-" + command, ["command"] = command, ["payload"] = payload ?? new(),
            }.ToJsonString()))!;
            Require(response["ok"]?.GetValue<bool>() == true, response["error"]?.GetValue<string>() ?? command);
            return response["data"]!;
        }
    }

    private static int Diff(byte[] before, byte[] after) =>
        before.Zip(after).Count(pair => pair.First != pair.Second);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
