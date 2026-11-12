using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Gs;
using Nba2k27Editor.Core.Match;

namespace Nba2k27Editor.WebHost;

internal sealed partial class Bridge
{
    /// <summary>
    /// The desktop app enables writes with native pause-state and fresh-snapshot guards.
    /// Acceptance checks and diagnostic hosts choose their own explicit write policy.
    /// Undo remains available for outstanding transactions under the same state guards.
    /// </summary>
    private bool MatchWritesEnabled => _matchDiagnosticWrites;

    private string MatchWriteReason => MatchWritesEnabled
        ? "写入已开启：只有游戏处于暂停菜单时才会写入（读取游戏自身的暂停标志），并核对数据未变化；任一步失败自动恢复已写入的项。"
        : "此运行方式未开启场内写入。";

    /// <summary>Minimum spacing between sample starts, so a misbehaving page cannot hammer the game.</summary>
    private const int MinSampleSpacingMs = 150;

    /// <summary>Delay between the two samples that must agree before a write.</summary>
    private const int QuietWindowMs = 300;

    private NativeGsConnection? _matchConnection;
    private MatchSnapshot? _matchSnapshot;
    private string? _matchReadToken;
    private string? _matchPreviewToken;
    private List<MatchEditRequest>? _matchPreviewRequests;
    private MatchWritePlan? _matchPreviewPlan;
    private readonly List<MatchWriteRecord> _matchUndo = new();
    private long _lastSampleStartTicks;
    internal int MatchReadRequests { get; private set; }
    internal int MatchChangedSamples { get; private set; }
    internal bool MatchConnectionOpen => _matchConnection?.IsOpen == true;

    private JsonNode MatchStatus() => new JsonObject
    {
        ["readsEnabled"] = true,
        ["writesEnabled"] = MatchWritesEnabled,
        ["automaticDefault"] = true,
        ["automaticEnabled"] = true,
        ["reason"] = null,
        ["writeReason"] = MatchWriteReason,
        ["debugReadLimit"] = null,
        ["undoDepth"] = _matchUndo.Count,
    };

