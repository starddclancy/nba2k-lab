/**
 * Per-game player stats for every player, rebuilt from the game's own season totals. The game
 * keeps only the last five Game Scores per player, but it updates the current-season totals after
 * every game. The editor snapshots those totals (bridge leagueSeason, read-only) and stores the
 * difference between consecutive snapshots: one more game played gives that game's exact box
 * score; several more give the combined line of those games (shown as one entry with its count).
 */
export type Totals = Record<string, number>;
export type Segment = {
    /** Capture time (ISO) of the snapshot that closed this segment. */
    at: string;
    year: number;
    /** Games covered: 1 for a single game. */
    games: number;
    totals: Totals;
};
type Entry = { name: string; year: number; last: Totals; segments: Segment[] };
export type TrackerState = { format: 'nba2k27-season-tracker/1'; players: Record<string, Entry> };

const storageKey = 'nba2k27.seasonTracker.v1';
const maxSegments = 120;
const GP = 'GAMESPLAYED';
const kept = ['POINTS', 'FIELDGOALSMADE', 'FIELDGOALSATTEMPTED', '3POINTERSMADE', '3POINTERSATTEMPTED', 'FREETHROWMADE', 'FREETHROWATTEMPTED',
    'OFFENSIVEREBOUNDS', 'DEFENSEREBOUNDS', 'ASSISTS', 'STEALS', 'BLOCKS', 'TURNOVERS', 'FOULS', 'MINUTES', 'GAMESPLAYED', 'GAMESSTARTED', 'BOX+-'];

export const emptyTracker = (): TrackerState => ({ format: 'nba2k27-season-tracker/1', players: {} });
const playerKey = (index: number, name: string) => `${index}:${name}`;

/**
 * Fold one league snapshot in. Rows whose games-played went down or whose season changed start a
 * new baseline (new season, roster reload); unchanged rows add nothing. Returns a new state.
 */
export function recordLeague(state: TrackerState, table: { fields: string[]; rows: { index: number; values: (number | null)[] }[] },
    names: Map<number, string>, at: string): { state: TrackerState; added: number } {
    const players = { ...state.players };
    const column = new Map(table.fields.map((name, order) => [name, order]));
    let added = 0;
    for (const row of table.rows) {
        const name = names.get(row.index);
        if (!name) continue;
        const totals: Totals = {};
        for (const field of kept) {
            const value = row.values[column.get(field) ?? -1];
            if (typeof value === 'number' && Number.isFinite(value)) totals[field] = value;
        }
        const year = row.values[column.get('COMPACTYEAR') ?? -1] ?? 0;
        if (totals[GP] === undefined) continue;
        const key = playerKey(row.index, name);
        const previous = players[key];
        if (!previous || previous.year !== year || totals[GP] < (previous.last[GP] ?? 0)) {
            players[key] = { name, year: year as number, last: totals, segments: previous && previous.year === year ? previous.segments : [] };
            continue;
        }
        const games = totals[GP] - (previous.last[GP] ?? 0);
        if (games <= 0) continue;
        const delta: Totals = {};
        for (const field of kept) delta[field] = (totals[field] ?? 0) - (previous.last[field] ?? 0);
        players[key] = { ...previous, last: totals, segments: [...previous.segments, { at, year: year as number, games, totals: delta }].slice(-maxSegments) };
        added += 1;
    }
    return { state: { ...state, players }, added };
}

export function loadTracker(storage: Pick<Storage, 'getItem'> = localStorage): TrackerState {
    if (typeof window !== 'undefined' && import.meta.env?.DEV && new URLSearchParams(window.location.search).has('preview')) {
        return previewTracker(2, 'Sample Forward');
    }
    try {
        const value = JSON.parse(storage.getItem(storageKey) ?? 'null');
        return value?.format === 'nba2k27-season-tracker/1' && value.players ? value : emptyTracker();
    } catch {
        return emptyTracker();
    }
}
export function saveTracker(state: TrackerState, storage: Pick<Storage, 'setItem'> = localStorage) {
    storage.setItem(storageKey, JSON.stringify(state));
}

/** A tracked segment as a box-score line (the shape the game-log analysis uses), with its game count. */
export type TrackedGame = {
    game: string; date: string; team: string; opponent: string; result: string; fgPct: number | null; games: number; minutes: number;
    name: string; id: number; points: number; rebounds: number; assists: number; steals: number; blocks: number; turnovers: number;
    fgMade: number; fgAttempted: number; threeMade: number; threeAttempted: number; ftMade: number; ftAttempted: number;
    offensiveRebounds: number; defensiveRebounds: number; plusMinus: number;
};

