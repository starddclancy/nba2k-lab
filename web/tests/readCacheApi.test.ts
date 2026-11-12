import assert from 'node:assert/strict';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { createServer } from 'vite';

test('real API wrappers preserve display caching, fresh edit reads and bridge invalidation', async (t) => {
    const previousWindow = globalThis.window;
    const sent: { id: string; command: string; payload: Record<string, unknown> }[] = [];
    const held: typeof sent = [];
    let hold = '';
    let respond!: (event: { data: unknown }) => void;
    let nextToken = 0;
    const reply = (request: typeof sent[number], data: unknown) =>
        respond({ data: { id: request.id, ok: true, data } });
    globalThis.window = {
        location: { search: '', hash: '' },
        setTimeout,
        clearTimeout,
        chrome: {
            webview: {
                addEventListener: (_type: string, handler: typeof respond) => { respond = handler; },
                postMessage: (request: typeof sent[number]) => {
                    sent.push(request);
                    if (hold === request.command) {
                        held.push(request);
                        return;
                    }
                    const index = request.payload.index ?? 0;
                    reply(request, request.command === 'connect' ? { pid: 42, base: '0x1000' }
                        : request.command === 'applyPlayer' ? { changes: [] }
                        : { index, name: `Record ${index}`, readToken: `token-${++nextToken}`,
                            address: '0x1000', sections: [], direct: [], seasons: [], warnings: [] });
                },
            },
        },
    } as unknown as Window & typeof globalThis;
    const server = await createServer({
        configFile: false,
        cacheDir: 'node_modules/.vite-display-cache-api-tests',
        root: fileURLToPath(new URL('../', import.meta.url)),
        server: { middlewareMode: true, hmr: false },
        optimizeDeps: { noDiscovery: true, include: [] },
        appType: 'custom',
    });
    try {
        const api = await server.ssrLoadModule('/src/api.ts');
        const { displayReadCache, ReadInvalidatedError } = await server.ssrLoadModule('/src/readCache.ts');
        const { setGameEditing } = await server.ssrLoadModule('/src/editingPolicy.ts');

        await t.test('display helpers reuse values but existing player reads always fetch a new token', async () => {
            await api.connect();
            const first = await api.readPlayerDisplay(7, 'roster');
            const before = sent.length;
            assert.equal(await api.readPlayerDisplay(7, 'roster'), first);
            assert.equal(sent.length, before);
            assert.equal(api.peekPlayer(7, 'roster').value, first);
            const draft = await api.readPlayerDisplay(7, 'draft');
            assert.notEqual(draft.readToken, first.readToken);
            const editable = await api.readPlayer(7, 'roster');
            assert.notEqual(editable.readToken, first.readToken);
            assert.equal(api.peekPlayer(7, 'roster').value, editable);
        });

        await t.test('manual display refresh, team and statistics reads use separate bounded entries', async () => {
            const before = api.peekPlayer(7, 'roster').value;
            const refreshed = await api.readPlayerDisplay(7, 'roster', { force: true });
            assert.notEqual(refreshed.readToken, before.readToken);
            const stats = await api.readPlayerStatsDisplay(7, 'roster');
            const team = await api.readTeamRecordDisplay(0);
            assert.equal(api.peekPlayerStats(7, 'roster').value, stats);
            assert.equal(api.peekTeamRecord(0).value, team);
            assert.notEqual((await api.readPlayerStats(7, 'roster')).readToken, stats.readToken);
            assert.notEqual((await api.readTeamRecord(0)).readToken, team.readToken);
        });

        await t.test('same-PID reconnect clears all display entries and rejects actual old host responses', async () => {
            hold = 'readPlayer';
            const old = api.readPlayerDisplay(9, 'roster');
            await Promise.resolve();
            assert.equal(held.length, 1);
            const request = held.shift()!;
            hold = '';
            await api.connect();
            assert.equal(api.peekPlayer(7, 'roster'), undefined);
            assert.equal(api.peekTeamRecord(0), undefined);
            assert.equal(api.peekPlayerStats(7, 'roster'), undefined);
            reply(request, { index: 9, readToken: 'obsolete-token', sections: [] });
            await assert.rejects(old, ReadInvalidatedError);
            assert.equal(api.peekPlayer(9, 'roster'), undefined);
        });

        await t.test('browse policy blocks host writes while allowed mutations invalidate before and after transport', async () => {
            setGameEditing(false);
            const initial = await api.readPlayerDisplay(7, 'roster');
            const before = sent.length;
            await assert.rejects(api.applyPlayer(7, {}, 'roster', initial.readToken));
            assert.equal(sent.length, before);
            assert.equal(api.peekPlayer(7, 'roster').value, initial);
            setGameEditing(true);
            hold = 'applyPlayer';
            const writing = api.applyPlayer(7, {}, 'roster', initial.readToken);
            assert.equal(api.peekPlayer(7, 'roster'), undefined);
            const duringWrite = await api.readPlayerDisplay(7, 'roster');
            assert.equal(api.peekPlayer(7, 'roster').value, duringWrite);
            hold = '';
            reply(held.shift()!, { changes: [] });
            await writing;
            assert.equal(api.peekPlayer(7, 'roster'), undefined);
            setGameEditing(false);
        });

        await t.test('failed writes invalidate racing display reads and prepare commands retain display values', async () => {
            const initial = await api.readPlayerDisplay(7, 'roster');
            await api.planMove(7, 0);
            assert.equal(api.peekPlayer(7, 'roster').value, initial);
            setGameEditing(true);
            hold = 'applyPlayer';
            const writing = api.applyPlayer(7, {}, 'roster', initial.readToken);
            const failure = assert.rejects(writing, /partial write failure/);
            await api.readPlayerDisplay(7, 'roster');
            hold = '';
            respond({ data: { id: held.shift()!.id, ok: false, error: 'partial write failure' } });
            await failure;
            assert.equal(api.peekPlayer(7, 'roster'), undefined);
            setGameEditing(false);
        });

        await t.test('an earlier connect response cannot replace the latest connection identity', async () => {
            hold = 'connect';
            const earlier = api.connect();
            const earlierFailure = assert.rejects(earlier, ReadInvalidatedError);
            const latest = api.connect();
            const firstRequest = held.shift()!;
            const latestRequest = held.shift()!;
            hold = '';
            reply(latestRequest, { pid: 99, base: '0x9900' });
            await latest;
            const identity = displayReadCache.connectionIdentity;
            assert.match(identity, /^99:0x9900:/);
            reply(firstRequest, { pid: 42, base: '0x4200' });
            await earlierFailure;
            assert.equal(displayReadCache.connectionIdentity, identity);
        });
        displayReadCache.invalidate();
    } finally {
        await server.close();
        if (previousWindow === undefined) delete globalThis.window;
        else globalThis.window = previousWindow;
    }
});
