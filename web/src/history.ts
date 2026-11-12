import type { FieldCell } from './types';

export type HistoryKind = 'league' | 'totals' | 'season' | 'franchise' | 'jersey' | 'records' | 'teamRecords';
export type HistoryItem = {
    key: string;
    kind: HistoryKind;
    index: number;
    teamIndex: number | null;
    category: string;
    year: number | null;
    label: string;
    team: string;
    summary: string;
    recordGroup?: string | null;
    metric?: string | null;
    rank?: number | null;
    notice?: string | null;
};
export type HistoryDetail = {
    key: string;
    label: string;
    address: string;
    readToken: string;
    fields: FieldCell[];
    sharedTeams: string[];
    undoDepth: number;
    undoKey: string | null;
};
export type HistoryDraft = Record<string, number | string | boolean>;

const recordGroups = ['单场纪录（常规赛）', '单场纪录（季后赛）', '单赛季纪录', '生涯累计纪录'];

export function confirmedHistoryItems(items: HistoryItem[]): HistoryItem[] {
    return items.filter((item) => !item.notice && !/未确认|未验证|待核对/.test(item.category)
        && (item.kind !== 'records' && item.kind !== 'teamRecords'
            || recordGroups.includes(item.recordGroup ?? '')
                && (item.kind !== 'teamRecords' || item.recordGroup === recordGroups[0]
                    || item.recordGroup === recordGroups[1] || item.metric === '得分')));
}

export function isHistoryFieldVisible(field: Pick<FieldCell, 'key' | 'value'>): boolean {
    return !['TYPE', 'POSITION', 'SIGNATURE', 'SIGNATUREID', 'DAY', 'MONTH', 'FILENAME'].includes(field.key)
        && field.value !== null && field.value !== '保留现有关联'
        && !(field.key === 'JERSEYNUMBER' && field.value === 65535);
}

export function filterHistory(items: HistoryItem[], query: string, category: string, year: number | null, recordGroup = '') {
    const needle = query.trim().toLowerCase();
    return confirmedHistoryItems(items).filter((item) => (!category || item.category === category)
        && (!recordGroup || item.recordGroup === recordGroup)
        && (year === null || item.year === year)
        && (!needle || `${item.label} ${item.team} ${item.summary} ${item.category} ${item.index} ${item.recordGroup ?? ''} ${item.metric ?? ''}`
            .toLowerCase().includes(needle)));
}

export function historyChanges(detail: HistoryDetail | null, draft: HistoryDraft, editable = true): HistoryDraft {
    if (!detail || !editable) {
        return {};
    }
    return Object.fromEntries(detail.fields.filter((field) => isHistoryFieldVisible(field) && field.writable
        && draft[field.key] !== undefined && draft[field.key] !== field.value)
        .map((field) => [field.key, draft[field.key]]));
}

export function seasonLabel(year: number | null) {
    return year === null ? '' : `${year}-${String((year + 1) % 100).padStart(2, '0')}`;
}
