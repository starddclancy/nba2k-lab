/** Pure numbers behind the trade comparison view. */

export const money = (value: number | null | undefined) =>
    value === null || value === undefined ? '—' : `$${Math.round(value).toLocaleString('en-US')}`;

/** Short form for tight cards: $19.0M, $830K. */
export function moneyShort(value: number | null | undefined): string {
    if (value === null || value === undefined) {
        return '—';
    }
    const abs = Math.abs(value);
    if (abs >= 1_000_000) {
        return `$${(value / 1_000_000).toFixed(1)}M`;
    }
    if (abs >= 1_000) {
        return `$${Math.round(value / 1_000)}K`;
    }
    return `$${value}`;
}

export type PayrollSide = { before?: number; out?: number; in?: number };

/** Team payroll after the swap, same arithmetic as the bridge's SALARY sync. */
export function payrollAfter(side: PayrollSide): number | null {
    if (side.before === undefined || side.out === undefined || side.in === undefined) {
        return null;
    }
    return side.before - side.out + side.in;
}

/** Salary-matching hint: incoming salary as a share of outgoing, for a quick fairness read. */
export function salaryMatch(outgoing?: number, incoming?: number): number | null {
    if (!outgoing || incoming === undefined) {
        return null;
    }
    return Math.round((incoming / outgoing) * 100);
}
