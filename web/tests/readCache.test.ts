import assert from 'node:assert/strict';
import test from 'node:test';
import { DisplayReadCache, ReadInvalidatedError } from '../src/readCache.ts';

function deferred<T>() {
    let resolve!: (value: T) => void;
    let reject!: (reason: Error) => void;
    const promise = new Promise<T>((yes, no) => {
        resolve = yes;
        reject = no;
    });
    return { promise, resolve, reject };
}

test('revisiting a player displays the original token while a fresh read is pending', async () => {
    const cache = new DisplayReadCache();
    cache.setConnection('process-42');
    await cache.read('player:roster:7', async () => ({ name: 'First', readToken: 'first-token' }));
    await cache.read('player:roster:8', async () => ({ name: 'Other', readToken: 'other-token' }));

    const next = deferred<{ name: string; readToken: string }>();
    const refresh = cache.read('player:roster:7', () => next.promise, { force: true });
    assert.deepEqual(cache.peek('player:roster:7')?.value, { name: 'First', readToken: 'first-token' });
    next.resolve({ name: 'Updated', readToken: 'new-token' });
    assert.deepEqual(await refresh, { name: 'Updated', readToken: 'new-token' });
    assert.deepEqual(cache.peek('player:roster:7')?.value, { name: 'Updated', readToken: 'new-token' });
});

test('concurrent reads share one loader while roster and draft identities remain independent', async () => {
    const cache = new DisplayReadCache();
    const roster = deferred<string>();
    const draft = deferred<string>();
    const first = cache.read('player:roster:7', () => roster.promise);
    const second = cache.read('player:roster:7', () => { throw new Error('duplicate loader'); });
    const third = cache.read('player:roster:7', () => { throw new Error('duplicate force loader'); }, { force: true });
    const independent = cache.read('player:draft:7', () => draft.promise);
    assert.equal(first, second);
    assert.equal(first, third);
    roster.resolve('roster player');
    draft.resolve('draft player');
    assert.deepEqual(await Promise.all([first, second, third, independent]),
        ['roster player', 'roster player', 'roster player', 'draft player']);
});

test('fresh reads avoid loading, stale reads refresh, and retention expires at its boundary', async () => {
    let time = 0;
    const cache = new DisplayReadCache({ freshMs: 10, retainMs: 50, now: () => time });
    await cache.read('team:0', async () => 'initial');
    time = 9;
    assert.equal(await cache.read('team:0', () => { throw new Error('fresh cache missed'); }), 'initial');
    assert.equal(cache.peek('team:0')?.stale, false);
    time = 10;
    assert.equal(cache.peek('team:0')?.stale, true);
    assert.equal(await cache.read('team:0', async () => 'updated'), 'updated');
    time = 59;
    assert.deepEqual(cache.peek('team:0'), { value: 'updated', readAt: 10, stale: true });
    time = 60;
    assert.equal(cache.peek('team:0'), undefined);
    assert.equal(await cache.read('team:0', async () => 'after expiry'), 'after expiry');
});

test('least recently displayed records are evicted even when timestamps are equal', async () => {
    const cache = new DisplayReadCache({ capacity: 2, now: () => 0 });
    await cache.read('player:1', async () => 'one');
    await cache.read('player:2', async () => 'two');
    cache.peek('player:1');
    await cache.read('player:3', async () => 'three');
    assert.equal(cache.peek('player:2'), undefined);
    assert.equal(cache.peek('player:1')?.value, 'one');
    await cache.read('player:1', () => { throw new Error('recent read missed'); });
    await cache.read('player:4', async () => 'four');
    assert.equal(cache.peek('player:3'), undefined);
    assert.equal(cache.peek('player:1')?.value, 'one');
    assert.equal(cache.peek('player:4')?.value, 'four');
});

test('late responses from an old connection cannot refill or replace a new session request', async () => {
    const cache = new DisplayReadCache();
    cache.setConnection('process-A');
    await cache.read('team:0', async () => 'old team');
    const old = deferred<string>();
    const oldRead = cache.read('player:7', () => old.promise);
    await Promise.resolve();
    cache.setConnection('process-B');
    assert.equal(cache.peek('team:0'), undefined);
    const next = deferred<string>();
    const nextRead = cache.read('player:7', () => next.promise);
    old.resolve('wrong session');
    await assert.rejects(oldRead, ReadInvalidatedError);
    assert.equal(cache.peek('player:7'), undefined);
    assert.equal(cache.read('player:7', () => { throw new Error('new flight was lost'); }), nextRead);
    next.resolve('right session');
    assert.equal(await nextRead, 'right session');
    assert.equal(cache.peek('player:7')?.value, 'right session');
});

