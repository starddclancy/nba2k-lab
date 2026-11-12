import assert from 'node:assert/strict';
import test from 'node:test';
import { archetypeShare, leaders, overallHistogram } from '../src/insights.ts';
import type { PlayerStats } from '../src/scouting.ts';

const rows = [
    { index: 1, name: 'A', overall: 92 },
    { index: 2, name: 'B', overall: 71 },
    { index: 3, name: 'C', overall: 70 },
    { index: 4, name: 'D', overall: 30 },
    { index: 5, name: 'E', overall: null },
];
const stat = (shooting: number, archetype: string): PlayerStats => ({
    scores: { finishing: 60, shooting, playmaking: 60, defense: 60, rebounding: 60, physical: 60 },
    archetype, potential: null, salary: null, yearsLeft: null,
});
const stats = new Map<number, PlayerStats>([[1, stat(90, '空间威胁')], [2, stat(80, '空间威胁')], [3, stat(80, '锁防者')]]);

test('overall histogram uses 5-point buckets and clamps low values', () => {
    const histogram = overallHistogram(rows);
    assert.equal(histogram.length, 12);
    assert.equal(histogram[0].count, 1, '30 falls into the 40-44 bucket');
    assert.equal(histogram.find((bucket) => bucket.from === 70)!.count, 2);
    assert.equal(histogram.find((bucket) => bucket.from === 90)!.count, 1);
    assert.equal(histogram.reduce((sum, bucket) => sum + bucket.count, 0), 4, 'null overall is skipped');
});

test('leaders and archetype share', () => {
    assert.deepEqual(leaders(rows, stats, 'shooting', 2).map((row) => [row.name, row.value]), [['A', 90], ['B', 80]]);
    assert.deepEqual(archetypeShare(rows, stats), [{ name: '空间威胁', count: 2, share: 67 }, { name: '锁防者', count: 1, share: 33 }]);
});
