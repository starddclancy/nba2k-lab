using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Gs;

namespace Nba2k27Editor.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // --self-check builds the window without a message loop so the packaged build can be
        // verified offline without a visible desktop session. --connect-check additionally
        // attaches to the live game read-only, which proves the packaged EXE reaches the process.
        // --write-check performs one field write, reads it back, undoes it in the same connection
        // and compares the whole 1272-byte player record, so the shipped EXE has write evidence.
        bool selfCheck = args.Contains("--self-check", StringComparer.Ordinal);
        bool connectCheck = args.Contains("--connect-check", StringComparer.Ordinal);
        bool writeCheck = args.Contains("--write-check", StringComparer.Ordinal);
        bool batchCheck = args.Contains("--batch-check", StringComparer.Ordinal);
        ApplicationConfiguration.Initialize();
        if (!selfCheck && !connectCheck && !writeCheck && !batchCheck)
        {
            Application.Run(new MainForm());
            return 0;
        }

        string? outPath = null;
        var playerNames = new List<string>();
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == "--out")
            {
                outPath = args[index + 1];
            }
            else if (args[index] == "--player")
            {
                playerNames.Add(args[index + 1]);
            }
        }

        if (writeCheck)
        {
            var writeReport = WriteCheck.Run(playerNames.FirstOrDefault());
            Write(writeReport, outPath);
            return writeReport["status"]!.GetValue<string>() == "ok" ? 0 : 1;
        }
        if (batchCheck)
        {
            var batchReport = BatchCheck.Run(playerNames);
            Write(batchReport, outPath);
            return batchReport["status"]!.GetValue<string>() == "ok" ? 0 : 1;
        }

        using var form = new MainForm();
        form.CreateControl();
        using var gs = new GsForm();
        gs.CreateControl();
        var report = new JsonObject
        {
            ["version"] = AppVersion.Value,
            ["form"] = form.Text,
            ["sections"] = MainForm.Sections.Length,
            ["tabs"] = form.TabCount,
            ["players"] = form.PlayerCount,
            ["player_fields"] = form.FieldCount,
            ["gs_fields"] = GsPresets.Catalog.Count,
            ["gs_keys"] = GsPresets.AllKeys.Count,
            ["gs_tabs"] = gs.TabCount,
            ["gs_legacy_fields"] = gs.LegacyFieldCount,
            ["gs_legacy_values"] = new JsonArray(gs.LegacyTestedValues.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            ["gs_legacy_target"] = gs.LegacyTarget,
            ["draft_players"] = form.DraftCount,
            ["connected"] = form.Connected,
            ["writes"] = form.WriteCount,
        };
        if (connectCheck)
        {
            try
            {
                form.ConnectForCheck();
                report["connected"] = form.Connected;
                report["pid"] = form.EpochPid;
                report["roster"] = form.RosterCount;
                report["teams"] = form.TeamCount;
                report["draft_status"] = form.DraftStatus;
                report["draft_players"] = form.DraftCount;
                report["sample_player"] = form.SamplePlayer;
                report["sample_fields"] = form.SampleFields;
                report["writes"] = form.WriteCount;
                report["status"] = "ok";
            }
            catch (Exception exc)
            {
                report["status"] = "failed";
                report["error"] = exc.Message;
                Write(report, outPath);
                return 1;
            }
        }
        string json = report.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        if (outPath is not null)
        {
            // Writing the file avoids console code page issues when the build is driven from a script.
            File.WriteAllText(outPath, json + "\n", new UTF8Encoding(false));
            return 0;
        }
        // A WinExe has no console handle; setting the encoding throws when output is redirected.
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
        }
        Console.WriteLine(json);
        return 0;
    }

    private static void Write(JsonObject report, string? outPath)
    {
        string json = report.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        if (outPath is not null)
        {
            File.WriteAllText(outPath, json + "\n", new UTF8Encoding(false));
            return;
        }
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
        }
        Console.WriteLine(json);
    }
}
