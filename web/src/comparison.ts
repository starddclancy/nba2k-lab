import type { RatingTable } from './scouting.ts';

/** Chinese names for the bulk rating columns the bridge returns (playerRatings). */
export const attributeLabels: Record<string, string> = {
    CLOSESHOT: '近距离投篮', DRIVINGLAYUP: '突破上篮', DRIVINGDUNK: '突破扣篮', STANDINGDUNK: '原地扣篮', POSTCONTROL: '背身控制',
    MIDRANGE: '中距离', '3POINT': '三分', FREETHROW: '罚球', PASSACCURACY: '传球准确性', BALLCONTROL: '控球',
    SPEEDWITHBALL: '持球速度', INTERIORDEFENSE: '内线防守', PERIMETERDEFENSE: '外线防守', STEAL: '抢断', BLOCK: '盖帽',
    OFFENSIVEREBOUND: '进攻篮板', DEFENSEREBOUND: '防守篮板', SPEED: '速度', AGILITY: '敏捷', STRENGTH: '力量',
    VERTICAL: '弹跳', POTENTIAL: '潜力',
};

export type AttributeRow = { key: string; label: string; values: (number | null)[]; best: number | null };

/** One row per attribute with each compared player's value; best marks the highest (ties: none). */
export function attributeRows(table: RatingTable | null, indices: number[]): AttributeRow[] {
    if (!table) return [];
    const byIndex = new Map(table.rows.map((row) => [row.index, row.values]));
    const rows: AttributeRow[] = [];
    table.fields.forEach((field, column) => {
        const name = field.split('/').pop() ?? field;
        const label = attributeLabels[name];
        if (!label) return;
        const values = indices.map((index) => {
            const value = byIndex.get(index)?.[column];
            return typeof value === 'number' ? value : null;
        });
        const present = values.filter((value): value is number => value !== null);
        const top = present.length ? Math.max(...present) : null;
        const best = top !== null && present.filter((value) => value === top).length === 1 ? values.indexOf(top) : null;
        rows.push({ key: name, label, values, best });
    });
    return rows;
}

/** Align several season lists on the union of years so they can share one chart. */
export function alignSeasons<T extends { year: number }>(lists: T[][], pick: (row: T) => number | null) {
    const years = [...new Set(lists.flatMap((list) => list.map((row) => row.year)))].sort((a, b) => a - b);
    return {
        years,
        series: lists.map((list) => {
            const byYear = new Map(list.map((row) => [row.year, row]));
            return years.map((year) => {
                const row = byYear.get(year);
                return row ? pick(row) : null;
            });
        }),
    };
}

/** Games-weighted career per-game average of a per-game metric. */
export function careerAverage<T extends { games: number }>(rows: T[], pick: (row: T) => number | null): number | null {
    let total = 0;
    let games = 0;
    for (const row of rows) {
        const value = pick(row);
        if (value === null) continue;
        total += value * row.games;
        games += row.games;
    }
    return games ? Math.round((total / games) * 10) / 10 : null;
}
