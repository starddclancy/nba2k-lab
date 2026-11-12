import assert from 'node:assert/strict';
import test from 'node:test';
import { leagueSeasons, leagueTeams, minimumGames, rank } from '../src/leaders.ts';
import { playerMetrics, teamMetrics } from '../src/metrics.ts';

const fields = ['COMPACTYEAR', 'GAMESPLAYED', 'MINUTES', 'POINTS', '3POINTERSMADE', '3POINTERSATTEMPTED', 'TURNOVERS'];
const table = { fields, rows: [
    { index: 1, values: [108, 75, 3000, 2363, 120, 300, 250] }, // 31.5 ppg
    { index: 2, values: [108, 82, 3100, 2493, 207, 520, 180] }, // most threes
    { index: 3, values: [108, 4, 40, 30, 8, 8, 1] },             // 100% from three in 4 games
    { index: 4, values: [108, 82, 2000, 1640, 120, 300, 180] },  // ties index 1 in threes made
    { index: 5, values: [null, null, null, null, null, null, null] },
] };
const metric = (key: string) => playerMetrics.find((item) => item.key === key)!;

test('league rows become seasons and rank by any metric', () => {
    const seasons = leagueSeasons(table);
    assert.equal(seasons.size, 4, 'empty row skipped');
    const ids = [...seasons.keys()];
    const all = () => true;
    const ppg = rank(ids, (id) => seasons.get(id) ?? null, metric('pts'), 'perGame', all);
    assert.equal(ppg[0].item, 1);
    assert.equal(ppg[0].value, 31.5);
    const threes = rank(ids, (id) => seasons.get(id) ?? null, metric('tpm'), 'total', all);
    assert.deepEqual(threes.map((entry) => [entry.item, entry.rank]), [[2, 1], [1, 2], [4, 2], [3, 4]], 'ties share a rank');
    const min = minimumGames([...seasons.values()].map((season) => season.games));
    assert.equal(min, 57);
    const pct = rank(ids, (id) => seasons.get(id) ?? null, metric('tpPct'), 'perGame', (id) => (seasons.get(id)?.games ?? 0) >= min);
    assert.equal(pct[0].item, 1, 'the 4-game shooter does not qualify');
    const tov = rank(ids, (id) => seasons.get(id) ?? null, metric('tov'), 'perGame', all);
    assert.equal(tov[0].item, 3, 'turnovers rank ascending');
});

test('team rows rank threes per game', () => {
    const teams = leagueTeams([
        { index: 0, label: 'A', COMPACTYEAR: 108, WINS: 59, LOSSES: 23, TOTALPOINTS: 8337, THREEPOINTSHOTSMADE: 263, THREEPOINTSHOTSATTEMPTED: 840 },
        { index: 1, label: 'B', COMPACTYEAR: 108, WINS: 30, LOSSES: 52, TOTALPOINTS: 8000, THREEPOINTSHOTSMADE: 700, THREEPOINTSHOTSATTEMPTED: 1900 },
    ]);
    const tpm = teamMetrics.find((item) => item.key === 'tpm')!;
    const ranked = rank(teams, (team) => team.season, tpm, 'perGame', () => true);
    assert.equal(ranked[0].item.label, 'B');
    assert.equal(ranked[0].value, 8.5);
});

test('current leaderboards reject last season for both players and teams', () => {
    const seasons = leagueSeasons({ ...table, seasonEndYear: 2009, rows: [
        ...table.rows, { index: 8, values: [109, 20, 600, 500, 30, 80, 25] },
    ] });
    assert.deepEqual([...seasons.keys()], [8]);
    const teams = leagueTeams([
        { index: 0, label: 'Last season', COMPACTYEAR: 108, WINS: 59, LOSSES: 23 },
        { index: 92, label: 'This season', COMPACTYEAR: 109, WINS: 10, LOSSES: 8 },
    ], 2009);
    assert.deepEqual(teams.map((team) => team.index), [92]);
});
