import test from 'node:test';
import assert from 'node:assert/strict';
import { assertGameEditing, isGameMutation, setGameEditing } from '../src/editingPolicy.ts';

test('browsing rejects every game mutation before transport', () => {
    setGameEditing(false);
    for (const command of [
        'applyPlayer', 'applyBatch', 'undoPlayer', 'importPlayer', 'applyTeam', 'undoTeam',
        'gsApply', 'gsUndo', 'inMatchApply', 'inMatchUndo', 'applyMove', 'applyTrade',
        'applyPackage', 'undoMove', 'historyApply', 'historyUndo', 'matchApply', 'matchUndo',
        'scheduleApply', 'scheduleUndo',
    ]) {
        assert.equal(isGameMutation(command), true, command);
        assert.throws(() => assertGameEditing(command), /编辑开关/, command);
    }
});

test('read and preparation commands remain usable while browsing', () => {
    setGameEditing(false);
    for (const command of [
        'connect', 'readPlayer', 'readPlayerStats', 'readTeam', 'readBatch',
        'historyList', 'historyRead', 'matchRead', 'matchPrepare',
        'schedulePrepare', 'planMove', 'planTrade', 'planPackage', 'exportPlayer',
    ]) {
        assert.equal(isGameMutation(command), false, command);
        assert.doesNotThrow(() => assertGameEditing(command), command);
    }
    assert.doesNotThrow(() => assertGameEditing('libraryApply', { preview: true }));
    for (const payload of [{}, { preview: false }, { preview: 'true' }]) {
        assert.throws(() => assertGameEditing('libraryApply', payload), /编辑开关/);
    }
});

test('turning editing off immediately revokes a previously opened operation', () => {
    setGameEditing(true);
    assert.doesNotThrow(() => assertGameEditing('historyApply'));
    assert.doesNotThrow(() => assertGameEditing('libraryApply', { preview: false }));
    setGameEditing(false);
    assert.throws(() => assertGameEditing('historyApply'), /编辑开关/);
});
