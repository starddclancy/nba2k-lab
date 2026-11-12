using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace Nba2k27Editor.WebHost;

/// <summary>Live-refreshing match page plus the actual editor and preview bridge; never writes.</summary>
internal static class MatchEditUiCheck
{
    internal static int Run(string? path)
    {
        var report = new JsonObject
        {
            ["format"] = "nba2k27-live-match-edit-ui/1",
            ["status"] = "failed",
            ["startedUtc"] = DateTime.UtcNow,
            ["limitations"] = "Live reads and edit preview only; writes are disabled. No apply, undo, pause validation, hidden-page or stability proof.",
        };
        if (path is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        }
        int exit = 1;
        bool started = false;
        EditorForm? form = null;
        form = new EditorForm(async core =>
        {
            if (started)
            {
                return;
            }
            started = true;
            try
            {
                async Task<JsonNode?> Evaluate(string script) =>
                    JsonNode.Parse(await core.ExecuteScriptAsync(script));
                async Task Wait(string predicate)
                {
                    for (int attempt = 0; attempt < 200; attempt++)
                    {
                        if ((await Evaluate(predicate))?.GetValue<bool>() == true)
                        {
                            return;
                        }
                        await Task.Delay(100);
                    }
                    throw new InvalidOperationException("Match edit UI condition timed out: " + predicate);
                }
                async Task Expect(string name, string predicate)
                {
                    bool ok = (await Evaluate(predicate))?.GetValue<bool>() == true;
                    report[name] = ok;
                    if (!ok)
                    {
                        throw new InvalidOperationException(name);
                    }
                }
                async Task SetNumber(string label, int value)
                {
                    await core.ExecuteScriptAsync("""
                        (() => {
                            const input = document.querySelector('.match-edit-drawer input[aria-label=LABEL]');
                            Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')
                                .set.call(input, VALUE);
                            input.dispatchEvent(new Event('input', {bubbles:true}));
                            input.dispatchEvent(new Event('change', {bubbles:true}));
                        })()
                        """.Replace("LABEL", JsonSerializer.Serialize(label)).Replace("VALUE", value.ToString()));
                    await Task.Delay(120);
                }
                async Task Click(string selector) =>
                    await core.ExecuteScriptAsync("document.querySelector(" + JsonSerializer.Serialize(selector) + ").click(); true");

                await Wait("document.querySelector('.match-workspace')?.dataset.readsEnabled === 'true'");
                await core.ExecuteScriptAsync("""
                    (() => {
                        const host = window.chrome.webview;
                        const original = host.postMessage.bind(host);
                        const pending = new Map();
                        window.__matchEditDebug = {reads:0, replies:0, inFlight:0, maxInFlight:0, changed:0,
                            prepares:0, writes:0, snapshot:null, plan:null, error:null, gaps:[], last:0, loadingSeen:0};
                        host.postMessage = request => {
                            const debug = window.__matchEditDebug;
                            if (request.command.startsWith('match')) {
                                pending.set(request.id, request.command);
                                if (request.command === 'matchRead') {
                                    debug.reads++;
                                    debug.inFlight++;
                                    debug.maxInFlight = Math.max(debug.maxInFlight, debug.inFlight);
                                    if (document.querySelector('.match-refresh.ant-btn-loading') && debug.replies > 0) debug.loadingSeen++;
                                }
                                if (request.command === 'matchPrepare') debug.prepares++;
                                if (['matchApply','matchUndo'].includes(request.command)) debug.writes++;
                            }
                            original(request);
                        };
                        host.addEventListener('message', event => {
                            const reply = event.data;
                            const debug = window.__matchEditDebug;
                            const command = pending.get(reply.id);
                            if (!command) return;
                            pending.delete(reply.id);
                            if (command === 'matchRead') {
                                debug.inFlight--;
                                debug.replies++;
                                const now = performance.now();
                                if (debug.last) debug.gaps.push(Math.round(now - debug.last));
                                debug.last = now;
                                if (!reply.ok && reply.code === 'matchChanged') { debug.changed++; return; }
                            }
                            if (!reply.ok) debug.error = reply.error;
                            if (reply.ok && command === 'matchRead') debug.snapshot = reply.data;
                            if (reply.ok && command === 'matchPrepare') debug.plan = reply.data;
                        });
                    })()
                    """);
                // No click: entering the page must start live reads by itself. The first read can
                // be posted before this hook exists, so a failure is also taken from the page.
                await Wait("window.__matchEditDebug.replies >= 4 || !!window.__matchEditDebug.error"
                    + " || !!document.querySelector('.match-workspace .ant-alert-error')");
                if ((await Evaluate("window.__matchEditDebug.error"
                    + " || document.querySelector('.match-workspace .ant-alert-error')?.textContent || null"))
                    is JsonValue readError)
                {
                    throw new InvalidOperationException("Match page reported: " + readError.GetValue<string>());
                }
                await Wait("!!document.querySelector('.match-player-table button[aria-label^=\"编辑\"]')");
                await Expect("liveReadsStartWithoutClick", "window.__matchEditDebug.replies >= 4");
                await Expect("liveSwitchOnAndEnabled",
                    "!document.querySelector('.match-auto-refresh button').disabled && "
                    + "document.querySelector('.match-auto-refresh button').getAttribute('aria-checked') === 'true'");
                // Mark one table row and type a filter; both must survive several refreshes.
                await core.ExecuteScriptAsync("""
                    (() => {
                        const row = document.querySelector('.match-player-table .ant-table-row');
                        row.__liveMarker = 1;
                        window.__markedKey = row.dataset.rowKey;
                        window.__readsAtMark = window.__matchEditDebug.replies;
                        window.__orderAtMark = Array.from(document.querySelectorAll('.match-player-table .ant-table-row'))
                            .map(r => r.dataset.rowKey).join(',');
                    })()
                    """);
                await Wait("window.__matchEditDebug.replies >= window.__readsAtMark + 4");
                await Expect("rowDomNodeKeptAcrossRefreshes", """
                    document.querySelector('.match-player-table .ant-table-row[data-row-key="' + window.__markedKey + '"]')?.__liveMarker === 1
                    """);
                await Expect("rowOrderStableAcrossRefreshes", """
                    Array.from(document.querySelectorAll('.match-player-table .ant-table-row')).map(r => r.dataset.rowKey).join(',')
                        === window.__orderAtMark
                    """);
                await Expect("readsNeverOverlap", "window.__matchEditDebug.maxInFlight === 1");
                await Expect("backgroundRefreshShowsNoLoading", "window.__matchEditDebug.loadingSeen === 0");
                report["liveReads"] = (await Evaluate("""
                    (() => { const d = window.__matchEditDebug, g = d.gaps.slice(1).sort((a, b) => a - b);
                        return {replies: d.replies, changed: d.changed, maxInFlight: d.maxInFlight,
                            replyIntervalMsP50: g[Math.floor(g.length / 2)], replyIntervalMsMax: g[g.length - 1]}; })()
                    """))?.DeepClone();
                await Click(".match-player-table button[aria-label^=\"编辑\"]");
                await Wait("document.querySelectorAll('.match-edit-drawer input[role=spinbutton]').length === 12");
                var selected = (await Evaluate("""
                    (() => {
                        const name = document.querySelector('.match-edit-identity > strong').textContent;
                        const player = window.__matchEditDebug.snapshot.teams[0].players.find(p => p.name === name);
                        return {playerId:player.id, name, row:player.stats.periods[
                            Math.min(window.__matchEditDebug.snapshot.period,5)-1]};
                    })()
                    """))!;
                report["selected"] = selected.DeepClone();
                int attempted = selected["row"]!["twoPointAttempted"]!.GetValue<int>();
                int made = selected["row"]!["twoPointMade"]!.GetValue<int>();
                int points = selected["row"]!["points"]!.GetValue<int>();
                if (attempted >= 65534)
                {
                    throw new InvalidOperationException("Selected shot counter is too close to its limit");
                }

                await SetNumber("两分命中", attempted + 1);
                await Expect("invalidShootingRejected", """
                    document.querySelector('.match-edit-drawer .ant-alert-error')?.textContent.includes('命中数不能超过出手数')
                    && Array.from(document.querySelectorAll('.match-edit-actions button'))
                        .find(b => b.textContent.includes('预览修改')).disabled
                    """);
                await SetNumber("两分命中", made);
                await SetNumber("两分出手", attempted + 1);
                await SetNumber("两分命中", made + 1);
                await Expect("draftScoreLinked",
                    "Number(document.querySelector('output[aria-label=\"草稿得分\"]').textContent) === " + (points + 2));
                await core.ExecuteScriptAsync("""
                    Array.from(document.querySelectorAll('.match-edit-actions button'))
                        .find(b => b.textContent.includes('预览修改')).click(); true
                    """);
                await Wait("!!window.__matchEditDebug.plan || !!window.__matchEditDebug.error");
                report["preview"] = (await Evaluate("window.__matchEditDebug"))?.DeepClone();
                if ((await Evaluate("window.__matchEditDebug.error")) is JsonValue planError)
                {
                    throw new InvalidOperationException(planError.GetValue<string>());
                }
                await Wait("!!document.querySelector('.match-edit-preview .ant-table-row')");
                await Expect("previewLinksTeamAndPlayerScores", """
                    (() => {
                        const {snapshot, plan} = window.__matchEditDebug;
                        const playerId = PLAYER;
                        const original = snapshot.teams[0].players.find(p => p.id === playerId);
                        return plan.writes === 0 && plan.players === 1
                            && plan.changes.some(c => c.playerId === playerId && c.slot === 0
                                && c.field === 'points' && c.after === original.stats.total.points + 2)
                            && plan.changes.some(c => c.playerId === null && c.side === 0 && c.slot === 0
                                && c.field === 'points' && c.after === snapshot.teams[0].stats.total.points + 2);
                    })()
                    """.Replace("PLAYER", selected["playerId"]!.ToJsonString()));
                await Expect("applyRemainsBlocked", """
                    Array.from(document.querySelectorAll('.match-edit-actions button'))
                        .find(b => b.textContent.trim() === '应用').disabled
                    """);
                await Expect("previewCompilesFixedWidthCounters", """
                    (() => {
                        const plan = window.__matchEditDebug.plan;
                        return plan.liveWriteReady === false && plan.patches.length === 12
                            && plan.patchBytes === 32
                            && plan.patches.every(p => p.width === (p.field === 'points' ? 4 : 2)
                                && p.beforeHex.length === p.width * 2 && p.afterHex.length === p.width * 2)
                            && document.querySelector('.match-edit-drawer').textContent.includes('不会写入游戏');
                    })()
                    """);
                // The editor pauses live reads: no new read may start while it is open.
                await core.ExecuteScriptAsync("window.__readsInEditor = window.__matchEditDebug.reads; true");
                await Task.Delay(1200);
                await Expect("editorPausesLiveReads", "window.__matchEditDebug.reads === window.__readsInEditor");
                var views = new JsonArray();
                form!.MinimumSize = new Size(380, 400);
                foreach (var (name, width, height) in new[]
                {
                    ("desktop", 1440, 1000), ("compact", 1100, 850), ("mobile", 520, 900),
                })
                {
                    form.ClientSize = new Size(width, height);
                    await Task.Delay(250);
                    var view = (await Evaluate("""
                        (() => {
                            const panel = document.querySelector('.match-edit-drawer .ant-drawer-content-wrapper');
                            const body = document.querySelector('.match-edit-drawer .ant-drawer-body');
                            const footer = document.querySelector('.match-edit-drawer .ant-drawer-footer');
                            return {
                                viewportWidth: innerWidth,
                                panelWidth: panel.getBoundingClientRect().width,
                                pixelRatio: devicePixelRatio,
                                bodyOverflow: body.scrollWidth > body.clientWidth + 1,
                                footerOverflow: footer.scrollWidth > footer.clientWidth + 1,
                                panelFits: panel.getBoundingClientRect().width <= innerWidth + 1,
                                fields: document.querySelectorAll('.match-edit-fields .field-row').length,
                                score: document.querySelector('output[aria-label="草稿得分"]').textContent
                            };
                        })()
                        """))!;
                    view["viewport"] = name;
                    if (path is not null)
                    {
                        string image = Path.ChangeExtension(path, name + ".png");
                        using var stream = File.Create(image);
                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                        view["screenshot"] = Path.GetFileName(image);
                    }
                    views.Add(view);
                    if (view["bodyOverflow"]!.GetValue<bool>() || view["footerOverflow"]!.GetValue<bool>()
                        || !view["panelFits"]!.GetValue<bool>() || view["fields"]!.GetValue<int>() != 12)
                    {
                        report["views"] = views;
                        throw new InvalidOperationException("Match editor layout overflow: " + name);
                    }
                }
                report["views"] = views;
                await SetNumber("助攻", selected["row"]!["assists"]!.GetValue<int>() + 1);
                await Expect("inputInvalidatesPreview", "!document.querySelector('.match-edit-preview')");
                await Click(".match-edit-drawer .ant-drawer-close");
                await Wait("!!document.querySelector('.ant-modal-confirm')");
                await core.ExecuteScriptAsync("""
                    Array.from(document.querySelectorAll('.ant-modal-confirm button'))
                        .find(b => b.textContent.includes('继续编辑')).click(); true
                    """);
                await Expect("cancelClosePreservesDraft",
                    "Number(document.querySelector('output[aria-label=\"草稿得分\"]').textContent) === " + (points + 2));
                // Close for real: drafts are discarded and live reads resume on their own.
                await Click(".match-edit-drawer .ant-drawer-close");
                await Wait("Array.from(document.querySelectorAll('.ant-modal-confirm button')).some(b => b.textContent.includes('放弃并关闭'))");
                await core.ExecuteScriptAsync("""
                    Array.from(document.querySelectorAll('.ant-modal-confirm button'))
                        .find(b => b.textContent.includes('放弃并关闭')).click(); true
                    """);
                await Wait("!document.querySelector('.match-edit-drawer input[role=spinbutton]')");
                await core.ExecuteScriptAsync("window.__readsAfterClose = window.__matchEditDebug.replies; true");
                await Wait("window.__matchEditDebug.replies >= window.__readsAfterClose + 3");
                await Expect("closingEditorResumesLiveReads", "window.__matchEditDebug.replies >= window.__readsAfterClose + 3");
                // Minimize the window as a user would: the page must become hidden and reads stop.
                var restoreSize = form!.ClientSize;
                form.WindowState = FormWindowState.Minimized;
                bool hidden = false;
                for (int attempt = 0; attempt < 30 && !hidden; attempt++)
                {
                    await Task.Delay(100);
                    hidden = (await Evaluate("document.hidden"))?.GetValue<bool>() == true;
                }
                report["pageHiddenObserved"] = hidden;
                if (!hidden)
                {
                    throw new InvalidOperationException("WebView2 did not report the page hidden; hidden-page stop not verified");
                }
                await Task.Delay(500); // A read already in flight when the page hid may still land.
                await core.ExecuteScriptAsync("window.__readsWhenHidden = window.__matchEditDebug.reads; true");
                await Task.Delay(1500);
                await Expect("hiddenPageStopsLiveReads", "window.__matchEditDebug.reads === window.__readsWhenHidden");
                form.WindowState = FormWindowState.Normal;
                form.ClientSize = restoreSize;
                await Wait("!document.hidden");
                await core.ExecuteScriptAsync("window.__readsAfterShow = window.__matchEditDebug.replies; true");
                await Wait("window.__matchEditDebug.replies >= window.__readsAfterShow + 3");
                await Expect("visiblePageResumesLiveReads", "window.__matchEditDebug.replies >= window.__readsAfterShow + 3");
                await Expect("noWriteCommands", "window.__matchEditDebug.writes === 0");
                report["liveReadsFinal"] = (await Evaluate(
                    "({reads: window.__matchEditDebug.reads, replies: window.__matchEditDebug.replies, changed: window.__matchEditDebug.changed})"))?.DeepClone();
                report["status"] = "ok";
                exit = 0;
            }
            catch (Exception error)
            {
                report["error"] = error.Message;
            }
            finally
            {
                report["writes"] = form!.WriteAttempts;
                report["readRequests"] = form.CommandBridge.MatchReadRequests;
                report["finishedUtc"] = DateTime.UtcNow;
                if (form.WriteAttempts != 0)
                {
                    report["status"] = "failed";
                    exit = 1;
                }
                form.Close();
            }
        }, matchOnly: true, matchDebugReads: true);
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-10000, -10000);
        using var timeout = new System.Windows.Forms.Timer { Interval = 60000 };
        timeout.Tick += (_, _) => { report["error"] = "Match edit UI check timed out"; form.Close(); };
        timeout.Start();
        Application.Run(form);
        Program.WriteReport(report, path);
        return exit;
    }
}
