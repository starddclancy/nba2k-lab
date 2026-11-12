import assert from 'node:assert/strict';
import test from 'node:test';
import { playerSeasons } from '../src/seasonTrends.ts';
import { bestIndex, careerRow, playerMetrics, show, teamMetrics } from '../src/metrics.ts';
import { teamSeasons } from '../src/seasonTrends.ts';

const block = (selector: string, values: Record<string, number>) => ({
    selector, fields: Object.entries(values).map(([name, value]) => ({ key: `SeasonStats/${selector}/Group/${name}`, value })),
});
// Live Allen Iverson 2007-08 and 2003-04 lines (read 2026-11-08).
const seasons = playerSeasons([
    block('CURRENTYEARSTATID', { COMPACTYEAR: 108, GAMESPLAYED: 82, MINUTES: 2502, POINTS: 1727, '3POINTERSMADE': 50, '3POINTERSATTEMPTED': 185,
        FIELDGOALSMADE: 611, FIELDGOALSATTEMPTED: 1332, FREETHROWMADE: 455, FREETHROWATTEMPTED: 588, OFFENSIVEREBOUNDS: 81, DEFENSEREBOUNDS: 239, ASSISTS: 323 }),
    block('STATSID4', { COMPACTYEAR: 104, GAMESPLAYED: 82, MINUTES: 3100, POINTS: 2448, '3POINTERSMADE': 100, '3POINTERSATTEMPTED': 300,
        FIELDGOALSMADE: 900, FIELDGOALSATTEMPTED: 2000, FREETHROWMADE: 548, FREETHROWATTEMPTED: 700, OFFENSIVEREBOUNDS: 137, DEFENSEREBOUNDS: 431, ASSISTS: 355 }),
]);
const metric = (key: string) => playerMetrics.find((item) => item.key === key)!;

test('per-game values keep one decimal; totals and per 36 are available for every counting stat', () => {
    const last = seasons[1];
    assert.equal(metric('pts').value(last, 'perGame'), 21.1);
    assert.equal(show(metric('pts'), metric('pts').value(last, 'perGame')), '21.1');
    assert.equal(metric('tpm').value(last, 'perGame'), 0.6, '50 threes in 82 games');
    assert.equal(metric('tpa').value(last, 'perGame'), 2.3);
    assert.equal(metric('tpm').value(last, 'total'), 50);
    assert.equal(show(metric('tpm'), 50, 'total'), '50');
    assert.equal(metric('pts').value(last, 'per36'), 24.8);
    assert.equal(metric('tpPct').value(last, 'perGame'), 27);
    assert.equal(show(metric('tpPct'), 27), '27.0%');
    assert.equal(metric('twoPct').value(last, 'perGame'), 48.9, '(611-50)/(1332-185) = 561/1147');
    const counting = playerMetrics.filter((item) => item.counting).map((item) => item.label);
    for (const label of ['得分', '篮板', '助攻', '抢断', '盖帽', '失误', '三分命中', '三分出手', '罚球命中', '前场篮板']) assert.ok(counting.includes(label), label);
});

test('career row sums totals and recomputes rates', () => {
    const career = careerRow(seasons)!;
    assert.equal(career.games, 164);
    assert.equal(metric('pts').value(career, 'perGame'), 25.5, '(1727+2448)/164');
    assert.equal(metric('tpm').value(career, 'total'), 150);
    assert.equal(career.tsPct, 53.5, '4175 / (2 * (3332 + 0.44 * 1288))');
    assert.equal(careerRow([]), null);
});

test('team metrics include threes per game and keep decimals', () => {
    const [row] = teamSeasons([{ slot: 0, id: 825, COMPACTYEAR: 108, WINS: 59, LOSSES: 23, TOTALPOINTS: 8337, TOTALPOINTSALLOWED: 7493,
        THREEPOINTSHOTSMADE: 263, THREEPOINTSHOTSATTEMPTED: 840, POSSESSIONS: 8091, FIELDGOALSMADE: 3100, FIELDGOALSATTEMPTED: 6700, MINUTESPLAYED: 19780 }]);
    const team = (key: string) => teamMetrics.find((item) => item.key === key)!;
    assert.equal(team('tpm').value(row, 'perGame'), 3.2);
    assert.equal(team('pts').value(row, 'perGame'), 101.7);
    assert.equal(team('opp').value(row, 'perGame'), 91.4);
    assert.equal(bestIndex([1, 3, 2]), 1);
    assert.equal(bestIndex([1, 3, 2], true), 0);
    assert.equal(bestIndex([3, 3]), -1);
});
