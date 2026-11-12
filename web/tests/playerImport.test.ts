import assert from 'node:assert/strict';
import test from 'node:test';
import { playerImportPreview } from '../src/playerImport.ts';
import type { FieldCell, PlayerImportPreview } from '../src/types.ts';

const field = (value: string): FieldCell => ({
    key: 'Signature/Misc/EMOTEDRB', label: 'EmoteDRB', section: 'Signature', group: 'Misc',
    kind: 'choice', value, display: value, writable: true, options: ['EMDRB_NONE', 'EMDRB_WAVE', 'EMDRB_67'],
});
const preview = (): Exclude<PlayerImportPreview, { cancelled: true }> => ({
    cancelled: false, index: 2, source: 'roster', fileName: 'backup.json', readToken: 'snapshot',
    changeCount: 1, skipped: 0, requestChanges: { 'Signature/Misc/EMOTEDRB': 'EMDRB_WAVE' },
    changes: [{
        field: 'Signature/Misc/EMOTEDRB', label: 'EmoteDRB', before: 0, after: 2,
        beforeField: field('EMDRB_NONE'), afterField: field('EMDRB_WAVE'),
    }],
});

test('backup import previews decoded choices and retains the exact decoded request', () => {
    const source = preview();
    const result = playerImportPreview(source);
    assert.equal(result.blocked, 0);
    assert.equal(result.rows.length, 1);
    assert.equal(result.rows[0].before, '无');
    assert.equal(result.rows[0].after, '挥手');
    assert.equal(source.requestChanges['Signature/Misc/EMOTEDRB'], 'EMDRB_WAVE');
});

test('backup import blocks the whole request when any decoded field or option is unresolved', () => {
    const unknown = preview();
    unknown.changes[0].afterField = field('EMDRB_67');
    unknown.requestChanges['Signature/Misc/EMOTEDRB'] = 'EMDRB_67';
    const hidden = preview();
    hidden.changes[0] = { field: 'Vitals/Team/TEAMHISTORY0DATE', label: 'TeamHistory0Date', before: 0, after: 1,
        beforeField: { ...field(''), key: 'Vitals/Team/TEAMHISTORY0DATE', kind: 'number', value: 0 },
        afterField: { ...field(''), key: 'Vitals/Team/TEAMHISTORY0DATE', kind: 'number', value: 1 } };
    hidden.requestChanges = { 'Vitals/Team/TEAMHISTORY0DATE': 1 };
    for (const source of [unknown, hidden]) assert.ok(playerImportPreview(source).blocked > 0);
});

test('backup import rejects hidden extra changes, missing values and request-preview disagreements', () => {
    const extra = preview();
    extra.requestChanges['Vitals/Team/TEAMHISTORY0DATE'] = 1;
    const missing = preview();
    missing.requestChanges = {};
    const different = preview();
    different.requestChanges['Signature/Misc/EMOTEDRB'] = 'EMDRB_67';
    const duplicate = preview();
    duplicate.changes.push(duplicate.changes[0]);
    duplicate.changeCount = 2;
    const absentMetadata = preview();
    delete absentMetadata.changes[0].afterField;
    for (const source of [extra, missing, different, duplicate, absentMetadata]) {
        assert.ok(playerImportPreview(source).blocked > 0);
    }
});
