import assert from 'node:assert/strict';
import test from 'node:test';
import { batchSelection, draftRows, matchesDraft, selectSameSource, sourceRows } from '../src/playerSources.ts';

const prospect = { key: '4', index: 4, name: 'Prospect', team: '', draftYear: 2007 };
const rookie = { key: '4', index: 4, name: 'Rookie', team: '' };
const rows = [...sourceRows([prospect], 'prospects'), ...sourceRows([rookie], 'draft')];

test('merged draft rows retain distinct identities and read sources for equal indices', () => {
    assert.deepEqual(rows.map((row) => [row.key, row.source, row.index]),
        [['prospects:4', 'prospects', 4], ['draft:4', 'draft', 4]]);
    assert.equal(prospect.key, '4');
});

test('one draft source failing keeps the other source visible and reports the failure', async () => {
    const result = draftRows(await Promise.allSettled([
        Promise.reject(new Error('unloaded')),
        Promise.resolve({ status: 'loaded', items: [rookie] }),
    ]));
    assert.deepEqual(result.items, [rows[1]]);
    assert.equal(result.statuses[0].failed, true);
    assert.match(result.statuses[0].status, /unloaded/);
    assert.equal(result.statuses[1].count, 1);
});

test('combined list filters source and known draft year without assigning years to rookies', () => {
    assert.equal(rows.filter((row) => matchesDraft(row, 'all', null)).length, 2);
    assert.deepEqual(rows.filter((row) => matchesDraft(row, 'all', 2007)), [rows[0]]);
    assert.deepEqual(rows.filter((row) => matchesDraft(row, 'draft', null)), [rows[1]]);
    assert.equal(rows.filter((row) => matchesDraft(row, 'draft', 2007)).length, 0);
});

test('batch routing rejects mixed sources even when numeric indices are equal', () => {
    assert.equal(batchSelection(rows, rows.map((row) => row.key)), null);
    assert.equal(batchSelection(rows, []), null);
    assert.deepEqual(batchSelection(rows, ['draft:4']), { source: 'draft', indices: [4] });
    assert.deepEqual(batchSelection(rows, ['prospects:4']), { source: 'prospects', indices: [4] });
});

test('select all stays within the current batch source and clearing permits another source', () => {
    const all = rows.map((row) => row.key);
    assert.deepEqual(selectSameSource(rows, all, []), ['prospects:4']);
    assert.deepEqual(selectSameSource(rows, all, ['draft:4']), ['draft:4']);
    assert.deepEqual(selectSameSource(rows, [], ['draft:4']), []);
    assert.deepEqual(selectSameSource(rows, ['draft:4'], []), ['draft:4']);
});
