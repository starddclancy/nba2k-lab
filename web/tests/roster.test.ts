import assert from 'node:assert/strict';
import test from 'node:test';
import { belongsTo, comparePlayers, emptyFilters, matchesPlayer } from '../src/roster.ts';
import type { PlayerRow } from '../src/types.ts';

const guard: PlayerRow = {
    key: '1', index: 1, name: 'Guard', team: 'Team', category: 'team', teamIndices: [0, 2],
    summary: { position: 'PG', secondaryPosition: 'SG', birthYear: 1988, heightCm: 198.12, weightKg: 90.7, threePoint: 85,
        age: 19, overall: 80 },
};
const center: PlayerRow = {
    key: '2', index: 2, name: 'Center', team: '', category: 'freeAgent', teamIndices: [],
    summary: { position: 'C', secondaryPosition: null, birthYear: 1985, heightCm: 215.9, weightKg: 117.9, threePoint: 40,
        age: 22, overall: 85 },
};
const missing: PlayerRow = { key: '3', index: 3, name: 'Absent', team: '' };

test('empty filters keep rows with absent facts', () => {
    assert.equal(matchesPlayer(missing, emptyFilters), true);
});

test('position can include or exclude secondary position', () => {
    assert.equal(matchesPlayer(guard, { ...emptyFilters, position: 'SG' }), true);
    assert.equal(matchesPlayer(guard, { ...emptyFilters, position: 'SG', includeSecondary: false }), false);
    assert.equal(matchesPlayer(center, { ...emptyFilters, position: 'SG' }), false);
    assert.equal(matchesPlayer(missing, { ...emptyFilters, position: 'PG' }), false);
});

test('combined ranges include boundaries and exclude unknown values', () => {
    const filters = { ...emptyFilters, ageFrom: 19, ageTo: 19,
        heightFrom: 198.12, heightTo: 198.12, overallMin: 80 };
    assert.equal(matchesPlayer(guard, filters), true);
    assert.equal(matchesPlayer(center, filters), false);
    assert.equal(matchesPlayer(missing, filters), false);
    assert.equal(matchesPlayer(guard, { ...filters, overallMin: 81 }), false);
});

test('an inverted interval has no matches', () => {
    assert.equal(matchesPlayer(guard, { ...emptyFilters, ageFrom: 30, ageTo: 20 }), false);
    assert.equal(matchesPlayer(guard, { ...emptyFilters, heightFrom: 210, heightTo: 190 }), false);
});

test('null, nonfinite and missing numeric facts never become zero', () => {
    for (const value of [null, undefined, Number.NaN, Number.POSITIVE_INFINITY]) {
        const row = { ...guard, summary: { ...guard.summary!, heightCm: value } } as PlayerRow;
        assert.equal(matchesPlayer(row, { ...emptyFilters, heightFrom: 0 }), false);
    }
});

test('numeric sort is descending and unknown facts stay last', () => {
    const rows = [missing, guard, center];
    assert.deepEqual([...rows].sort((a, b) => comparePlayers(a, b, 'heightCm')).map((row) => row.index), [2, 1, 3]);
    assert.deepEqual([...rows].sort((a, b) => comparePlayers(a, b, 'overall')).map((row) => row.index), [2, 1, 3]);
    assert.deepEqual([...rows].sort((a, b) => comparePlayers(a, b, 'age')).map((row) => row.index), [1, 2, 3]);
    assert.deepEqual(rows.map((row) => row.index), [3, 1, 2]);
});

test('age and overall filters never fall back to birth year or three-point values', () => {
    const legacy = { ...guard, summary: { ...guard.summary!, age: undefined, overall: null } };
    assert.equal(matchesPlayer(legacy, { ...emptyFilters, ageFrom: 0 }), false);
    assert.equal(matchesPlayer(legacy, { ...emptyFilters, overallMin: 25 }), false);
});

test('equal facts sort deterministically and name sort includes unknown rows', () => {
    assert.equal(comparePlayers(guard, { ...guard, index: 5 }, 'heightCm'), -4);
    assert.deepEqual([guard, missing, center].sort((a, b) => comparePlayers(a, b, 'name'))
        .map((row) => row.index), [3, 2, 1]);
});

test('facts are combined with the actual team and free-agent membership', () => {
    assert.equal(belongsTo(guard, 'team:2') && matchesPlayer(guard, emptyFilters), true);
    assert.equal(belongsTo(guard, 'team:1'), false);
    assert.equal(belongsTo(center, 'freeAgent'), true);
    assert.equal(belongsTo(missing, 'freeAgent'), false);
});
