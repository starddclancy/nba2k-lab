import test from 'node:test';
import assert from 'node:assert/strict';
import { compactSeason, historySeason } from '../src/seasonYears.ts';
import { displayValue } from '../src/labels.ts';
import type { FieldCell } from '../src/types.ts';

test('compact statistics use the end year; NBA history uses the start year', () => {
    assert.equal(compactSeason(109), '2008-2009 赛季');
    assert.equal(compactSeason(100), '1999-2000 赛季');
    assert.equal(historySeason(217), '2008-2009 赛季');
    assert.equal(historySeason(208), '1999-2000 赛季');
});

test('missing or invalid year codes never become a fabricated season', () => {
    for (const value of [null, undefined, '', '109', false, -1, 109.5, 256, NaN]) {
        assert.equal(compactSeason(value), '年份未知');
        assert.equal(historySeason(value), '年份未知');
    }
    assert.equal(compactSeason(0), '年份未知');
});

test('read-only fields display decoded seasons without changing their stored values', () => {
    const cell = { key: 'SeasonStats/CURRENTYEARSTATID/Season Context/COMPACTYEAR',
        label: 'CompactYear', kind: 'number', value: 109, display: '109', options: [] } as FieldCell;
    assert.equal(displayValue(cell), '2008-2009 赛季');
    assert.equal(cell.value, 109);
    assert.equal(displayValue({ ...cell, key: 'SEASON', value: 217 }), '2008-2009 赛季');
    assert.equal(displayValue({ ...cell, key: 'Vitals/History/DRAFTEDYEAR', value: 1996 }), '1996');
});
