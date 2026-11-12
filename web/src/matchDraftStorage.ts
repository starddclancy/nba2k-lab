import { matchDraftKey, matchEditFields, matchValues } from './match.ts';
import type { MatchEditDraft, MatchSnapshot } from './match';

type DraftStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;
type Drafts = Record<string, MatchEditDraft>;

function storageKey(snapshot: MatchSnapshot): string {
    return `nba2k27.matchDraft.v1.${encodeURIComponent(JSON.stringify([
        snapshot.pid, snapshot.processCreatedUtc, snapshot.readToken,
    ]))}`;
}

function identity(snapshot: MatchSnapshot): string {
    // Include captured values, so even an accidentally reused token cannot restore against a changed baseline.
    return JSON.stringify([snapshot.capturedUtc, snapshot.period, snapshot.teams]);
}

function validate(snapshot: MatchSnapshot, value: unknown): Drafts {
    if (!Array.isArray(value) || value.length > 180) throw new Error('场内草稿格式无效');
    const result: Drafts = {};
    for (const item of value) {
        if (!item || typeof item !== 'object') throw new Error('场内草稿记录无效');
        const draft = item as MatchEditDraft;
        const team = snapshot.teams.find((entry) => entry.side === draft.side);
        const players = team?.players.filter((entry) => entry.id === draft.playerId) ?? [];
        if (players.length !== 1 || !Number.isInteger(draft.slot)
            || draft.slot < 1 || draft.slot > Math.min(snapshot.period, 5)
            || !draft.changes || typeof draft.changes !== 'object' || Array.isArray(draft.changes)) {
            throw new Error('场内草稿的球员或分节无效');
        }
        const original = matchValues(players[0].stats, draft.slot);
        for (const [field, value] of Object.entries(draft.changes)) {
            if (!matchEditFields.some((entry) => entry.key === field)
                || !(field in original)
                || (value !== null && (!Number.isInteger(value) || value < 0 || value > 65535))) {
                throw new Error('场内草稿包含无效统计');
            }
        }
        const key = matchDraftKey(draft.side, draft.playerId, draft.slot);
        if (Object.hasOwn(result, key)) throw new Error('场内草稿记录重复');
        result[key] = { side: draft.side, playerId: draft.playerId, slot: draft.slot, changes: { ...draft.changes } };
    }
    return result;
}

export function loadMatchDrafts(storage: DraftStorage, snapshot: MatchSnapshot): Drafts {
    const content = storage.getItem(storageKey(snapshot));
    if (content === null) return {};
    if (content.length > 1_000_000) throw new Error('场内草稿超过读取限制');
    const record = JSON.parse(content);
    if (!record || record.format !== 'nba2k27-match-draft/1' || record.identity !== identity(snapshot)) {
        throw new Error('保存的场内草稿与当前快照不一致，已保留原记录');
    }
    return validate(snapshot, record.drafts);
}

export function saveMatchDrafts(storage: DraftStorage, snapshot: MatchSnapshot, drafts: Drafts): void {
    const checked = validate(snapshot, Object.values(drafts));
    if (Object.keys(checked).length === 0) {
        clearMatchDrafts(storage, snapshot);
        return;
    }
    storage.setItem(storageKey(snapshot), JSON.stringify({
        format: 'nba2k27-match-draft/1', identity: identity(snapshot),
        savedUtc: new Date().toISOString(), drafts: Object.values(checked),
    }));
}

export function clearMatchDrafts(storage: DraftStorage, snapshot: MatchSnapshot): void {
    storage.removeItem(storageKey(snapshot));
}