export function trackedGames(state: TrackerState, index: number, name: string): TrackedGame[] {
    const entry = state.players[playerKey(index, name)];
    if (!entry) return [];
    return entry.segments.map((segment, order) => {
        const t = (key: string) => segment.totals[key] ?? 0;
        return {
            game: `${segment.at}#${order}`, date: segment.at.slice(0, 10), team: '', opponent: '', result: segment.games > 1 ? `${segment.games} 场合计` : '',
            fgPct: t('FIELDGOALSATTEMPTED') ? Math.round((t('FIELDGOALSMADE') / t('FIELDGOALSATTEMPTED')) * 1000) / 10 : null,
            games: segment.games, minutes: t('MINUTES'), name, id: index, points: t('POINTS'),
            rebounds: t('OFFENSIVEREBOUNDS') + t('DEFENSEREBOUNDS'), assists: t('ASSISTS'), steals: t('STEALS'), blocks: t('BLOCKS'),
            turnovers: t('TURNOVERS'), fgMade: t('FIELDGOALSMADE'), fgAttempted: t('FIELDGOALSATTEMPTED'), threeMade: t('3POINTERSMADE'),
            threeAttempted: t('3POINTERSATTEMPTED'), ftMade: t('FREETHROWMADE'), ftAttempted: t('FREETHROWATTEMPTED'),
            offensiveRebounds: t('OFFENSIVEREBOUNDS'), defensiveRebounds: t('DEFENSEREBOUNDS'), plusMinus: t('BOX+-'),
        };
    });
}

/** Number of tracked games (summing multi-game segments). */
export const gameCount = (rows: { games?: number }[]) => rows.reduce((sum, row) => sum + (row.games ?? 1), 0);

/** The most recent rows that cover at least `count` games (a multi-game segment is kept whole). */
export function lastGames<T extends { games?: number }>(rows: T[], count: number): T[] {
    const result: T[] = [];
    let covered = 0;
    for (let i = rows.length - 1; i >= 0 && covered < count; i--) {
        result.unshift(rows[i]);
        covered += rows[i].games ?? 1;
    }
    return result;
}

/**
 * Take one league snapshot now (bridge leagueSeason + player names) and store it. Read-only for
 * the game. Returns how many players gained new games.
 */
export async function snapshotLeague(read: () => Promise<{ fields: string[]; rows: { index: number; values: (number | null)[] }[] }>,
    players: () => Promise<{ index: number; name: string }[]>): Promise<number> {
    const [table, rows] = await Promise.all([read(), players()]);
    const { state, added } = recordLeague(loadTracker(), table, new Map(rows.map((row) => [row.index, row.name])), new Date().toISOString());
    saveTracker(state);
    return added;
}

/**
 * One list of a player's recent games: tracked season-total segments (every game the game
 * simulated or played, exact) plus live-match log entries that are not already covered (same
 * date and points). Values of multi-game segments are divided down to per-game numbers so charts
 * and tables read per game; `games` keeps how many games the row stands for.
 */
export function recentGames<T extends { date: string; points: number }>(tracked: TrackedGame[], logged: T[]): (TrackedGame | (T & { games: number }))[] {
    const perGame = tracked.map((row) => {
        if (row.games <= 1) return row;
        const n = row.games;
        const scale = (value: number) => Math.round((value / n) * 10) / 10;
        return { ...row, points: scale(row.points), rebounds: scale(row.rebounds), assists: scale(row.assists), steals: scale(row.steals),
            blocks: scale(row.blocks), turnovers: scale(row.turnovers), fgMade: scale(row.fgMade), fgAttempted: scale(row.fgAttempted),
            threeMade: scale(row.threeMade), threeAttempted: scale(row.threeAttempted), ftMade: scale(row.ftMade), ftAttempted: scale(row.ftAttempted),
            offensiveRebounds: scale(row.offensiveRebounds), defensiveRebounds: scale(row.defensiveRebounds), minutes: scale(row.minutes), plusMinus: scale(row.plusMinus) };
    });
    const extra = logged.filter((game) => !tracked.some((row) => row.games === 1 && row.date === game.date && row.points === game.points))
        .map((game) => ({ ...game, games: 1 }));
    return [...perGame, ...extra].sort((a, b) => a.date.localeCompare(b.date));
}

/** Preview only: 24 tracked games for the first sample player (index from the preview roster). */
export function previewTracker(index: number, name: string): TrackerState {
    const segments: Segment[] = Array.from({ length: 22 }, (_, game) => {
        const hot = game >= 17;
        const points = Math.round(18 + 6 * Math.sin(game / 2) + (hot ? 7 : 0));
        const fga = 16 + (game % 5);
        const games = game === 8 || game === 13 ? 2 : 1;
        return { at: `2026-10-${String(5 + game).padStart(2, '0')}T12:00:00Z`, year: 109, games, totals: {
            POINTS: points * games, FIELDGOALSMADE: Math.round(points * 0.37) * games, FIELDGOALSATTEMPTED: fga * games, '3POINTERSMADE': ((game % 4) + (hot ? 1 : 0)) * games,
            '3POINTERSATTEMPTED': (5 + (game % 3)) * games, FREETHROWMADE: (3 + (game % 3)) * games, FREETHROWATTEMPTED: (4 + (game % 3)) * games,
            OFFENSIVEREBOUNDS: (1 + (game % 2)) * games, DEFENSEREBOUNDS: (5 + (game % 4)) * games, ASSISTS: (4 + (game % 5)) * games, STEALS: (game % 3) * games,
            BLOCKS: (game % 2) * games, TURNOVERS: (2 + (game % 3) - (hot ? 1 : 0)) * games, FOULS: 2 * games, MINUTES: (33 + (game % 6)) * games,
            GAMESPLAYED: games, GAMESSTARTED: games, 'BOX+-': (hot ? 8 : (game % 7) - 3) * games } };
    });
    return { format: 'nba2k27-season-tracker/1', players: { [`${index}:${name}`]: { name, year: 109, last: {}, segments } } };
}
