import type { DraftPickRow, TeamRow } from './types';

export type TeamOverview = {
    rosterUsed: number | null;
    rosterCapacity: number | null;
    payroll: number | null;
    budget: number | null;
    picksOwned: number;
    firstRounders: number;
    acquired: number;
    traded: number;
    nextPicks: DraftPickRow[];
};

/** Field values arrive as strings or numbers depending on the field kind. */
const numberOf = (value: unknown) => (value === null || value === undefined || value === '' ? null : Number(value));

/**
 * Team summary from data the editor already reads: slot usage (teams), payroll and budget (team
 * record fields) and draft picks. traded counts this team's own picks now held by others.
 */
export function teamOverview(
    teamIndex: number, slots: TeamRow | undefined, fields: Record<string, unknown>, picks: DraftPickRow[],
): TeamOverview {
    const open = picks.filter((pick) => !pick.used);
    const owned = open.filter((pick) => pick.ownerIndex === teamIndex);
    return {
        rosterUsed: slots?.used ?? null,
        rosterCapacity: slots?.capacity ?? null,
        payroll: numberOf(fields['Business/Budget/SALARY']),
        budget: numberOf(fields['Business/Budget/PLAYERPAYROLL']),
        picksOwned: owned.length,
        firstRounders: owned.filter((pick) => pick.round === 1).length,
        acquired: owned.filter((pick) => pick.originalIndex !== null && pick.originalIndex !== teamIndex).length,
        traded: open.filter((pick) => pick.originalIndex === teamIndex && pick.ownerIndex !== teamIndex).length,
        nextPicks: [...owned].sort((a, b) => (a.year ?? 0) - (b.year ?? 0) || a.round - b.round).slice(0, 6),
    };
}

/** TEAMCOLOR fields are RGBA byte strings such as "00 3d a6 ff"; returns "#003da6" or null. */
export function teamColor(text: unknown): string | null {
    const match = /^\s*([0-9a-f]{2})\s+([0-9a-f]{2})\s+([0-9a-f]{2})(?:\s+[0-9a-f]{2})?\s*$/i.exec(String(text ?? ''));
    return match ? `#${match[1]}${match[2]}${match[3]}`.toLowerCase() : null;
}

/** Near-black primaries (Bulls #1b2023) read as no color on a dark page; fall back to the secondary. */
export function accentOf(primary: string | null, secondary: string | null): string | null {
    const light = (hex: string) => {
        const [r, g, b] = [1, 3, 5].map((at) => parseInt(hex.slice(at, at + 2), 16));
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    };
    if (primary && light(primary) >= 40) return primary;
    return secondary ?? primary;
}
