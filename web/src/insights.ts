import { categories } from './archetype.ts';
import type { Category } from './archetype.ts';
import type { PlayerStats } from './scouting.ts';

type Row = { index: number; name: string; overall: number | null };

/** Overall histogram in 5-point buckets from 40 to 99 (anything lower lands in the first). */
export function overallHistogram(rows: Row[]): { from: number; count: number }[] {
    const buckets = Array.from({ length: 12 }, (_, i) => ({ from: 40 + i * 5, count: 0 }));
    for (const row of rows) {
        if (row.overall === null) continue;
        const slot = Math.min(buckets.length - 1, Math.max(0, Math.floor((row.overall - 40) / 5)));
        buckets[slot].count++;
    }
    return buckets;
}

/** Top players by one category score (ties broken by name for a stable order). */
export function leaders(rows: Row[], stats: Map<number, PlayerStats>, category: Category, limit = 3) {
    return rows.flatMap((row) => {
        const value = stats.get(row.index)?.scores?.[category];
        return value === undefined ? [] : [{ ...row, value }];
    }).sort((a, b) => b.value - a.value || a.name.localeCompare(b.name)).slice(0, limit);
}

/** Most common archetype names with their share of players that have one. */
export function archetypeShare(rows: Row[], stats: Map<number, PlayerStats>, limit = 6) {
    const counts = new Map<string, number>();
    let total = 0;
    for (const row of rows) {
        const name = stats.get(row.index)?.archetype;
        if (!name) continue;
        total++;
        counts.set(name, (counts.get(name) ?? 0) + 1);
    }
    return [...counts].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0])).slice(0, limit)
        .map(([name, count]) => ({ name, count, share: total ? Math.round((count / total) * 100) : 0 }));
}

export const leaderCategories: Category[] = [...categories];
