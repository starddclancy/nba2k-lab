import type { MatchRow, MatchSnapshot } from './match.ts';

/**
 * Local fallback box scores captured from live reads. Native season games take precedence
 * in analytics; these entries cover sessions where the native table is unavailable.
 */
export type GameLine = { name: string; id: number; points: number; rebounds: number; assists: number; steals: number;
    blocks: number; turnovers: number; fgMade: number; fgAttempted: number; threeMade: number; threeAttempted: number;
    // Added later; games recorded before have no value.
    ftMade?: number; ftAttempted?: number; offensiveRebounds?: number; defensiveRebounds?: number;
    minutes?: number; plusMinus?: number; fouls?: number };
export type GameTeam = { label: string; points: number; players: GameLine[] };
export type GameEntry = { id: string; pid: number; startedUtc: string; updatedUtc: string; period: number; clock: number; teams: GameTeam[] };
export type GameLog = { format: 'nba2k27-game-log/1'; games: GameEntry[] };

export const emptyLog = (): GameLog => ({ format: 'nba2k27-game-log/1', games: [] });
const storageKey = 'nba2k27.gameLog.v1';
const maxGames = 300;

const n = (row: MatchRow, key: string) => (Number.isFinite(row[key]) ? row[key] : 0);
function line(name: string, id: number, row: MatchRow): GameLine {
    return { name, id, points: n(row, 'points'), rebounds: n(row, 'offensiveRebounds') + n(row, 'defensiveRebounds'),
        assists: n(row, 'assists'), steals: n(row, 'steals'), blocks: n(row, 'blocks'), turnovers: n(row, 'turnovers'),
        fgMade: n(row, 'twoPointMade') + n(row, 'threePointMade'), fgAttempted: n(row, 'twoPointAttempted') + n(row, 'threePointAttempted'),
        threeMade: n(row, 'threePointMade'), threeAttempted: n(row, 'threePointAttempted'),
        ftMade: n(row, 'freeThrowMade'), ftAttempted: n(row, 'freeThrowAttempted'),
        offensiveRebounds: n(row, 'offensiveRebounds'), defensiveRebounds: n(row, 'defensiveRebounds') };
}
const total = (entry: Pick<GameEntry, 'teams'>) => entry.teams.reduce((sum, team) => sum + team.points, 0);

/** Fold one live snapshot into the log; returns a new log (the input is not changed). */
export function recordSnapshot(log: GameLog, snapshot: MatchSnapshot): GameLog {
    const teams: GameTeam[] = snapshot.teams.map((team) => ({
        label: team.label, points: n(team.stats.total, 'points'),
        players: team.players.map((player) => line(player.name, player.id, player.stats.total)),
    }));
    if (teams.length !== 2) return log;
    const games = [...log.games];
    const last = games[games.length - 1];
    const sameTeams = last && last.pid === snapshot.pid && last.teams.map((t) => t.label).join('|') === teams.map((t) => t.label).join('|');
    const next = { teams, period: snapshot.period, clock: snapshot.clockValue, updatedUtc: snapshot.capturedUtc };
    if (sameTeams && total({ teams }) >= total(last)) {
        games[games.length - 1] = { ...last, ...next };
    } else {
        games.push({ id: `${snapshot.pid}:${snapshot.capturedUtc}`, pid: snapshot.pid, startedUtc: snapshot.capturedUtc, ...next });
    }
    return { ...log, games: games.slice(-maxGames) };
}

export function loadLog(storage: Pick<Storage, 'getItem'> = localStorage): GameLog {
    try {
        const value = JSON.parse(storage.getItem(storageKey) ?? 'null');
        return value?.format === 'nba2k27-game-log/1' && Array.isArray(value.games) ? value : emptyLog();
    } catch {
        return emptyLog();
    }
}
export function saveLog(log: GameLog, storage: Pick<Storage, 'setItem'> = localStorage) {
    storage.setItem(storageKey, JSON.stringify(log));
}

export type PlayerGame = GameLine & { game: string; date: string; team: string; opponent: string; result: string; fgPct: number | null };

