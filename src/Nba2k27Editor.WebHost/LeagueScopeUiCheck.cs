using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace Nba2k27Editor.WebHost;

/// <summary>Exercise current-league views, real entity links and decoded seasons without writes.</summary>
internal static class LeagueScopeUiCheck
{
    internal static int Run(string? output)
    {
        var report = new JsonObject { ["format"] = "nba2k27-league-scope-ui-check/1", ["status"] = "failed" };
        var checks = new JsonArray();
        int exit = 1;
        bool started = false;
        EditorForm? form = null;
        form = new EditorForm(async core =>
        {
            if (started) return;
            started = true;
            try
            {
                async Task<JsonNode?> Eval(string script) => JsonNode.Parse(await core.ExecuteScriptAsync(script));
                async Task Wait(string expression, int timeoutMs = 20000)
                {
                    for (int attempt = 0; attempt < timeoutMs / 100; attempt++)
                    {
                        if ((await Eval(expression))?.GetValue<bool>() == true) return;
                        await Task.Delay(100);
                    }
                    throw new InvalidOperationException("League UI condition timed out: " + expression);
                }
                async Task Expect(string name, string expression)
                {
                    bool ok = (await Eval(expression))?.GetValue<bool>() == true;
                    checks.Add(new JsonObject { ["name"] = name, ["ok"] = ok });
                    if (!ok) throw new InvalidOperationException(name);
                }
                async Task ClickText(string selector, string text)
                {
                    string expression = $"Array.from(document.querySelectorAll('{selector}')).find(e => e.textContent.trim() === '{text}')";
                    await Wait($"!!({expression})");
                    await Eval($"({expression}).click(); true");
                }
                async Task Capture(string name)
                {
                    await Eval("document.getAnimations().forEach(a => { if(a.effect?.getTiming().iterations !== Infinity) a.finish(); }); true");
                    await Task.Delay(100);
                    if (output is null) return;
                    using var stream = File.Create(Path.ChangeExtension(output, name + ".png"));
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                }
                async Task Resize(int width, int height)
                {
                    double scale = (await Eval("devicePixelRatio"))!.GetValue<double>();
                    form!.ClientSize = new Size((int)Math.Round(width * scale), (int)Math.Round(height * scale));
                    await Wait($"Math.abs(innerWidth-{width})<=1 && Math.abs(innerHeight-{height})<=1");
                    await Task.Delay(100);
                }
                async Task Layout(string name)
                {
                    report[name] = await Eval("""
                        (() => {
                            const page = document.querySelector('.team-page'), rail = document.querySelector('.team-strip');
                            const detail = document.querySelector('.detail-host');
                            const rect = e => { const r=e.getBoundingClientRect(); return {x:r.x,y:r.y,w:r.width,h:r.height}; };
                            const font = e => { const s=getComputedStyle(e); return {family:s.fontFamily,size:s.fontSize,weight:s.fontWeight}; };
                            return { viewport:[innerWidth,innerHeight], page:rect(page), rail:rect(rail), detail:rect(detail),
                                bodyFont:font(document.body), teamFont:font(document.querySelector('.team-strip button')),
                                rosterFont:font(document.querySelector('.roster-card-name b')),
                                radar:rect(document.querySelector('.team-roster-radar .big-radar')),
                                rosterFacts:rect(document.querySelector('.team-roster-facts')),
                                rosterHead:rect(document.querySelector('.team-roster-head')),
                                fits: document.documentElement.scrollWidth <= innerWidth
                                    && detail.scrollWidth <= detail.clientWidth + 1
                                    && rail.getBoundingClientRect().right <= detail.getBoundingClientRect().left + 1,
                                brokenImages: [...page.querySelectorAll('img')].filter(i=>i.complete && !i.naturalWidth).length };
                        })()
                        """);
                    await Expect(name + "Fits", $"document.documentElement.scrollWidth <= innerWidth && document.querySelector('.detail-host').scrollWidth <= document.querySelector('.detail-host').clientWidth + 1");
                    await Expect(name + "CompactRadar", """
                        (()=>{ const r=document.querySelector('.team-roster-radar .big-radar').getBoundingClientRect();
                            return r.width<=181 && r.height<=181; })()
                        """);
                    await Expect(name + "RosterFollowsFacts", """
                        (()=>{ const facts=document.querySelector('.team-roster-facts').getBoundingClientRect();
                            const head=document.querySelector('.team-roster-head').getBoundingClientRect();
                            const list=document.querySelector('.team-roster-list').getBoundingClientRect();
                            const radar=document.querySelector('.team-roster-radar').getBoundingClientRect();
                            return head.top>=facts.bottom && head.top-facts.bottom<=20 &&
                                list.top>=head.bottom && list.top-head.bottom<=20 &&
                                list.right<=radar.left && head.top<radar.bottom; })()
                        """);
                }

                await Wait("!!document.querySelector('.shell-status button')");
                await Eval("""
                    window.__scopeUi={replies:{},requests:{},commands:[],errors:[]};
                    window.addEventListener('error',e=>window.__scopeUi.errors.push(e.message));
                    const post=window.chrome.webview.postMessage.bind(window.chrome.webview);
                    window.chrome.webview.postMessage=request=>{
                        window.__scopeUi.commands.push({command:request.command,payload:request.payload});
                        window.__scopeUi.requests[request.id]=request.command; post(request);
                    };
                    window.chrome.webview.addEventListener('message',event=>{
                        const command=window.__scopeUi.requests[event.data.id];
                        if(command) window.__scopeUi.replies[command]=event.data;
                    });
                    document.querySelector('.shell-status button').click(); true
                    """);
                await Wait("window.__scopeUi.replies.connect?.ok===true", 90000);
                await Wait("document.querySelector('.shell-status')?.innerText.includes('PID')");
                report["connection"] = await Eval("window.__scopeUi.replies.connect.data");
                await Expect("currentConnectionCounts", "window.__scopeUi.replies.connect.data.teams===30 && window.__scopeUi.replies.connect.data.resourceTeams>30");
                await Wait("!!window.__scopeUi.replies.listPlayers");
                await Expect("defaultPlayersCurrentOnly", """
                    window.__scopeUi.replies.listPlayers.ok &&
                    window.__scopeUi.replies.listPlayers.data.every(p=>p.category==='team'||p.category==='freeAgent')
                    """);
                await Resize(1440, 940);
                await Eval("location.hash='/teams'; true");
                await Wait("document.querySelectorAll('.team-strip > button').length===30 && !!document.querySelector('.roster-card')", 60000);
                await Expect("thirtyCurrentTeams", "document.querySelectorAll('.team-strip > button').length===30");
                await Expect("noNestedInteractiveLinks", "!document.querySelector('a a, button a, a button, [role=option] a')");
                await Wait("!!window.__scopeUi.replies.draftPicks");
                await Expect("unusedPickLabelsDoNotClaimConfirmedOrder", """
                    (()=>{ const rows=window.__scopeUi.replies.draftPicks.data;
                        const labels=[...document.querySelectorAll('.team-next-picks small')].map(e=>e.textContent);
                        return rows.some(p=>!p.used && p.order===1) && labels.length>0 &&
                            labels.every(t=>t.includes('顺位未定') && !t.includes('当前第')); })()
                    """);
                report["pickLabels"] = await Eval("[...document.querySelectorAll('.team-next-picks small')].map(e=>e.textContent)");
                await Layout("desktopLayout");
                await Capture("teams-desktop");
                await Resize(1000, 760);
                await Layout("compactLayout");
                await Expect("compactOverviewDensity", "document.querySelector('.team-overview').getBoundingClientRect().height < 240");
                await Capture("teams-compact");
                await Resize(1440, 940);
                await Eval("""
                    const button=Array.from(document.querySelectorAll('.team-strip > button')).find(b=>b.title.includes('Charlotte'));
                    window.__scopeUi.teamName=button.title;
                    button.click(); true
                    """);
                await Wait("location.hash.startsWith('#/teams/') && document.querySelector('.team-page .field-toolbar')?.innerText.includes(window.__scopeUi.teamName)");
                await Wait("!!document.querySelector('.roster-card')");
                await Eval("""
                    window.__scopeUi.teamHash=location.hash;
                    const link=document.querySelector('.roster-card');
                    window.__scopeUi.playerHash=link.getAttribute('href');
                    window.__scopeUi.playerName=link.querySelector('b').textContent;
                    link.click(); true
                    """);
                await Wait("!!document.querySelector('.player-drawer .field-editor .scout-name')", 60000);
                await Expect("rosterLinkExactPlayer", """
                    location.hash===window.__scopeUi.playerHash &&
                    document.querySelector('.player-drawer .scout-name').textContent===window.__scopeUi.playerName
                    """);
                await ClickText(".fe-tab", "数据与荣誉");
                await Wait("!!document.querySelector('.statistics-editor .ant-select-selection-item')");
                await Expect("playerSeasonRealYears", """
                    /\d{4}-\d{4} 赛季/.test(document.querySelector('.statistics-toolbar .ant-select-selection-item').textContent) &&
                    !/年份码|赛季编号/.test(document.querySelector('.statistics-editor').innerText)
                    """);
                report["playerYear"] = await Eval("document.querySelector('.statistics-toolbar .ant-select-selection-item').textContent");
                await Eval("document.querySelector('.statistics-toolbar').scrollIntoView({block:'center'}); true");
                await Capture("player-year");
                await Eval("history.back(); true");
                await Wait("location.hash===window.__scopeUi.teamHash && !!document.querySelector('.team-page')");
                await Wait("document.querySelector('.team-page .field-toolbar')?.innerText.includes(window.__scopeUi.teamName)");
                await Expect("backRestoresExactTeam", "document.querySelector('.team-page .field-toolbar').innerText.includes(window.__scopeUi.teamName)");
                await Eval("history.forward(); true");
                await Wait("location.hash===window.__scopeUi.playerHash && !!document.querySelector('.player-drawer .field-editor')", 60000);
                await Expect("forwardRestoresPlayer", "document.querySelector('.player-drawer .scout-name').textContent===window.__scopeUi.playerName");
                await Wait("!!document.querySelector('.fe-team a')");
                await Eval("document.querySelector('.fe-team a').click(); true");
                await Wait("!!document.querySelector('.team-page .roster-card')", 60000);
                await Expect("playerTeamLink", "location.hash.startsWith('#/teams/')");
                await ClickText(".team-view-tabs .ant-segmented-item", "球队资料");
                await Wait("!!document.querySelector('.team-field-grid')");
                await Expect("teamFieldsSeparateFromRoster", "!document.querySelector('.team-roster') && !!document.querySelector('.team-field-grid')");
                await Capture("team-fields");
                await Eval("location.hash='/leaders'; true");
                await Wait("!!document.querySelector('.leader-row')", 60000);
                await Expect("leaderSeasonMatchesSave", """
                    (()=>{ const d=window.__scopeUi.replies.leagueSeason.data;
                        const column=d.fields.indexOf('COMPACTYEAR');
                        return d.seasonEndYear===window.__scopeUi.replies.connect.data.seasonEndYear &&
                            d.rows.every(r=>1900+r.values[column]===d.seasonEndYear); })()
                    """);
                await Expect("leaderNamesAreLinks", "!!document.querySelector('.leader-row a[href^=\"#/players/roster/\"]')");
                await ClickText(".analytics-bar .ant-segmented-item", "球队榜");
                await Wait("document.querySelectorAll('.leader-row').length===30");
                await Expect("thirtyTeamsOnLeaderboard", "document.querySelectorAll('.leader-row').length===30");
                await Capture("team-leaders");
                await Eval("location.hash='/teamHistory'; true");
                await Wait("!!document.querySelector('.history-field-grid')", 60000);
                await ClickText(".history-toolbar .ant-segmented-item", "逐赛季统计");
                await Wait("window.__scopeUi.replies.historyRead?.data?.fields?.some(f=>f.key==='COMPACTYEAR')===true", 60000);
                await Expect("teamHistoryRealYears", """
                    !/年份码|赛季编号/.test(document.querySelector('.history-workspace').innerText) &&
                    /\d{4}-\d{4} 赛季/.test(document.querySelector('.history-field-grid').innerText) &&
                    window.__scopeUi.replies.historyRead.data.fields.find(f=>f.key==='COMPACTYEAR').value < 256
                    """);
                report["teamYear"] = await Eval("document.querySelector('.history-field-grid').innerText.slice(0,160)");
                await Capture("team-year");
                await Eval("location.hash='/leagueHistory'; true");
                await Wait("window.__scopeUi.replies.historyRead?.data?.fields?.some(f=>f.key==='SEASON')===true", 60000);
                await Expect("nbaHistoryRealYears", """
                    /\d{4}-\d{4} 赛季/.test(document.querySelector('.history-field-grid').innerText) &&
                    window.__scopeUi.replies.historyRead.data.fields.find(f=>f.key==='SEASON').value < 256
                    """);
                await Capture("nba-year");
                await Eval("location.hash='/schedule'; true");
                await Wait("!!document.querySelector('.sched-game a')", 90000);
                await Expect("scheduleTeamLinks", "!!document.querySelector('.sched-game a[href^=\"#/teams/\"]')");
                await Expect("noRuntimeErrors", "window.__scopeUi.errors.length===0");
                await Expect("noWriteCommands", "window.__scopeUi.commands.every(r=> !/apply|undo|import|move|release|clone|delete/i.test(r.command))");
                report["commands"] = await Eval("window.__scopeUi.commands");
                report["status"] = "ok";
                exit = 0;
            }
            catch (Exception error)
            {
                report["error"] = error.ToString();
                try
                {
                    report["failureUi"] = JsonNode.Parse(await core.ExecuteScriptAsync("""
                        ({hash:location.hash,text:document.body.innerText.slice(0,7000),replies:window.__scopeUi?.replies,errors:window.__scopeUi?.errors})
                        """));
                    if (output is not null)
                    {
                        using var stream = File.Create(Path.ChangeExtension(output, "failure.png"));
                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                    }
                }
                catch (Exception captureError) { report["captureError"] = captureError.Message; }
            }
            finally
            {
                report["checks"] = checks;
                report["writes"] = form!.WriteAttempts;
                report["finishedUtc"] = DateTime.UtcNow;
                if (form.WriteAttempts != 0) { report["status"] = "failed"; exit = 1; }
                form.Close();
            }
        });
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-10000, -10000);
        using var timeout = new System.Windows.Forms.Timer { Interval = 240000 };
        timeout.Tick += (_, _) =>
        {
            report["error"] = "League UI check timed out";
            report["checks"] = checks;
            report["writes"] = form.WriteAttempts;
            form.Close();
        };
        timeout.Start();
        Application.Run(form);
        Program.WriteReport(report, output);
        return exit;
    }
}
