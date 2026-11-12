import { archetypeOf, categories, categoryScores } from './archetype.ts';
import type { Category, CategoryScores } from './archetype.ts';
import type { PlayerRow } from './types';

/** Roster field keys returned by the bridge's playerRatings command, in its column order. */
export type RatingTable = { fields: string[]; rows: { index: number; values: (number | null)[] }[] };

export type PlayerStats = {
    scores: CategoryScores | null;
    archetype: string | null;
    potential: number | null;
    salary: number | null;
    yearsLeft: number | null;
};

const SALARY = 'Contract/Salary/SALARYYEAR1';
const YEARS = 'Contract/Contract Terms/YEARSLEFT';
const POTENTIAL = 'Attributes/Misc/POTENTIAL';

/** Per-player derived columns keyed by roster index. */
export function statsByIndex(table: RatingTable | null): Map<number, PlayerStats> {
    const result = new Map<number, PlayerStats>();
    if (!table) {
        return result;
    }
    for (const row of table.rows) {
        const values: Record<string, unknown> = {};
        table.fields.forEach((field, column) => {
            values[field] = row.values[column];
        });
        const scores = categoryScores(values);
        const number = (key: string) => (typeof values[key] === 'number' ? values[key] as number : null);
        result.set(row.index, {
            scores,
            archetype: scores ? archetypeOf(scores).name : null,
            potential: number(POTENTIAL),
            salary: number(SALARY),
            yearsLeft: number(YEARS),
        });
    }
    return result;
}

/** Share of values strictly below value plus half the ties, 0-100. */
export function percentile(values: number[], value: number): number {
    if (values.length === 0) {
        return 0;
    }
    let below = 0;
    let equal = 0;
    for (const item of values) {
        if (item < value) below++;
        else if (item === value) equal++;
    }
    return Math.round(((below + equal / 2) / values.length) * 100);
}

export type Standing = { category: Category | 'overall'; value: number; league: number; position: number | null };

/**
 * League and same-position percentiles for a scouting report. Players are compared with every
 * roster player that has the score; position uses the primary position only.
 */
export function standings(
    target: { scores: CategoryScores; overall: number | null; position: string | null },
    pool: { scores: CategoryScores | null; overall: number | null; position: string | null }[],
): Standing[] {
    const peers = pool.filter((row) => row.scores);
    const samePosition = target.position ? peers.filter((row) => row.position === target.position) : [];
    const rows: Standing[] = [];
    if (target.overall !== null) {
        const all = pool.flatMap((row) => (row.overall === null ? [] : [row.overall]));
        const pos = samePosition.flatMap((row) => (row.overall === null ? [] : [row.overall]));
        rows.push({ category: 'overall', value: target.overall, league: percentile(all, target.overall),
            position: pos.length ? percentile(pos, target.overall) : null });
    }
    for (const category of categories) {
        const value = target.scores[category];
        rows.push({
            category, value,
            league: percentile(peers.map((row) => row.scores![category]), value),
            position: samePosition.length ? percentile(samePosition.map((row) => row.scores![category]), value) : null,
        });
    }
    return rows;
}

/** Rows a player pool can be built from: roster rows plus their derived stats. */
export function poolOf(rows: PlayerRow[], stats: Map<number, PlayerStats>) {
    return rows.map((row) => ({
        scores: stats.get(row.index)?.scores ?? null,
        overall: row.summary?.overall ?? row.overall ?? null,
        position: row.summary?.position ?? null,
    }));
}

export const positions = ['PG', 'SG', 'SF', 'PF', 'C'] as const;
export type Averages = { league: CategoryScores | null; byPosition: Record<string, CategoryScores | null>; counts: Record<string, number> };

/**
 * Six-category averages over the roster pool: the whole league and each primary position.
 * Only rated players (with scores) count; values are rounded to one decimal.
 */
export function categoryAverages(pool: { scores: CategoryScores | null; position: string | null }[]): Averages {
    const mean = (rows: CategoryScores[]): CategoryScores | null => {
        if (rows.length === 0) return null;
        const result = {} as CategoryScores;
        for (const category of categories) result[category] = Math.round((rows.reduce((sum, row) => sum + row[category], 0) / rows.length) * 10) / 10;
        return result;
    };
    const rated = pool.filter((row): row is { scores: CategoryScores; position: string | null } => row.scores !== null);
    const byPosition: Record<string, CategoryScores | null> = {};
    const counts: Record<string, number> = { league: rated.length };
    for (const position of positions) {
        const rows = rated.filter((row) => row.position === position).map((row) => row.scores);
        byPosition[position] = mean(rows);
        counts[position] = rows.length;
    }
    return { league: mean(rated.map((row) => row.scores)), byPosition, counts };
}
