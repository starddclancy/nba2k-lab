using System.Text.Json.Nodes;

namespace Nba2k27Editor.Core.Match;

/// <summary>A counter span that changed between preview and write; nothing was written for it.</summary>
public sealed class MatchExternalChangeException(string message) : InvalidOperationException(message);

/// <summary>One applied transaction, kept for undo. Before/after bytes are the patch values.</summary>
public sealed record MatchWriteRecord(string Id, IReadOnlyList<MatchCounterPatch> Patches, DateTime AppliedUtc);

/// <summary>
/// Writes compiled counter patches to the running game as one transaction:
/// every span must still hold its "before" bytes; the plan is journaled before the first write;
/// each span is re-checked, written and read back; any failure restores the spans already written
/// (only where they still hold our "after" bytes, so a game update is never overwritten); a final
/// full read-back confirms the result. Process access is injected, so the logic is testable offline.
/// </summary>
public static class MatchWriter
{
    public static MatchWriteRecord Apply(IReadOnlyList<MatchCounterPatch> patches,
        Func<long, int, byte[]> read, Action<long, byte[]> write, Action<string, JsonObject> journal)
    {
        var spans = Validate(patches);
        string id = Guid.NewGuid().ToString("N");
        foreach (var span in spans)
        {
            if (!read(span.Patch.Address, span.Patch.Width).AsSpan().SequenceEqual(span.Before))
            {
                throw new MatchExternalChangeException("比赛数据在预览后已变化，未写入任何数据；请重新读取后再修改");
            }
        }
        journal("prepared", Describe(id, spans));
        Commit(id, spans, read, write, journal, forward: true);
        journal("applied", Describe(id, spans));
        return new(id, patches.ToList(), DateTime.UtcNow);
    }

    /// <summary>Restore the before bytes of one applied transaction, refusing if the game changed them.</summary>
    public static void Undo(MatchWriteRecord record,
        Func<long, int, byte[]> read, Action<long, byte[]> write, Action<string, JsonObject> journal)
    {
        var spans = Validate(record.Patches);
        foreach (var span in spans)
        {
            if (!read(span.Patch.Address, span.Patch.Width).AsSpan().SequenceEqual(span.After))
            {
                throw new MatchExternalChangeException("比赛数据在上次修改后已被游戏更新，撤销已停止，未写入任何数据");
            }
        }
        journal("undo-prepared", Describe(record.Id, spans));
        Commit(record.Id, spans, read, write, journal, forward: false);
        journal("undone", Describe(record.Id, spans));
    }

    private sealed record Span(MatchCounterPatch Patch, byte[] Before, byte[] After);

    private static void Commit(string id, List<Span> spans, Func<long, int, byte[]> read,
        Action<long, byte[]> write, Action<string, JsonObject> journal, bool forward)
    {
        var done = new List<Span>();
        try
        {
            foreach (var span in spans)
            {
                byte[] from = forward ? span.Before : span.After;
                byte[] to = forward ? span.After : span.Before;
                // Re-check immediately before writing: the game may update a counter at any moment.
                if (!read(span.Patch.Address, span.Patch.Width).AsSpan().SequenceEqual(from))
                {
                    throw new MatchExternalChangeException("比赛数据在写入过程中被游戏更新，已停止并恢复已写入的项");
                }
                done.Add(span);
                write(span.Patch.Address, to);
                if (!read(span.Patch.Address, span.Patch.Width).AsSpan().SequenceEqual(to))
                {
                    throw new InvalidOperationException("写入后读回不一致，已停止并恢复已写入的项");
                }
            }
            foreach (var span in spans)
            {
                byte[] to = forward ? span.After : span.Before;
                if (!read(span.Patch.Address, span.Patch.Width).AsSpan().SequenceEqual(to))
                {
                    throw new InvalidOperationException("整体读回不一致，已恢复已写入的项");
                }
            }
        }
        catch (Exception exc)
        {
            var unresolved = new JsonArray();
            for (int index = done.Count - 1; index >= 0; index--)
            {
                var span = done[index];
                byte[] ours = forward ? span.After : span.Before;
                byte[] original = forward ? span.Before : span.After;
                try
                {
                    byte[] current = read(span.Patch.Address, span.Patch.Width);
                    if (current.AsSpan().SequenceEqual(original))
                    {
                        continue;
                    }
                    if (!current.AsSpan().SequenceEqual(ours))
                    {
                        // The game moved this counter on; restoring now would overwrite its update.
                        unresolved.Add(Hex(span.Patch.Address));
                        continue;
                    }
                    write(span.Patch.Address, original);
                    if (!read(span.Patch.Address, span.Patch.Width).AsSpan().SequenceEqual(original))
                    {
                        unresolved.Add(Hex(span.Patch.Address));
                    }
                }
                catch (Exception)
                {
                    unresolved.Add(Hex(span.Patch.Address));
                }
            }
            var detail = Describe(id, spans);
            detail["error"] = exc.Message;
            detail["unresolved"] = unresolved;
            journal(unresolved.Count == 0 ? "rolled-back" : "needs-review", detail);
            if (unresolved.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{exc.Message}；有 {unresolved.Count} 项因游戏同时更新未能恢复，已记入日志", exc);
            }
            throw;
        }
    }

    private static List<Span> Validate(IReadOnlyList<MatchCounterPatch> patches)
    {
        if (patches.Count == 0)
        {
            throw new InvalidOperationException("没有需要写入的场内计数器");
        }
        var spans = new List<Span>();
        long previousEnd = long.MinValue;
        foreach (var patch in patches.OrderBy(patch => patch.Address))
        {
            if (patch.Width is not (2 or 4) || patch.Address % patch.Width != 0 || patch.Address < previousEnd)
            {
                throw new InvalidOperationException("场内计数器补丁宽度、对齐或范围无效");
            }
            byte[] before = Convert.FromHexString(patch.BeforeHex);
            byte[] after = Convert.FromHexString(patch.AfterHex);
            if (before.Length != patch.Width || after.Length != patch.Width)
            {
                throw new InvalidOperationException("场内计数器补丁字节长度与宽度不符");
            }
            previousEnd = patch.Address + patch.Width;
            spans.Add(new(patch, before, after));
        }
        return spans;
    }

    private static JsonObject Describe(string id, List<Span> spans) => new()
    {
        ["id"] = id,
        ["patches"] = new JsonArray(spans.Select(span => (JsonNode)new JsonObject
        {
            ["side"] = span.Patch.Side, ["playerId"] = span.Patch.PlayerId, ["slot"] = span.Patch.Slot,
            ["field"] = span.Patch.Field, ["address"] = Hex(span.Patch.Address), ["width"] = span.Patch.Width,
            ["before"] = span.Patch.BeforeHex, ["after"] = span.Patch.AfterHex,
        }).ToArray()),
    };

    private static string Hex(long address) => $"0x{address:X}";
}
