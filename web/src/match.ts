import type { TeamBrand } from './teamLogos';

export type MatchRow = Record<string, number>;
export type MatchStats = { total: MatchRow; periods: MatchRow[] };
export type MatchPlayer = {
    name: string;
    id: number;
    photoId: number;
    onCourt: boolean;
    entity: number;
    stats: MatchStats;
};
export type MatchTeam = TeamBrand & {
    side: number;
    players: MatchPlayer[];
    stats: MatchStats;
};
export type MatchSnapshot = {
    pid: number;
    processCreatedUtc: string;
    capturedUtc: string;
    readToken: string;
    period: number;
    clockValue: number;
    clockStableDuringRead: boolean;
    pauseStateVerified: false;
    editable: false;
    writes: number;
    elapsedMs: number;
    totalElapsedMs: number;
    teams: MatchTeam[];
    issues: { owner: string; field: string; comparison: string; total: number; sum: number }[];
};

export type MatchStatus = {
    readsEnabled: boolean;
    writesEnabled: boolean;
    automaticDefault: boolean;
    automaticEnabled?: boolean;
    debugReadLimit?: number | null;
    reason: string | null;
    writeReason?: string;
    undoDepth?: number;
};

/**
 * Keep the row order stable while values change live: a row keeps its previous position, new
 * rows are appended in `initial` order, and departed rows drop out. Re-sorting on every refresh
 * would make players jump whenever someone scores.
 */
export function stableOrder<T>(previous: readonly number[], rows: readonly T[],
    key: (row: T) => number, initial: (first: T, second: T) => number): T[] {
    const byKey = new Map(rows.map((row) => [key(row), row]));
    const kept = previous.filter((entry) => byKey.has(entry)).map((entry) => byKey.get(entry)!);
    const keptKeys = new Set(previous);
    const added = rows.filter((row) => !keptKeys.has(key(row))).sort(initial);
    return [...kept, ...added];
}

export type MatchEditRequest = {
    side: number;
    playerId: number;
    slot: number;
    changes: Record<string, number>;
};
export type MatchEditDraft = Omit<MatchEditRequest, 'changes'> & {
    changes: Record<string, number | null>;
};
export type MatchEditChange = {
    owner: string;
    side: number;
    playerId: number | null;
    slot: number;
    field: string;
    before: number;
    after: number;
    derived: boolean;
};
export type MatchEditPlan = {
    readToken: string;
    previewToken: string;
    changes: MatchEditChange[];
    players: number;
    periods: number;
    writes: number;
    patches?: {
        side: number;
        playerId: number | null;
        slot: number;
        field: string;
        address: number;
        width: number;
        beforeHex: string;
        afterHex: string;
    }[];
    patchBytes?: number;
    liveWriteReady?: boolean;
};

export const matchEditFields = [
    { key: 'twoPointMade', label: '两分命中' },
    { key: 'twoPointAttempted', label: '两分出手' },
    { key: 'threePointMade', label: '三分命中' },
    { key: 'threePointAttempted', label: '三分出手' },
    { key: 'freeThrowMade', label: '罚球命中' },
    { key: 'freeThrowAttempted', label: '罚球出手' },
    { key: 'offensiveRebounds', label: '前场篮板' },
    { key: 'defensiveRebounds', label: '后场篮板' },
    { key: 'assists', label: '助攻' },
    { key: 'steals', label: '抢断' },
    { key: 'blocks', label: '盖帽' },
    { key: 'turnovers', label: '失误' },
] as const;

const matchFieldNames = new Map<string, string>([
    ['points', '得分'], ['rebounds', '篮板'],
    ...matchEditFields.map(({ key, label }) => [key, label] as [string, string]),
]);

export const matchFieldLabel = (field: string): string => matchFieldNames.get(field) ?? '统计数据';

export function matchSlotLabel(slot: number): string {
    return slot === 0 ? '全场' : slot === 5 ? '加时合计' : `第${slot}节`;
}

export function matchDraftKey(side: number, playerId: number, slot: number): string {
    return `${side}:${playerId}:${slot}`;
}

export function matchDraftValues(original: MatchRow, draft?: MatchEditDraft): MatchRow {
    const row = { ...original };
    for (const [field, value] of Object.entries(draft?.changes ?? {})) {
        row[field] = value ?? Number.NaN;
    }
    row.points = 2 * row.twoPointMade + 3 * row.threePointMade + row.freeThrowMade;
    row.rebounds = row.offensiveRebounds + row.defensiveRebounds;
    return row;
}

export function matchEditRequests(snapshot: MatchSnapshot, drafts: MatchEditDraft[]): MatchEditRequest[] {
    const result: MatchEditRequest[] = [];
    const seen = new Set<string>();
    for (const draft of drafts) {
        const key = matchDraftKey(draft.side, draft.playerId, draft.slot);
        if (seen.has(key)) {
            throw new Error('同一球员同一分节不能重复提交');
        }
        seen.add(key);
        const team = snapshot.teams.find((item) => item.side === draft.side);
        const players = team?.players.filter((item) => item.id === draft.playerId) ?? [];
        if (players.length !== 1 || !Number.isInteger(draft.slot)
            || draft.slot < 1 || draft.slot > Math.min(snapshot.period, 5)) {
            throw new Error('球员或分节已失效，请重新读取比赛');
        }
        const original = matchValues(players[0].stats, draft.slot);
        const changes: Record<string, number> = {};
        for (const [field, value] of Object.entries(draft.changes)) {
            if (!matchEditFields.some((item) => item.key === field)
                || value === null || !Number.isInteger(value) || value < 0 || value > 65535) {
                throw new Error('统计值必须是0至65535之间的整数');
            }
            if (value !== original[field]) {
                changes[field] = value;
            }
        }
        if (Object.keys(changes).length > 0) {
            const row = matchDraftValues(original, draft);
            if (row.twoPointMade > row.twoPointAttempted || row.threePointMade > row.threePointAttempted
                || row.freeThrowMade > row.freeThrowAttempted) {
                throw new Error(`${players[0].name} · ${matchSlotLabel(draft.slot)}：命中数不能超过出手数`);
            }
            result.push({ side: draft.side, playerId: draft.playerId, slot: draft.slot, changes });
        }
    }
    return result;
}

export function matchValues(stats: MatchStats, slot: number): MatchRow {
    return slot === 0 ? stats.total : stats.periods[slot - 1];
}

export function matchShooting(row: MatchRow, kind: 'field' | 'three' | 'free') {
    const made = kind === 'field' ? row.twoPointMade + row.threePointMade
        : kind === 'three' ? row.threePointMade : row.freeThrowMade;
    const attempted = kind === 'field' ? row.twoPointAttempted + row.threePointAttempted
        : kind === 'three' ? row.threePointAttempted : row.freeThrowAttempted;
    return { made, attempted, percentage: attempted > 0 ? `${(made * 100 / attempted).toFixed(1)}%` : '—' };
}

export function matchPeriodLabel(period: number): string {
    return period <= 4 ? `第 ${period} 节` : `加时 ${period - 4}`;
}

export function matchClock(value: number): string {
    const seconds = Math.floor(value);
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}
