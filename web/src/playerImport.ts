import type { PlayerImportPreview } from './types';
import { libraryPreview } from './library.ts';

/** Every submitted value must be the decoded value from an unblocked preview row. */
export function playerImportPreview(preview: Exclude<PlayerImportPreview, { cancelled: true }>) {
    const result = libraryPreview(preview);
    const keys = Object.keys(preview.requestChanges);
    if (keys.length !== preview.changeCount
        || new Set(preview.changes.map((change) => change.field)).size !== preview.changeCount
        || preview.changes.some((change) => !Object.hasOwn(preview.requestChanges, change.field)
            || preview.requestChanges[change.field] !== change.afterField?.value)) {
        ++result.blocked;
    }
    return result;
}
