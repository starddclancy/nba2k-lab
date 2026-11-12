using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace Nba2k27Editor.WebHost;

/// <summary>Verify roster filtering against the facts actually rendered from the live bridge.</summary>
internal static class SummaryUiCheck
{
    internal static async Task<bool> Run(CoreWebView2 core, JsonObject report, string? outPath)
    {
        const string rowsScript = """
            Array.from(document.querySelectorAll('.roster-player-pane .ant-table-row')).map(row => ({
                index: Number(row.getAttribute('data-row-key').split(':').pop()),
                summary: JSON.parse(row.querySelector('.player-facts')?.dataset.summary ?? 'null')
            }))
            """;
        var initial = JsonNode.Parse(await core.ExecuteScriptAsync(rowsScript))!.AsArray();
        var checks = new JsonObject();
        report["summaryUi"] = checks;
        checks["initialRows"] = initial.Count;
        checks["allSummariesPresent"] = initial.Count > 0 && initial.All(row => row?["summary"] is JsonObject);
        JsonNode? selected = JsonNode.Parse(await core.ExecuteScriptAsync(
            "JSON.parse(document.querySelector('.detail-facts .player-facts')?.dataset.summary ?? 'null')"));
        checks["listDetailAgree"] = selected is not null
            && JsonNode.DeepEquals(initial.FirstOrDefault()?["summary"], selected);

        await core.ExecuteScriptAsync("document.querySelector('.roster-filters').open = true; true");
        async Task SetNumber(string label, int value)
        {
            await core.ExecuteScriptAsync(
                "(() => { const input = document.querySelector('input[aria-label=\"" + label + "\"]');"
                + "Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input,"
                + "'" + value + "'); input.dispatchEvent(new Event('input', {bubbles:true})); })()");
        }
        async Task<JsonArray> WaitRows(int[] expected)
        {
            JsonArray actual = new();
            for (int attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(100);
                actual = JsonNode.Parse(await core.ExecuteScriptAsync(rowsScript))!.AsArray();
                int[] indices = actual.Select(row => row!["index"]!.GetValue<int>()).Order().ToArray();
                if (indices.SequenceEqual(expected.Order()))
                {
                    return actual;
                }
            }
            throw new InvalidOperationException("Roster filter result did not match the visible facts");
        }
        const int minimumOverall = 80;
        int[] filtered = initial.Where(row => row?["summary"]?["overall"] is JsonValue rating
                && rating.GetValue<double>() >= minimumOverall)
            .Select(row => row!["index"]!.GetValue<int>()).ToArray();
        await SetNumber("总评下限", minimumOverall);
        await WaitRows(filtered);
        checks["overallRows"] = filtered.Length;
        checks["overallFilter"] = true;

        if (filtered.Length > 1)
        {
            await core.ExecuteScriptAsync(
                "document.querySelector('.roster-player-pane .ant-table-thead .ant-checkbox-input')?.click(); true");
            await Task.Delay(150);
            checks["selectOnlyFiltered"] = JsonNode.Parse(await core.ExecuteScriptAsync(
                "Array.from(document.querySelectorAll('.roster-player-pane button'))"
                + $".some(e => e.textContent.trim() === '批量编辑 {filtered.Length}')"))!.GetValue<bool>();
        }
        else
        {
            checks["selectOnlyFiltered"] = null;
        }

        const int minimumHeight = 195;
        int[] combined = initial.Where(row => row?["summary"]?["overall"] is JsonValue rating
                && rating.GetValue<double>() >= minimumOverall
                && row?["summary"]?["heightCm"] is JsonValue height && height.GetValue<double>() >= minimumHeight)
            .Select(row => row!["index"]!.GetValue<int>()).ToArray();
        await SetNumber("身高下限", minimumHeight);
        await WaitRows(combined);
        checks["combinedRows"] = combined.Length;
        checks["combinedFilter"] = true;
        checks["selectionCleared"] = JsonNode.Parse(await core.ExecuteScriptAsync(
            "!Array.from(document.querySelectorAll('.roster-player-pane button'))"
            + ".some(e => e.textContent.includes('批量编辑'))"))!.GetValue<bool>();
        const int maximumAge = 30;
        int[] ageCombined = initial.Where(row => combined.Contains(row!["index"]!.GetValue<int>())
                && row?["summary"]?["age"] is JsonValue age && age.GetValue<int>() <= maximumAge)
            .Select(row => row!["index"]!.GetValue<int>()).ToArray();
        await SetNumber("年龄上限", maximumAge);
        await WaitRows(ageCombined);
        checks["ageRows"] = ageCombined.Length;
        checks["ageFilter"] = true;
        checks["visibleLabels"] = JsonNode.Parse(await core.ExecuteScriptAsync(
            "Array.from(document.querySelectorAll('.player-facts')).every(e => "
            + "!e.textContent.includes('年生') && !e.textContent.includes('三分')"
            + "&& (e.textContent.includes('岁') || e.textContent.includes('年龄未知'))"
            + "&& e.textContent.includes('总评'))"))!.GetValue<bool>();
        if (outPath is not null)
        {
            string screenshot = Path.ChangeExtension(outPath, ".filters.png");
            using var image = File.Create(screenshot);
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            report["filtersScreenshot"] = Path.GetFileName(screenshot);
        }
        await core.ExecuteScriptAsync(
            "Array.from(document.querySelectorAll('.roster-filters button'))"
            + ".find(e => e.textContent.includes('清除筛选'))?.click(); true");
        await WaitRows(initial.Select(row => row!["index"]!.GetValue<int>()).ToArray());
        checks["clearRestoresAll"] = true;
        await core.ExecuteScriptAsync("document.querySelector('.roster-filters').open = false; true");
        bool ok = checks["allSummariesPresent"]!.GetValue<bool>()
            && checks["listDetailAgree"]!.GetValue<bool>()
            && checks["selectionCleared"]!.GetValue<bool>()
            && checks["visibleLabels"]!.GetValue<bool>()
            && (checks["selectOnlyFiltered"]?.GetValue<bool>() ?? true);
        checks["ok"] = ok;
        return ok;
    }
}
