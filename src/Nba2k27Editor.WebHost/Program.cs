using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Nba2k27Editor.WebHost;

internal static class Program
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        // .NET 8 freezes an options instance on first use and then requires an explicit resolver.
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--browser-match-server", StringComparer.Ordinal))
        {
            return BrowserMatchServer.Run(ArgumentValue(args, "--token"), ArgumentValue(args, "--out-dir"),
                args.Contains("--allow-writes", StringComparer.Ordinal));
        }
        // --assets-check and --bridge-check let the packaged EXE prove itself without a visible
        // desktop session: the first validates the embedded frontend, the second drives the same
        // bridge the UI uses against the running game.
        bool assetsCheck = args.Contains("--assets-check", StringComparer.Ordinal);
        bool bridgeCheck = args.Contains("--bridge-check", StringComparer.Ordinal);
        bool uiCheck = args.Contains("--ui-check", StringComparer.Ordinal);
        if (args.Contains("--league-scope-check", StringComparer.Ordinal))
        {
            return LeagueScopeCheck.Run(ArgumentValue(args, "--out"));
        }
        if (args.Contains("--match-edit-check", StringComparer.Ordinal))
        {
            return MatchEditCheck.Run(ArgumentValue(args, "--out"));
        }
        if (args.Contains("--season-games-check", StringComparer.Ordinal))
        {
            return SeasonGamesCheck.Run(ArgumentValue(args, "--out"));
        }
        if (args.Contains("--match-read-check", StringComparer.Ordinal))
        {
            return MatchReadCheck.Run(ArgumentValue(args, "--out"));
        }
        if (args.Contains("--history-check", StringComparer.Ordinal))
        {
            return HistoryReadCheck.Run(ArgumentValue(args, "--out"), args.Contains("--write", StringComparer.Ordinal));
        }
        if (args.Contains("--stats-read-check", StringComparer.Ordinal))
        {
            return StatisticsReadCheck.Run(int.Parse(ArgumentValue(args, "--index") ?? "544"),
                ArgumentValue(args, "--out"));
        }
        if (args.Contains("--prospect-check", StringComparer.Ordinal))
        {
            return ProspectWriteCheck.Run(ArgumentValue(args, "--out"));
        }
        if (args.Contains("--type-check", StringComparer.Ordinal))
        {
            return Checks.RunTypeCheck(ArgumentValue(args, "--out"));
        }
        if (assetsCheck || bridgeCheck)
        {
            return Checks.Run(assetsCheck, bridgeCheck, ArgumentValue(args, "--out"));
        }
        ApplicationConfiguration.Initialize();
        if (args.Contains("--workspace-usability-ui-check", StringComparer.Ordinal))
        {
            return WorkspaceUsabilityUiCheck.Run(ArgumentValue(args, "--out"));
        }
        if (args.Contains("--league-scope-ui-check", StringComparer.Ordinal))
        {
            return LeagueScopeUiCheck.Run(ArgumentValue(args, "--out"));
        }
        if (args.Contains("--season-games-ui-check", StringComparer.Ordinal))
        {
            return SeasonGamesUiCheck.Run(ArgumentValue(args, "--out"));
        }
        if (args.Contains("--match-edit-ui-check", StringComparer.Ordinal))
        {
            return MatchEditUiCheck.Run(ArgumentValue(args, "--out"));
        }
        if (args.Contains("--match-ui-check", StringComparer.Ordinal)
            || args.Contains("--match-poll-check", StringComparer.Ordinal)
            || args.Contains("--match-safety-check", StringComparer.Ordinal))
        {
            return MatchUiCheck.Run(ArgumentValue(args, "--out"),
                args.Contains("--match-poll-check", StringComparer.Ordinal),
                args.Contains("--match-safety-check", StringComparer.Ordinal));
        }
        if (args.Contains("--ui-write-check", StringComparer.Ordinal))
        {
            return UiWriteCheck.Run(ArgumentValue(args, "--out"));
        }
        if (uiCheck)
        {
            return Checks.RunUi(ArgumentValue(args, "--out"));
        }
        bool matchDebug = args.Contains("--match-edit-debug", StringComparer.Ordinal);
        Application.Run(new EditorForm(matchOnly: matchDebug || args.Contains("--match", StringComparer.Ordinal),
            matchDebugReads: matchDebug,
            // In-match writes are on for the desktop app, gated on the game's pause-menu flags.
            // --no-match-writes turns them off; checks and debug forms never enable them.
            matchDiagnosticWrites: !args.Contains("--no-match-writes", StringComparer.Ordinal),
            scheduleWrites: args.Contains("--allow-schedule-writes", StringComparer.Ordinal)));
        return 0;
    }

    internal static string? ArgumentValue(string[] args, string name)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == name)
            {
                return args[index + 1];
            }
        }
        return null;
    }

    internal static void WriteReport(JsonObject report, string? outPath)
    {
        string json = report.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        if (outPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
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

/// <summary>Frontend bundle carried inside the assembly and served from a virtual origin.</summary>
internal static class EmbeddedAssets
{
    internal const string Prefix = "www";

    private static readonly Dictionary<string, byte[]> Files = Load();

    internal static IReadOnlyCollection<string> Paths => Files.Keys;

    internal static bool TryGet(string path, out byte[] bytes) => Files.TryGetValue(path, out bytes!);

    private static Dictionary<string, byte[]> Load()
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        Assembly assembly = typeof(EmbeddedAssets).Assembly;
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(Prefix + "/", StringComparison.Ordinal))
            {
                continue;
            }
            using Stream? stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                continue;
            }
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            // LogicalName expansion can leave a separator right after the prefix, so the path is
            // rebuilt as exactly one leading slash plus the resource-relative name.
            string relative = name[Prefix.Length..].Replace('\\', '/').TrimStart('/');
            result["/" + relative] = buffer.ToArray();
        }
        return result;
    }

    internal static string ContentType(string path)
    {
        int dot = path.LastIndexOf('.');
        string extension = dot < 0 ? string.Empty : path[dot..].ToLowerInvariant();
        return extension switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".map" => "application/json; charset=utf-8",
            _ => "application/octet-stream",
        };
    }
}

