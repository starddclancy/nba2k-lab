import type { PlayerRow, TeamItem } from './types';

export type PlayerFilters = {
    position: string | null;
    includeSecondary: boolean;
    ageFrom: number | null;
    ageTo: number | null;
    heightFrom: number | null;
    heightTo: number | null;
    overallMin: number | null;
};

export const emptyFilters: PlayerFilters = {
    position: null,
    includeSecondary: true,
    ageFrom: null,
    ageTo: null,
    heightFrom: null,
    heightTo: null,
    overallMin: null,
};

export type PlayerSort = 'name' | 'age' | 'heightCm' | 'overall';

export function matchesPlayer(row: PlayerRow, filters: PlayerFilters): boolean {
    const summary = row.summary;
    const inRange = (value: number | null | undefined, low: number | null, high: number | null) =>
        (low === null && high === null)
        || (typeof value === 'number' && Number.isFinite(value)
            && (low === null || value >= low) && (high === null || value <= high));
    return (!filters.position || summary?.position === filters.position
        || (filters.includeSecondary && summary?.secondaryPosition === filters.position))
        && inRange(summary?.age, filters.ageFrom, filters.ageTo)
        && inRange(summary?.heightCm, filters.heightFrom, filters.heightTo)
        && inRange(summary?.overall, filters.overallMin, null);
}

export function comparePlayers(left: PlayerRow, right: PlayerRow, sort: PlayerSort): number {
    if (sort !== 'name') {
        const a = left.summary?.[sort];
        const b = right.summary?.[sort];
        const hasA = typeof a === 'number' && Number.isFinite(a);
        const hasB = typeof b === 'number' && Number.isFinite(b);
        if (hasA !== hasB) {
            return hasA ? -1 : 1;
        }
        if (hasA && hasB && a !== b) {
            return sort === 'age' ? a - b : b - a;
        }
    }
    return left.name.localeCompare(right.name) || left.index - right.index;
}

export function belongsTo(row: PlayerRow, group: string): boolean {
    return group.startsWith('team:')
        ? (row.teamIndices ?? []).includes(Number(group.slice(5)))
        : row.category === group;
}

export function rosterGroups(teams: Pick<TeamItem, 'index' | 'label'>[], players: PlayerRow[]) {
    return [
        ...teams.map((team) => ({ value: `team:${team.index}`, label: team.label })),
        { value: 'freeAgent', label: '自由球员' },
        { value: 'historic', label: '未在队的历史球员' },
        { value: 'unassigned', label: '归属待确认' },
    ].map((group) => ({
        ...group,
        count: players.filter((row) => belongsTo(row, group.value)).length,
    }));
}
