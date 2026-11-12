import assert from 'node:assert/strict';
import test from 'node:test';
import {
    matchDraftKey, matchDraftValues, matchEditRequests, matchFieldLabel, matchShooting, matchSlotLabel, stableOrder,
} from '../src/match.ts';
import type { MatchEditDraft, MatchSnapshot } from '../src/match.ts';

test('match diagnostics explain every decoded counter without exposing raw keys', () => {
    for (const [field, label] of [
        ['points', '得分'], ['rebounds', '篮板'], ['twoPointMade', '两分命中'],
        ['offensiveRebounds', '前场篮板'], ['assists', '助攻'],
    ]) {
        assert.equal(matchFieldLabel(field), label);
    }
    assert.equal(matchFieldLabel('unexpectedCounter'), '统计数据');
});

test('live refresh keeps row order while scores change', () => {
    type Row = { entity: number; points: number };
    const byPoints = (first: Row, second: Row) => second.points - first.points;
    const key = (row: Row) => row.entity;
    const first = stableOrder([], [{ entity: 1, points: 2 }, { entity: 2, points: 9 }, { entity: 3, points: 5 }], key, byPoints);
    assert.deepEqual(first.map(key), [2, 3, 1], 'first sample uses the initial sort');
    const next = stableOrder(first.map(key),
        [{ entity: 1, points: 20 }, { entity: 2, points: 9 }, { entity: 3, points: 5 }], key, byPoints);
    assert.deepEqual(next.map(key), [2, 3, 1], 'a scoring player does not jump');
    assert.equal(next[2].points, 20, 'values still come from the newest sample');
    const roster = stableOrder(next.map(key),
        [{ entity: 3, points: 5 }, { entity: 2, points: 9 }, { entity: 4, points: 1 }, { entity: 5, points: 7 }], key, byPoints);
    assert.deepEqual(roster.map(key), [2, 3, 5, 4], 'departed rows drop, new rows append in initial order');
});

function sample(): MatchSnapshot {
    const row = {
        points: 2, twoPointMade: 1, twoPointAttempted: 1, threePointMade: 0, threePointAttempted: 0,
        freeThrowMade: 0, freeThrowAttempted: 0, offensiveRebounds: 0, defensiveRebounds: 0,
        rebounds: 0, assists: 0, steals: 0, blocks: 0, turnovers: 0,
    };
    return {
        pid: 1, processCreatedUtc: '', capturedUtc: '', readToken: 'test',
        period: 2, clockValue: 600, clockStableDuringRead: true, pauseStateVerified: false,
        editable: false, writes: 0, elapsedMs: 0, totalElapsedMs: 0, issues: [],
        teams: [{
            side: 0, label: 'Team', historicYearRaw: 0, logoReferences: [],
            stats: { total: { ...row }, periods: Array.from({ length: 5 }, () => ({ ...row })) },
            players: [{
                id: 12, name: 'Player', photoId: 0, onCourt: true, entity: 100,
                stats: { total: { ...row }, periods: Array.from({ length: 5 }, () => ({ ...row })) },
            }],
        }],
    };
}

const draft = (changes: MatchEditDraft['changes'], slot = 1): MatchEditDraft =>
    ({ side: 0, playerId: 12, slot, changes });

test('edit draft derives score and rebounds without changing the captured row', () => {
    const original = sample().teams[0].players[0].stats.periods[0];
    const before = structuredClone(original);
    const values = matchDraftValues(original, draft({
        threePointMade: 2, threePointAttempted: 3, freeThrowMade: 1, freeThrowAttempted: 1,
        offensiveRebounds: 2, defensiveRebounds: 3,
    }));
    assert.equal(values.points, 9);
    assert.equal(values.rebounds, 5);
    assert.equal(matchShooting(values, 'three').percentage, '66.7%');
    assert.deepEqual(original, before);
    assert.equal(matchSlotLabel(0), '全场');
    assert.equal(matchSlotLabel(5), '加时合计');
});

test('draft keys isolate team, player and period and unchanged inputs are omitted', () => {
    assert.notEqual(matchDraftKey(0, 12, 1), matchDraftKey(1, 12, 1));
    assert.notEqual(matchDraftKey(0, 12, 1), matchDraftKey(0, 13, 1));
    assert.notEqual(matchDraftKey(0, 12, 1), matchDraftKey(0, 12, 2));
    assert.deepEqual(matchEditRequests(sample(), [draft({ twoPointMade: 1 })]), []);
    assert.deepEqual(matchEditRequests(sample(), [draft({ assists: 2, twoPointMade: 1 })]),
        [{ side: 0, playerId: 12, slot: 1, changes: { assists: 2 } }]);
});

test('draft rejects empty, fractional, negative, overflowing and unrecognized counters', () => {
    for (const changes of [
        { assists: null }, { assists: 1.5 }, { assists: -1 }, { assists: 65536 },
        { assists: Number.NaN }, { assists: Number.POSITIVE_INFINITY }, { points: 20 }, { entity: 123 },
    ]) {
        assert.throws(() => matchEditRequests(sample(), [draft(changes)]));
    }
    assert.ok(Number.isNaN(matchDraftValues(
        sample().teams[0].players[0].stats.periods[0], draft({ threePointMade: null })).points));
});

test('draft rejects impossible shooting, duplicate requests and future periods', () => {
    assert.throws(() => matchEditRequests(sample(), [draft({ threePointMade: 1 })]));
    assert.throws(() => matchEditRequests(sample(), [draft({ assists: 1 }), draft({ assists: 2 })]));
    for (const slot of [0, 3, 5, -1, 1.5]) {
        assert.throws(() => matchEditRequests(sample(), [draft({ assists: 1 }, slot)]));
    }
    assert.doesNotThrow(() => matchEditRequests(sample(), [
        draft({ assists: 1 }), draft({ assists: 2 }, 2),
    ]));
});

test('draft rejects missing or ambiguous player identity', () => {
    const missing = sample();
    missing.teams[0].players = [];
    assert.throws(() => matchEditRequests(missing, [draft({ assists: 1 })]));
    const ambiguous = sample();
    ambiguous.teams[0].players.push(ambiguous.teams[0].players[0]);
    assert.throws(() => matchEditRequests(ambiguous, [draft({ assists: 1 })]));
});
