using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace Nba2k27Editor.WebHost;

internal static class ProspectUiCheck
{
    internal static async Task<bool> Run(CoreWebView2 core, JsonObject report, string? outPath)
    {
        var requests = new List<JsonObject>();
        core.WebMessageReceived += (_, args) =>
        {
            var request = JsonNode.Parse(args.WebMessageAsJson)?.AsObject();
            if (request is not null)
            {
                requests.Add(request);
            }
        };
        await core.ExecuteScriptAsync(
            "window.__snapshotReplies = {}; chrome.webview.addEventListener('message', e => {"
            + " if (e.data?.id) window.__snapshotReplies[e.data.id] = e.data; }); true");

        async Task<JsonNode?> Wait(string script, Func<JsonNode?, bool> ready)
        {
            JsonNode? value = null;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                await Task.Delay(200);
                value = JsonNode.Parse(await core.ExecuteScriptAsync(script));
                if (ready(value))
                {
                    return value;
                }
            }
            throw new InvalidOperationException("Prospect UI did not settle: " + value?.ToJsonString());
        }

        async Task ClickSource(string label)
        {
            if (label is "待选秀" or "新秀名单")
            {
                await core.ExecuteScriptAsync(
                    "Array.from(document.querySelectorAll('.player-source-tabs .ant-segmented-item'))"
                    + ".find(e => e.textContent.trim() === '选秀球员')?.click(); true");
                await Wait("!!document.querySelector('.draft-source-filter')",
                    value => value?.GetValue<bool>() == true);
            }
            string selector = label is "待选秀" or "新秀名单"
                ? ".draft-source-filter .ant-segmented-item"
                : ".player-source-tabs .ant-segmented-item";
            string target = label == "球队" ? "球队球员" : label;
            await core.ExecuteScriptAsync(
                $"Array.from(document.querySelectorAll({JsonSerializer.Serialize(selector)}))"
                + $".find(e => e.textContent.trim() === {JsonSerializer.Serialize(target)})?.click(); true");
            await Wait(
                "!document.querySelector('.player-pane .ant-spin-spinning')"
                + " && !document.querySelector('.player-pane .ant-btn-loading')",
                value => value?.GetValue<bool>() == true);
        }

        async Task Search(string name)
        {
            await core.ExecuteScriptAsync(
                "(() => { const input = document.querySelector('input[placeholder=\"搜索姓名\"]');"
                + " Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input,"
                + JsonSerializer.Serialize(name) + ");"
                + " input.dispatchEvent(new Event('input', {bubbles:true})); })()");
            await Task.Delay(100);
        }

