using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace Nba2k27Editor.WebHost;

internal static class AppearanceUiCheck
{
    internal static async Task<bool> Run(CoreWebView2 core, JsonObject report, string? outPath)
    {
        var notices = new JsonArray();
        foreach (string label in new[] { "扫描体型", "皮肤与纹身", "纹身" })
        {
            await core.ExecuteScriptAsync(
                "Array.from(document.querySelectorAll('.detail-host .ant-tabs-tab'))"
                + $".find(e => e.textContent.trim() === {JsonSerializer.Serialize(label)})?.click(); true");
            await Task.Delay(150);
            notices.Add(JsonNode.Parse(await core.ExecuteScriptAsync(
                "(() => { const e = document.querySelector('.ant-tabs-tabpane-active .absent-child-object');"
                + "return {section:e?.dataset.section ?? null, text:e?.textContent ?? null}; })()")));
        }
        bool ok = notices.All(notice => notice?["section"] is not null
            && notice["text"]?.GetValue<string>() == "该球员无自定义外貌/纹身数据");
        report["absentAppearance"] = new JsonObject { ["notices"] = notices, ["ok"] = ok };
        if (ok && outPath is not null)
        {
            string screenshot = Path.ChangeExtension(outPath, ".appearance.png");
            using var image = File.Create(screenshot);
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            report["appearanceScreenshot"] = Path.GetFileName(screenshot);
        }
        return ok;
    }
}
