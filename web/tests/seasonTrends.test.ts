import assert from 'node:assert/strict';
import test from 'node:test';
import { axis, playerSeasons, seasonLabel, teamSeasons } from '../src/seasonTrends.ts';

const block = (selector: string, values: Record<string, number>) => ({
    selector,
    fields: Object.entries(values).map(([name, value]) => ({ key: `SeasonStats/${selector}/Group/${name}`, value })),
});

test('player seasons follow the live Iverson records (2026-11-08 read)', () => {
    // CURRENTYEARSTATID and STATSID4 values as read from the game; STATSID28 is an aggregate (year 0).
    const seasons = playerSeasons([
        block('CURRENTYEARSTATID', { COMPACTYEAR: 108, GAMESPLAYED: 82, POINTS: 1727, OFFENSIVEREBOUNDS: 81, DEFENSEREBOUNDS: 239,
            ASSISTS: 323, FIELDGOALSMADE: 611, FIELDGOALSATTEMPTED: 1332, '3POINTERSMADE': 50, '3POINTERSATTEMPTED': 185,
            FREETHROWMADE: 455, FREETHROWATTEMPTED: 588, MINUTES: 2502, OVERALL: 90 }),
        block('STATSID4', { COMPACTYEAR: 104, GAMESPLAYED: 82, POINTS: 2448, OFFENSIVEREBOUNDS: 137, DEFENSEREBOUNDS: 431, ASSISTS: 355, OVERALL: 95 }),
        block('STATSID28', { COMPACTYEAR: 0, GAMESPLAYED: 6, POINTS: 184 }),
    ]);
    assert.deepEqual(seasons.map((season) => season.year), [2004, 2008]);
    const last = seasons[1];
    assert.equal(last.points, 21.1);
    assert.equal(last.rebounds, 3.9);
    assert.equal(last.assists, 3.9);
    assert.equal(last.fgPct, 45.9);
    assert.equal(last.threePct, 27);
    assert.equal(last.overall, 90);
    assert.equal(seasons[0].points, 29.9);
    assert.equal(seasonLabel(2008), '2007-08');
});

test('team seasons compute win% and per-game margin, newest kept per year', () => {
    const rows = teamSeasons([
        { slot: 0, id: 1, COMPACTYEAR: 108, WINS: 50, LOSSES: 32, TOTALPOINTS: 8200, TOTALPOINTSALLOWED: 7790 },
        { slot: 1, id: 2, COMPACTYEAR: 107, WINS: 35, LOSSES: 47, TOTALPOINTS: 7900, TOTALPOINTSALLOWED: 8100, POSSESSIONS: 7626 },
        { slot: 2, id: 3, COMPACTYEAR: 0, WINS: 0, LOSSES: 0 },
    ]);
    assert.deepEqual(rows.map((row) => row.year), [2007, 2008]);
    assert.equal(rows[1].winPct, 61);
    assert.equal(rows[1].margin, 5);
    assert.equal(rows[1].current, true);
    assert.equal(rows[0].pace, 93);
});

test('team seasons without points allowed (live Spurs 2001-02: 58-24, 7932 pts, allowed 0) show no margin', () => {
    const [row] = teamSeasons([{ slot: 6, id: 491, COMPACTYEAR: 102, WINS: 58, LOSSES: 24, TOTALPOINTS: 7932, TOTALPOINTSALLOWED: 0, POSSESSIONS: 0 }]);
    assert.equal(row.pointsFor, 96.7);
    assert.equal(row.pointsAgainst, null);
    assert.equal(row.margin, null);
    assert.equal(row.pace, null);
});

test('axis picks round ticks around the data', () => {
    const scale = axis([21.1, 29.9, 33.0]);
    assert.ok(scale.min <= 21.1 && scale.max >= 33);
    assert.ok(scale.ticks.length >= 3 && scale.ticks.length <= 7);
    assert.deepEqual(axis([]), { min: 0, max: 1, ticks: [0, 1] });
});

test('advanced player metrics from the live Iverson 2007-08 record', () => {
    const [season] = playerSeasons([block('CURRENTYEARSTATID', { COMPACTYEAR: 108, GAMESPLAYED: 82, GAMESSTARTED: 82, MINUTES: 2502,
        POINTS: 1727, FIELDGOALSMADE: 611, FIELDGOALSATTEMPTED: 1332, '3POINTERSMADE': 50, '3POINTERSATTEMPTED': 185,
        FREETHROWMADE: 455, FREETHROWATTEMPTED: 588, OFFENSIVEREBOUNDS: 81, DEFENSEREBOUNDS: 239, ASSISTS: 323, STEALS: 134,
        BLOCKS: 1, TURNOVERS: 199, FOULS: 187, 'BOX+-': -194, POSSESSIONS: 5265, TOTALPOINTSALLOWED: 947,
        DEFENSIVEFIELDGOALSMADE: 447, DEFENSIVEFIELDGOALSATTEMPTED: 996, DOUBLEDOUBLES: 1, FORTYPOINTGAMES: 2, ALLNBATEAM: 0 })]);
    assert.equal(season.tsPct, 54.3); // 1727 / (2 * (1332 + 0.44 * 588))
    assert.equal(season.efgPct, 47.7); // (611 + 0.5 * 50) / 1332 = 47.75%
    assert.equal(season.astTo, 1.62);
    assert.equal(season.per36, 24.8);
    assert.equal(season.boxPlusMinus, -2.4);
    assert.equal(season.oppFgPct, 44.9);
    assert.equal(season.defRating, 18);
    assert.equal(season.fortyPoint, 2);
    assert.deepEqual(season.honors, []);
    assert.equal(season.gameScore, 14.6); // Hollinger Game Score total 1201.2 / 82
});

test('advanced team metrics from the live Spurs 2007-08 record', () => {
    const [row] = teamSeasons([{ slot: 0, id: 825, COMPACTYEAR: 108, WINS: 59, LOSSES: 23, TOTALPOINTS: 8337, TOTALPOINTSALLOWED: 7493,
        THREEPOINTSHOTSMADE: 263, THREEPOINTSHOTSATTEMPTED: 840, POSSESSIONS: 8091, FIELDGOALSMADE: 3100, FIELDGOALSATTEMPTED: 6700 }]);
    assert.equal(row.offRating, 103);
    assert.equal(row.defRating, 92.6);
    assert.equal(row.netRating, 10.4);
    assert.equal(row.pace, 98.7);
    assert.equal(row.paint, null, 'missing inputs stay empty');
});