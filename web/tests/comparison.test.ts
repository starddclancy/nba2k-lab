import assert from 'node:assert/strict';
import test from 'node:test';
import { alignSeasons, attributeRows, careerAverage } from '../src/comparison.ts';

test('attribute rows label known columns and mark a unique best', () => {
    const table = {
        fields: ['Attributes/Offense/3POINT', 'Attributes/Defense/STEAL', 'Contract/Salary/SALARYYEAR1'],
        rows: [{ index: 1, values: [90, 70, 5_000_000] }, { index: 2, values: [80, 70, 9_000_000] }],
    };
    const rows = attributeRows(table, [1, 2, 9]);
    assert.deepEqual(rows.map((row) => row.label), ['三分', '抢断'], 'salary is not an attribute');
    assert.deepEqual(rows[0].values, [90, 80, null], 'unknown player shows null');
    assert.equal(rows[0].best, 0);
    assert.equal(rows[1].best, null, 'a tie has no single best');
    assert.deepEqual(attributeRows(null, [1]), []);
});

test('seasons align on the union of years', () => {
    const a = [{ year: 2006, games: 60, points: 30 }, { year: 2008, games: 82, points: 21 }];
    const b = [{ year: 2007, games: 70, points: 18 }, { year: 2008, games: 80, points: 20 }];
    const aligned = alignSeasons([a, b], (row) => row.points);
    assert.deepEqual(aligned.years, [2006, 2007, 2008]);
    assert.deepEqual(aligned.series, [[30, null, 21], [null, 18, 20]]);
    assert.equal(careerAverage(a, (row) => row.points), 24.8);
    assert.equal(careerAverage([], (row: { games: number }) => row.games), null);
});
