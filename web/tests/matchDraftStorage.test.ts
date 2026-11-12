import assert from 'node:assert/strict';
import test from 'node:test';
import { loadMatchDrafts, saveMatchDrafts, clearMatchDrafts } from '../src/matchDraftStorage.ts';
import { readPreviewMatch } from '../src/matchPreview.ts';
import { matchDraftKey } from '../src/match.ts';

function storage() {
    const data = new Map<string, string>();
    return {
        data,
        getItem: (key: string) => data.get(key) ?? null,
        setItem: (key: string, value: string) => { data.set(key, value); },
        removeItem: (key: string) => { data.delete(key); },
    };
}

test('match input survives editor recreation, including incomplete and temporarily invalid shots', () => {
    const disk = storage();
    const snapshot = readPreviewMatch();
    const draft = { side: 0, playerId: 1, slot: 1, changes: { assists: null, twoPointMade: 100 } };
    const drafts = { [matchDraftKey(0, 1, 1)]: draft };
    saveMatchDrafts(disk, snapshot, drafts);
    assert.deepEqual(loadMatchDrafts(disk, structuredClone(snapshot)), drafts);
    assert.deepEqual(loadMatchDrafts(disk, { ...snapshot, pid: 22508 }), {});
    assert.deepEqual(loadMatchDrafts(disk, { ...snapshot, processCreatedUtc: 'new-process' }), {});
    assert.deepEqual(loadMatchDrafts(disk, { ...snapshot, readToken: 'new-capture' }), {});
    assert.equal(disk.data.size, 1);
});

test('old draft is rejected when captured counters or player identity change under the same token', () => {
    const disk = storage();
    const snapshot = readPreviewMatch();
    saveMatchDrafts(disk, snapshot, { '0:1:1': { side: 0, playerId: 1, slot: 1, changes: { assists: 5 } } });
    const changed = structuredClone(snapshot);
    changed.teams[0].players[0].stats.periods[0].assists++;
    assert.throws(() => loadMatchDrafts(disk, changed));
    const replaced = structuredClone(snapshot);
    replaced.teams[0].players[0].entity++;
    assert.throws(() => loadMatchDrafts(disk, replaced));
    assert.equal(disk.data.size, 1);
});

test('clearing only removes the selected capture and malformed records remain available for recovery', () => {
    const disk = storage();
    const first = readPreviewMatch();
    const second = readPreviewMatch();
    const drafts = { '0:1:1': { side: 0, playerId: 1, slot: 1, changes: { assists: 5 } } };
    saveMatchDrafts(disk, first, drafts);
    saveMatchDrafts(disk, second, drafts);
    clearMatchDrafts(disk, first);
    assert.deepEqual(loadMatchDrafts(disk, first), {});
    assert.deepEqual(loadMatchDrafts(disk, second), drafts);
    const key = [...disk.data.keys()][0];
    disk.data.set(key, '{bad json');
    assert.throws(() => loadMatchDrafts(disk, second));
    assert.equal(disk.data.get(key), '{bad json');
});

test('malformed counter fields and unavailable storage cannot silently report a saved draft', () => {
    const disk = storage();
    const snapshot = readPreviewMatch();
    assert.throws(() => saveMatchDrafts(disk, snapshot, {
        '0:1:1': { side: 0, playerId: 1, slot: 1, changes: { entity: 100 } },
    }));
    assert.throws(() => saveMatchDrafts(disk, snapshot, {
        '0:1:1': { side: 0, playerId: 1, slot: 1, changes: { assists: 1.5 } },
    }));
    const denied = { ...disk, setItem() { throw new Error('quota exceeded'); } };
    assert.throws(() => saveMatchDrafts(denied, snapshot, {
        '0:1:1': { side: 0, playerId: 1, slot: 1, changes: { assists: 5 } },
    }), /quota exceeded/);
});
