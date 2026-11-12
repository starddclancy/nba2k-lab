import assert from 'node:assert/strict';
import test from 'node:test';
import { seasonLog, scheduleStats } from '../src/seasonGames.ts';
import type { SeasonGameRow, SeasonGames } from '../src/seasonGames.ts';
import { playerGames } from '../src/gameLog.ts';
import { playerGameMetrics } from '../src/metrics.ts';
import { windowStats } from '../src/form.ts';

const game = (id: number, date: string, home = 1, away = 2, played = false): SeasonGameRow => ({
    id, date, slot: 1, home, away, homeLabel: `Team ${home}`, awayLabel: `Team ${away}`, played,
    homePoints: played ? 100 : null, awayPoints: played ? 90 : null,
    homeQuarters: played ? [25, 25, 25, 25] : null, awayQuarters: played ? [20, 20, 25, 25] : null,
    homeLines: played ? [] : null, awayLines: played ? [] : null,
});
const table = (games: SeasonGameRow[]): SeasonGames => ({ format: 'nba2k27-season-games/1', columns: [], games });

test('an available empty native table remains authoritative over old local records', () => {
    assert.deepEqual(seasonLog(table([])), { format: 'nba2k27-game-log/1', games: [] });
    assert.deepEqual(seasonLog(table([game(1, '2009-01-01')])), { format: 'nba2k27-game-log/1', games: [] });
    assert.equal(seasonLog(null), null);
});

test('native games sort by game date and slot and retain the full Iverson line', () => {
    const first = game(382, '2008-12-19', 1, 2, true);
    first.homeLines = [[10, 'Allen Iverson', 25, -10, 14, 4, 12, 0, 2, 6, 8, 1, 4, 4, 0, 0, 2, 2]];
    const earlier = game(370, '2008-12-17', 1, 2, true);
    earlier.homeLines = [[10, 'Allen Iverson', 28, 22, 28, 11, 19, 1, 2, 5, 5, 1, 4, 3, 3, 0, 4, 1]];
    const duplicateName = game(400, '2008-12-20', 1, 2, true);
    duplicateName.homeLines = [[11, 'Allen Iverson', 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]];
    const log = seasonLog(table([first, duplicateName, earlier]))!;
    const rows = playerGames(log, 'Allen Iverson', 10);
    assert.deepEqual(rows.map((r) => r.date), ['2008-12-17', '2008-12-19']);
    assert.equal(rows[1].points, 14);
    assert.equal((rows[1] as { minutes?: number }).minutes, 25);
    assert.equal((rows[1] as { plusMinus?: number }).plusMinus, -10);
    const score = playerGameMetrics.find((m) => m.key === 'gameScore')!;
    assert.equal(score.value(rows[1], 'perGame'), 8.3);
    assert.equal(score.value(rows[0], 'perGame'), 21.7);
    assert.equal(windowStats([rows[1]])!.gameScore, 8.3);
    assert.equal(playerGameMetrics.find((m) => m.key === 'pf')?.value(rows[1], 'perGame'), 2);
});

test('schedule counts a back-to-back across the played/unplayed year boundary', () => {
    const stats = scheduleStats(table([
        game(3, '2009-01-03', 2, 1), game(1, '2008-12-31', 1, 2, true),
        game(2, '2009-01-01', 1, 2), game(4, '2009-01-04', 1, 3),
        game(5, '2008-12-29', 2, 3, true),
    ]));
    const team = stats.find((s) => s.team === 1)!;
    assert.equal(team.played, 1);
    assert.equal(team.remaining, 3);
    assert.equal(team.homeLeft, 2);
    assert.equal(team.awayLeft, 1);
    assert.equal(team.backToBacksLeft, 2);
    assert.equal(team.wins, 1);
    assert.equal(team.strengthLeft, 33.3);
    assert.deepEqual(team.nextGames.map((g) => g.id), [2, 3, 4]);
});

test('other competitions cannot alter NBA player form or schedule strength', () => {
    const nba = game(1, '2008-12-17', 1, 2, true);
    nba.homeLines = [[10, 'Allen Iverson', 28, 22, 28, 11, 19, 1, 2, 5, 5, 1, 4, 3, 3, 0, 4, 1]];
    const next = game(2, '2008-12-19', 1, 2);
    const special = game(3, '2008-12-18', 2, 31, true);
    special.awayLines = [[10, 'Allen Iverson', 40, 20, 80, 30, 40, 10, 15, 10, 10, 0, 0, 0, 0, 0, 0, 0]];
    const baseline = table([nba, next]);
    const mixed = { ...baseline, otherGames: [special], tableCount: 3 };
    assert.deepEqual(seasonLog(mixed), seasonLog(baseline));
    assert.deepEqual(scheduleStats(mixed), scheduleStats(baseline));
    const rows = playerGames(seasonLog(mixed)!, 'Allen Iverson', 10);
    assert.equal(rows.length, 1);
    assert.equal(windowStats(rows)!.gameScore, 21.7);
    assert.deepEqual(scheduleStats(mixed).map((row) => row.team), [1, 2]);
});