    private JsonNode MatchRead()
    {
        MatchReadRequests++;
        ClearMatchDraft();
        var watch = Stopwatch.StartNew();
        bool cold = _matchConnection is null;
        long beforeBytes = _matchConnection?.BytesRead ?? 0;
        try
        {
            var (snapshot, connection) = Sample();
            var result = JsonSerializer.SerializeToNode(snapshot,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!.AsObject();
            result["pid"] = connection.Pid;
            result["processCreatedUtc"] = new DateTime(connection.Created, DateTimeKind.Utc);
            result["moduleBase"] = $"0x{connection.Base:X}";
            result["capturedUtc"] = DateTime.UtcNow;
            result["coldConnection"] = cold;
            result["totalElapsedMs"] = watch.Elapsed.TotalMilliseconds;
            result["writes"] = connection.WriteAttempts;
            result["processBytesRead"] = connection.BytesRead - beforeBytes;
            result["identityBytesRead"] = connection.BytesRead - beforeBytes - snapshot.BytesRead;
            result["undoDepth"] = _matchUndo.Count;
            result["pauseState"] = PauseStateNode();
            result["fieldValidation"] = new JsonObject
            {
                ["points"] = "Native score getter, scoring handlers and live values",
                ["shooting"] = "Native handlers/accessors; standard shot categories, provisional labels",
                ["otherStats"] = "Native snapshot/accessors and live sums; further semantic validation pending",
            };
            _matchSnapshot = snapshot;
            _matchReadToken = Guid.NewGuid().ToString("N");
            result["readToken"] = _matchReadToken;
            return result;
        }
        catch (MatchSampleChangedException)
        {
            // Expected while the match is running. Keep the verified connection for the next sample.
            MatchChangedSamples++;
            throw;
        }
    }

    /// <summary>One bounded sample on the shared connection; any non-transient failure closes it.</summary>
    private (MatchSnapshot Snapshot, NativeGsConnection Connection) Sample()
    {
        long elapsed = (Stopwatch.GetTimestamp() - _lastSampleStartTicks) * 1000 / Stopwatch.Frequency;
        if (_lastSampleStartTicks != 0 && elapsed < MinSampleSpacingMs)
        {
            Thread.Sleep((int)(MinSampleSpacingMs - elapsed));
        }
        _lastSampleStartTicks = Stopwatch.GetTimestamp();
        try
        {
            _matchConnection ??= NativeGsConnection.Open();
            _matchConnection.Guard();
            var snapshot = new MatchReader(_matchConnection.Base,
                _matchConnection.Read, _matchConnection.Query).Read();
            _matchConnection.Guard();
            return (snapshot, _matchConnection);
        }
        catch (MatchSampleChangedException)
        {
            throw;
        }
        catch
        {
            // An invalid sample ends this connection without another diagnostic memory read.
            _matchConnection?.Dispose();
            _matchConnection = null;
            throw;
        }
    }

    private JsonNode MatchPrepare(JsonObject payload)
    {
        _matchPreviewToken = null;
        _matchPreviewRequests = null;
        _matchPreviewPlan = null;
        string token = payload["readToken"]?.GetValue<string>() ?? "";
        if (_matchSnapshot is null || _matchReadToken is null
            || !string.Equals(token, _matchReadToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("场内快照已失效，请重新读取比赛");
        }
        var requests = JsonSerializer.Deserialize<List<MatchEditRequest>>(
            Required(payload, "edits").ToJsonString(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("缺少球员统计修改");
        var writePlan = MatchWritePlanner.Prepare(_matchSnapshot, requests);
        var result = JsonSerializer.SerializeToNode(writePlan.Preview,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!.AsObject();
        result["patches"] = JsonSerializer.SerializeToNode(writePlan.Patches,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        result["patchBytes"] = writePlan.Patches.Sum(patch => patch.Width);
        result["liveWriteReady"] = MatchWritesEnabled && writePlan.Patches.Count > 0;
        _matchPreviewToken = Guid.NewGuid().ToString("N");
        _matchPreviewRequests = requests;
        _matchPreviewPlan = writePlan;
        result["readToken"] = _matchReadToken;
        result["previewToken"] = _matchPreviewToken;
        result["writes"] = 0;
        return result;
    }

    private JsonNode MatchApply(JsonObject payload)
    {
        if (!MatchWritesEnabled)
        {
            throw new InvalidOperationException(MatchWriteReason);
        }
        string readToken = payload["readToken"]?.GetValue<string>() ?? "";
        string previewToken = payload["previewToken"]?.GetValue<string>() ?? "";
        if (_matchPreviewPlan is null || _matchPreviewRequests is null
            || !string.Equals(readToken, _matchReadToken, StringComparison.Ordinal)
            || !string.Equals(previewToken, _matchPreviewToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("修改预览已失效，请重新预览");
        }
        var requests = _matchPreviewRequests;
        var planned = _matchPreviewPlan;
        // A one-shot preview: whatever happens below, the same preview cannot be applied twice.
        ClearMatchDraft();

        // Hard gate: the game's own pause flags, before and after the quiet window. Then two fresh
        // samples must re-plan to exactly the previewed patches with a stopped clock; the per-span
        // re-check inside MatchWriter covers anything that still moves afterwards.
        RequirePaused("应用");
        var first = QuietSample(requests, planned);
        Thread.Sleep(QuietWindowMs);
        var second = QuietSample(requests, planned);
        if (first.ClockValue != second.ClockValue)
        {
            throw new InvalidOperationException("比赛计时仍在走，请先在游戏内暂停比赛后再应用");
        }
        RequirePaused("应用");

        var connection = _matchConnection
            ?? throw new InvalidOperationException("游戏连接已断开，请重新读取比赛");
        var record = WriteCounters(connection, journalPrefix: "apply", (read, write, journal) =>
            MatchWriter.Apply(planned.Patches, read, write, journal));
        _matchUndo.Add(record!);
        return new JsonObject
        {
            ["writes"] = connection.WriteAttempts,
            ["patches"] = planned.Patches.Count,
            ["patchBytes"] = planned.Patches.Sum(patch => patch.Width),
            ["undoDepth"] = _matchUndo.Count,
            ["transaction"] = record!.Id,
        };
    }

    private JsonNode MatchUndo()
    {
        if (_matchUndo.Count == 0)
        {
            throw new InvalidOperationException("没有可撤销的场内修改");
        }
        var record = _matchUndo[^1];
        ClearMatchDraft();
        // Same gates as apply: undo must never write unless the game is in its pause menu.
        RequirePaused("撤销");
        MatchSnapshot first, second;
        // (Checked again after the quiet window, immediately before writing.)
        try
        {
            first = Sample().Snapshot;
            Thread.Sleep(QuietWindowMs);
            second = Sample().Snapshot;
        }
        catch (MatchSampleChangedException)
        {
            throw new InvalidOperationException("比赛数据正在变化，请先在游戏内暂停比赛后再撤销");
        }
        if (first.ClockValue != second.ClockValue || !second.ClockStableDuringRead)
        {
            throw new InvalidOperationException("比赛计时仍在走，请先在游戏内暂停比赛后再撤销");
        }
        RequirePaused("撤销");
        var connection = _matchConnection is { IsOpen: true } open ? open
            : throw new InvalidOperationException("游戏连接已断开，无法撤销；原值已记入修改日志");
        connection.Guard();
        WriteCounters(connection, journalPrefix: "undo", (read, write, journal) =>
        {
            MatchWriter.Undo(record, read, write, journal);
            return record;
        });
        _matchUndo.RemoveAt(_matchUndo.Count - 1);
        return new JsonObject
        {
            ["writes"] = connection.WriteAttempts,
            ["patches"] = record.Patches.Count,
            ["undoDepth"] = _matchUndo.Count,
            ["transaction"] = record.Id,
        };
    }

    private void RequirePaused(string action)
    {
        var connection = _matchConnection ??= NativeGsConnection.Open();
        connection.Guard();
        MatchPauseState.Require(connection.Base, connection.Read, action);
    }

    /// <summary>The game's pause flags, for the page to show whether writing is possible now.</summary>
    private JsonNode? PauseStateNode()
    {
        if (_matchConnection is not { IsOpen: true } connection)
        {
            return null;
        }
        var state = MatchPauseState.Read(connection.Base, connection.Read);
        return new JsonObject
        {
            ["paused"] = state.Paused, ["simulationStopped"] = state.SimulationStopped, ["pauseMenu"] = state.PauseMenu,
        };
    }

    private MatchSnapshot QuietSample(List<MatchEditRequest> requests, MatchWritePlan planned)
    {
        MatchSnapshot sample;
        try
        {
            sample = Sample().Snapshot;
        }
        catch (MatchSampleChangedException)
        {
            throw new InvalidOperationException("比赛数据正在变化，请先在游戏内暂停比赛后再应用");
        }
        var replanned = MatchWritePlanner.Prepare(sample, requests).Patches;
        if (replanned.Count != planned.Patches.Count
            || !replanned.Zip(planned.Patches).All(pair => pair.First == pair.Second))
        {
            throw new InvalidOperationException("比赛数据在预览后已变化，未写入任何数据；请重新读取后再修改");
        }
        return sample;
    }

    /// <summary>Durable journal + one write handle for the whole transaction.</summary>
    private static MatchWriteRecord? WriteCounters(NativeGsConnection connection, string journalPrefix,
        Func<Func<long, int, byte[]>, Action<long, byte[]>, Action<string, JsonObject>, MatchWriteRecord?> body)
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NBA2K27PlayerEditor", "match-writes");
        var checkpoint = new MatchCheckpoint(directory);
        void Journal(string phase, JsonObject detail)
        {
            detail["pid"] = connection.Pid;
            detail["processCreatedUtc"] = new DateTime(connection.Created, DateTimeKind.Utc);
            detail["moduleBase"] = $"0x{connection.Base:X}";
            checkpoint.Record(phase, journalPrefix, detail);
            checkpoint.Save($"{journalPrefix}-{detail["id"]}.json", new JsonObject
            {
                ["phase"] = phase, ["utc"] = DateTime.UtcNow, ["detail"] = detail.DeepClone(),
            });
        }
        connection.Guard();
        IntPtr handle = connection.OpenWriteHandle();
        try
        {
            return body(connection.Read, (address, data) =>
            {
                connection.WriteAttempts++;
                NativeGsConnection.WriteThrough(handle, address, data);
            }, Journal);
        }
        finally
        {
            NativeGsConnection.CloseWriteHandle(handle);
            connection.Guard();
        }
    }

    private void ClearMatchDraft()
    {
        _matchSnapshot = null;
        _matchReadToken = null;
        _matchPreviewToken = null;
        _matchPreviewRequests = null;
        _matchPreviewPlan = null;
    }
}
