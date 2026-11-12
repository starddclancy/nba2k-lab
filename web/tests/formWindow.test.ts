import assert from 'node:assert/strict';
import test from 'node:test';
import { verdict, windowStats } from '../src/form.ts';

const line = (points: number, fgMade: number, fgAttempted: number, threeMade = 2, threeAttempted = 5) =>
    ({ points, rebounds: 6, assists: 5, steals: 1, blocks: 0, turnovers: 2, fgMade, fgAttempted, threeMade, threeAttempted, ftMade: 4, ftAttempted: 5 });

test('window averages use summed attempts for rates', () => {
    const stats = windowStats([line(30, 11, 20), line(10, 4, 16)])!;
    assert.equal(stats.games, 2);
    assert.equal(stats.pts, 20);
    assert.equal(stats.fgPct, 41.7, '15/36, not the mean of 55% and 25%');
    assert.equal(stats.tpPct, 40);
    assert.equal(windowStats([]), null);
});

test('verdict names what changed and its direction', () => {
    const hot = verdict(windowStats([line(32, 12, 20), line(30, 11, 19)]), windowStats([line(18, 7, 18), line(16, 6, 18), line(20, 8, 19)]), '近 20 场');
    assert.equal(hot.tone, 'hot');
    assert.ok(hot.lines.some((text) => text.startsWith('得分上升')), hot.lines.join(' | '));
    const flat = verdict(windowStats([line(20, 8, 18)]), windowStats([line(20, 8, 18)]), '赛季');
    assert.equal(flat.tone, 'normal');
    assert.equal(verdict(null, null, '赛季').label, '数据不足');
});