test('reconnecting to the same process and disconnecting both invalidate values and pending reads', async () => {
    const cache = new DisplayReadCache();
    cache.setConnection('process-42');
    await cache.read('player:7', async () => 'before reconnect');
    const old = deferred<string>();
    const oldRead = cache.read('team:0', () => old.promise);
    await Promise.resolve();
    cache.setConnection('process-42');
    assert.equal(cache.peek('player:7'), undefined);
    old.resolve('old connection');
    await assert.rejects(oldRead, ReadInvalidatedError);
    await cache.read('player:7', async () => 'before disconnect');
    cache.setConnection(null);
    assert.equal(cache.peek('player:7'), undefined);
});

test('mutation invalidation prevents a previous read from restoring pre-write values or tokens', async () => {
    const cache = new DisplayReadCache();
    await cache.read('player:7', async () => ({ rating: 70, readToken: 'old-token' }));
    await cache.read('player:8', async () => ({ rating: 80, readToken: 'unaffected-token' }));
    const beforeWrite = deferred<{ rating: number; readToken: string }>();
    const oldRead = cache.read('player:7', () => beforeWrite.promise, { force: true });
    await Promise.resolve();
    cache.invalidate('player:7');
    assert.equal(cache.peek('player:7'), undefined);
    const afterWrite = await cache.read('player:7', async () => ({ rating: 90, readToken: 'fresh-token' }));
    beforeWrite.resolve({ rating: 70, readToken: 'late-token' });
    await assert.rejects(oldRead, ReadInvalidatedError);
    assert.deepEqual(cache.peek('player:7')?.value, afterWrite);
    assert.deepEqual(cache.peek('player:8')?.value, { rating: 80, readToken: 'unaffected-token' });
});

test('predicate invalidation clears related panels and pending reads without invalidating unrelated panels', async () => {
    const cache = new DisplayReadCache();
    await cache.read('player:7:detail', async () => 'detail');
    await cache.read('player:8:detail', async () => 'other player');
    const statistics = deferred<string>();
    const pending = cache.read('player:7:statistics', () => statistics.promise);
    await Promise.resolve();
    cache.invalidate((key) => key.startsWith('player:7:'));
    assert.equal(cache.peek('player:7:detail'), undefined);
    assert.equal(cache.peek('player:8:detail')?.value, 'other player');
    statistics.resolve('old statistics');
    await assert.rejects(pending, ReadInvalidatedError);
    assert.equal(cache.peek('player:7:statistics'), undefined);
    cache.invalidate();
    assert.equal(cache.peek('player:8:detail'), undefined);
});

test('manual refresh bypasses a fresh cached value and strict edit refresh bypasses an earlier flight', async () => {
    const cache = new DisplayReadCache();
    await cache.read('player:7', async () => ({ readToken: 'display-token' }));
    const background = deferred<{ readToken: string }>();
    const backgroundRead = cache.read('player:7', () => background.promise, { force: true });
    await Promise.resolve();
    cache.invalidate('player:7');
    const editable = await cache.read('player:7', async () => ({ readToken: 'edit-token' }), { force: true });
    background.resolve({ readToken: 'background-token' });
    await assert.rejects(backgroundRead, ReadInvalidatedError);
    assert.deepEqual(editable, { readToken: 'edit-token' });
    assert.deepEqual(cache.peek('player:7')?.value, { readToken: 'edit-token' });
});

test('failed refresh retains a display value and a later read can recover', async () => {
    let time = 0;
    const cache = new DisplayReadCache({ freshMs: 10, retainMs: 50, now: () => time });
    await cache.read('team:0', async () => 'retained team');
    time = 10;
    await assert.rejects(cache.read('team:0', async () => { throw new Error('transport unavailable'); }),
        /transport unavailable/);
    assert.equal(cache.peek('team:0')?.value, 'retained team');
    assert.equal(cache.peek('team:0')?.readAt, 0);
    assert.equal(await cache.read('team:0', async () => 'recovered team'), 'recovered team');
    await assert.rejects(cache.read('player:7', () => { throw new Error('synchronous failure'); }),
        /synchronous failure/);
    assert.equal(await cache.read('player:7', async () => 'retry player'), 'retry player');
});

test('a failing obsolete read reports invalidation instead of a misleading current connection error', async () => {
    const cache = new DisplayReadCache();
    const old = deferred<string>();
    const oldRead = cache.read('player:7', () => old.promise);
    await Promise.resolve();
    cache.setConnection('new process');
    old.reject(new Error('old transport failed'));
    await assert.rejects(oldRead, ReadInvalidatedError);
    assert.equal(cache.peek('player:7'), undefined);
});
