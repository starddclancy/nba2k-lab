using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace Nba2k27Editor.WebHost;

internal static class TeamLogoUiCheck
{
    internal static async Task<bool> Run(CoreWebView2 core, JsonObject report, string? outPath)
    {
        var checks = new JsonObject();
        report["teamLogos"] = checks;
        async Task<JsonNode?> Probe(string script) => JsonNode.Parse(await core.ExecuteScriptAsync(script));
        const string imageProbe = """
            (() => {
                const rows = [...document.querySelectorAll('.team-navigation .team-identity')];
                return {
                    teams: rows.length,
                    logos: rows.filter(e => e.querySelector('img')).length,
                    pending: rows.filter(e => !e.querySelector('img')).map(e => ({
                        name:e.dataset.team, reference:e.dataset.logoReference
                    })),
                    imagesLoaded: rows.filter(e => e.querySelector('img')).every(e => {
                        const img = e.querySelector('img');
                        return img.complete && img.naturalWidth > 0 && new URL(img.src).host === 'app.local';
                    }),
                    records: rows.map(e => ({
                        name:e.dataset.team, reference:e.dataset.logoReference,
                        file:e.querySelector('img')?.getAttribute('src') ?? null
                    }))
                };
            })()
            """;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            checks["navigation"] = await Probe(imageProbe);
            if (checks["navigation"]?["imagesLoaded"]?.GetValue<bool>() == true)
            {
                break;
            }
            await Task.Delay(200);
        }
        var eraChecks = new JsonArray();
        foreach ((int year, string expected) in new[]
        {
            (65, "los-angeles-lakers-1960"),
            (87, "los-angeles-lakers-1976"),
            (104, "los-angeles-lakers-2001"),
        })
        {
            await core.ExecuteScriptAsync($$"""
                document.querySelector('.team-navigation [data-team="Los Angeles Lakers"][data-historic-year="{{year}}"]')
                    ?.closest('.ant-menu-item').click(); true
                """);
            JsonNode? era = null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(100);
                era = await Probe("""
                    (() => {
                        const e = document.querySelector('.selected-team-heading .team-identity');
                        const img = e?.querySelector('img');
                        return {year:e?.dataset.historicYear, file:img?.getAttribute('src'),
                            title:e?.title, loaded:!!img?.complete && img.naturalWidth > 0};
                    })()
                    """);
                if (era?["year"]?.GetValue<string>() == year.ToString()
                    && era?["loaded"]?.GetValue<bool>() == true)
                {
                    break;
                }
            }
            var result = era?.AsObject() ?? new JsonObject();
            result["ok"] = result["loaded"]?.GetValue<bool>() == true
                && result["file"]?.GetValue<string>().Contains(expected) == true;
            eraChecks.Add(result);
        }
        checks["sameTeamAcrossEras"] = eraChecks;
        if (outPath is not null)
        {
            string screenshot = Path.ChangeExtension(outPath, ".same-team-era.png");
            using var image = File.Create(screenshot);
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            checks["sameTeamEraScreenshot"] = Path.GetFileName(screenshot);
        }
        checks["conferenceContrast"] = await Probe("""
            (() => {
                const images = [...document.querySelectorAll('.team-navigation .team-logo-conference')];
                return {count:images.length, visible:images.length === 4 && images.every(i =>
                    i.complete && i.naturalWidth > 0 &&
                    getComputedStyle(i).backgroundColor === 'rgb(57, 64, 71)')};
            })()
            """);
        if (outPath is not null)
        {
            await core.ExecuteScriptAsync("""
                document.querySelector('.team-navigation [data-team="East All-Stars"]')?.scrollIntoView({block:'center'}); true
                """);
            await Task.Delay(200);
            string screenshot = Path.ChangeExtension(outPath, ".special-team-logos.png");
            using var image = File.Create(screenshot);
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            checks["specialTeamsScreenshot"] = Path.GetFileName(screenshot);
        }
        await core.ExecuteScriptAsync("""
            document.querySelector('.team-navigation [data-team="Seattle SuperSonics"]')?.closest('.ant-menu-item').click(); true
            """);
        await Task.Delay(400);
        checks["historicalHeading"] = await Probe("""
            (() => {
                const e = document.querySelector('.selected-team-heading .team-identity');
                const img = e?.querySelector('img');
                return {name:e?.dataset.team, file:img?.getAttribute('src'),
                    loaded:!!img?.complete && img.naturalWidth > 0};
            })()
            """);
        if (outPath is not null)
        {
            string screenshot = Path.ChangeExtension(outPath, ".historical-team.png");
            using var image = File.Create(screenshot);
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            checks["historicalScreenshot"] = Path.GetFileName(screenshot);
        }
        await core.ExecuteScriptAsync("""
            document.querySelector('.team-navigation .team-identity')?.closest('.ant-menu-item').click(); true
            """);
        await Task.Delay(300);
        int teamCount = checks["navigation"]?["teams"]?.GetValue<int>() ?? 0;
        bool ok = teamCount >= 71
            && checks["navigation"]?["logos"]?.GetValue<int>() == teamCount
            && checks["navigation"]?["pending"]?.AsArray().Count == 0
            && checks["navigation"]?["imagesLoaded"]?.GetValue<bool>() == true
            && checks["conferenceContrast"]?["visible"]?.GetValue<bool>() == true
            && eraChecks.All(era => era?["ok"]?.GetValue<bool>() == true)
            && checks["historicalHeading"]?["loaded"]?.GetValue<bool>() == true
            && checks["historicalHeading"]?["file"]?.GetValue<string>().Contains("game-logo024okc_1996") == true;
        checks["ok"] = ok;
        return ok;
    }

    internal static async Task<bool> TeamPage(CoreWebView2 core, JsonObject report, string? outPath)
    {
        var data = JsonNode.Parse(await core.ExecuteScriptAsync("""
            (() => {
                const images = [...document.querySelectorAll('.player-pane .team-identity img')];
                const heading = document.querySelector('.field-toolbar .team-identity img');
                return {logos:images.length, loaded:images.length > 0 && images.every(i => i.complete && i.naturalWidth > 0),
                    headingLoaded:!!heading?.complete && heading.naturalWidth > 0};
            })()
            """));
        report["teamLogoPage"] = data;
        if (outPath is not null)
        {
            string screenshot = Path.ChangeExtension(outPath, ".team-logos.png");
            using var image = File.Create(screenshot);
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            report["teamLogoPageScreenshot"] = Path.GetFileName(screenshot);
        }
        return data?["loaded"]?.GetValue<bool>() == true && data["headingLoaded"]?.GetValue<bool>() == true;
    }

    internal static async Task<bool> RosterPage(CoreWebView2 core, JsonObject report, string? outPath)
    {
        await core.ExecuteScriptAsync("""
            (() => {
                const input = document.querySelectorAll('.roster-panel .ant-select')[2]?.querySelector('input');
                input?.dispatchEvent(new MouseEvent('mousedown', {bubbles:true}));
                input?.focus();
                if (input) {
                    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, 'New Jersey');
                    input.dispatchEvent(new Event('input', {bubbles:true}));
                }
                return true;
            })()
            """);
        JsonNode? data = null;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(200);
            data = JsonNode.Parse(await core.ExecuteScriptAsync("""
                (() => {
                    const rows = [...document.querySelectorAll('.ant-select-dropdown:not(.ant-select-dropdown-hidden) .team-identity')];
                    return {options:rows.length, names:rows.map(e => e.dataset.team),
                        loaded:rows.length > 0 && rows.every(e => {
                            const img = e.querySelector('img');
                            return img?.complete && img.naturalWidth > 0 && img.src.includes('new-jersey-nets-1997');
                        })};
                })()
                """));
            if (data?["loaded"]?.GetValue<bool>() == true)
            {
                break;
            }
        }
        report["teamLogoSearch"] = data;
        if (outPath is not null)
        {
            string screenshot = Path.ChangeExtension(outPath, ".team-select.png");
            using var image = File.Create(screenshot);
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            report["teamLogoSearchScreenshot"] = Path.GetFileName(screenshot);
        }
        await core.ExecuteScriptAsync("""
            document.querySelector('.ant-select-dropdown:not(.ant-select-dropdown-hidden) .ant-select-item-option')?.click(); true
            """);
        await Task.Delay(200);
        report["teamLogoSelected"] = JsonNode.Parse(await core.ExecuteScriptAsync("""
            (() => {
                const img = document.querySelectorAll('.roster-panel .ant-select')[2]?.querySelector('img');
                return {loaded:!!img?.complete && img.naturalWidth > 0, file:img?.getAttribute('src')};
            })()
            """));
        return data?["loaded"]?.GetValue<bool>() == true
            && report["teamLogoSelected"]?["loaded"]?.GetValue<bool>() == true;
    }
}