internal sealed class EditorForm : Form
{
    private const string Origin = "https://app.local";
    private readonly WebView2 _webView = new();
    private readonly Bridge _bridge;
    private readonly Action<CoreWebView2>? _onReady;
    private readonly bool _matchOnly;
    private string? _closeRequest;
    private bool _allowClose;
    internal int WriteAttempts => _bridge.TotalWriteAttempts;
    internal Bridge CommandBridge => _bridge;

    public EditorForm(Action<CoreWebView2>? onReady = null, bool matchOnly = false, bool matchDebugReads = false,
        bool matchDiagnosticWrites = false, bool scheduleWrites = false)
    {
        _bridge = new Bridge(matchDebugReads, matchDiagnosticWrites, scheduleWrites);
        _onReady = onReady;
        _matchOnly = matchOnly;
        Text = "NBA2K Lab";
        Width = 1440;
        Height = 900;
        MinimumSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        _webView.Dock = DockStyle.Fill;
        Controls.Add(_webView);
        // WebView2 does not mark the page hidden when the window is minimized or hidden, so the
        // page's visibility-driven work (live match reads) would keep running. Tie the control's
        // visibility to the window; WebView2 then reports document.hidden to the page.
        void SyncPageVisibility() => _webView.Visible = Visible && WindowState != FormWindowState.Minimized;
        Resize += (_, _) => SyncPageVisibility();
        VisibleChanged += (_, _) => SyncPageVisibility();
        // The bridge runs off the UI thread, so the file dialog has to hop back onto it.
        _bridge.PickFile = (filter) =>
        {
            string? picked = null;
            Invoke(() =>
            {
                using var dialog = new OpenFileDialog
                {
                    Title = "选择文件",
                    Filter = filter,
                };
                picked = dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
            });
            return picked;
        };
        Load += async (_, _) => await InitializeAsync();
        FormClosing += OnFormClosing;
        FormClosed += (_, _) => _bridge.Dispose();
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs args)
    {
        if (_allowClose || _webView.CoreWebView2 is not CoreWebView2 core) return;
        args.Cancel = true;
        if (_closeRequest is not null) return;
        string requestId = Guid.NewGuid().ToString("N");
        _closeRequest = requestId;
        try
        {
            await core.ExecuteScriptAsync($$"""
                (() => {
                    const requestId = {{JsonSerializer.Serialize(requestId)}};
                    if (typeof window.__requestEditorClose === 'function') {
                        window.__requestEditorClose(requestId).catch(() => {});
                    } else {
                        window.chrome.webview.postMessage({kind:'editorCloseDecision',requestId,allowed:true});
                    }
                })()
                """);
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _closeRequest = null;
            if (!IsDisposed)
                MessageBox.Show(this, "无法确认未应用的修改，请稍后重试关闭窗口。\n\n" + error.Message,
                    "关闭窗口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private bool HandleCloseDecision(string raw, string source)
    {
        using JsonDocument document = JsonDocument.Parse(raw);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("kind", out JsonElement kind)
            || kind.ValueKind != JsonValueKind.String || kind.GetString() != "editorCloseDecision")
            return false;
        if (!source.StartsWith(Origin + "/", StringComparison.Ordinal)
            || _closeRequest is null
            || !root.TryGetProperty("requestId", out JsonElement requestId)
            || requestId.ValueKind != JsonValueKind.String || requestId.GetString() != _closeRequest
            || !root.TryGetProperty("allowed", out JsonElement allowed)
            || allowed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return true;
        _closeRequest = null;
        if (allowed.GetBoolean())
        {
            _allowClose = true;
            BeginInvoke(Close);
        }
        return true;
    }

    private async Task InitializeAsync()
    {
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nba2k27Editor", "webview");
            Directory.CreateDirectory(userData);
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await _webView.EnsureCoreWebView2Async(environment);
            CoreWebView2 core = _webView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = true;
            core.AddWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnResourceRequested;
            core.WebMessageReceived += OnMessageReceived;
            if (_onReady is not null)
            {
                core.NavigationCompleted += (_, args) =>
                {
                    if (args.IsSuccess)
                    {
                        _onReady(core);
                    }
                };
            }
            core.Navigate(Origin + "/index.html" + (_matchOnly ? "?match" : ""));
        }
        catch (Exception exc)
        {
            MessageBox.Show(
                this,
                "WebView2 初始化失败，请确认已安装 Microsoft Edge WebView2 运行时。\n\n" + exc.Message,
                "启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        Uri uri = new(args.Request.Uri);
        string path = uri.AbsolutePath;
        if (path == "/" || path.Length == 0)
        {
            path = "/index.html";
        }
        CoreWebView2 core = _webView.CoreWebView2;
        if (EmbeddedAssets.TryGet(path, out byte[] bytes))
        {
            args.Response = core.Environment.CreateWebResourceResponse(
                new MemoryStream(bytes), 200, "OK", $"Content-Type: {EmbeddedAssets.ContentType(path)}");
            return;
        }
        // A client-side route that has no file behind it falls back to the shell document.
        if (!Path.HasExtension(path) && EmbeddedAssets.TryGet("/index.html", out byte[] fallback))
        {
            args.Response = core.Environment.CreateWebResourceResponse(
                new MemoryStream(fallback), 200, "OK", "Content-Type: text/html; charset=utf-8");
            return;
        }
        args.Response = core.Environment.CreateWebResourceResponse(
            new MemoryStream(Array.Empty<byte>()), 404, "Not Found", "Content-Type: text/plain");
    }

    private void OnMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string raw = args.WebMessageAsJson;
        if (HandleCloseDecision(raw, args.Source)) return;
        Trace("recv " + raw);
        Task.Run(() =>
        {
            string reply;
            try
            {
                reply = _bridge.Handle(raw);
            }
            catch (Exception exc)
            {
                reply = new System.Text.Json.Nodes.JsonObject
                {
                    ["ok"] = false,
                    ["error"] = "桥接异常：" + exc.Message,
                }.ToJsonString(Program.JsonOptions);
            }
            Trace("send " + reply[..Math.Min(reply.Length, 200)]);
            if (IsDisposed)
            {
                return;
            }
            try
            {
                BeginInvoke(() => _webView.CoreWebView2?.PostWebMessageAsJson(reply));
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    /// <summary>Diagnostics are opt-in so a normal run never writes files.</summary>
    private static void Trace(string line) => Diagnostics.Note(line);
}
