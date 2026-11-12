import assert from 'node:assert/strict';
import test from 'node:test';
import { kindLabel, libraryFilter, libraryPreview, savedAt } from '../src/library.ts';
import type { FieldCell, LibraryApplyResult, LibraryEntry } from '../src/types.ts';

const entry = (id: string, name: string, kind: 'face' | 'player', sourcePlayer: string): LibraryEntry => ({
    id, name, kind, sourcePlayer, created: '2026-11-08T05:30:00+08:00', fieldCount: 10, notes: '',
});

test('library filter by kind and accent-insensitive search', () => {
    const rows = [
        entry('a', 'Iverson 面部', 'face', 'Allen Iverson'),
        entry('b', 'My build', 'player', 'Manu Ginóbili'),
        entry('c', 'Kobe', 'face', 'Kobe Bryant'),
    ];
    assert.deepEqual(libraryFilter(rows, 'face', '').map((row) => row.id), ['a', 'c']);
    assert.deepEqual(libraryFilter(rows, 'all', 'ginobili').map((row) => row.id), ['b']);
    assert.deepEqual(libraryFilter(rows, 'all', '  IVERSON ').map((row) => row.id), ['a']);
    assert.deepEqual(libraryFilter(rows, 'player', 'kobe'), []);
    assert.equal(kindLabel('face'), '面部');
    assert.equal(savedAt('2026-11-08T05:30:00+08:00'), '2026-11-08 05:30');
    assert.equal(savedAt('bad'), 'bad');
});

const choice = (value: string): FieldCell => ({
    key: 'Vitals/General/POSITION', label: 'POSITION', section: 'Vitals', group: 'General',
    kind: 'choice', value, display: value, writable: true, options: ['PG', 'SG', 'UNKNOWN_POSITION'],
});
const preview = (beforeField?: FieldCell, afterField?: FieldCell): LibraryApplyResult => ({
    entry: entry('a', 'Test', 'player', 'Player'), index: 2, player: 'Target', scope: 'player',
    changeCount: 1, skipped: 0, applied: false,
    changes: [{ field: 'Vitals/General/POSITION', label: 'POSITION', before: 0, after: 1,
        beforeField, afterField }],
});

test('library preview translates decoded choices rather than raw enum numbers', () => {
    assert.deepEqual(libraryPreview(preview(choice('PG'), choice('SG'))), {
        rows: [{ field: 'Vitals/General/POSITION', label: '主要位置', before: '控球后卫', after: '得分后卫' }],
        blocked: 0,
    });
});

test('library preview blocks missing metadata and unknown values on either side', () => {
    for (const result of [
        preview(), preview(choice('PG'), choice('UNKNOWN_POSITION')),
        preview(choice('UNKNOWN_POSITION'), choice('SG')),
        preview(choice('PG'), { ...choice('SG'), key: 'Vitals/General/SECONDARYPOSITION' }),
        { ...preview(choice('PG'), choice('SG')), changeCount: 2 },
    ]) {
        assert.ok(libraryPreview(result).blocked > 0);
    }
});
