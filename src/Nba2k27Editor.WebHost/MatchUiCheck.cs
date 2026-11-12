using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace Nba2k27Editor.WebHost;

/// <summary>Drive the editor's own WebView2 DOM against the live, read-only match bridge.</summary>
internal static class MatchUiCheck
{
    internal static int Run(string? path, bool pollingOnly = false, bool safetyOnly = false)
    {
        var report = new JsonObject { ["format"] = "nba2k27-match-ui-check/1", ["status"] = "failed" };
        int exit = 1;
        bool running = false;
        EditorForm? form = null;
        form = new EditorForm(async core =>
        {
            if (running)
            {
                return;
            }
            running = true;
            try
            {
                async Task Wait(string predicate)
                {
                    for (int attempt = 0; attempt < 100; attempt++)
                    {
                        var node = JsonNode.Parse(await core.ExecuteScriptAsync(predicate));
                        if (node is JsonValue value && value.TryGetValue<bool>(out bool result) && result)
                        {
                            return;
                        }
                        await Task.Delay(100);
                    }
                    throw new InvalidOperationException("Match UI condition timed out: " + predicate);
                }
                async Task<JsonNode> Probe() => JsonNode.Parse(await core.ExecuteScriptAsync("""
                    (() => {
                        const root = document.querySelector('.match-workspace');
                        const rows = Array.from(document.querySelectorAll('.match-player-table .ant-table-tbody .ant-table-row'));
                        const logos = Array.from(document.querySelectorAll('.match-scoreboard img.team-logo'));
                        const portraits = Array.from(document.querySelectorAll('.match-player-table img.avatar'));
                        return {
                            pid: Number(root?.dataset.pid), period: Number(root?.dataset.period),
                            side: Number(root?.dataset.side), slot: Number(root?.dataset.slot),
                            scores: Array.from(document.querySelectorAll('.match-score-team > strong')).map(e => Number(e.textContent)),
                            players: rows.map(e => e.innerText),
                            logos: logos.map(e => ({src:e.getAttribute('src'),loaded:e.complete && e.naturalWidth > 0})),
                            portraits: portraits.length,
                            avatars: root.querySelectorAll('.match-player .avatar').length,
                            loadedPortraits: portraits.filter(e => e.complete && e.naturalWidth > 0).length,
                            editableInputs: root.querySelectorAll('input[type=number]').length,
                            totalButtons: Array.from(root.querySelectorAll('button')).map(e => e.textContent),
                            horizontalOverflow: document.documentElement.scrollWidth > innerWidth,
                            overlap: (() => {
                                const boxes = Array.from(document.querySelectorAll('.match-scoreboard > div')).map(e => e.getBoundingClientRect());
                                return boxes.some((box,i) => i > 0 && box.left < boxes[i-1].right - 1);
                            })(),
                            text: root.innerText
                        };
                    })()
                    """))!;

                await Wait("!!document.querySelector('.match-workspace .ant-alert')"
                    + " || document.querySelector('.match-workspace')?.dataset.readsEnabled === 'true'");
                if (safetyOnly)
                {
                    report["format"] = "nba2k27-match-safety-ui/1";
                    await core.ExecuteScriptAsync("""
                        document.querySelector('.match-auto-refresh button').click();
                        document.querySelector('.match-toolbar > .ant-btn').click();
                        true
                        """);
                    await Task.Delay(1200);
                    var pausedViews = new JsonArray();
                    bool safe = true;
                    form!.MinimumSize = new Size(400, 400);
                    foreach (var (name, width, height) in new[] { ("desktop", 1440, 900), ("mobile", 520, 850) })
                    {
                        form.ClientSize = new Size(width, height);
                        await Task.Delay(150);
                        var state = JsonNode.Parse(await core.ExecuteScriptAsync("""
                            ({
                                autoDisabled: document.querySelector('.match-auto-refresh button').disabled,
                                autoOff: document.querySelector('.match-auto-refresh button').getAttribute('aria-checked') === 'false',
                                refreshDisabled: document.querySelector('.match-toolbar > .ant-btn').disabled,
                                blocked: document.querySelector('.match-workspace').dataset.readsEnabled === 'false',
                                reason: document.querySelector('.match-workspace .ant-alert')?.textContent,
                                staleScores: !!document.querySelector('.match-scoreboard'),
                                horizontalOverflow: document.documentElement.scrollWidth > innerWidth
                            })
                            """))!;
                        safe &= state["autoDisabled"]!.GetValue<bool>()
                            && state["autoOff"]!.GetValue<bool>()
                            && state["refreshDisabled"]!.GetValue<bool>()
                            && state["blocked"]!.GetValue<bool>()
                            && !state["staleScores"]!.GetValue<bool>()
                            && !state["horizontalOverflow"]!.GetValue<bool>()
                            && state["reason"]!.GetValue<string>().Contains("排查中");
                        state["viewport"] = name;
                        if (path is not null)
                        {
                            string image = Path.ChangeExtension(path, name + ".png");
                            using var stream = File.Create(image);
                            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                            state["screenshot"] = Path.GetFileName(image);
                        }
                        pausedViews.Add(state);
                    }
                    report["views"] = pausedViews;
                    report["uiReadRequests"] = form.CommandBridge.MatchReadRequests;
                    safe &= form.CommandBridge.MatchReadRequests == 0;
                    var rejected = JsonNode.Parse(form.CommandBridge.Handle(
                        """{"id":"blocked","command":"matchRead","payload":{}}"""))!;
                    report["backendRejection"] = rejected;
                    report["gameConnectionOpen"] = form.CommandBridge.MatchConnectionOpen;
                    safe &= rejected["ok"]!.GetValue<bool>() == false
                        && rejected["error"]!.GetValue<string>().Contains("排查中")
                        && !form.CommandBridge.MatchConnectionOpen
                        && form.WriteAttempts == 0;
                    report["status"] = safe ? "ok" : "failed";
                    exit = safe ? 0 : 1;
                    return;
                }
                if (JsonNode.Parse(await core.ExecuteScriptAsync(
                    "document.querySelector('.match-workspace').dataset.readsEnabled === 'true'"))!
                    .GetValue<bool>() != true)
                {
                    throw new InvalidOperationException("Match reads are paused; live UI checks are unavailable");
                }

                if (pollingOnly)
                {
                    // Observe the real transport without substituting game data or host replies.
                    await core.ExecuteScriptAsync("""
                        (() => {
                            const host = window.chrome.webview;
                            const send = host.postMessage.bind(host);
                            const ids = new Set();
                            window.__matchPoll = {requests:0, replies:0, maxPending:0, samples:[]};
                            host.postMessage = message => {
                                if (message.command === 'matchRead') {
                                    ids.add(message.id);
                                    window.__matchPoll.requests++;
                                    window.__matchPoll.maxPending = Math.max(window.__matchPoll.maxPending, ids.size);
                                }
                                send(message);
                            };
                            host.addEventListener('message', event => {
                                if (ids.delete(event.data.id)) {
                                    window.__matchPoll.replies++;
                                    const data = event.data.data;
                                    window.__matchPoll.samples.push(data ? {
                                        pid:data.pid, capturedUtc:data.capturedUtc,
                                        scores:data.teams.map(team => team.stats.total.points),
                                        elapsedMs:data.totalElapsedMs, writes:data.writes,
                                        editable:data.editable, pauseStateVerified:data.pauseStateVerified
                                    } : {error:event.data.error});
                                }
                            });
                        })()
                        """);
                    await core.ExecuteScriptAsync("document.querySelector('.match-auto-refresh button').click(); true");
                    await Task.Delay(4500);
                    var automatic = JsonNode.Parse(await core.ExecuteScriptAsync("window.__matchPoll"))!;
                    report["automatic"] = automatic;
                    await core.ExecuteScriptAsync("document.querySelector('.match-auto-refresh button').click(); true");
                    await Task.Delay(150);
                    int stopped = JsonNode.Parse(await core.ExecuteScriptAsync("window.__matchPoll.requests"))!.GetValue<int>();
                    await Task.Delay(2200);
                    int afterStop = JsonNode.Parse(await core.ExecuteScriptAsync("window.__matchPoll.requests"))!.GetValue<int>();
                    report["stoppedRequests"] = stopped;
                    report["afterStopRequests"] = afterStop;
                    await core.ExecuteScriptAsync("document.querySelector('.match-toolbar > .ant-btn').click(); true");
                    await Wait("window.__matchPoll.requests > " + afterStop
                        + " && window.__matchPoll.requests === window.__matchPoll.replies");
                    report["manual"] = JsonNode.Parse(await core.ExecuteScriptAsync("window.__matchPoll"));
                    report["display"] = JsonNode.Parse(await core.ExecuteScriptAsync("""
                        ({
                            pid: document.querySelector('.match-workspace').dataset.pid ?? null,
                            error: document.querySelector('.match-workspace .ant-alert')?.textContent ?? null,
                            oldScoresPresent: !!document.querySelector('.match-scoreboard'),
                            autoOff: document.querySelector('.match-auto-refresh button').getAttribute('aria-checked') === 'false'
                        })
                        """));
                    bool pollingOk = automatic["requests"]!.GetValue<int>() >= 2
                        && automatic["maxPending"]!.GetValue<int>() == 1
                        && stopped == afterStop
                        && report["manual"]!["requests"]!.GetValue<int>() == afterStop + 1
                        && report["display"]!["autoOff"]!.GetValue<bool>()
                        && form!.WriteAttempts == 0;
                    report["status"] = pollingOk ? "ok" : "failed";
                    exit = pollingOk ? 0 : 1;
                    if (path is not null)
                    {
                        using var image = File.Create(Path.ChangeExtension(path, ".png"));
                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                    }
                    return;
                }

                await core.ExecuteScriptAsync("document.querySelector('.match-toolbar > .ant-btn').click(); true");
                await Wait("!!document.querySelector('.match-workspace[data-pid]')");
                await Wait("Array.from(document.querySelectorAll('.match-scoreboard img')).every(e => e.complete)");
                await Wait("Array.from(document.querySelectorAll('.match-player-table img')).every(e => e.complete)");
                var initial = await Probe();
                report["initial"] = initial;
                bool ok = initial["pid"]!.GetValue<int>() > 0
                    && initial["players"]!.AsArray().Count >= 5
                    && initial["logos"]!.AsArray().Count == 2
                    && initial["logos"]!.AsArray().All(item => item!["loaded"]!.GetValue<bool>())
                    && initial["avatars"]!.GetValue<int>() == initial["players"]!.AsArray().Count
                    && initial["loadedPortraits"]!.GetValue<int>() == initial["portraits"]!.GetValue<int>()
                    && initial["editableInputs"]!.GetValue<int>() == 0
                    && initial["text"]!.GetValue<string>().Contains("暂停状态未核实");

                // Click the real segmented controls and verify both team and period views.
                await core.ExecuteScriptAsync(
                    "document.querySelectorAll('.match-table-toolbar > .ant-segmented')[0]"
                    + ".querySelectorAll('.ant-segmented-item')[1].click(); true");
                await Wait("document.querySelector('.match-workspace')?.dataset.side === '1'");
                report["otherTeam"] = await Probe();
                ok &= report["otherTeam"]!["avatars"]!.GetValue<int>()
                    == report["otherTeam"]!["players"]!.AsArray().Count;
                await core.ExecuteScriptAsync(
                    "document.querySelectorAll('.match-table-toolbar > .ant-segmented')[1]"
                    + ".querySelectorAll('.ant-segmented-item')[1].click(); true");
                await Wait("document.querySelector('.match-workspace')?.dataset.slot === '1'");
                report["firstPeriod"] = await Probe();
                ok &= report["otherTeam"]!["players"]!.AsArray().Count >= 5;
                await core.ExecuteScriptAsync(
                    "document.querySelectorAll('.match-table-toolbar > .ant-segmented')[1]"
                    + ".querySelectorAll('.ant-segmented-item')[0].click(); true");
                await Wait("document.querySelector('.match-workspace')?.dataset.slot === '0'");

                var views = new JsonArray();
                form!.MinimumSize = new Size(400, 400);
                foreach (var (name, width, height) in new[] { ("desktop", 1440, 900), ("compact", 1100, 760), ("mobile", 520, 850) })
                {
                    form.ClientSize = new Size(width, height);
                    await Task.Delay(300);
                    var view = await Probe();
                    view["viewport"] = name;
                    ok &= !view["horizontalOverflow"]!.GetValue<bool>() && !view["overlap"]!.GetValue<bool>();
                    if (path is not null)
                    {
                        string image = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,
                            Path.GetFileNameWithoutExtension(path) + "." + name + ".png");
                        using var stream = File.Create(image);
                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                        view["screenshot"] = Path.GetFileName(image);
                    }
                    views.Add(view);
                }
                report["views"] = views;
                string previous = await core.ExecuteScriptAsync("document.querySelector('.match-workspace').dataset.captured");
                await core.ExecuteScriptAsync("document.querySelector('.match-toolbar > .ant-btn').click(); true");
                await Wait("!!document.querySelector('.match-workspace[data-pid]') && "
                    + "document.querySelector('.match-workspace').dataset.captured !== " + previous);
                report["refreshed"] = await Probe();
                report["writes"] = form.WriteAttempts;
                ok &= form.WriteAttempts == 0;
                report["status"] = ok ? "ok" : "failed";
                exit = ok ? 0 : 1;
            }
            catch (Exception exc)
            {
                report["error"] = exc.Message;
            }
            finally
            {
                report["writes"] = form!.WriteAttempts;
                form.Close();
            }
        }, matchOnly: true);
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-10000, -10000);
        using var timeout = new System.Windows.Forms.Timer { Interval = 60000 };
        timeout.Tick += (_, _) => { report["error"] = "Match UI check timed out"; form.Close(); };
        timeout.Start();
        Application.Run(form);
        Program.WriteReport(report, path);
        return exit;
    }
}
