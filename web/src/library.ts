import type { LibraryApplyResult, LibraryEntry, LibraryKind } from './types';
import { displayValue, fieldLabel, isConfirmedField } from './labels.ts';

export const kindLabel = (kind: LibraryKind) => (kind === 'face' ? '面部' : '球员');

/** Filter by kind and a case/accent-insensitive search over name and source player. */
export function libraryFilter(entries: LibraryEntry[], kind: 'all' | LibraryKind, query: string): LibraryEntry[] {
    const fold = (text: string) => text.normalize('NFKD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
    const needle = fold(query.trim());
    return entries.filter((entry) => (kind === 'all' || entry.kind === kind)
        && (!needle || fold(entry.name).includes(needle) || fold(entry.sourcePlayer).includes(needle)));
}

/** "2026-11-08T05:30:00+08:00" -> "2026-11-08 05:30"; anything unparsable is shown as is. */
export function savedAt(text: string): string {
    const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})/.exec(text);
    return match ? `${match[1]} ${match[2]}` : text;
}

/** A library entry can be applied only when every changed field has a meaningful preview. */
export function libraryPreview(preview: Pick<LibraryApplyResult, 'changeCount' | 'changes'>) {
    const rows: { field: string; label: string; before: string; after: string }[] = [];
    let blocked = preview.changeCount === preview.changes.length ? 0 : 1;
    for (const change of preview.changes) {
        const { beforeField, afterField } = change;
        if (!beforeField || !afterField || beforeField.key !== change.field || afterField.key !== change.field
            || beforeField.value === null || afterField.value === null
            || !isConfirmedField(beforeField) || !isConfirmedField(afterField)) {
            ++blocked;
            continue;
        }
        const before = beforeField.kind === 'text' && beforeField.value === '' ? '空' : displayValue(beforeField);
        const after = afterField.kind === 'text' && afterField.value === '' ? '空' : displayValue(afterField);
        if (!before.trim() || !after.trim()) {
            ++blocked;
            continue;
        }
        rows.push({ field: change.field, label: fieldLabel(beforeField), before, after });
    }
    return { rows, blocked };
}
