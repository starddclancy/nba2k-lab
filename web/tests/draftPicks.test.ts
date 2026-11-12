import assert from 'node:assert/strict';
import test from 'node:test';
import { detailLabel, pickLabel, tradablePicks } from '../src/draftPicks.ts';
import type { DraftPickRow } from '../src/types.ts';

const row = (patch: Partial<DraftPickRow>): DraftPickRow => ({
    index: 0, ownerIndex: 1, owner: 'Atlanta Hawks', originalIndex: 1, original: 'Atlanta Hawks',
    year: 2009, round: 1, order: 5, used: false, player: null, hasTerms: false, ...patch,
});

test('tradable picks are owned, unused and ordered by year, round, order', () => {
    const rows = [
        row({ index: 1, year: 2010, round: 1 }),
        row({ index: 2, year: 2009, round: 2 }),
        row({ index: 3, year: 2009, round: 1, order: 9 }),
        row({ index: 4, ownerIndex: 2 }),
        row({ index: 5, used: true, player: 'X' }),
    ];
    assert.deepEqual(tradablePicks(rows, 1).map((item) => item.index), [3, 2, 1]);
    assert.deepEqual(tradablePicks(rows, null), []);
});

test('pick labels mark acquired picks and undecoded terms', () => {
    assert.equal(pickLabel(row({})), '2009 年第 1 轮 · 顺位未定');
    const acquired = pickLabel(row({ originalIndex: 7, original: 'Utah Jazz', hasTerms: true, year: null }));
    assert.match(acquired, /年份未知/);
    assert.match(acquired, /（来自.+）/);
    assert.match(acquired, /附带条件/);
});

test('unused pick order does not establish a confirmed draft position', () => {
    for (const order of [1, 5, 30]) {
        assert.equal(pickLabel(row({ year: 2010, order })), '2010 年第 1 轮 · 顺位未定');
    }
});

test('used picks show the position within their round without claiming an overall pick', () => {
    assert.equal(pickLabel(row({ used: true, player: 'X', order: 1 })),
        '2009 年第 1 轮 · 轮内第 1 顺位');
    assert.equal(pickLabel(row({ used: true, player: 'X', round: 2, order: 1 })),
        '2009 年第 2 轮 · 轮内第 1 顺位');
    assert.equal(pickLabel(row({ used: true, player: null, round: 2, order: 30 })),
        '2009 年第 2 轮 · 轮内第 30 顺位');
});

test('invalid decoded order is never presented as a confirmed position', () => {
    for (const order of [0, -1, 1.5, NaN, Infinity]) {
        assert.equal(pickLabel(row({ used: true, order })), '2009 年第 1 轮 · 顺位未定');
    }
});

test('linked change lines are translated, unknown lines pass through', () => {
    assert.match(detailLabel('payroll Atlanta Hawks: 49138250 -> 51000000'), /薪资总额：\$49,138,250 → \$51,000,000/);
    assert.match(detailLabel('pick 70 (round 1): Utah Jazz -> New Orleans Hornets'), /^选秀权 #70（第 1 轮）：/);
    assert.equal(detailLabel('something else'), 'something else');
});
