import { playerSeasons, teamSeasons } from './seasonTrends.ts';
import type { PlayerSeason, TeamSeason } from './seasonTrends.ts';
import type { Metric, Mode } from './metrics.ts';

/** Bulk current-season read from the bridge (leagueSeason): one value row per player. */
export type LeagueTable = { fields: string[]; rows: { index: number; values: (number | null)[] }[]; seasonEndYear?: number | null };

/** Turn the bulk rows into PlayerSeason lines so every catalog metric works unchanged. */
export function leagueSeasons(table: LeagueTable | null): Map<number, PlayerSeason> {
    const result = new Map<number, PlayerSeason>();
    if (!table) return result;
    for (const row of table.rows) {
        const fields = table.fields.map((name, column) => ({ key: `SeasonStats/CURRENTYEARSTATID/Bulk/${name}`, value: row.values[column] }))
            .filter((field) => field.value !== null);
        const [season] = playerSeasons([{ selector: 'CURRENTYEARSTATID', fields }]);
        if (season && (table.seasonEndYear === undefined || season.year === table.seasonEndYear)) result.set(row.index, season);
    }
    return result;
}

export function leagueTeams(rows: Record<string, number | string>[], seasonEndYear?: number | null): { index: number; label: string; season: TeamSeason }[] {
    return rows.flatMap((row) => {
        const numbers = Object.fromEntries(Object.entries(row).filter(([, value]) => typeof value === 'number')) as Record<string, number>;
        const [season] = teamSeasons([{ ...numbers, slot: 0 }]);
        const year = seasonEndYear === undefined ? row.seasonEndYear : seasonEndYear;
        return season && (year === undefined || season.year === year) ? [{ index: Number(row.index), label: String(row.label), season }] : [];
    });
}

export type Ranked<T> = { item: T; value: number; rank: number };

/**
 * Rank items by a metric. Ties share a rank (1, 2, 2, 4). Rate stats need a minimum number of
 * games so a 2-for-2 shooter does not lead the league; lowerBetter metrics rank ascending.
 */
export function rank<T, R>(items: T[], row: (item: T) => R | null, metric: Metric<R>, mode: Mode, qualifies: (item: T) => boolean): Ranked<T>[] {
    const scored = items.filter(qualifies).flatMap((item) => {
        const source = row(item);
        const value = source ? metric.value(source, mode) : null;
        return value === null || !Number.isFinite(value) ? [] : [{ item, value }];
    });
    scored.sort((a, b) => (metric.lowerBetter ? a.value - b.value : b.value - a.value));
    let previous: number | null = null;
    let current = 0;
    return scored.map((entry, position) => {
        if (entry.value !== previous) {
            current = position + 1;
            previous = entry.value;
        }
        return { ...entry, rank: current };
    });
}

/** Minimum games for rate leaders: 70% of the most games anyone has played (at least 1). */
export function minimumGames(games: number[], share = 0.7): number {
    const most = Math.max(0, ...games);
    return Math.max(1, Math.floor(most * share));
}
