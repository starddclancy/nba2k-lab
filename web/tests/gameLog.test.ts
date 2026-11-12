import assert from 'node:assert/strict';
import test from 'node:test';
import { emptyLog, gameAverages, loadLog, loggedPlayers, playerGames, recordSnapshot, saveLog, teamGames } from '../src/gameLog.ts';
import type { MatchSnapshot } from '../src/match.ts';

const row = (two: number, three: number, ft: number, extra: Record<string, number> = {}) => ({
    twoPointMade: two, twoPointAttempted: two * 2, threePointMade: three, threePointAttempted: three * 3, freeThrowMade: ft, freeThrowAttempted: ft,
    points: two * 2 + three * 3 + ft, offensiveRebounds: 1, defensiveRebounds: 4, assists: 5, steals: 1, blocks: 0, turnovers: 2, ...extra,
});
const snap = (captured: string, home: number, away: number, labels = ['Philadelphia 76ers', 'Boston Celtics']): MatchSnapshot => ({
    pid: 7396, processCreatedUtc: 'p', capturedUtc: captured, readToken: 't', period: 4, clockValue: 0, clockStableDuringRead: true,
    pauseStateVerified: false, editable: false, writes: 0, elapsedMs: 1, totalElapsedMs: 1, issues: [],
    teams: labels.map((label, side) => ({ side, label, abbreviation: '', logo: null, players: [
        { name: ({ 'Philadelphia 76ers': 'Allen Iverson', 'Boston Celtics': 'Paul Pierce', 'Miami Heat': 'Dwyane Wade' } as Record<string, string>)[label], id: side + 1, photoId: 0, onCourt: true, entity: side, stats: { total: row(side ? away : home, 1, 2), periods: [] } },
    ], stats: { total: { points: (side ? away : home) * 2 + 5 }, periods: [] } })) as unknown as MatchSnapshot['teams'],
});

test('live reads update one game until the score resets or the teams change', () => {
    let log = recordSnapshot(emptyLog(), snap('2026-11-08T01:00:00Z', 10, 8));
    log = recordSnapshot(log, snap('2026-11-08T01:10:00Z', 30, 25));
    assert.equal(log.games.length, 1, 'same game, score went up');
    assert.equal(log.games[0].teams[0].points, 65);
    log = recordSnapshot(log, snap('2026-11-08T02:00:00Z', 2, 1));
    assert.equal(log.games.length, 2, 'score went down: new game');
    log = recordSnapshot(log, snap('2026-11-08T03:00:00Z', 5, 9, ['Boston Celtics', 'Miami Heat']));
    assert.equal(log.games.length, 3, 'other teams: new game');

    const games = playerGames(log, 'Allen Iverson');
    assert.equal(games.length, 2);
    assert.equal(games[0].points, 30 * 2 + 3 + 2);
    assert.equal(games[0].result, '胜 65-55');
    assert.equal(games[0].rebounds, 5);
    assert.equal(gameAverages(games).games, 2);
    assert.deepEqual(teamGames(log, 'Boston Celtics').map((game) => game.margin), [-10, -2, 0 - 8]);
    assert.equal(loggedPlayers(log)[0].name, 'Paul Pierce', 'Pierce appears in all three games');
});

test('log round-trips through storage and ignores foreign data', () => {
    const store = new Map<string, string>();
    const storage = { getItem: (key: string) => store.get(key) ?? null, setItem: (key: string, value: string) => void store.set(key, value) };
    assert.deepEqual(loadLog(storage), emptyLog());
    const log = recordSnapshot(emptyLog(), snap('2026-11-08T01:00:00Z', 10, 8));
    saveLog(log, storage);
    assert.deepEqual(loadLog(storage), log);
    store.set('nba2k27.gameLog.v1', '{"format":"other"}');
    assert.deepEqual(loadLog(storage), emptyLog());
});