/** One row per recorded game the player appeared in, oldest first. */
export function playerGames(log: GameLog, name: string, index?: number): PlayerGame[] {
    const rows: PlayerGame[] = [];
    for (const game of log.games) {
        game.teams.forEach((team, side) => {
            const player = team.players.find((item) => index === undefined ? item.name === name : item.id === index);
            if (!player) return;
            const other = game.teams[1 - side];
            rows.push({ ...player, game: game.id, date: game.startedUtc.slice(0, 10), team: team.label, opponent: other.label,
                result: `${team.points > other.points ? '胜' : team.points < other.points ? '负' : '平'} ${team.points}-${other.points}`,
                fgPct: player.fgAttempted ? Math.round((player.fgMade / player.fgAttempted) * 1000) / 10 : null });
        });
    }
    return rows;
}

export type TeamGame = { game: string; date: string; opponent: string; points: number; allowed: number; margin: number; won: boolean;
    home?: boolean;
    /** Team box score summed from the recorded player lines (same shape as a player line). */
    box: Omit<GameLine, 'name' | 'id'>; oppBox: Omit<GameLine, 'name' | 'id'> };

/** Sum player lines into one team line. */
export function teamBox(players: GameLine[]): Omit<GameLine, 'name' | 'id'> {
    const sum = (pick: (line: GameLine) => number | undefined) => players.reduce((total, line) => total + (pick(line) ?? 0), 0);
    return { points: sum((l) => l.points), rebounds: sum((l) => l.rebounds), assists: sum((l) => l.assists), steals: sum((l) => l.steals),
        blocks: sum((l) => l.blocks), turnovers: sum((l) => l.turnovers), fgMade: sum((l) => l.fgMade), fgAttempted: sum((l) => l.fgAttempted),
        threeMade: sum((l) => l.threeMade), threeAttempted: sum((l) => l.threeAttempted), ftMade: sum((l) => l.ftMade), ftAttempted: sum((l) => l.ftAttempted),
        offensiveRebounds: sum((l) => l.offensiveRebounds), defensiveRebounds: sum((l) => l.defensiveRebounds),
        fouls: sum((l) => l.fouls) };
}
export function teamGames(log: GameLog, label: string): TeamGame[] {
    const rows: TeamGame[] = [];
    for (const game of log.games) {
        game.teams.forEach((team, side) => {
            if (team.label !== label) return;
            const other = game.teams[1 - side];
            rows.push({ game: game.id, date: game.startedUtc.slice(0, 10), opponent: other.label, points: team.points,
                allowed: other.points, margin: team.points - other.points, won: team.points > other.points, home: side === 0,
                box: teamBox(team.players), oppBox: teamBox(other.players) });
        });
    }
    return rows;
}

/** Per-game averages over recorded games. */
export function gameAverages(rows: GameLine[]) {
    const avg = (pick: (row: GameLine) => number) => rows.length ? Math.round((rows.reduce((s, r) => s + pick(r), 0) / rows.length) * 10) / 10 : null;
    const made = rows.reduce((s, r) => s + r.fgMade, 0);
    const attempted = rows.reduce((s, r) => s + r.fgAttempted, 0);
    return { games: rows.length, points: avg((r) => r.points), rebounds: avg((r) => r.rebounds), assists: avg((r) => r.assists),
        steals: avg((r) => r.steals), blocks: avg((r) => r.blocks), fgPct: attempted ? Math.round((made / attempted) * 1000) / 10 : null };
}

/** Names of every player in the log, most games first. */
export function loggedPlayers(log: GameLog): { name: string; games: number }[] {
    const counts = new Map<string, number>();
    for (const game of log.games) for (const team of game.teams) for (const player of team.players) counts.set(player.name, (counts.get(player.name) ?? 0) + 1);
    return [...counts].map(([name, games]) => ({ name, games })).sort((a, b) => b.games - a.games || a.name.localeCompare(b.name));
}
export const loggedTeams = (log: GameLog) => [...new Set(log.games.flatMap((game) => game.teams.map((team) => team.label)))].sort();
