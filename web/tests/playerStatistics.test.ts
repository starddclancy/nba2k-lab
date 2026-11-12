import assert from 'node:assert/strict';
import test from 'node:test';
import {
    average, cumulativeAwards, statValue, totalFromAverage,
} from '../src/playerStatistics.ts';

const field = (name: string, raw: number | string, group = 'Awards & Honors') => ({
    key: `SeasonStats/CURRENTYEARSTATID/${group}/${name}`,
    label: name, group, section: 'SeasonStats', value: raw, raw,
    display: String(raw), writable: true, kind: 'number' as const, options: [],
});

test('averages handle zero games and preserve stored totals when rounded for display', () => {
    assert.equal(average(100, 0), null);
    assert.equal(average(101, 3), 101 / 3);
    assert.equal(totalFromAverage(25.36, 34), 862);
    assert.equal(average(totalFromAverage(25.36, 34), 34)?.toFixed(2), '25.35');
    assert.throws(() => totalFromAverage(12, 0));
    assert.throws(() => totalFromAverage(Number.NaN, 30));
    assert.throws(() => totalFromAverage(-1, 30));
});

test('award flags use raw values and draft choices without treating No as true', () => {
    const mvp = field('MOSTVALUABLEPLAYER', 0);
    assert.equal(statValue(mvp), 0);
    assert.equal(statValue(mvp, { [mvp.key]: 'Yes' }), 1);
    assert.equal(statValue(mvp, { [mvp.key]: 'No' }), 0);
    assert.equal(statValue(mvp, { [mvp.key]: false }), 0);
});

test('cumulative awards deduplicate season IDs and count teams as selections, not summed codes', () => {
    const mvp = field('MOSTVALUABLEPLAYER', 1);
    const allNba = field('ALLNBATEAM', 3, 'Awards');
    const season = { selector: 'CURRENTYEARSTATID', id: 1, address: '', fields: [mvp, allNba] };
    const result = cumulativeAwards([season, { ...season, selector: 'STATSID1' }], {});
    assert.equal(result.find((item) => item.label === '常规赛 MVP')?.count, 1);
    assert.equal(result.find((item) => item.label === '最佳阵容编码 3')?.count, 1);
    assert.equal(cumulativeAwards([season], { [mvp.key]: 'No' })[0].count, 0);
});