        await ClickSource("待选秀");
        var state = await Wait(
            "({rows:document.querySelectorAll('.player-pane .ant-table-row').length,"
            + "name:document.querySelector('.player-pane .ant-table-row div[title]')?.getAttribute('title'),"
            + "text:document.querySelector('.player-pane')?.innerText ?? ''})",
            value => (value?["rows"]?.GetValue<int>() ?? 0) > 1);
        string sample = state!["name"]!.GetValue<string>();
        report["prospectList"] = state;
        await Search(sample);
        var filtered = await Wait(
            "({rows:document.querySelectorAll('.player-pane .ant-table-row').length,"
            + "selected:document.querySelector('.draft-source-filter .ant-segmented-item-selected')?.textContent.trim()})",
            value => value?["rows"]?.GetValue<int>() == 1);
        report["prospectSearch"] = filtered;
        await core.ExecuteScriptAsync("document.querySelector('.player-pane .ant-table-row')?.click(); true");
        var detail = await Wait(
            "({name:document.querySelector('.field-toolbar .ant-typography')?.textContent,"
            + "tabs:document.querySelectorAll('.detail-host .ant-tabs-tab').length})",
            value => value?["name"]?.GetValue<string>() == sample
                && (value?["tabs"]?.GetValue<int>() ?? 0) > 5);
        report["prospectDetail"] = detail;
        await core.ExecuteScriptAsync(
            "Array.from(document.querySelectorAll('.detail-host .ant-tabs-tab'))"
            + ".find(e => e.textContent.startsWith('基本资料'))?.click(); true");
        // Stage the original jersey number and submit it: exercises the real UI token path
        // while producing no game-memory patches.
        string inputSelector = ".detail-host [data-row-key=\"Vitals/Vitals/NUMBER\"] input";
        string originalValue = (await Wait(
            $"document.querySelector({JsonSerializer.Serialize(inputSelector)})?.value ?? null",
            value => value is not null))!.GetValue<string>();
        async Task SetNumber(string next)
        {
            await core.ExecuteScriptAsync(
                "(() => { const input = document.querySelector("
                + JsonSerializer.Serialize(inputSelector) + ");"
                + "Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input,"
                + JsonSerializer.Serialize(next) + ");"
                + "input.dispatchEvent(new Event('input', {bubbles:true})); })()");
            await Task.Delay(100);
        }
        await SetNumber(originalValue == "1" ? "2" : "1");
        await SetNumber(originalValue);
        await Wait(
            "Array.from(document.querySelectorAll('.field-toolbar button'))"
            + ".some(e => e.textContent.includes('应用 (1)') && !e.disabled)",
            value => value?.GetValue<bool>() == true);
        await core.ExecuteScriptAsync(
            "Array.from(document.querySelectorAll('.field-toolbar button'))"
            + ".find(e => e.textContent.includes('应用'))?.click(); true");
        await Wait(
            "Array.from(document.querySelectorAll('.field-toolbar button'))"
            + ".some(e => e.textContent.trim() === '应用' && e.disabled && !e.classList.contains('ant-btn-loading'))",
            value => value?.GetValue<bool>() == true);
        JsonObject submit = requests.Last(request => request["command"]?.GetValue<string>() == "applyPlayer");
        var submitReply = JsonNode.Parse(await core.ExecuteScriptAsync(
            "window.__snapshotReplies[" + JsonSerializer.Serialize(submit["id"]!.GetValue<string>()) + "]"));
        bool singleTokenOk = !string.IsNullOrEmpty(submit["payload"]?["readToken"]?.GetValue<string>())
            && submit["payload"]?["source"]?.GetValue<string>() == "prospects"
            && submitReply?["ok"]?.GetValue<bool>() == true
            && submitReply?["data"]?["patches"]?.GetValue<int>() == 0;
        report["singleUiSnapshot"] = new JsonObject
        {
            ["tokenPassed"] = singleTokenOk,
            ["patches"] = submitReply?["data"]?["patches"]?.DeepClone(),
        };
        if (outPath is not null)
        {
            string screenshot = Path.ChangeExtension(outPath, ".prospects.png");
            using var image = File.Create(screenshot);
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            report["prospectScreenshot"] = Path.GetFileName(screenshot);
        }

