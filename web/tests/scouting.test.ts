import assert from 'node:assert/strict';
import test from 'node:test';
import { percentile, standings, statsByIndex } from '../src/scouting.ts';
import type { CategoryScores } from '../src/archetype.ts';

const scores = (value: number): CategoryScores => ({
    finishing: value, shooting: value, playmaking: value, defense: value, rebounding: value, physical: value,
});

test('percentile counts strictly lower values and half the ties', () => {
    assert.equal(percentile([60, 70, 80, 90], 80), 63);
    assert.equal(percentile([60, 70, 80, 90], 95), 100);
    assert.equal(percentile([60, 70, 80, 90], 50), 0);
    assert.equal(percentile([], 50), 0);
});

test('standings compare against the league and the same position', () => {
    const pool = [
        { scores: scores(60), overall: 70, position: 'PG' },
        { scores: scores(70), overall: 75, position: 'PG' },
        { scores: scores(80), overall: 85, position: 'C' },
        { scores: null, overall: 60, position: 'PG' },
    ];
    const rows = standings({ scores: scores(70), overall: 75, position: 'PG' }, pool);
    const overall = rows.find((row) => row.category === 'overall')!;
    assert.equal(overall.league, 63, 'overalls 70, 75, 85, 60: two below, one tie');
    assert.equal(overall.position, 75, 'PG peers with scores: 70 and 75');
    const shooting = rows.find((row) => row.category === 'shooting')!;
    assert.equal(shooting.league, 50, 'players without scores are left out of category pools');
    assert.equal(shooting.position, 75);
});

test('rating table rows become per-player columns', () => {
    const fields = ['Attributes/Offense/3POINT', 'Contract/Salary/SALARYYEAR1', 'Contract/Contract Terms/YEARSLEFT', 'Attributes/Misc/POTENTIAL'];
    const map = statsByIndex({ fields, rows: [{ index: 4, values: [90, 2_600_850, 2, 77] }] });
    const row = map.get(4)!;
    assert.equal(row.salary, 2_600_850);
    assert.equal(row.yearsLeft, 2);
    assert.equal(row.potential, 77);
    assert.equal(row.scores, null, 'one attribute is not enough for category scores');
    assert.equal(statsByIndex(null).size, 0);
});
