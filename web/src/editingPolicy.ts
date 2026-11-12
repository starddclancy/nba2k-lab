const gameMutations = new Set([
    'applyPlayer', 'applyBatch', 'undoPlayer', 'importPlayer', 'applyTeam', 'undoTeam',
    'gsApply', 'gsUndo', 'inMatchApply', 'inMatchUndo', 'applyMove', 'applyTrade',
    'applyPackage', 'undoMove', 'historyApply', 'historyUndo', 'matchApply', 'matchUndo',
    'scheduleApply', 'scheduleUndo',
]);

let editing = false;

export function setGameEditing(value: boolean) {
    editing = value;
}

export function isGameMutation(command: string, payload: Record<string, unknown> = {}) {
    return gameMutations.has(command) || command === 'libraryApply' && payload.preview !== true;
}

/** Check at dispatch time so closing the switch also revokes old dialogs and callbacks. */
export function assertGameEditing(command: string, payload: Record<string, unknown> = {}) {
    if (!editing && isGameMutation(command, payload)) {
        throw new Error('当前为浏览模式，请先打开编辑开关');
    }
}