        await Search("");
        await Wait("document.querySelectorAll('.player-pane .ant-table-row').length", value =>
            value?.GetValue<int>() > 1);
        await core.ExecuteScriptAsync(
            "Array.from(document.querySelectorAll('.player-pane .ant-table-row')).slice(0,2)"
            + ".forEach(e => e.querySelector('.ant-checkbox-input')?.click()); true");
        await Wait(
            "Array.from(document.querySelectorAll('.player-pane button')).some(e => e.textContent.includes('批量编辑'))",
            value => value?.GetValue<bool>() == true);
        await core.ExecuteScriptAsync(
            "Array.from(document.querySelectorAll('.player-pane button'))"
            + ".find(e => e.textContent.includes('批量编辑'))?.click(); true");
        var batch = await Wait(
            "({title:document.querySelector('.ant-modal-title')?.textContent,"
            + "fields:document.querySelector('.ant-modal .ant-select') !== null,"
            + "errors:Array.from(document.querySelectorAll('.ant-message-error')).map(e => e.textContent)})",
            value => value?["fields"]?.GetValue<bool>() == true);
        report["prospectBatch"] = batch;
        await Wait(
            "document.querySelector('.ant-modal .ant-select')?.classList.contains('ant-select-disabled') === false",
            value => value?.GetValue<bool>() == true);
        const string batchField = "Attributes/Offense/3POINT";
        const string batchLabel = "三分投篮";
        await core.ExecuteScriptAsync(
            "document.querySelector('.ant-modal .ant-select-selector')"
            + "?.dispatchEvent(new MouseEvent('mousedown', {bubbles:true})); true");
        await core.ExecuteScriptAsync(
            "(() => { const input = document.querySelector('.ant-modal .ant-select input');"
            + "Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input,"
            + JsonSerializer.Serialize(batchLabel) + ");"
            + "input.dispatchEvent(new Event('input', {bubbles:true})); })()");
        await Wait(
            "Array.from(document.querySelectorAll('.ant-select-item-option'))"
            + ".some(e => (e.getAttribute('title') ?? '').includes(" + JsonSerializer.Serialize(batchLabel) + "))",
            value => value?.GetValue<bool>() == true);
        await core.ExecuteScriptAsync(
            "Array.from(document.querySelectorAll('.ant-select-item-option'))"
            + ".find(e => (e.getAttribute('title') ?? '').includes("
            + JsonSerializer.Serialize(batchLabel) + "))?.click(); true");
        var preview = await Wait(
            "({rows:document.querySelectorAll('.batch-preview .ant-table-row').length,"
            + "values:Array.from(document.querySelectorAll('.batch-preview .ant-table-row')).map(e => e.innerText),"
            + "disabled:document.querySelector('.ant-modal-footer .ant-btn-primary')?.disabled,"
            + "errors:Array.from(document.querySelectorAll('.ant-modal .ant-alert')).map(e => e.textContent)})",
            value => value?["rows"]?.GetValue<int>() == 2);
        report["batchUiPreview"] = preview;
        JsonObject previewRequest = requests.Last(request => request["command"]?.GetValue<string>() == "readBatch");
        bool previewOk = previewRequest["payload"]?["source"]?.GetValue<string>() == "prospects"
            && previewRequest["payload"]?["field"]?.GetValue<string>() == batchField
            && (previewRequest["payload"]?["indices"] as JsonArray)?.Count == 2
            && preview?["disabled"]?.GetValue<bool>() == true;
        if (outPath is not null)
        {
            string screenshot = Path.ChangeExtension(outPath, ".batch.png");
            using var image = File.Create(screenshot);
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            report["batchScreenshot"] = Path.GetFileName(screenshot);
        }
        int readsBeforeRefresh = requests.Count(request => request["command"]?.GetValue<string>() == "readBatch");
        await core.ExecuteScriptAsync(
            "Array.from(document.querySelectorAll('.ant-modal button'))"
            + ".find(e => e.textContent.includes('刷新预览'))?.click(); true");
        await Wait("document.querySelectorAll('.batch-preview .ant-table-row').length",
            value => value?.GetValue<int>() == 2);
        previewOk &= requests.Count(request => request["command"]?.GetValue<string>() == "readBatch")
            == readsBeforeRefresh + 1;
        report["batchUiPreviewOk"] = previewOk;
        await core.ExecuteScriptAsync("document.querySelector('.ant-modal-close')?.click(); true");
        await ClickSource("新秀名单");
        var standalone = await core.ExecuteScriptAsync(
            "({source:document.querySelector('.draft-source-filter .ant-segmented-item-selected')?.textContent.trim(),"
            + "detail:!!document.querySelector('.field-toolbar'),"
            + "batch:Array.from(document.querySelectorAll('.player-pane button')).some(e => e.textContent.includes('批量编辑')),"
            + "text:document.querySelector('.player-pane')?.innerText ?? ''})");
        report["standaloneAfterProspects"] = JsonNode.Parse(standalone);
        await ClickSource("球队");
        return filtered!["selected"]?.GetValue<string>() == "待选秀"
            && singleTokenOk && previewOk
            && batch!["title"]?.GetValue<string>() == "批量编辑 2 名球员"
            && report["standaloneAfterProspects"]?["detail"]?.GetValue<bool>() == false
            && report["standaloneAfterProspects"]?["batch"]?.GetValue<bool>() == false;
    }
}
