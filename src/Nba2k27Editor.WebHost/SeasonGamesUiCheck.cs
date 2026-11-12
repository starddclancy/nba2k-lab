using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace Nba2k27Editor.WebHost;

/// <summary>Verify the actual embedded UI with native game data. All write capabilities stay off.</summary>
internal static class SeasonGamesUiCheck
{
    internal static int Run(string? output)
    {
        var report = new JsonObject { ["format"] = "nba2k27-season-ui-check/1", ["status"] = "failed" };
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
                async Task Wait(string expression, int timeoutMs = 16000)
                {
                    for (int attempt = 0; attempt < timeoutMs / 100; attempt++)
                    {
                        if ((await Eval(expression))?.GetValue<bool>() == true) return;
                        await Task.Delay(100);
                    }
                    throw new InvalidOperationException("Season UI condition timed out: " + expression);
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
                async Task Select(string selector, string search)
                {
                    await Eval($$"""
                        (() => {
                            const select = document.querySelector('{{selector}}');
                            select.querySelector('.ant-select-selector').dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
                            const input = select.querySelector('input');
                            Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, '{{search}}');
                            input.dispatchEvent(new Event('input', { bubbles: true }));
                        })(); true
                        """);
                    await Wait($"Array.from(document.querySelectorAll('.ant-select-item-option')).some(e => e.title.includes('{search}'))");
                    await Eval($"Array.from(document.querySelectorAll('.ant-select-item-option')).find(e => e.title.includes('{search}')).click(); true");
                }
                async Task Capture(string name)
                {
                    await Wait("!document.querySelector('.ant-spin-spinning')");
                    await Eval("document.getAnimations().forEach(a => { if(a.effect?.getTiming().iterations !== Infinity) a.finish(); }); true");
                    if (output is null) return;
                    using var stream = File.Create(Path.ChangeExtension(output, name + ".png"));
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                }
                async Task CheckFocus(string kind, int kpi)
                {
                    await Wait("document.querySelectorAll('.metric-chart .chart-legend button').length >= 2");
                    await Eval("""
                        window.__focusLines = document.querySelector('.metric-chart').querySelectorAll('.chart-line').length;
                        document.querySelector('.metric-chart').querySelectorAll('.chart-legend button')[1].click(); true
                        """);
                    await Wait("document.querySelector('.metric-chart').querySelectorAll('.chart-hi').length === 1");
                    await Expect(kind + "LegendFocusKeepsSeries", """
                        document.querySelector('.metric-chart').querySelectorAll('.chart-line').length === window.__focusLines
                            && document.querySelector('.metric-chart').querySelectorAll('.chart-dim').length === window.__focusLines - 1
                            && Array.from(document.querySelectorAll('.metric-chart:first-child .chart-area')).every(area => Number(getComputedStyle(area.parentElement).opacity) === 0.18)
                        """);
                    await Eval("document.querySelector('.metric-chart').querySelectorAll('.chart-legend button')[1].click(); true");
                    await Wait("!document.querySelector('.metric-chart .chart-hi')");
                    await Expect(kind + "LegendFocusTogglesOff", "!document.querySelector('.metric-chart .chart-dim')");
                    await Eval("document.querySelector('.metric-chart .chart-hit').dispatchEvent(new MouseEvent('click', { bubbles: true })); true");
                    await Wait("!!document.querySelector('.metric-chart .chart-hi')");
                    await Expect(kind + "CurveFocus", "document.querySelector('.metric-chart .chart-legend button').getAttribute('aria-pressed') === 'true'");
                    await Eval($$"""
                        window.__otherChart = document.querySelectorAll('.metric-chart')[1].querySelector('.chart-legend').textContent;
                        document.querySelectorAll('.analytics-hero button.kpi')[{{kpi}}].click(); true
                        """);
                    await Wait($$"""document.querySelectorAll('.analytics-hero button.kpi')[{{kpi}}].getAttribute('aria-pressed') === 'true'""");
                    await Expect(kind + "KpiSelectsMetric", $$"""
                        document.querySelector('.metric-chart .chart-legend').textContent.includes(
                            document.querySelectorAll('.analytics-hero button.kpi')[{{kpi}}].querySelector('span').textContent.split(' · ')[0])
                            && document.querySelector('.metric-chart').querySelectorAll('.chart-line').length === 1
                            && document.querySelectorAll('.metric-chart')[1].querySelector('.chart-legend').textContent === window.__otherChart
                        """);
                    await Eval($$"""document.querySelectorAll('.analytics-hero button.kpi')[{{kpi}}].click(); true""");
                    await Wait("!document.querySelector('.analytics-hero .kpi[aria-pressed=\"true\"]')");
                    await Expect(kind + "KpiFocusTogglesOff", "!document.querySelector('.metric-chart .chart-hi')");
                }
                async Task CheckCompareFocus(string kind)
                {
                    await Wait("document.querySelectorAll('.compare-head').length === 2 && !!document.querySelector('.metric-chart .chart-legend button')");
                    await Wait("!document.querySelector('.ant-spin-spinning')");
                    await Eval("document.querySelectorAll('.metric-chart .chart-legend button')[1].click(); true");
                    await Wait("document.querySelectorAll('.compare-head')[1].classList.contains('focused')");
                    await Expect(kind + "CompareLegendSynchronizes", """
                        document.querySelectorAll('.metric-chart').length === document.querySelectorAll('.metric-chart .chart-hi').length
                            && Array.from(document.querySelectorAll('.compare-card .compare-table')).every(table => table.querySelector('thead th:nth-child(3)').classList.contains('focus'))
                        """);
                    await Eval("document.querySelector('.compare-card tbody tr td:nth-child(2) button').click(); true");
                    await Wait("document.querySelector('.compare-head').classList.contains('focused')");
                    await Expect(kind + "CompareValueSynchronizes", """
                        Array.from(document.querySelectorAll('.metric-chart')).every(chart => chart.querySelector('.chart-legend button').getAttribute('aria-pressed') === 'true')
                        """);
                    await Eval("document.querySelector('.compare-card tbody tr td:nth-child(2) button').click(); true");
                    await Wait("!document.querySelector('.compare-head.focused')");
                    await Expect(kind + "CompareValueTogglesOff", "!document.querySelector('.metric-chart .chart-dim')");
                    await Eval("""
                        document.querySelector('.compare-head').dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true })); true
                        """);
                    await Wait("!!document.querySelector('.compare-head.focused')");
                    await Expect(kind + "CompareHeaderKeyboard", "!!document.querySelector('.compare-table td.focus')");
                }
                async Task CheckWindows(string kind)
                {
                    var result = await Eval($$"""
                        (() => {
                            const kind = '{{kind}}';
                            const games = window.__seasonUi.replies.seasonGames.data.games.filter(g => g.played)
                                .sort((a, b) => a.date.localeCompare(b.date) || a.slot - b.slot || a.id - b.id);
                            const player = games.filter(g => g.date === '2008-12-19')
                                .flatMap(g => [...g.homeLines, ...g.awayLines])
                                .find(row => row[1] === 'Allen Iverson')?.[0];
                            const rows = games.flatMap(g => {
                                if (kind === 'player') return [...g.homeLines, ...g.awayLines]
                                    .filter(row => row[0] === player).map(row => ({ pts: row[4], reb: row[11] + row[12], ast: row[13] }));
                                const lines = g.homeLabel === 'Houston Rockets' ? g.homeLines
                                    : g.awayLabel === 'Houston Rockets' ? g.awayLines : null;
                                return lines ? [{ pts: lines.reduce((n, row) => n + row[4], 0),
                                    reb: lines.reduce((n, row) => n + row[11] + row[12], 0),
                                    ast: lines.reduce((n, row) => n + row[13], 0) }] : [];
                            });
                            const table = document.querySelector('.window-table');
                            const headers = Array.from(table.querySelectorAll('thead th')).map(e => e.textContent);
                            const counts = [...new Set([5, 10, 20].map(n => Math.min(n, rows.length)))];
                            const values = counts.flatMap(count => [['pts','得分'],['reb','篮板'],['ast','助攻']].map(([key, label]) => {
                                const column = headers.indexOf('近 ' + count + ' 场');
                                const row = Array.from(table.querySelectorAll('tbody tr')).find(r => r.cells[0].textContent === label);
                                const expected = (Math.round(rows.slice(-count).reduce((sum, r) => sum + r[key], 0) / count * 10) / 10).toFixed(1);
                                const observed = column >= 0 && row ? row.cells[column]?.textContent : null;
                                return { count, metric: label, expected, observed, ok: expected === observed };
                            }));
                            return { games: rows.length, values, ok: rows.length > 0 && values.length > 0 && values.every(v => v.ok) };
                        })()
                        """);
                    report[kind + "Windows"] = result?.DeepClone();
                    bool ok = result?["ok"]?.GetValue<bool>() == true;
                    checks.Add(new JsonObject { ["name"] = kind + "NativeWindows", ["ok"] = ok });
                    if (!ok) throw new InvalidOperationException(kind + " recent windows differ from native records");
                }

                await Wait("!!document.querySelector('.shell-status button')");
                await Eval("""
                    window.__seasonUi = { replies: {}, commands: [], requests: {} };
                    const originalPost = window.chrome.webview.postMessage.bind(window.chrome.webview);
                    window.chrome.webview.postMessage = request => {
                        window.__seasonUi.commands.push(request.command);
                        window.__seasonUi.requests[request.id] = request.command;
                        originalPost(request);
                    };
                    window.chrome.webview.addEventListener('message', event => {
                        const command = window.__seasonUi.requests[event.data.id];
                        if (command) window.__seasonUi.replies[command] = event.data;
                    });
                    document.querySelector('.shell-status button').click(); true
                    """);
                await Wait("window.__seasonUi.replies.connect?.ok === true", 90000);
                await Wait("document.querySelector('.shell-status')?.innerText.includes('PID')");
                report["connection"] = (await Eval("window.__seasonUi.replies.connect.data"))?.DeepClone();
                await ClickText(".shell-groups button", "分析");
                await ClickText(".shell-tabs button", "赛程");
                await Wait("!!window.__seasonUi.replies.seasonGames", 90000);
                await Expect("nativeReadSucceeded", "window.__seasonUi.replies.seasonGames.ok === true");
                await Wait("!!document.querySelector('.sched-game')");
                await Expect("nativeCountsReconcile", """
                    (() => { const d = window.__seasonUi.replies.seasonGames.data;
                        return d.games.length + d.otherGames.length === d.tableCount
                            && new Set(d.games.flatMap(g => [g.home, g.away])).size === d.leagueTeamCount;
                    })()
                    """);
                await Expect("noSampleData", "!document.querySelector('.preview-indicator')");
                await Expect("rocketsSelected", "document.querySelector('.sched-summary')?.innerText.includes('Houston Rockets')");
                report["schedule"] = await Eval("""
                    (() => { const d = window.__seasonUi.replies.seasonGames.data; return {
                        games: d.games.length, otherGames: d.otherGames.length, tableCount: d.tableCount,
                        leagueTeamCount: d.leagueTeamCount, unmappedGameCount: d.unmappedGameCount,
                        rocketRows: document.querySelectorAll('.sched-game').length
                    }; })()
                    """);
                await Expect("rocketsFullSchedule", "document.querySelectorAll('.sched-game').length === 82");
                await Expect("sixRocketFixturesRendered", """
                    ['2008-12-22','2008-12-26','2008-12-28','2008-12-30','2009-01-02','2009-01-03']
                        .every(date => document.querySelector('time[datetime="' + date + '"]'))
                    """);
                await Capture("rockets");
                await Select(".sched-team-select", "Charlotte");
                await Wait("document.querySelector('.sched-summary')?.innerText.includes('Charlotte')");
                await Expect("charlotteFullSchedule", "document.querySelectorAll('.sched-game').length === 82");
                await Capture("charlotte");
                await ClickText(".schedule-page .ant-segmented-item", "其他比赛");
                await Wait("document.querySelector('.sched-meta')?.innerText.includes(String(window.__seasonUi.replies.seasonGames.data.otherGames.length))");
                await Expect("unmappedTeamWarningVisible", """
                    !window.__seasonUi.replies.seasonGames.data.unmappedGameCount
                        || document.querySelector('.schedule-page .ant-alert')?.innerText.includes(String(window.__seasonUi.replies.seasonGames.data.unmappedGameCount))
                    """);
                await Capture("other");
                await ClickText(".shell-tabs button", "数据分析");
                await Wait("!!document.querySelector('.form-card')");
                await Select(".analytics-bar .ant-select", "Allen Iverson");
                await Wait("document.querySelector('.analytics-hero')?.innerText.includes('Allen Iverson')");
                await Wait("!document.querySelector('.ant-spin-spinning')");
                await Expect("iversonNativeRowsAndFouls", """
                    (() => {
                        const table = document.querySelector('.data-grid .grid-table');
                        const headers = Array.from(table.querySelectorAll('th .grid-head')).map(e => e.textContent);
                        const rows = Array.from(table.querySelectorAll('tbody tr'));
                        const date = rows.find(row => row.cells[0].textContent === '2008-12-19');
                        const native = window.__seasonUi.replies.seasonGames.data.games
                            .filter(g => g.played && g.date === '2008-12-19')
                            .flatMap(g => [...g.homeLines, ...g.awayLines])
                            .find(row => row[1] === 'Allen Iverson');
                        return !!date && !!native && Number(date.cells[headers.indexOf('得分')].textContent) === native[4]
                            && Number(date.cells[headers.indexOf('犯规')].textContent) === native[17]
                            && Number(date.cells[headers.indexOf('上场分钟')].textContent) === native[2];
                    })()
                    """);
                await Expect("nativeSourceLabel", "document.querySelector('.data-grid h3')?.innerText.includes('游戏原生赛季比赛记录')");
                await CheckWindows("player");
                await CheckFocus("player", 5);
                await Capture("iverson");
                await ClickText(".analytics-bar .ant-segmented-item", "球队分析");
                await Wait("!!document.querySelector('.form-games')");
                await Select(".analytics-bar .ant-select", "Houston");
                await Wait("document.querySelector('.analytics-hero')?.innerText.includes('Houston')");
                await Wait("!document.querySelector('.ant-spin-spinning')");
                await Expect("nativeTeamFormAndTable", """
                    (() => {
                        const d = window.__seasonUi.replies.seasonGames.data;
                        const rows = d.games.filter(g => g.played && (g.homeLabel === 'Houston Rockets' || g.awayLabel === 'Houston Rockets'));
                        return document.querySelectorAll('.data-grid tbody tr').length === rows.length
                            && document.querySelector('.form-card h3')?.innerText.includes('游戏记录');
                    })()
                    """);
                await CheckWindows("team");
                await CheckFocus("team", 0);
                await Expect("seasonOnlyMetricSwitchesSource", "document.querySelector('.metric-chart h3')?.textContent.includes('逐季')");
                foreach (int count in new[] { 5, 20, 10 })
                {
                    await ClickText(".form-games .ant-segmented-item", $"近 {count} 场");
                    await Wait($"document.querySelectorAll('.form-games .game-line').length === Math.min({count}, document.querySelectorAll('.data-grid tbody tr').length)");
                    await Expect($"teamRecentList{count}", $$"""
                        (() => {
                            const expected = window.__seasonUi.replies.seasonGames.data.games
                                .filter(g => g.played && (g.homeLabel === 'Houston Rockets' || g.awayLabel === 'Houston Rockets'))
                                .sort((a, b) => a.date.localeCompare(b.date) || a.slot - b.slot || a.id - b.id)
                                .slice(-{{count}}).reverse();
                            return Array.from(document.querySelectorAll('.form-games .game-line')).every((row, i) => {
                                const g = expected[i], home = g.homeLabel === 'Houston Rockets';
                                return row.querySelector('small').textContent === g.date.slice(5)
                                    && row.querySelector('b').textContent === (home ? g.homePoints + '-' + g.awayPoints : g.awayPoints + '-' + g.homePoints);
                            });
                        })()
                        """);
                }
                await Capture("team");
                await ClickText(".shell-tabs button", "对比");
                await CheckCompareFocus("player");
                await Capture("player-focus");
                await Eval("document.querySelector('.compare-tag .ant-tag-close-icon').click(); true");
                await Wait("document.querySelectorAll('.compare-head').length === 1");
                await Expect("removedSubjectClearsFocus", "!document.querySelector('.compare-head.focused') && !document.querySelector('.chart-dim')");
                await Eval("document.querySelector('.metric-chart .chart-legend button').click(); true");
                await Wait("!!document.querySelector('.compare-head.focused')");
                await Expect("singleSubjectKeepsEntityIdentity", "document.querySelectorAll('.metric-chart .chart-hi').length === 2");
                await ClickText(".analytics-bar .ant-segmented-item", "球队对比");
                await Wait("!!document.querySelector('.compare-head .team-identity')");
                await Expect("modeSwitchClearsFocus", "!document.querySelector('.compare-head.focused')");
                await CheckCompareFocus("team");
                await Capture("team-focus");
                await Expect("noWriteCommands", """
                    window.__seasonUi.commands.every(command => !/apply|undo|import|move|release|clone|delete/i.test(command))
                    """);
                report["commands"] = await Eval("window.__seasonUi.commands");
                report["status"] = "ok";
                exit = 0;
            }
            catch (Exception exc)
            {
                report["error"] = exc.Message;
                try
                {
                    report["failureUi"] = JsonNode.Parse(await core.ExecuteScriptAsync("""
                        ({ text: document.body.innerText.slice(0, 6000),
                            commands: window.__seasonUi?.commands,
                            failures: Object.entries(window.__seasonUi?.replies ?? {}).filter(([, reply]) => !reply.ok) })
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
            report["error"] = "Season UI check timed out";
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
