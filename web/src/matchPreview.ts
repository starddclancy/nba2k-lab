import { matchEditFields, matchEditRequests, matchDraftValues, matchValues } from './match.ts';
import type {
    MatchEditChange, MatchEditPlan, MatchEditRequest, MatchRow, MatchSnapshot, MatchStats,
} from './match';

let sequence = 0;
let captured: MatchSnapshot | null = null;

function row(seed = 0): MatchRow {
    const values: MatchRow = Object.fromEntries(matchEditFields.map(({ key }) => [key, 0]));
    if (seed > 0) {
        Object.assign(values, {
            twoPointMade: seed, twoPointAttempted: seed + 2,
            threePointMade: 1, threePointAttempted: 3,
            freeThrowMade: 2, freeThrowAttempted: 2,
            offensiveRebounds: 1, defensiveRebounds: seed,
            assists: seed, steals: 1, blocks: 0, turnovers: 1,
        });
    }
    return matchDraftValues(values);
}

function sum(rows: MatchRow[]): MatchRow {
    return Object.fromEntries(Object.keys(row()).map((key) =>
        [key, rows.reduce((total, item) => total + item[key], 0)]));
}

function stats(seed: number): MatchStats {
    const periods = [row(seed), row(seed + 1), row(), row(), row()];
    return { periods, total: sum(periods) };
}

/** Development-only sample; no game handles, addresses or writes are available here. */
export function readPreviewMatch(): MatchSnapshot {
    captured = {
        pid: 0, processCreatedUtc: '', capturedUtc: new Date().toISOString(),
        readToken: `sample-match-${++sequence}`, period: 2, clockValue: 438,
        clockStableDuringRead: true, pauseStateVerified: false, editable: false,
        writes: 0, elapsedMs: 0, totalElapsedMs: 0, issues: [],
        teams: ['Philadelphia 76ers', 'Los Angeles Lakers'].map((label, side) => {
            const players = ['Guard', 'Forward', 'Center'].map((role, index) => ({
                id: side * 10 + index + 1, name: `Sample ${side === 0 ? '' : 'Away '}${role}`,
                photoId: index === 0 && side === 0 ? 9820 : 0,
                entity: side * 10 + index + 1, onCourt: index < 2, stats: stats(index + 1 + side),
            }));
            const periods = Array.from({ length: 5 }, (_, slot) =>
                sum(players.map((player) => player.stats.periods[slot])));
            return {
                side, label, logoReference: side === 0 ? 'logo000' : 'logo015',
                players, stats: { periods, total: sum(periods) },
            };
        }),
    };
    return structuredClone(captured);
}

/** Projects sample changes for layout review; the C# planner remains authoritative in the EXE. */
export function preparePreviewMatch(readToken: string, edits: MatchEditRequest[]): MatchEditPlan {
    if (!captured || readToken !== captured.readToken) {
        throw new Error('示例比赛快照已失效，请刷新');
    }
    const snapshot = captured;
    const requests = matchEditRequests(snapshot, edits);
    const changes = new Map<string, MatchEditChange>();
    for (const request of requests) {
        const team = snapshot.teams.find((item) => item.side === request.side)!;
        const player = team.players.find((item) => item.id === request.playerId)!;
        const before = matchValues(player.stats, request.slot);
        const after = matchDraftValues(before, request);
        for (const field of Object.keys(after)) {
            const delta = after[field] - before[field];
            if (delta === 0) {
                continue;
            }
            for (const [owner, playerId, stats] of [
                [player.name, player.id, player.stats],
                [team.label, null, team.stats],
            ] as const) {
                for (const slot of [request.slot, 0]) {
                    const key = `${request.side}:${playerId ?? 'team'}:${slot}:${field}`;
                    const value = matchValues(stats, slot)[field];
                    const change = changes.get(key) ?? {
                        owner, side: request.side, playerId, slot, field, before: value, after: value,
                        derived: playerId === null || slot === 0 || !(field in request.changes),
                    };
                    change.after += delta;
                    changes.set(key, change);
                }
            }
        }
    }
    const result = [...changes.values()].filter((change) => change.before !== change.after);
    if (result.some((change) => change.after < 0 || change.after > 65535)) {
        throw new Error('关联统计超出0至65535的范围');
    }
    return {
        readToken, previewToken: `sample-plan-${++sequence}`, changes: result,
        players: new Set(requests.map((item) => `${item.side}:${item.playerId}`)).size,
        periods: new Set(requests.map((item) => item.slot)).size, writes: 0,
    };
}
