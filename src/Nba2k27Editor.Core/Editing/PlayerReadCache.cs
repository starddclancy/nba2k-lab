namespace Nba2k27Editor.Core.Editing;

/// <summary>Bounded, expiring read snapshots for one editor connection. Callers serialize access.</summary>
public sealed class PlayerReadCache
{
    private sealed record Entry(
        string Source, PlayerSnapshot[] Snapshots, bool Full, long Created, long Order);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private readonly int _capacity;
    private long _nextOrder;

    public PlayerReadCache(int capacity = 32, TimeSpan? lifetime = null, TimeProvider? clock = null)
    {
        _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
        _lifetime = lifetime ?? TimeSpan.FromMinutes(30);
        if (_lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }
        _clock = clock ?? TimeProvider.System;
    }

    public string Store(string source, IReadOnlyList<PlayerSnapshot> snapshots, bool full)
    {
        string actualSource = ActualSource(source);
        if (snapshots.Count == 0
            || snapshots.Select(snapshot => snapshot.Item.Index).Distinct().Count() != snapshots.Count
            || snapshots.Select(snapshot => snapshot.Item.Address).Distinct().Count() != snapshots.Count)
        {
            throw new ArgumentException("读取快照必须包含不同的球员");
        }
        if (snapshots.Any(snapshot => snapshot.Source != actualSource
            || snapshot.Epoch != snapshots[0].Epoch || snapshot.Signature != snapshots[0].Signature))
        {
            throw new ArgumentException("读取快照的来源或游戏会话不一致");
        }
        Prune();
        while (_entries.Count >= _capacity)
        {
            _entries.Remove(_entries.MinBy(pair => pair.Value.Order).Key);
        }
        string token = Guid.NewGuid().ToString("N");
        _entries.Add(token, new Entry(source, snapshots.ToArray(), full, _clock.GetTimestamp(), _nextOrder++));
        return token;
    }

    /// <summary>Resolve only the exact source, selection and fields that the user read.</summary>
    public IReadOnlyList<PlayerSnapshot> Get(
        string token, string source, IReadOnlyList<int> indices, IEnumerable<string>? fields = null)
    {
        ActualSource(source);
        Prune();
        if (!_entries.TryGetValue(token, out Entry? entry))
        {
            throw new InvalidOperationException("读取快照已失效，请重新读取球员或刷新批量预览");
        }
        if (source != entry.Source)
        {
            throw new InvalidOperationException("读取快照的球员来源不一致，请重新读取");
        }
        var byIndex = entry.Snapshots.ToDictionary(snapshot => snapshot.Item.Index);
        if (indices.Count != byIndex.Count || indices.Distinct().Count() != indices.Count
            || indices.Any(index => !byIndex.ContainsKey(index)))
        {
            throw new InvalidOperationException("读取快照的球员选择不一致，请重新读取");
        }
        if (fields is null && !entry.Full)
        {
            throw new InvalidOperationException("此快照仅包含批量字段，请重新读取完整球员资料");
        }
        if (fields is not null)
        {
            string[] keys = fields.ToArray();
            if (entry.Snapshots.Any(snapshot => keys.Any(key => !snapshot.Values.ContainsKey(key))))
            {
                throw new InvalidOperationException("字段不在读取快照中，请重新读取或刷新批量预览");
            }
        }
        return indices.Select(index => byIndex[index]).ToArray();
    }

    public void Discard(string? token)
    {
        if (token is not null)
        {
            _entries.Remove(token);
        }
    }

    public void Clear() => _entries.Clear();

    private void Prune()
    {
        long now = _clock.GetTimestamp();
        foreach (string token in _entries.Where(pair =>
            _clock.GetElapsedTime(pair.Value.Created, now) >= _lifetime).Select(pair => pair.Key).ToArray())
        {
            _entries.Remove(token);
        }
    }

    private static string ActualSource(string source) => source switch
    {
        "roster" or "prospects" => "roster",
        "draft" => "draft",
        _ => throw new ArgumentException($"未知球员来源：{source}"),
    };
}
