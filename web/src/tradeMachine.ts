/**
 * Trade machine arithmetic: what each team sends, receives and ends up with. Mirrors the bridge's
 * planPackage, which recomputes and validates everything again before writing.
 */
export type BasketPlayer = { index: number; name: string; overall: number | null; salary: number | null; teamIndex: number };
export type BasketPick = { index: number; ownerIndex: number; label: string };

export type SideSummary = {
    outgoingSalary: number;
    incomingSalary: number;
    payrollBefore: number | null;
    payrollAfter: number | null;
    rosterBefore: number | null;
    rosterAfter: number | null;
    capacity: number | null;
    overRoster: boolean;
    bestOutgoing: number | null;
    bestIncoming: number | null;
};

const sum = (players: BasketPlayer[]) => players.reduce((total, player) => total + (player.salary ?? 0), 0);
const best = (players: BasketPlayer[]) => players.reduce<number | null>((top, player) =>
    player.overall === null ? top : Math.max(top ?? 0, player.overall), null);

export function sideSummary(
    sends: BasketPlayer[], receives: BasketPlayer[],
    team: { payroll?: number | null; used?: number | null; capacity?: number | null },
): SideSummary {
    const outgoingSalary = sum(sends);
    const incomingSalary = sum(receives);
    const payrollBefore = team.payroll ?? null;
    const rosterBefore = team.used ?? null;
    const rosterAfter = rosterBefore === null ? null : rosterBefore - sends.length + receives.length;
    return {
        outgoingSalary, incomingSalary, payrollBefore,
        payrollAfter: payrollBefore === null ? null : payrollBefore - outgoingSalary + incomingSalary,
        rosterBefore, rosterAfter, capacity: team.capacity ?? null,
        overRoster: rosterAfter !== null && team.capacity != null && rosterAfter > team.capacity,
        bestOutgoing: best(sends), bestIncoming: best(receives),
    };
}

/**
 * Salary balance as incoming / outgoing for the side taking on more money. Real NBA rules allow
 * roughly 125%; the editor only shows the ratio, it does not block the trade.
 */
export function salaryRatio(first: SideSummary): number | null {
    const sent = first.outgoingSalary;
    const received = first.incomingSalary;
    if (sent === 0 && received === 0) {
        return null;
    }
    const low = Math.min(sent, received);
    const high = Math.max(sent, received);
    return low === 0 ? Infinity : Math.round((high / low) * 100);
}

/** Toggle membership of a key in a list, keeping order. */
export function toggle<T>(list: T[], item: T): T[] {
    return list.includes(item) ? list.filter((value) => value !== item) : [...list, item];
}
