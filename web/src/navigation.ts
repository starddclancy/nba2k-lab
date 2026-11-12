export type EntitySource = 'roster' | 'prospects' | 'draft';
export type WorkspaceRoute = { page: string; index?: number; source?: EntitySource };
const pages = new Set(['players', 'analytics', 'versus', 'leaders', 'schedule', 'gs', 'match', 'roster',
    'playerLibrary', 'teams', 'leagueHistory', 'teamHistory', 'journals', 'builder', 'badgeRequirements',
    'animations', 'library', 'compare']);

export function parseRoute(hash: string): WorkspaceRoute | null {
    const parts = hash.replace(/^#\/?/, '').split('/');
    const page = parts[0];
    if (!pages.has(page)) return null;
    if (parts.length === 1) return { page };
    const source = page === 'players' ? parts[1] : undefined;
    if (page === 'players' && !['roster', 'prospects', 'draft'].includes(source!)) return null;
    if (page !== 'players' && page !== 'teams') return null;
    if (parts.length !== (page === 'players' ? 3 : 2)) return null;
    const value = parts[parts.length - 1];
    if (!/^\d+$/.test(value)) return null;
    const index = Number(value);
    if (!Number.isSafeInteger(index)) return null;
    return page === 'players' ? { page, index, source: source as EntitySource } : { page, index };
}

export function entityHref(page: 'players' | 'teams', index: number, source: EntitySource = 'roster'): string {
    return page === 'players' ? `#/players/${source}/${index}` : `#/teams/${index}`;
}
