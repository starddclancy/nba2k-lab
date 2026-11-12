import type { PlayerRow } from './types';

export type PlayerSource = 'roster' | 'draft' | 'prospects';
export type BrowserPlayer = PlayerRow & { source: PlayerSource };
export type DraftFilter = 'all' | 'prospects' | 'draft';

export function sourceRows(items: PlayerRow[], source: PlayerSource): BrowserPlayer[] {
    return items.map((row) => ({ ...row, key: `${source}:${row.index}`, source }));
}

export function matchesDraft(row: BrowserPlayer, source: DraftFilter, year: number | null): boolean {
    return (source === 'all' || row.source === source)
        && (year === null || row.draftYear === year);
}

export function batchSelection(rows: BrowserPlayer[], keys: string[]) {
    const selected = rows.filter((row) => keys.includes(row.key));
    const source = selected[0]?.source;
    return source && selected.every((row) => row.source === source)
        ? { source, indices: selected.map((row) => row.index) } : null;
}

export function selectSameSource(rows: BrowserPlayer[], keys: string[], current: string[]): string[] {
    const source = batchSelection(rows, current)?.source
        ?? rows.find((row) => keys.includes(row.key))?.source;
    return rows.filter((row) => row.source === source && keys.includes(row.key)).map((row) => row.key);
}

export function draftRows(results: PromiseSettledResult<{ status: string; items: PlayerRow[] }>[]) {
    const sources = ['prospects', 'draft'] as const;
    return {
        items: results.flatMap((result, index) =>
            result.status === 'fulfilled' ? sourceRows(result.value.items, sources[index]) : []),
        statuses: results.map((result, index) => ({
            source: sources[index],
            label: index === 0 ? '待选秀' : '新秀名单',
            count: result.status === 'fulfilled' ? result.value.items.length : 0,
            status: result.status === 'fulfilled' ? result.value.status
                : `读取失败：${result.reason instanceof Error ? result.reason.message : String(result.reason)}`,
            failed: result.status === 'rejected',
        })),
    };
}
