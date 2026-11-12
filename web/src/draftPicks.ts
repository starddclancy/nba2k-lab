import type { DraftPickRow } from './types';
import { teamLabel } from './labels.ts';

/** Picks a team can trade away: owned, not yet used, sorted by year, round and order. */
export function tradablePicks(rows: DraftPickRow[], teamIndex: number | null | undefined): DraftPickRow[] {
    if (teamIndex === null || teamIndex === undefined) {
        return [];
    }
    return rows
        .filter((row) => row.ownerIndex === teamIndex && !row.used)
        .sort((a, b) => (a.year ?? 0) - (b.year ?? 0) || a.round - b.round || a.order - b.order);
}

export function pickLabel(row: DraftPickRow): string {
    const year = row.year === null ? '年份未知' : `${row.year} 年`;
    const own = row.originalIndex === row.ownerIndex || row.original === null
        ? '' : `（来自${teamLabel(row.original)}）`;
    // Unused rows retain an order value that does not prove a finalized draft position.
    const order = row.used && Number.isInteger(row.order) && row.order > 0
        ? `轮内第 ${row.order} 顺位` : '顺位未定';
    return `${year}第 ${row.round} 轮 · ${order}${own}${row.hasTerms ? ' · 附带条件' : ''}`;
}

/** Translate the bridge's linked-change lines; unknown lines are shown as received. */
export function detailLabel(line: string): string {
    const payroll = /^payroll (.+): (\d+) -> (\d+)$/.exec(line);
    if (payroll) {
        const money = (value: string) => `$${Number(value).toLocaleString('en-US')}`;
        return `${teamLabel(payroll[1])} 薪资总额：${money(payroll[2])} → ${money(payroll[3])}`;
    }
    const pick = /^pick (\d+) \(round (\d)\): (.+) -> (.+)$/.exec(line);
    if (pick) {
        return `选秀权 #${pick[1]}（第 ${pick[2]} 轮）：${teamLabel(pick[3])} → ${teamLabel(pick[4])}`;
    }
    return line;
}
