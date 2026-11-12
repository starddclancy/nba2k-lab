using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace Nba2k27Editor.WebHost;

/// <summary>Read-only UI acceptance for display caching, edit gates, record navigation and hit areas.</summary>
internal static class WorkspaceUsabilityUiCheck
{
    internal static int Run(string? output)
    {
        var report = new JsonObject { ["format"] = "nba2k27-workspace-usability-ui-check/1", ["status"] = "failed" };
        var checks = new JsonArray();
        var screenshots = new JsonArray();
        int exit = 1;
        bool started = false;
        bool finishing = false;
        EditorForm? form = null;
        if (output is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        }
        form = new EditorForm(async core =>
        {
            if (started) return;
            started = true;
            try
            {
                static string Js(string text) => JsonSerializer.Serialize(text);
                async Task<JsonNode?> Eval(string script) => JsonNode.Parse(await core.ExecuteScriptAsync(script));
                async Task Wait(string expression, int timeoutMs = 20000)
                {
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    while (elapsed.ElapsedMilliseconds < timeoutMs)
                    {
                        if ((await Eval(expression))?.GetValue<bool>() == true) return;
                        await Task.Delay(75);
                    }
                    throw new InvalidOperationException("Workspace UI condition timed out: " + expression);
                }
                async Task Expect(string name, string expression)
                {
                    bool ok = (await Eval(expression))?.GetValue<bool>() == true;
                    checks.Add(new JsonObject { ["name"] = name, ["ok"] = ok });
                    if (!ok) throw new InvalidOperationException(name);
                }
                async Task ClickText(string selector, string text)
                {
                    string target = $"[...document.querySelectorAll({Js(selector)})].find(e=>e.textContent.trim()==={Js(text)})";
                    await Wait($"!!({target}) && !({target}).disabled");
                    await Eval($"({target}).click(); true");
                }
                async Task SetEditing(bool enabled)
                {
                    await Wait("!!window.__workspaceUi.editSwitch()");
                    string value = enabled ? "true" : "false";
                    await Eval($"if(window.__workspaceUi.editSwitch().getAttribute('aria-checked')!=={Js(value)}) window.__workspaceUi.editSwitch().click(); true");
                    await Wait($"window.__workspaceUi.editSwitch()?.getAttribute('aria-checked')==={Js(value)}");
                }
                async Task Capture(string name)
                {
                    await Eval("document.getAnimations().forEach(a=>{if(a.effect?.getTiming().iterations!==Infinity)a.finish();});true");
                    await Task.Delay(700);
                    if (output is null) return;
                    string filename = Path.GetFileNameWithoutExtension(output) + "-" + name + ".png";
                    string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, filename);
                    using var stream = File.Create(path);
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                    screenshots.Add(new JsonObject { ["name"] = name, ["file"] = filename });
                }
                async Task Layout(string name)
                {
                    report[name] = await Eval("""
                        (()=>{
                            const selectors=['body','.shell-tabs','.scout-name','.field-label','.field-control',
                                '.team-field-grid','.history-field > label','.history-field > .ant-typography','.compare-head strong'];
                            return {viewport:[innerWidth,innerHeight],documentWidth:document.documentElement.scrollWidth,
                                fonts:selectors.flatMap(selector=>{
                                    const e=document.querySelector(selector);if(!e)return[];
                                    const s=getComputedStyle(e),r=e.getBoundingClientRect();
                                    return[{selector,font:s.fontFamily,size:s.fontSize,weight:s.fontWeight,color:s.color,
                                        rect:{x:r.x,y:r.y,w:r.width,h:r.height}}];
                                }),brokenImages:[...document.querySelectorAll('img')].filter(e=>e.complete&&!e.naturalWidth).length};
                        })()
                        """);
                    await Expect(name + "FitsViewport", "document.documentElement.scrollWidth<=innerWidth+1");
                    await Expect(name + "HasOnlyExplainedValues", """
                        !/未确认选项|未确认动作|含义待核实|分类待核对|内部参数|System\.Byte\[\]|球员关联未解析/.test(document.body.innerText)
                        """);
                }
                async Task Resize(int width, int height)
                {
                    double scale = (await Eval("devicePixelRatio"))!.GetValue<double>();
                    form!.ClientSize = new Size((int)Math.Round(width * scale), (int)Math.Round(height * scale));
                    await Wait($"Math.abs(innerWidth-{width})<=1 && Math.abs(innerHeight-{height})<=1");
                }
                async Task PlayerMenuLayout(string name)
                {
                    await Eval("document.getAnimations().forEach(a=>{if(a.effect?.getTiming().iterations!==Infinity)a.finish();});true");
                    report[name] = await Eval("""
                        (()=>{
                            const root=document.querySelector('.player-drawer .fe'),
                                nav=root.querySelector('.fe-nav'),content=root.querySelector('.fe-content'),
                                select=root.querySelector('.fe-section-select'),frame=content.getBoundingClientRect(),
                                groups=[...content.querySelectorAll('.field-section')],
                                rows=[...content.querySelectorAll('.field-row')],
                                keys=rows.map(e=>e.dataset.rowKey);
                            return{viewport:[innerWidth,innerHeight],
                                navVisible:getComputedStyle(nav).display!=='none',
                                selectorVisible:getComputedStyle(select).display!=='none',
                                navigation:[...nav.querySelectorAll('.fe-nav-group-title')].map(e=>e.textContent.trim()),
                                selected:nav.querySelector('.fe-tab.on .fe-tab-label')?.textContent,
                                groups:groups.map(e=>({label:e.querySelector('.field-section-title span').textContent,
                                    fields:e.querySelectorAll('.field-row').length})),
                                rowCount:rows.length,uniqueKeys:new Set(keys).size,
                                fields:rows.map(e=>({key:e.dataset.rowKey,value:e.querySelector('.field-control').textContent.trim()})),
                                fits:content.scrollWidth<=content.clientWidth+1&&groups.every(e=>{
                                    const r=e.getBoundingClientRect();return e.scrollWidth<=e.clientWidth+1
                                        &&r.left>=frame.left-1&&r.right<=frame.right+1;}),
                                separated:rows.every(e=>e.querySelector('.field-label').getBoundingClientRect().right
                                    <=e.querySelector('.field-control').getBoundingClientRect().left+1)};
                        })()
                        """);
                    await Expect(name + "GroupedNavigation", $$"""
                        (()=>{const r={{report[name]!.ToJsonString()}};
                            return r.navigation.join('|')==='资料与数据|能力与表现|合同与名单|外貌与身份'
                                &&(innerWidth>720?r.navVisible&&!r.selectorVisible:!r.navVisible&&r.selectorVisible);})()
                        """);
                    await Expect(name + "ReadableFieldGroups", $$"""
                        (()=>{const r={{report[name]!.ToJsonString()}};
                            return r.rowCount>0&&r.rowCount===r.uniqueKeys&&r.fits&&r.separated;})()
                        """);
                }
                async Task SliderLayout(string name, string selector)
                {
                    report[name] = await Eval($$"""
                        (()=>{const root=document.querySelector({{Js(selector)}});
                            return{viewport:[innerWidth,innerHeight],
                                documentFits:document.documentElement.scrollWidth<=innerWidth+1,
                                controls:[...root.querySelectorAll('.numeric-slider')].map(e=>{
                                    const slider=e.querySelector('[role=slider]'),track=e.querySelector('.ant-slider').getBoundingClientRect(),
                                        input=e.querySelector('input'),number=e.querySelector('.ant-input-number').getBoundingClientRect();
                                    return{label:slider.getAttribute('aria-label'),min:Number(slider.getAttribute('aria-valuemin')),
                                        max:Number(slider.getAttribute('aria-valuemax')),value:Number(slider.getAttribute('aria-valuenow')),
                                        number:Number(input.value),width:track.width,separated:track.right<=number.left+1};
                                })};})()
                        """);
                    await Expect(name + "ReadableSynchronizedSliders", $$"""
                        (()=>{const r={{report[name]!.ToJsonString()}};
                            return r.documentFits&&r.controls.length>0&&r.controls.every(c=>
                                c.label&&c.width>=45&&c.separated&&c.value===c.number&&c.min<c.max);})()
                        """);
                }
                async Task RadarLayout(string name, string selector)
                {
                    await Eval("document.getAnimations().forEach(a=>{if(a.effect?.getTiming().iterations!==Infinity)a.finish();});true");
                    await Wait("""
                        (()=>{const drawer=document.querySelector('.player-drawer .ant-drawer-content-wrapper');
                            if(!drawer)return true;const r=drawer.getBoundingClientRect();
                            return Math.abs(r.right-innerWidth)<=1&&Math.abs(r.width-Math.min(1180,innerWidth*.88))<=1;})()
                        """);
                    report[name] = await Eval($$"""
                        (()=>{
                            const root=document.querySelector({{Js(selector)}}),svg=root.querySelector('.big-radar'),
                                wrap=svg.parentElement,tools=wrap.querySelector('.radar-tools');
                            const rect=e=>{const r=e.getBoundingClientRect();return{x:r.x,y:r.y,w:r.width,h:r.height,
                                right:r.right,bottom:r.bottom};};
                            const frame=rect(svg),controls=rect(tools),
                                labels=[...svg.querySelectorAll('.br-label text')].map(e=>({
                                    text:e.textContent,font:getComputedStyle(e).fontFamily,fontSize:getComputedStyle(e).fontSize,
                                    scale:e.getScreenCTM().a,...rect(e)}));
                            const outside=labels.filter(r=>r.x<frame.x-1||r.y<frame.y-1
                                ||r.right>frame.right+1||r.bottom>frame.bottom+1);
                            const facts=root.querySelector('.insight-facts');
                            const groups=[...root.querySelectorAll(':scope > .fe-overview,:scope > .fe-chart,:scope > .fe-ranks')]
                                .map(e=>({name:e.className,...rect(e)}));
                            const overlaps=groups.flatMap((a,i)=>groups.slice(i+1).filter(b=>
                                Math.min(a.right,b.right)-Math.max(a.x,b.x)>1
                                &&Math.min(a.bottom,b.bottom)-Math.max(a.y,b.y)>1).map(b=>[a.name,b.name]));
                            return{viewport:[innerWidth,innerHeight],frame,labels,outside,controls,groups,overlaps,
                                options:tools.querySelectorAll('.ant-segmented-item').length,
                                wrapWidth:wrap.clientWidth,wrapScroll:wrap.scrollWidth,
                                labelsBelowControls:labels.every(r=>r.y>=controls.bottom),
                                factsHeight:facts?rect(facts).h:null};
                        })()
                        """);
                    await Expect(name + "CompactReadableChart", $$"""
                        (()=>{const r={{report[name]!.ToJsonString()}};
                            return r.frame.w<=281&&r.frame.w>=260&&Math.abs(r.frame.w-r.frame.h)<=1
                                &&r.labels.length===18&&r.outside.length===0&&r.labelsBelowControls
                                &&r.wrapScroll<=r.wrapWidth+1&&r.options===9&&r.overlaps.length===0
                                &&(r.factsHeight===null||r.factsHeight>=60);})()
                        """);
                }
                async Task Navigate(string hash, string ready, int timeoutMs = 90000)
                {
                    await Eval($"location.hash={Js(hash)}; true");
                    await Wait(ready, timeoutMs);
                }
                async Task CacheRevisit(string name, string hash, string header, string expected, string fields, string command, int index)
                {
                    await Eval($"window.__workspaceUi.watch({Js(name)},{Js(header)},{Js(expected)},{Js(fields)},{Js(command)},{index});location.hash={Js(hash)};true");
                    await Wait($"!!window.__workspaceUi.visits[{Js(name)}]?.first", 60000);
                    await Expect(name + "ShowsBeforeNetworkReply", $"window.__workspaceUi.cacheEvidence({Js(name)}).cachedFirst===true");
                    report[name] = await Eval($"window.__workspaceUi.cacheEvidence({Js(name)})");
                }
                async Task ManualRefresh(string name, string selector, string command, int index)
                {
                    await Wait($"!!document.querySelector({Js(selector)}) && !document.querySelector({Js(selector)}).disabled");
                    await Eval($"window.__workspaceUi.mark=performance.now();window.__workspaceUi.refreshBefore=window.__workspaceUi.count({Js(command)},{index});document.querySelector({Js(selector)}).click();true");
                    await Wait($"window.__workspaceUi.after({Js(command)},{index},window.__workspaceUi.mark).some(r=>r.ok===true)");
                    await Expect(name + "OneReadRequest", $"window.__workspaceUi.count({Js(command)},{index})-window.__workspaceUi.refreshBefore===1");
                    report[name] = await Eval($"window.__workspaceUi.after({Js(command)},{index},window.__workspaceUi.mark)");
                }
                async Task ObservePolling(string name, string command, int index)
                {
                    await Eval("window.__workspaceUi.pollStart=performance.now();true");
                    await Task.Delay(10500);
                    report[name] = await Eval($$"""
                        (()=>{const u=window.__workspaceUi,reads=u.requests.filter(r=>r.at>=u.pollStart
                            &&['readPlayer','readPlayerStats','readTeam'].includes(r.command));
                            return{elapsedMs:performance.now()-u.pollStart,reads,
                                currentOnly:reads.every(r=>r.command==={{Js(command)}}&&r.payload.index==={{index}})};})()
                        """);
                    await Expect(name + "OnlyCurrentSubject", report[name]!["currentOnly"]!.GetValue<bool>() ? "true" : "false");
                    int count = report[name]!["reads"]!.AsArray().Count;
                    await Expect(name + "FiveSecondRate", count is 2 or 3 ? "true" : "false");
                    await Expect(name + "EveryReadCompleted", $$"""
                        (()=>{const reads={{report[name]!["reads"]!.ToJsonString()}};
                            return reads.length>0&&reads.every(r=>r.ok===true&&r.durationMs>=0
                                &&r.fields>0&&(r.command==='readTeam'
                                    ||typeof r.readToken==='string'&&r.readToken.length>0));})()
                        """);
                }
                async Task CornerClicks(string name, string selector, string focusSelector)
                {
                    var points = new JsonArray();
                    for (int corner = 0; corner < 4; corner++)
                    {
                        await Wait($$"""
                            (()=>{const e=document.querySelector({{Js(selector)}});
                                return !!e&&getComputedStyle(e).pointerEvents!=='none'
                                    &&!document.querySelector('.analytics .ant-spin-spinning,.analytics .ant-spin-blur')
                                    &&window.__workspaceUi.requests.filter(r=>['readPlayerStats','seasonGames','playerForm']
                                        .includes(r.command)).every(r=>r.replyAt);})()
                            """, 90000);
                        await Eval($"document.querySelector({Js(selector)}).scrollIntoView({{block:'center',inline:'center'}});document.getAnimations().forEach(a=>{{if(a.effect?.getTiming().iterations!==Infinity)a.finish();}});true");
                        await Task.Delay(75);
                        var hit = await Eval($$"""
                            (()=>{
                                const e=document.querySelector({{Js(selector)}}),r=e.getBoundingClientRect();
                                const style=getComputedStyle(e);
                                const inset=Math.max(3,Math.ceil(Math.max(...['TopLeft','TopRight','BottomLeft','BottomRight']
                                    .map(c=>parseFloat(style['border'+c+'Radius'])||0))/2));
                                const x={{corner % 2}}?r.right-inset:r.left+inset,y={{corner / 2}}?r.bottom-inset:r.top+inset;
                                const target=document.elementFromPoint(x,y),focus=document.querySelector({{Js(focusSelector)}});
                                window.__workspaceUi.pressed=focus.getAttribute('aria-pressed');
                                const belongs=!!target&&(target===e||e.contains(target));
                                if(belongs)target.click();
                                const ancestors=[];
                                for(let node=e;node;node=node.parentElement)ancestors.push({
                                    tag:node.tagName,class:node.className,
                                    pointerEvents:getComputedStyle(node).pointerEvents});
                                return {corner:{{corner}},x,y,inset,hit:target?.tagName,belongs,
                                    target:target?.outerHTML.slice(0,1200),
                                    rect:{left:r.left,top:r.top,right:r.right,bottom:r.bottom},
                                    radius:style.borderRadius,pointerEvents:style.pointerEvents,
                                    ancestors,
                                    stack:document.elementsFromPoint(x,y).slice(0,8).map(node=>({
                                        tag:node.tagName,class:node.className}))};
                            })()
                            """);
                        points.Add(hit);
                        report[name] = points.DeepClone();
                        await Expect(name + "Corner" + corner + "Hits", hit?["belongs"]?.GetValue<bool>() == true ? "true" : "false");
                        await Wait($"document.querySelector({Js(focusSelector)}).getAttribute('aria-pressed')!==window.__workspaceUi.pressed");
                    }
                    report[name] = points;
                }
                async Task PaddingClick(string name, string selector)
                {
                    await Wait($"!!document.querySelector({Js(selector)})");
                    await Eval($"document.querySelector({Js(selector)}).scrollIntoView({{block:'center',inline:'center'}});true");
                    report[name] = await Eval($$"""
                        (()=>{
                            const e=document.querySelector({{Js(selector)}}),r=e.getBoundingClientRect();
                            const x=r.left+2,y=r.top+r.height/2,target=document.elementFromPoint(x,y);
                            window.__workspaceUi.pressed=document.querySelector('.compare-head').getAttribute('aria-pressed');
                            const belongs=!!target&&(target===e||e.contains(target));
                            if(belongs)target.click();
                            return {x,y,hit:target?.tagName,belongs,padding:getComputedStyle(e).padding};
                        })()
                        """);
                    await Expect(name + "HitsCell", $"window.__workspaceUi.pressed!==null && {report[name]!["belongs"]!.ToJsonString()}");
                    await Wait("document.querySelector('.compare-head').getAttribute('aria-pressed')!==window.__workspaceUi.pressed");
                }
                async Task SelectOption(string label, string text)
                {
                    string selector = $"[aria-label=\"{label}\"]";
                    await Wait($"!!document.querySelector({Js(selector)})");
                    await Eval($"document.querySelector({Js(selector)}).closest('.ant-select').querySelector('.ant-select-selector').dispatchEvent(new MouseEvent('mousedown',{{bubbles:true}}));true");
                    await ClickText(".ant-select-dropdown:not(.ant-select-dropdown-hidden) .ant-select-item-option-content", text);
                }

                await Wait("!!document.querySelector('.shell-status button')");
                if (form!.Text != "NBA2K Lab") throw new InvalidOperationException("Native window title mismatch");
                await Expect("productBranding", """
                    document.title==='NBA2K Lab'&&document.querySelector('.shell-brand')?.textContent.includes('NBA2K Lab')
                    """);
                await Eval("""
                    window.__workspaceUi={requests:[],byId:{},replies:{},errors:[],blocked:[],visits:{}};
                    const u=window.__workspaceUi;
                    u.writeCommand=c=>/apply|undo|import|release|clone|delete|avatarPick|avatarClear/i.test(c);
                    u.editSwitch=()=>document.querySelector('.player-drawer .editing-switch [role=switch]')
                        ??document.querySelector('.shell-tabs .editing-switch [role=switch]');
                    u.count=(c,i)=>u.requests.filter(r=>r.command===c&&r.payload?.index===i).length;
                    u.after=(c,i,t)=>u.requests.filter(r=>r.command===c&&r.payload?.index===i&&r.at>=t);
                    u.watch=(name,header,expected,fields,command,index)=>{
                        u.visits[name]={at:performance.now(),header,expected,fields,command,index,first:null};
                        const visit=u.visits[name],probe=()=>{
                            const e=document.querySelector(header),nodes=document.querySelectorAll(fields);
                            if(!visit.first&&e?.textContent.trim()===expected&&nodes.length){
                                visit.first={at:performance.now(),name:e.textContent.trim(),fields:nodes.length,value:nodes[0].textContent.trim()};
                                visit.observer.disconnect();
                            }
                        };
                        visit.observer=new MutationObserver(probe);
                        visit.observer.observe(document.body,{childList:true,subtree:true,characterData:true});
                        probe();
                    };
                    u.cacheEvidence=name=>{
                        const v=u.visits[name],reads=u.after(v.command,v.index,v.at),reply=reads.find(r=>r.replyAt);
                        return {at:v.at,first:v.first,reads,displayMs:v.first?v.first.at-v.at:null,
                            cachedFirst:!!v.first&&(!reply||v.first.at<reply.replyAt)};
                    };
                    window.addEventListener('error',e=>u.errors.push(e.message));
                    window.addEventListener('unhandledrejection',e=>u.errors.push(String(e.reason?.stack??e.reason)));
                    const post=window.chrome.webview.postMessage.bind(window.chrome.webview);
                    window.chrome.webview.postMessage=request=>{
                        if(request.kind==='editorCloseDecision'){
                            (u.closeDecisions??=[]).push(request);post(request);return;
                        }
                        if(u.writeCommand(request.command)){u.blocked.push(request);throw Error('Write command forbidden in read-only acceptance');}
                        const row={id:request.id,command:request.command,payload:request.payload,at:performance.now()};
                        u.requests.push(row);u.byId[request.id]=row;post(request);
                    };
                    window.chrome.webview.addEventListener('message',event=>{
                        const r=u.byId[event.data.id];if(!r)return;
                        r.replyAt=performance.now();r.durationMs=r.replyAt-r.at;r.ok=event.data.ok;
                        r.readToken=event.data.data?.readToken??null;
                        const data=event.data.data;
                        r.fields=data?.sections?.reduce((n,s)=>n+s.fields.length,0)??data?.fields?.length
                            ??(Array.isArray(data?.direct)?data.direct.length
                                +(data.seasons??[]).reduce((n,s)=>n+s.fields.length,0):null);
                        u.replies[r.command]=event.data;
                    });
                    document.querySelector('.shell-status button').click();true
                    """);
                await Wait("window.__workspaceUi.replies.connect?.ok===true", 90000);
                await Wait("window.__workspaceUi.replies.listPlayers?.data?.filter(p=>p.category==='team').length>1", 90000);
                report["connection"] = await Eval("window.__workspaceUi.replies.connect.data");
                await Wait("window.__workspaceUi.replies.leagueSeason?.ok===true", 90000);
                report["leagueSeason"] = await Eval("window.__workspaceUi.replies.leagueSeason.data");
                await Expect("leagueSeasonMatchesNativeCalendar", """
                    (()=>{const data=window.__workspaceUi.replies.leagueSeason.data,
                        year=window.__workspaceUi.replies.connect.data.seasonEndYear,
                        column=data.fields.indexOf('COMPACTYEAR');
                        return Number.isInteger(year)&&column>=0&&data.seasonEndYear===year
                            &&data.rows.every(row=>1900+row.values[column]===year);})()
                    """);
                await Resize(1440, 940);
                await Expect("defaultBrowseSwitch", "window.__workspaceUi.editSwitch()?.getAttribute('aria-checked')==='false'");
                var players = (await Eval("window.__workspaceUi.replies.listPlayers.data.filter(p=>p.category==='team').slice(0,2).map(p=>({index:p.index,name:p.name}))"))!.AsArray();
                int playerA = players[0]!["index"]!.GetValue<int>(), playerB = players[1]!["index"]!.GetValue<int>();
                string playerAName = players[0]!["name"]!.GetValue<string>(), playerBName = players[1]!["name"]!.GetValue<string>();
                string playerHeader = ".player-drawer .scout-name";
                string playerFields = ".player-drawer .field-control .ant-typography";
                await Navigate($"/players/roster/{playerA}", $"document.querySelector({Js(playerHeader)})?.textContent.trim()==={Js(playerAName)} && document.querySelectorAll({Js(playerFields)}).length>0");
                await Expect("playerBrowseHasNoValueInputs", "!document.querySelector('.player-drawer .field-control input,.player-drawer .field-control [role=switch],.player-drawer .field-control .ant-select')");
                await RadarLayout("desktopPlayerRadar", ".fe-side");
                await Layout("playerBrowseLayout");
                await PlayerMenuLayout("playerEditorVitals");
                await Capture("player-browse");
                await Eval("[...document.querySelectorAll('.player-drawer .fe-tab')].find(e=>e.textContent.startsWith('能力值')).click();true");
                await Wait("document.querySelectorAll('.player-drawer [data-row-key^=\"Attributes/\"]').length>0");
                await PlayerMenuLayout("playerEditorAttributes");
                await Expect("playerAttributesUseBuilderCategories", """
                    ['终结','投射','组织','防守','篮板','身体'].every(label=>
                        [...document.querySelectorAll('.player-drawer .field-section-title > span')]
                            .some(e=>e.textContent===label))
                    """);
                await Capture("player-attributes");
                await SetEditing(true);
                await Wait("!!document.querySelector('.player-drawer .numeric-slider [role=slider]')");
                await Expect("playerSliderRangesMatchLiveSchema", """
                    (()=>{const fields=window.__workspaceUi.replies.readPlayer.data.sections.flatMap(s=>s.fields);
                        return [...document.querySelectorAll('.player-drawer .field-row .numeric-slider')].every(e=>{
                            const key=e.closest('[data-row-key]').dataset.rowKey,f=fields.find(f=>f.key===key),
                                slider=e.querySelector('[role=slider]');
                            return f&&f.writable&&Number(slider.getAttribute('aria-valuemin'))===f.min
                                &&Number(slider.getAttribute('aria-valuemax'))===f.max;});})()
                    """);
                await SliderLayout("playerSlidersDesktop", ".player-drawer");
                await Capture("player-attribute-sliders");
                await Resize(900, 940);
                await SliderLayout("playerSlidersCompact", ".player-drawer");
                await Resize(640, 940);
                await SliderLayout("playerSlidersNarrow", ".player-drawer");
                await Resize(1440, 940);
                await SetEditing(false);
                await Eval("[...document.querySelectorAll('.player-drawer .fe-tab')].find(e=>e.textContent.startsWith('动作')).click();true");
                await Wait("document.querySelectorAll('.player-drawer [data-row-key^=\"Signature/\"]').length>0");
                report["playerActionFields"] = await Eval("""
                    [...document.querySelectorAll('.player-drawer .field-row')].map(row=>({
                        key:row.getAttribute('data-row-key'),label:row.querySelector('.field-label').textContent.trim(),
                        value:row.querySelector('.field-control').textContent.trim()
                    }))
                    """);
                report["playerMovedActionEnums"] = await Eval("""
                    (()=>{const fields=window.__workspaceUi.replies.readPlayer.data.sections.flatMap(s=>s.fields);
                        return ['FREETHROW','DRIBBLEPULLUP','GOTOSHOT'].map(name=>{
                            const field=fields.find(f=>f.key==='Signature/Jump Shooting II/'+name);
                            const row=[...document.querySelectorAll('.player-drawer .field-row')]
                                .find(e=>e.getAttribute('data-row-key')===field?.key);
                            return{key:field?.key,kind:field?.kind,value:field?.value,options:field?.options?.length,
                                listed:field?.options?.includes(field?.value),
                                text:row?.querySelector('.field-control')?.textContent.trim()};});})()
                    """);
                await Expect("movedShootingEnumsHaveReadableLiveValues", $$"""
                    (()=>{const rows={{report["playerMovedActionEnums"]!.ToJsonString()}};
                        return rows.length===3&&rows.every(f=>f.kind==='choice'&&typeof f.value==='string'
                            &&f.options>0&&f.listed&&f.text&&!/^\d+(?:\.\d+)?$/.test(f.text));})()
                    """);
                await Expect("undecodedAmbientActionSlotsAreHidden", """
                    !document.querySelector('.player-drawer [data-row-key*="/AMBIENTPACKAGE"]')
                    """);
                await Layout("playerActionLayout");
                await PlayerMenuLayout("playerEditorActions");
                await Expect("playerActionsUseReadableLabels", """
                    !/\b(?:EMDRB_|EMTT_|LUP_|TIP_INTRO_|PLACEHOLDER_)/.test(document.querySelector('.player-drawer .fe-content').innerText)
                    """);
                await Capture("player-actions");
                await Eval("[...document.querySelectorAll('.player-drawer .fe-tab')].find(e=>e.textContent.startsWith('基本资料')).click();true");
                await Wait("document.querySelectorAll('.player-drawer [data-row-key^=\"Vitals/\"]').length>0");
                await Eval("window.__workspaceUi.beforeToken=window.__workspaceUi.replies.readPlayer.data.readToken;window.__workspaceUi.mark=performance.now();true");
                await SetEditing(true);
                await Wait($"window.__workspaceUi.after('readPlayer',{playerA},window.__workspaceUi.mark).some(r=>r.ok===true&&r.readToken&&r.readToken!==window.__workspaceUi.beforeToken)");
                await Wait("!!document.querySelector('.player-drawer .field-control input:not([disabled]),.player-drawer .field-control .ant-select:not(.ant-select-disabled),.player-drawer .field-control [role=switch]:not([disabled])')");
                report["playerEditFreshRead"] = await Eval($"window.__workspaceUi.after('readPlayer',{playerA},window.__workspaceUi.mark)");
                await Capture("player-edit-enabled");
                await SetEditing(false);
                await Wait($"document.querySelectorAll({Js(playerFields)}).length>0 && !document.querySelector('.player-drawer .field-control input,.player-drawer .field-control .ant-select')");
                await Expect("playerWriteActionsDisabledAfterBrowse", """
                    [...document.querySelectorAll('.player-drawer .fe-bar button')].filter(e=>/^(应用|撤销|导入)/.test(e.textContent.trim())).every(e=>e.disabled)
                    """);
                await Navigate($"/players/roster/{playerB}", $"document.querySelector({Js(playerHeader)})?.textContent.trim()==={Js(playerBName)}");
                await CacheRevisit("playerCacheRevisit", $"/players/roster/{playerA}", playerHeader, playerAName, playerFields, "readPlayer", playerA);
                await ManualRefresh("playerManualRefresh", ".player-drawer [aria-label='刷新球员资料']", "readPlayer", playerA);
                await Capture("player-cache-refreshed");
                await ClickText(".player-drawer .fe-tab", "数据与荣誉");
                await Wait($"window.__workspaceUi.replies.readPlayerStats?.ok===true && window.__workspaceUi.replies.readPlayerStats.data.index==={playerA} && !!document.querySelector('.statistics-editor')");
                await ClickText(".statistics-editor .ant-tabs-tab", "单场最高");
                await Wait("document.querySelectorAll('.statistics-editor .field-control .ant-typography').length>0");
                await Expect("statisticsBrowseHasNoValueInputs", "!document.querySelector('.statistics-editor .field-control input,.statistics-editor .field-control .ant-select')");
                await Eval("window.__workspaceUi.mark=performance.now();window.__workspaceUi.beforeStatsToken=window.__workspaceUi.replies.readPlayerStats.data.readToken;true");
                await SetEditing(true);
                await Wait($"window.__workspaceUi.after('readPlayerStats',{playerA},window.__workspaceUi.mark).some(r=>r.ok===true&&r.readToken&&r.readToken!==window.__workspaceUi.beforeStatsToken)");
                await Wait("!!document.querySelector('.statistics-editor .field-control input:not([disabled])')");
                report["statisticsRead"] = await Eval("window.__workspaceUi.replies.readPlayerStats.data");
                await Expect("statisticsActionsUsePlayerHeader", """
                    !!document.querySelector('.fe-bar [aria-label="应用统计修改"]')
                        &&!document.querySelector('.statistics-toolbar [aria-label="应用统计修改"]')
                        &&document.querySelector('.fe-bar [aria-label="应用统计修改"]').disabled
                    """);
                report["statisticsLocalDraft"] = await Eval("""
                    (()=>{const input=document.querySelector('.statistics-editor .field-control input:not([disabled])'),
                        key=input.closest('[data-row-key]').dataset.rowKey,
                        f=window.__workspaceUi.replies.readPlayerStats.data.direct.find(f=>f.key===key);
                        if(!f||!f.writable||typeof f.value!=='number')throw Error('No writable direct statistics sample');
                        const next=f.value+1<=(f.max??Infinity)?f.value+1:f.value-1;
                        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,String(next));
                        input.dispatchEvent(new Event('input',{bubbles:true}));
                        input.dispatchEvent(new Event('change',{bubbles:true}));
                        return{key,before:f.value,next,applied:false};})()
                    """);
                await Wait("document.querySelector('.fe-bar [aria-label=\"应用统计修改\"]')?.disabled===false");
                await Expect("statisticsDraftCountAppearsInHeader",
                    "document.querySelector('.fe-bar [aria-label=\"应用统计修改\"]').textContent.includes('(1)')");
                await Expect("statisticsPendingBeforeAfter", $$"""
                    (()=>{const text=document.querySelector('[aria-label="统计待应用修改"]')?.textContent??'';
                        return text.includes({{Js(report["statisticsLocalDraft"]!["before"]!.ToString())}})
                            &&text.includes({{Js(report["statisticsLocalDraft"]!["next"]!.ToString())}});})()
                    """);
                await Eval("window.__workspaceUi.draftToken=window.__workspaceUi.replies.readPlayerStats.data.readToken;true");
                await ClickText(".fe-tab-label", "基本资料");
                await Wait("!document.querySelector('.statistics-editor')");
                await ClickText(".fe-tab-label", "数据与荣誉");
                await Wait("!!document.querySelector('.statistics-editor .field-control input:not([disabled])')");
                await Expect("statisticsDraftSurvivesCategoryRoundTrip", $$"""
                    [...document.querySelectorAll('.statistics-editor [data-row-key]')].find(e=>
                        e.dataset.rowKey==={{Js(report["statisticsLocalDraft"]!["key"]!.GetValue<string>())}})
                        ?.querySelector('input')?.value==={{Js(report["statisticsLocalDraft"]!["next"]!.ToString())}}
                    """);
                await Expect("statisticsDraftKeepsOriginalToken",
                    "window.__workspaceUi.replies.readPlayerStats.data.readToken===window.__workspaceUi.draftToken");
                await Eval("window.location.hash='/journals';true");
                await Wait("!!document.querySelector('.ant-modal-confirm')");
                await ClickText(".ant-modal-confirm button", "保留草稿");
                await Wait($"window.location.hash==='#/players/roster/{playerA}' && !document.querySelector('.ant-modal-confirm')");
                await Expect("statisticsDraftSurvivesCancelledNavigation",
                    "document.querySelector('.fe-bar [aria-label=\"应用统计修改\"]')?.disabled===false");
                bool? nativeCloseCancelled = null;
                FormClosingEventHandler observeClose = (_, args) =>
                {
                    nativeCloseCancelled = args.Cancel;
                    args.Cancel = true;
                };
                form!.FormClosing += observeClose;
                try { form.Close(); }
                finally { form.FormClosing -= observeClose; }
                checks.Add(new JsonObject { ["name"] = "nativeCloseWaitsForDraftDecision", ["ok"] = nativeCloseCancelled == true });
                if (nativeCloseCancelled != true)
                    throw new InvalidOperationException("Native window close bypasses pending player drafts");
                await Wait("!!document.querySelector('.ant-modal-confirm')");
                await ClickText(".ant-modal-confirm button", "保留草稿");
                await Wait("!document.querySelector('.ant-modal-confirm')");
                await Expect("nativeCloseCancelPreservesStatisticsDraft",
                    "document.querySelector('.fe-bar [aria-label=\"应用统计修改\"]')?.disabled===false");
                await Capture("statistics-header-ready");
                await Resize(640, 940);
                await Eval("document.querySelector('.fe-bar [aria-label=\"应用统计修改\"]').scrollIntoView({block:'center'});true");
                await Wait("""
                    (()=>{const r=document.querySelector('.fe-bar [aria-label="应用统计修改"]').getBoundingClientRect();
                        return r.top>=0&&r.bottom<=innerHeight;})()
                    """);
                await Layout("statisticsHeaderNarrow");
                report["statisticsHeaderControls"] = await Eval("""
                    (()=>{const nodes=[...document.querySelectorAll(
                        '.fe-bar button,.fe-bar .editing-switch,.fe-bar>.ant-input-affix-wrapper')];
                        const controls=nodes.filter(e=>!nodes.some(parent=>parent!==e&&parent.contains(e)))
                            .map(e=>{const r=e.getBoundingClientRect();return {text:e.textContent,
                                label:e.getAttribute('aria-label'),left:r.left,right:r.right,
                                top:r.top,bottom:r.bottom,width:r.width,height:r.height};})
                            .filter(r=>r.width>0&&r.height>0);
                        window.__workspaceUi.statisticsHeaderControls=controls;
                        return controls;})()
                    """);
                await Expect("statisticsHeaderControlsDoNotOverlap", """
                    (()=>{const controls=window.__workspaceUi.statisticsHeaderControls;
                        return controls.every((a,i)=>controls.slice(i+1).every(b=>
                            a.right<=b.left+1||b.right<=a.left+1||a.bottom<=b.top+1||b.bottom<=a.top+1));})()
                    """);
                await Capture("statistics-header-ready-640");
                await Resize(1440, 940);
                await Eval("document.querySelector('[aria-label=\"统计待应用修改\"] [aria-label^=\"还原 \"]').click();true");
                await Wait("!document.querySelector('[aria-label=\"统计待应用修改\"]')");
                await Expect("statisticsIndividualResetDisablesApply",
                    "document.querySelector('.fe-bar [aria-label=\"应用统计修改\"]').disabled");
                await SetEditing(false);
                await Wait("document.querySelectorAll('.statistics-editor .field-control .ant-typography').length>0 && !document.querySelector('.statistics-editor .field-control input')");
                await Expect("statisticsHeaderRevokedAfterBrowse",
                    "document.querySelector('.fe-bar [aria-label=\"应用统计修改\"]').disabled");
                await ManualRefresh("statisticsManualRefresh", ".fe-bar [aria-label='刷新球员统计']", "readPlayerStats", playerA);
                await ObservePolling("statisticsVisiblePolling", "readPlayerStats", playerA);
                await Capture("statistics-browse");
                await Navigate("/teams", "document.querySelectorAll('.team-strip > button').length>1 && !!document.querySelector('.team-page .roster-card')");
                var teamIndexes = (await Eval("window.__workspaceUi.replies.listTeams.data.slice(0,2).map(t=>t.index)"))!.AsArray();
                int teamA = teamIndexes[0]!.GetValue<int>(), teamB = teamIndexes[1]!.GetValue<int>();
                await Navigate($"/teams/{teamA}", $"window.__workspaceUi.replies.readTeam?.data?.index==={teamA} && !!document.querySelector('.team-page .field-toolbar')");
                await ClickText(".team-view-tabs .ant-segmented-item", "球队资料");
                await Wait("!!document.querySelector('.team-field-grid')");
                await Expect("teamBrowseHasNoValueInputs", "!document.querySelector('.team-field-grid input,.team-field-grid [role=switch],.team-field-grid .ant-select')");
                await Expect("teamTextReferencesAreExactAndReadOnly", """
                    (()=>{const fields=window.__workspaceUi.replies.readTeam.data.sections.flatMap(s=>s.fields)
                        .filter(f=>/\/LOCALIZED(?:CITY|SHORTTEAM|STATE)NAME$/.test(f.key));
                        return fields.length>0&&fields.every(f=>f.writable===false&&f.kind==='text'
                            &&typeof f.value==='string'&&f.value===f.display);})()
                    """);
                await Eval("window.__workspaceUi.mark=performance.now();true");
                await SetEditing(true);
                await Wait($"window.__workspaceUi.after('readTeam',{teamA},window.__workspaceUi.mark).some(r=>r.ok===true)");
                await Wait("!!document.querySelector('.team-field-grid input:not([disabled]),.team-field-grid .ant-select:not(.ant-select-disabled),.team-field-grid [role=switch]:not([disabled])')");
                report["teamEditFreshRead"] = await Eval($"window.__workspaceUi.after('readTeam',{teamA},window.__workspaceUi.mark)");
                await SetEditing(false);
                await Wait("!!document.querySelector('.team-field-grid .ant-typography') && !document.querySelector('.team-field-grid input,.team-field-grid .ant-select')");
                await Layout("teamBrowseLayout");
                await Capture("team-browse");
                string teamAName = (await Eval("document.querySelector('.team-page .field-toolbar .team-identity').textContent.trim()"))!.GetValue<string>();
                await Navigate($"/teams/{teamB}", $"window.__workspaceUi.replies.readTeam?.data?.index==={teamB} && !!document.querySelector('.team-field-grid') && document.querySelector('.team-page .field-toolbar .team-identity')?.textContent.trim()!=={Js(teamAName)}");
                await CacheRevisit("teamCacheRevisit", $"/teams/{teamA}", ".team-page .field-toolbar .team-identity",
                    teamAName, ".team-field-grid .ant-typography", "readTeam", teamA);
                await ManualRefresh("teamManualRefresh", ".team-page [aria-label='刷新球队资料']", "readTeam", teamA);
                await ObservePolling("teamVisiblePolling", "readTeam", teamA);
                report["teamPlayerSlots"] = await Eval("""
                    (()=>{
                        const u=window.__workspaceUi,roster=u.replies.listPlayers.data;
                        const fields=u.replies.readTeam.data.sections
                            .filter(s=>['Team Players','Team Lineups'].includes(s.name)).flatMap(s=>s.fields);
                        const linked=fields.filter(f=>f.playerIndex!==undefined);
                        return {total:fields.length,linked:linked.length,slots:linked.map(f=>({
                            key:f.key,value:f.value,index:f.playerIndex,name:f.playerName,
                            rosterName:roster.find(p=>p.index===f.playerIndex)?.name??null
                        }))};
                    })()
                    """);
                await Expect("teamPlayerSlotsHaveResolvedNames", $$"""
                    (()=>{const r={{report["teamPlayerSlots"]!.ToJsonString()}};
                        return r.linked>0&&r.slots.every(f=>Number.isInteger(f.index)&&f.index>=0
                            &&Number(f.value)>0&&typeof f.name==='string'&&f.name.trim().length>0
                            &&(f.rosterName===null||f.rosterName===f.name));})()
                    """);
                await Navigate("/leagueHistory", "!!document.querySelector('.history-field-grid')");
                await ClickText(".history-toolbar .ant-segmented-item", "NBA 纪录");
                await Wait("window.__workspaceUi.replies.historyList?.data?.length===3100 && window.__workspaceUi.replies.historyRead?.data?.key?.startsWith('records:')", 60000);
                await Expect("leagueRecordPhysicalIds", """
                    (()=>{const rows=window.__workspaceUi.replies.historyList.data;return new Set(rows.map(r=>r.index)).size===3100&&rows[0].index===0&&rows.at(-1).index===3099;})()
                    """);
                var recordSamples = new JsonArray();
                foreach (string group in new[] { "单场纪录（常规赛）", "单场纪录（季后赛）", "单赛季纪录", "生涯累计纪录" })
                {
                    await SelectOption("纪录范围", group);
                    await Wait($"document.querySelector('.history-editor-heading h5')?.textContent.startsWith({Js(group)}) && window.__workspaceUi.replies.historyRead?.data?.key===document.querySelector('.history-row-selected')?.getAttribute('data-row-key')");
                    await Expect(group + "LiveValueMatchesRecord", """
                        (()=>{
                            const u=window.__workspaceUi,d=u.replies.historyRead.data,row=u.replies.historyList.data.find(r=>r.key===d.key);
                            const value=d.fields.find(f=>f.key==='DATA')?.value;
                            return typeof value==='number'&&Number.isFinite(value)&&Math.abs(value-Number(row.summary))<=Math.max(1e-4,Math.abs(value)*1e-6)
                                &&!document.querySelector('.history-field input,.history-field .ant-select');
                        })()
                        """);
                    recordSamples.Add(await Eval("""
                        (()=>{const u=window.__workspaceUi,d=u.replies.historyRead.data,row=u.replies.historyList.data.find(r=>r.key===d.key);
                            return{key:d.key,index:row.index,group:row.recordGroup,metric:row.metric,rank:row.rank,value:d.fields.find(f=>f.key==='DATA').value};})()
                        """));
                }
                report["leagueRecordSamples"] = recordSamples;
                await Expect("leagueUiShowsOnlyMappedRecords", """
                    document.querySelector('.history-toolbar')?.textContent.includes('2850 条记录')
                        && !document.body.innerText.includes('分类待核对')
                    """);
                await Layout("historyLayout");
                await Capture("nba-career-record");
                await Navigate("/teamHistory", "!!document.querySelector('.history-field-grid')");
                await ClickText(".history-toolbar .ant-segmented-item", "队史纪录");
                await Wait("window.__workspaceUi.replies.historyList?.data?.length===510 && window.__workspaceUi.replies.historyRead?.data?.key?.startsWith('teamRecords:')", 60000);
                await Expect("teamRecordsStayInside510RowBlock", """
                    (()=>{const rows=window.__workspaceUi.replies.historyList.data,t=rows[0].teamIndex,start=3100+t*510;
                        return rows.length===510&&new Set(rows.map(r=>r.index)).size===510&&rows.every((r,i)=>r.index===start+i&&r.teamIndex===t)
                            &&rows[100].recordGroup==='单赛季纪录'&&rows[100].metric==='得分'&&!rows[100].notice
                            &&rows[300].recordGroup==='生涯累计纪录'&&rows[300].metric==='得分'&&!rows[300].notice
                            &&!!rows[110].notice&&!!rows[310].notice;})()
                    """);
                await Expect("teamUiShowsOnlyMappedRecords", """
                    document.querySelector('.history-toolbar')?.textContent.includes('120 条记录')
                        && !document.body.innerText.includes('分类待核对')
                    """);
                await Expect("teamRecordLiveValueMatchesRecord", """
                    (()=>{const u=window.__workspaceUi,d=u.replies.historyRead.data,r=u.replies.historyList.data.find(r=>r.key===d.key);
                        const value=d.fields.find(f=>f.key==='DATA')?.value;
                        return typeof value==='number'&&Number.isFinite(value)&&Math.abs(value-Number(r.summary))<=Math.max(1e-4,Math.abs(value)*1e-6)
                            &&!document.querySelector('.history-field input,.history-field .ant-select');})()
                    """);
                report["teamRecordBounds"] = await Eval("""
                    (()=>{const r=window.__workspaceUi.replies.historyList.data;return{team:r[0].teamIndex,count:r.length,first:r[0].index,last:r.at(-1).index,
                        mapped:r.filter(e=>!e.notice).length,unresolved:r.filter(e=>e.notice).length,
                        sample:window.__workspaceUi.replies.historyRead.data.fields.find(f=>f.key==='DATA').value};})()
                    """);
                report["teamRecordBlockHeads"] = await Eval("""
                    (()=>{const rows=window.__workspaceUi.replies.historyList.data,start=rows[0].index;
                        return rows.filter(r=>r.index-start>=100&&(r.index-start)%10<2).map(r=>({
                            index:r.index,offset:r.index-start,name:r.label,year:r.year,value:Number(r.summary),
                            group:r.notice?null:r.recordGroup,metric:r.notice?null:r.metric
                        }));})()
                    """);
                var teamRecordSamples = new JsonArray();
                foreach (string group in new[] { "单场纪录（常规赛）", "单场纪录（季后赛）", "单赛季纪录", "生涯累计纪录" })
                {
                    await SelectOption("纪录范围", group);
                    await Wait($"document.querySelector('.history-editor-heading h5')?.textContent.startsWith({Js(group)}) && window.__workspaceUi.replies.historyRead?.data?.key===document.querySelector('.history-row-selected')?.getAttribute('data-row-key')");
                    await Expect("team" + group + "LiveValueMatchesRecord", """
                        (()=>{const u=window.__workspaceUi,d=u.replies.historyRead.data,r=u.replies.historyList.data.find(r=>r.key===d.key);
                            const value=d.fields.find(f=>f.key==='DATA')?.value;
                            return typeof value==='number'&&Number.isFinite(value)&&Math.abs(value-Number(r.summary))<=Math.max(1e-4,Math.abs(value)*1e-6)
                                &&!document.querySelector('.history-field input,.history-field .ant-select');})()
                        """);
                    teamRecordSamples.Add(await Eval("""
                        (()=>{const u=window.__workspaceUi,d=u.replies.historyRead.data,r=u.replies.historyList.data.find(r=>r.key===d.key);
                            return{key:d.key,index:r.index,group:r.recordGroup,metric:r.metric,rank:r.rank,name:r.label,
                                year:r.year,value:d.fields.find(f=>f.key==='DATA').value};})()
                        """));
                }
                report["teamRecordSamples"] = teamRecordSamples;
                await Capture("team-records");
                await Navigate("/analytics", "document.querySelectorAll('.analytics-hero button.kpi').length>1 && !!document.querySelector('.analytics-hero strong a.entity-link') && !document.querySelector('.analytics .ant-spin-spinning')", 90000);
                await CornerClicks("kpiCornerHitAreas", ".analytics-hero button.kpi", ".analytics-hero button.kpi");
                await Capture("analytics-kpi-hit-area");
                await Navigate("/versus", "document.querySelectorAll('.compare-head').length>1 && !!document.querySelector('.compare-card .compare-value') && !document.querySelector('.compare .ant-spin-spinning')", 90000);
                await CornerClicks("compareHeaderCornerHitAreas", ".compare-head", ".compare-head");
                await PaddingClick("attributeCellPadding", ".attr-card .compare-table tbody tr:first-child td:nth-child(2)");
                await PaddingClick("metricCellPadding", ".compare-card .compare-table tbody tr:first-child td:nth-child(2)");
                await Expect("entityLinksAreNotNestedAnchors", "!document.querySelector('a a,button a,a button')");
                await Layout("compareLayout");
                await Capture("compare-padding-hit-area");
                await Eval("""
                    const link=document.querySelector('.compare-head strong a.entity-link');
                    window.__workspaceUi.linkHref=link.getAttribute('href');window.__workspaceUi.linkName=link.textContent.trim();
                    link.click();true
                    """);
                await Wait("location.hash===window.__workspaceUi.linkHref && document.querySelector('.player-drawer .scout-name')?.textContent.trim()===window.__workspaceUi.linkName", 60000);
                await Expect("embeddedEntityLinkNavigatesExactPlayer", "location.hash===window.__workspaceUi.linkHref && document.querySelector('.player-drawer .scout-name').textContent.trim()===window.__workspaceUi.linkName");
                await Resize(1100, 760);
                await RadarLayout("compactPlayerRadar", ".fe-side");
                await Expect("compactPlayerRanksBesideRadar", """
                    (()=>{const info=document.querySelector('.fe-overview').getBoundingClientRect(),
                        chart=document.querySelector('.fe-chart').getBoundingClientRect(),
                        ranks=document.querySelector('.fe-ranks').getBoundingClientRect(),
                        main=document.querySelector('.fe-main').getBoundingClientRect();
                        return chart.left>=info.right&&ranks.right<=chart.left&&main.top<600;})()
                    """);
                await Layout("compactPlayerLayout");
                await Capture("player-compact");
                foreach (int width in new[] { 1250, 1101, 900, 721, 720 })
                {
                    await Resize(width, 760);
                    await RadarLayout("playerRadar" + width, ".fe-side");
                    await Layout("playerRadarLayout" + width);
                    await PlayerMenuLayout("playerEditorMenu" + width);
                    if (width is 900 or 720) await Capture("player-radar-" + width);
                }
                await Resize(1440, 940);
                await Eval("document.querySelector('.fe-bar [aria-label=\"关闭\"]').click();true");
                await Wait("!document.querySelector('.player-drawer .fe-side')");
                await Wait("!!document.querySelector('.insight .big-radar')");
                await RadarLayout("desktopPlayerPreviewRadar", ".insight");
                await Capture("player-preview-radar");
                await Resize(900, 760);
                await RadarLayout("compactPlayerPreviewRadar", ".insight");
                await Capture("player-preview-radar-900");
                await Resize(1440, 940);
                await Navigate("/gs", "window.__workspaceUi.replies.gsRead?.ok===true && document.querySelectorAll('[data-gs-field]').length===70", 90000);
                await Expect("gsBrowseIsReadOnly", "!document.querySelector('.gs-field-row input,.gs-field-row [role=slider]')");
                report["gsGeneralRead"] = await Eval("window.__workspaceUi.replies.gsRead.data");
                await Capture("gs-browse");
                await SetEditing(true);
                await Wait("!!document.querySelector('.gs-content .numeric-slider [role=slider]')");
                await Expect("gsBothSidesRetainEveryLoadedField", """
                    (()=>{const data=window.__workspaceUi.replies.gsRead.data,
                        count=data.fields.reduce((n,f)=>n+Number(f.user!==null)+Number(f.cpu!==null),0);
                        return document.querySelectorAll('[data-gs-field]').length===data.fields.length
                            &&document.querySelectorAll('.gs-content [role=slider]').length===count
                            &&[...document.querySelectorAll('.gs-content [role=slider]')].every(e=>
                                e.getAttribute('aria-valuemin')==='0'&&e.getAttribute('aria-valuemax')==='100');})()
                    """);
                await SliderLayout("gsSlidersDesktop", ".gs-panel");
                await Capture("gs-sliders");
                await Resize(900, 940);
                await SliderLayout("gsSlidersCompact", ".gs-panel");
                await Resize(640, 940);
                await SliderLayout("gsSlidersNarrow", ".gs-panel");
                await Expect("gsNarrowCategorySelectorVisible", """
                    getComputedStyle(document.querySelector('.gs-nav')).display==='none'
                        &&getComputedStyle(document.querySelector('.gs-group-select')).display!=='none'
                    """);
                await Capture("gs-sliders-640");
                await Resize(1440, 940);
                await ClickText(".gs-panel .ant-segmented-item-label", "当前比赛设置");
                await Wait("window.__workspaceUi.replies.inMatchRead?.ok===true && document.querySelectorAll('[data-gs-field]').length===70", 60000);
                report["gsInMatchRead"] = await Eval("window.__workspaceUi.replies.inMatchRead.data");
                await Expect("gsInMatchUnloadedValuesCannotCreateEdits", """
                    (()=>{const data=window.__workspaceUi.replies.inMatchRead.data,
                        count=data.fields.reduce((n,f)=>n+Number(f.user!==null)+Number(f.cpu!==null),0);
                        return document.querySelectorAll('.gs-content [role=slider]').length===count
                            &&(count>0||Boolean(data.notice)&&document.querySelector('.gs-panel .ant-alert')?.textContent.includes(data.notice)
                                &&document.querySelector('.gs-panel [aria-label="应用 GS 改动"]').disabled);})()
                    """);
                if ((await Eval("window.__workspaceUi.replies.inMatchRead.data.fields.some(f=>f.user!==null||f.cpu!==null)"))?.GetValue<bool>() == true)
                    await SliderLayout("gsInMatchSliders", ".gs-panel");
                else
                    await Layout("gsInMatchUnloaded");
                await Capture("gs-in-match-sliders");
                await SetEditing(false);
                await Wait("document.querySelectorAll('[data-gs-field]').length===70 && !document.querySelector('.gs-content [role=slider]')");
                await Expect("gsBrowseActionsStayDisabled", """
                    document.querySelector('.gs-panel [aria-label="应用 GS 改动"]').disabled
                    """);
                await Navigate("/journals", "window.__workspaceUi.replies.journalList?.ok===true");
                report["journalSummary"] = await Eval("""
                    (()=>{const rows=window.__workspaceUi.replies.journalList.data;
                        return{files:rows.length,supported:rows.filter(r=>r.detail?.state==='supported').length,
                            unsupported:rows.filter(r=>r.detail?.state==='unsupported').length,
                            invalid:rows.filter(r=>r.detail?.state==='invalid').length};})()
                    """);
                await Expect("journalsExposeExplicitDetailStates", """
                    window.__workspaceUi.replies.journalList.data.every(r=>
                        ['supported','unsupported','invalid'].includes(r.detail?.state))
                    """);
                await Eval("""
                    (()=>{const rows=window.__workspaceUi.replies.journalList.data;
                        const row=rows.find(r=>r.detail?.state==='supported');
                        window.__workspaceUi.journalSample=row;
                        if(!row)return false;
                        [...document.querySelectorAll('tr[data-row-key]')].find(e=>
                            e.dataset.rowKey===row.kind+'/'+row.name)?.querySelector('button').click();
                        return true;})()
                    """);
                await Wait("!!document.querySelector('[aria-label=\"事务详情\"]')");
                await Expect("journalBeforeAfterValuesRendered", """
                    (()=>{const sample=window.__workspaceUi.journalSample;
                        const text=document.querySelector('[aria-label="事务详情"]').textContent;
                        return sample.detail.changes.slice(0,15).every(c=>
                            text.includes(c.before)&&text.includes(c.after));})()
                    """);
                await Layout("journalDetailsDesktop");
                await Capture("journal-details");
                await Resize(640, 940);
                await Layout("journalDetailsNarrow");
                await Capture("journal-details-640");
                await Expect("noRuntimeErrors", "window.__workspaceUi.errors.length===0");
                await Expect("noMutationRequests", "window.__workspaceUi.blocked.length===0 && window.__workspaceUi.requests.every(r=>!window.__workspaceUi.writeCommand(r.command))");
                report["requests"] = await Eval("window.__workspaceUi.requests");
                report["status"] = "ok";
                exit = 0;
            }
            catch (Exception error)
            {
                report["error"] = error.ToString();
                try
                {
                    report["failureUi"] = JsonNode.Parse(await core.ExecuteScriptAsync("""
                        ({hash:location.hash,text:document.body.innerText.slice(0,9000),requests:window.__workspaceUi?.requests,
                            errors:window.__workspaceUi?.errors,blocked:window.__workspaceUi?.blocked})
                        """));
                    if (output is not null)
                    {
                        string filename = Path.GetFileNameWithoutExtension(output) + "-failure.png";
                        using var stream = File.Create(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, filename));
                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                        screenshots.Add(new JsonObject { ["name"] = "failure", ["file"] = filename });
                    }
                }
                catch (Exception captureError) { report["captureError"] = captureError.Message; }
            }
            finally
            {
                report["checks"] = checks;
                report["screenshots"] = screenshots;
                report["writes"] = form!.WriteAttempts;
                report["finishedUtc"] = DateTime.UtcNow;
                if (form.WriteAttempts != 0) { report["status"] = "failed"; exit = 1; }
                finishing = exit != 0;
                form.Close();
            }
        });
        form.MinimumSize = new Size(380, 400);
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-10000, -10000);
        form.FormClosing += (_, args) => { if (finishing) args.Cancel = false; };
        using var timeout = new System.Windows.Forms.Timer { Interval = 360000 };
        timeout.Tick += (_, _) =>
        {
            report["error"] = "Workspace usability UI check timed out";
            report["checks"] = checks;
            report["screenshots"] = screenshots;
            report["writes"] = form.WriteAttempts;
            finishing = true;
            form.Close();
        };
        timeout.Start();
        Application.Run(form);
        Program.WriteReport(report, output);
        return exit;
    }
}
