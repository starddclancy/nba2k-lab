import type { GameLine, GameLog } from './gameLog.ts';

/** Bridge seasonGames: the season's schedule and box scores read from the game's own game table. */
export type SeasonGameRow = {
    id: number; date: string; slot: number; played: boolean; home: number; away: number; homeLabel: string; awayLabel: string;
    homePoints: number | null; awayPoints: number | null; homeQuarters: number[] | null; awayQuarters: number[] | null;
    /** Rows in `columns` order: player index, name, min, pm, pts, fgm, fga, tpm, tpa, ftm, fta, oreb, dreb, ast, stl, blk, tov, pf. */
    homeLines: (number | string)[][] | null; awayLines: (number | string)[][] | null;
    homeUid?: number; awayUid?: number; teamsResolved?: boolean;
};
export type SeasonGames = {
    format: 'nba2k27-season-games/1'; columns: string[]; games: SeasonGameRow[]; table?: string;
    writesEnabled?: boolean; undoDepth?: number; writeReason?: string | null;
    otherGames?: SeasonGameRow[]; tableCount?: number; leagueTeamCount?: number; unmappedGameCount?: number;
};
export type SchedulePreview = {
    token: string; gameId: number; beforeDate: string; afterDate: string; patchBytes: number; writesEnabled: boolean;
};

/** One player line with the fields only the game's table has (minutes, +/-, fouls). */
export type FullLine = GameLine & { minutes: number; plusMinus: number; fouls: number; index: number };

function line(row: (number | string)[]): FullLine {
    const n = (i: number) => Number(row[i]) || 0;
    return { index: n(0), name: String(row[1]), id: n(0), minutes: n(2), plusMinus: n(3), points: n(4), fgMade: n(5), fgAttempted: n(6),
        threeMade: n(7), threeAttempted: n(8), ftMade: n(9), ftAttempted: n(10), offensiveRebounds: n(11), defensiveRebounds: n(12),
        rebounds: n(11) + n(12), assists: n(13), steals: n(14), blocks: n(15), turnovers: n(16), fouls: n(17) };
}

/**
 * The played games as a GameLog, so every existing per-game view uses the game's own box scores.
 * Home is listed first; the date is the in-game date (not the capture time).
 */
export function seasonLog(data: SeasonGames | null): GameLog | null {
    if (!data) return null;
    const games = data.games.filter((game) => game.played && game.homeLines && game.awayLines)
        .sort((a, b) => a.date.localeCompare(b.date) || a.slot - b.slot || a.id - b.id).map((game) => ({
        id: `season:${game.id}`, pid: 0, startedUtc: `${game.date}T00:00:00Z`, updatedUtc: '', period: 4, clock: 0,
        teams: [
            { label: game.homeLabel, points: game.homePoints ?? 0, players: game.homeLines!.map(line) },
            { label: game.awayLabel, points: game.awayPoints ?? 0, players: game.awayLines!.map(line) },
        ],
    }));
    return { format: 'nba2k27-game-log/1', games };
}

export type TeamSchedule = {
    team: number; label: string; played: number; remaining: number; wins: number; losses: number;
    home: number; away: number; homeLeft: number; awayLeft: number; backToBacks: number; backToBacksLeft: number;
    /** Average win% of remaining opponents (current records), 0-100. */
    strengthLeft: number | null; strengthPlayed: number | null; nextGames: SeasonGameRow[]; lastGames: SeasonGameRow[];
};

const dayNumber = (date: string) => Math.round(Date.parse(`${date}T00:00:00Z`) / 86400000);

/** Per-team schedule statistics from the season table. */
export function scheduleStats(data: SeasonGames | null): TeamSchedule[] {
    if (!data) return [];
    const teams = new Map<number, string>();
    for (const game of data.games) { teams.set(game.home, game.homeLabel); teams.set(game.away, game.awayLabel); }
    const record = new Map<number, { w: number; l: number }>();
    for (const game of data.games) {
        if (!game.played || game.homePoints === null || game.awayPoints === null) continue;
        const homeWon = game.homePoints > game.awayPoints;
        for (const [team, won] of [[game.home, homeWon], [game.away, !homeWon]] as const) {
            const r = record.get(team) ?? { w: 0, l: 0 };
            if (won) r.w += 1; else r.l += 1;
            record.set(team, r);
        }
    }
    const pct = (team: number) => { const r = record.get(team); return r && r.w + r.l ? (r.w / (r.w + r.l)) * 100 : null; };
    const avg = (values: (number | null)[]) => { const v = values.filter((x): x is number => x !== null); return v.length ? Math.round((v.reduce((s, x) => s + x, 0) / v.length) * 10) / 10 : null; };
    return [...teams].map(([team, label]) => {
        const games = data.games.filter((game) => game.home === team || game.away === team).sort((a, b) => a.date.localeCompare(b.date) || a.slot - b.slot);
        const played = games.filter((game) => game.played);
        const left = games.filter((game) => !game.played);
        const opponent = (game: SeasonGameRow) => (game.home === team ? game.away : game.home);
        const b2b = (list: SeasonGameRow[]) => list.filter((game, i) => i > 0 && dayNumber(game.date) - dayNumber(list[i - 1].date) === 1).length;
        const r = record.get(team) ?? { w: 0, l: 0 };
        return {
            team, label, played: played.length, remaining: left.length, wins: r.w, losses: r.l,
            home: played.filter((game) => game.home === team).length, away: played.filter((game) => game.away === team).length,
            homeLeft: left.filter((game) => game.home === team).length, awayLeft: left.filter((game) => game.away === team).length,
            backToBacks: b2b(played), backToBacksLeft: b2b([...played.slice(-1), ...left]) ,
            strengthLeft: avg(left.map((game) => pct(opponent(game)))), strengthPlayed: avg(played.map((game) => pct(opponent(game)))),
            nextGames: left.slice(0, 10), lastGames: played.slice(-10).reverse(),
        };
    }).sort((a, b) => a.label.localeCompare(b.label));
}

/** The latest date with a played game (the game's "today" is the day after or the next scheduled day). */
export function currentDate(data: SeasonGames | null): string | null {
    const played = data?.games.filter((game) => game.played).map((game) => game.date) ?? [];
    return played.length ? played.reduce((a, b) => (a > b ? a : b)) : null;
}
