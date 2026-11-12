import assert from 'node:assert/strict';
import test from 'node:test';
import { emptyTracker, gameCount, lastGames, recentGames, recordLeague, trackedGames } from '../src/seasonTracker.ts';
import { windowStats } from '../src/form.ts';

const fields = ['COMPACTYEAR', 'GAMESPLAYED', 'MINUTES', 'POINTS', 'FIELDGOALSMADE', 'FIELDGOALSATTEMPTED', '3POINTERSMADE', '3POINTERSATTEMPTED',
    'FREETHROWMADE', 'FREETHROWATTEMPTED', 'OFFENSIVEREBOUNDS', 'DEFENSEREBOUNDS', 'ASSISTS', 'STEALS', 'BLOCKS', 'TURNOVERS'];
const snap = (gp: number, pts: number, fgm: number, fga: number, tpm = 0, year = 109) =>
    ({ fields, rows: [{ index: 7, values: [year, gp, gp * 36, pts, fgm, fga, tpm, tpm * 2, 4 * gp, 5 * gp, gp, 5 * gp, 6 * gp, gp, 0, 2 * gp] }] });
const names = new Map([[7, 'Carmelo Anthony']]);

test('consecutive season snapshots become per-game box scores', () => {
    let state = emptyTracker();
    state = recordLeague(state, snap(10, 300, 110, 220, 10), names, '2026-11-01T10:00:00Z').state;
    assert.equal(trackedGames(state, 7, 'Carmelo Anthony').length, 0, 'first snapshot is only the baseline');
    const one = recordLeague(state, snap(11, 338, 124, 245, 13), names, '2026-11-02T10:00:00Z');
    assert.equal(one.added, 1);
    state = one.state;
    const unchanged = recordLeague(state, snap(11, 338, 124, 245, 13), names, '2026-11-02T11:00:00Z');
    assert.equal(unchanged.added, 0, 'no new game, nothing recorded');
    state = recordLeague(state, snap(13, 380, 140, 280, 15), names, '2026-11-04T10:00:00Z').state;
    const games = trackedGames(state, 7, 'Carmelo Anthony');
    assert.equal(games.length, 2);
    assert.equal(games[0].points, 38);
    assert.equal(games[0].fgMade, 14);
    assert.equal(games[0].threeMade, 3);
    assert.equal(games[0].minutes, 36);
    assert.equal(games[1].games, 2, 'two games between snapshots stay one combined entry');
    assert.equal(gameCount(games), 3);
    const perGame = recentGames(games, []);
    assert.equal(perGame[1].points, 21, 'two-game segment shown as its per-game average');
    const stats = windowStats(perGame)!;
    assert.equal(stats.games, 3);
    assert.equal(stats.pts, 26.7, '(38 + 42) / 3 games, not / 2 rows');
    assert.equal(lastGames(games, 1).length, 1);
    assert.equal(lastGames(games, 3).length, 2);
});

test('a new season or a reload resets the baseline instead of recording negatives', () => {
    let state = recordLeague(emptyTracker(), snap(50, 1500, 500, 1000), names, 'a').state;
    state = recordLeague(state, snap(2, 60, 20, 40, 0, 110), names, 'b').state;
    assert.equal(trackedGames(state, 7, 'Carmelo Anthony').length, 0);
    state = recordLeague(state, snap(3, 95, 33, 60, 0, 110), names, 'c').state;
    assert.equal(trackedGames(state, 7, 'Carmelo Anthony')[0].points, 35);
});
