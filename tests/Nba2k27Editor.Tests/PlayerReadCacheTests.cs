using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Tests;

internal static class PlayerReadCacheTests
{
    private const string Key = "Attributes/Offense/3POINT";

    public static void Run(Harness harness)
    {
        var epoch = new SessionEpoch(1, 2, 3, 4, 5, 6, 100);
        PlayerSnapshot Snapshot(int index, string source = "roster", long address = 0) => new(
            new RecordListItem("Players", index, address == 0 ? 0x1000 + index * 1272 : address, "Same Name"),
            epoch, new byte[80],
            new Dictionary<string, FieldValue>
            {
                [Key] = new(190L, 85L, 0x100 + index, true),
            },
            new Dictionary<string, string>(), source, source == "draft" ? (0x2000, 150) : null);

        var first = Snapshot(1);
        var second = Snapshot(2);
        var cache = new PlayerReadCache();
        string token = cache.Store("prospects", new[] { first }, full: true);
        harness.Equal("reads: retains original snapshot", first,
            cache.Get(token, "prospects", new[] { 1 }, new[] { Key }).Single());
        harness.Equal("reads: retains displayed value", 85L,
            cache.Get(token, "prospects", new[] { 1 }).Single().Values[Key].Display);
        harness.Throws("reads: roster cannot reuse prospect token",
            () => cache.Get(token, "roster", new[] { 1 }, new[] { Key }));
        harness.Throws("reads: independent draft cannot reuse prospect token",
            () => cache.Get(token, "draft", new[] { 1 }, new[] { Key }));
        harness.Throws("reads: same name cannot reuse another record",
            () => cache.Get(token, "prospects", new[] { 2 }, new[] { Key }));
        harness.Throws("reads: extra player is rejected",
            () => cache.Get(token, "prospects", new[] { 1, 2 }, new[] { Key }));
        harness.Throws("reads: empty selection rejected",
            () => cache.Get(token, "prospects", Array.Empty<int>(), new[] { Key }));
        harness.Throws("reads: unknown token rejected",
            () => cache.Get("missing", "prospects", new[] { 1 }, new[] { Key }));
        harness.Throws("reads: empty token rejected",
            () => cache.Get("", "prospects", new[] { 1 }, new[] { Key }));
        harness.Throws("reads: unread field rejected",
            () => cache.Get(token, "prospects", new[] { 1 }, new[] { "other" }));
        harness.Throws("reads: unknown browse source rejected",
            () => cache.Get(token, "other", new[] { 1 }, new[] { Key }));

        string batch = cache.Store("roster", new[] { first, second }, full: false);
        var reversed = cache.Get(batch, "roster", new[] { 2, 1 }, new[] { Key });
        harness.Equal("reads: selection order follows request", second, reversed[0]);
        harness.Equal("reads: second snapshot preserved", first, reversed[1]);
        harness.Throws("reads: partial batch cannot import a full player",
            () => cache.Get(batch, "roster", new[] { 1, 2 }));
        harness.Throws("reads: batch subset rejected",
            () => cache.Get(batch, "roster", new[] { 1 }, new[] { Key }));
        harness.Throws("reads: duplicate selection rejected",
            () => cache.Get(batch, "roster", new[] { 1, 1 }, new[] { Key }));
        var draft = Snapshot(1, "draft");
        string draftToken = cache.Store("draft", new[] { draft }, full: true);
        harness.Equal("reads: draft source and signature retained", draft,
            cache.Get(draftToken, "draft", new[] { 1 }).Single());
        harness.Throws("reads: mixed actual sources rejected",
            () => cache.Store("roster", new[] { first, Snapshot(2, "draft") }, full: false));
        harness.Throws("reads: prospect snapshot must use roster storage",
            () => cache.Store("prospects", new[] { draft }, full: true));
        harness.Throws("reads: empty snapshot rejected",
            () => cache.Store("roster", Array.Empty<PlayerSnapshot>(), full: true));
        harness.Throws("reads: duplicate snapshot rejected",
            () => cache.Store("roster", new[] { first, first }, full: false));
        harness.Throws("reads: shared address rejected",
            () => cache.Store("roster", new[] { first, Snapshot(2, address: first.Item.Address) }, full: false));
        cache.Discard(token);
        harness.Throws("reads: consumed token rejected", () => cache.Get(token, "prospects", new[] { 1 }));
        harness.Equal("reads: discard leaves other read intact", draft,
            cache.Get(draftToken, "draft", new[] { 1 }).Single());
        cache.Clear();
        harness.Throws("reads: reconnect clears draft read", () => cache.Get(draftToken, "draft", new[] { 1 }));
        harness.Throws("reads: reconnect clears batch read",
            () => cache.Get(batch, "roster", new[] { 1, 2 }, new[] { Key }));

        var clock = new TestClock();
        var bounded = new PlayerReadCache(2, TimeSpan.FromSeconds(10), clock);
        string oldest = bounded.Store("roster", new[] { first }, full: true);
        string middle = bounded.Store("roster", new[] { first }, full: true);
        string latest = bounded.Store("roster", new[] { first }, full: true);
        harness.Throws("reads: capacity evicts oldest even at same timestamp",
            () => bounded.Get(oldest, "roster", new[] { 1 }));
        harness.Equal("reads: capacity retains newest", first,
            bounded.Get(latest, "roster", new[] { 1 }).Single());
        string newest = bounded.Store("roster", new[] { first }, full: true);
        harness.Throws("reads: eviction order survives dictionary slot reuse",
            () => bounded.Get(middle, "roster", new[] { 1 }));
        harness.Equal("reads: previous newest still retained", first,
            bounded.Get(latest, "roster", new[] { 1 }).Single());
        clock.Advance(TimeSpan.FromSeconds(9));
        harness.Equal("reads: snapshot is valid before expiry", first,
            bounded.Get(newest, "roster", new[] { 1 }).Single());
        clock.Advance(TimeSpan.FromSeconds(1));
        harness.Throws("reads: snapshot expires at boundary",
            () => bounded.Get(newest, "roster", new[] { 1 }));
        string renewed = bounded.Store("roster", new[] { first }, full: true);
        harness.Equal("reads: fresh snapshot after expiry works", first,
            bounded.Get(renewed, "roster", new[] { 1 }).Single());
        harness.Throws("reads: nonpositive capacity rejected", () => new PlayerReadCache(0));
        harness.Throws("reads: nonpositive lifetime rejected",
            () => new PlayerReadCache(lifetime: TimeSpan.Zero));
    }

    private sealed class TestClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
    }
}
