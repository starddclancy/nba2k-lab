import test from 'node:test';
import assert from 'node:assert/strict';
import { confirmedHistoryItems, filterHistory, historyChanges, isHistoryFieldVisible, seasonLabel } from '../src/history.ts';
import type { HistoryDetail, HistoryItem } from '../src/history.ts';
import { previewHistory } from '../src/historyPreview.ts';
import type { TeamItem } from '../src/types.ts';

const items: HistoryItem[] = [
    { key: 'league:0', kind: 'league', index: 0, teamIndex: null, category: 'MVP', year: 2001,
        label: 'Tim Duncan', team: 'San Antonio Spurs', summary: 'Tim Duncan' },
    { key: 'league:1', kind: 'league', index: 1, teamIndex: null, category: 'MVP', year: 2000,
        label: 'Allen Iverson', team: 'Philadelphia 76ers', summary: 'Allen Iverson' },
    { key: 'league:2', kind: 'league', index: 2, teamIndex: null, category: '总冠军', year: 2001,
        label: 'Shaquille O Neal', team: 'Los Angeles Lakers', summary: '4 : 0' },
];

test('history filters combine year category and case-insensitive names without rewriting records', () => {
    assert.deepEqual(filterHistory(items, 'DUNCAN', 'MVP', 2001).map((r) => r.key), ['league:0']);
    assert.equal(filterHistory(items, '76ers', '', null)[0], items[1]);
    assert.equal(filterHistory(items, '', 'MVP', null).length, 2);
    assert.equal(filterHistory(items, 'Lakers', 'MVP', null).length, 0);
    assert.equal(filterHistory(items, '', '', null).length, 3);
});

test('history changes exclude unchanged, stale and immutable inputs', () => {
    const detail = { fields: [
        { key: 'WINS', value: 0, writable: true }, { key: 'LOSSES', value: 10, writable: true },
        { key: 'TYPE', value: 1, writable: false }, { key: 'NAME', value: 'A', writable: true },
    ] } as HistoryDetail;
    assert.deepEqual(historyChanges(detail, { WINS: 0, LOSSES: 11, TYPE: 3, NAME: '', STALE: 10 }),
        { LOSSES: 11, NAME: '' });
    assert.deepEqual(historyChanges(null, { WINS: 1 }), {});
});

test('season range rolls over centuries and leaves unknown year blank', () => {
    assert.equal(seasonLabel(1999), '1999-00');
    assert.equal(seasonLabel(2001), '2001-02');
    assert.equal(seasonLabel(null), '');
});

test('record filters keep confirmed scopes separate and exclude unresolved rows from searches', () => {
    const records = [
        { ...items[0], key: 'records:100', kind: 'records', index: 100, recordGroup: '单赛季纪录', metric: '得分' },
        { ...items[0], key: 'records:350', kind: 'records', index: 350, recordGroup: '生涯累计纪录', metric: '得分' },
        { ...items[0], key: 'records:2850', kind: 'records', index: 2850, recordGroup: '分类待核对', metric: '原始记录' },
    ] as HistoryItem[];
    assert.deepEqual(filterHistory(records, '', '', null, '单赛季纪录').map((row) => row.key), ['records:100']);
    assert.deepEqual(filterHistory(records, '生涯', '', null).map((row) => row.key), ['records:350']);
    assert.deepEqual(filterHistory(records, '2850', '', null), []);
    assert.deepEqual(filterHistory(records, '', '', null, '分类待核对'), []);
    assert.deepEqual(confirmedHistoryItems(records), records.slice(0, 2));
    assert.equal(records.length, 3);
});

test('record presentation exposes 2850 league and 120 team rows without changing source IDs', () => {
    const makeRows = (kind: 'records' | 'teamRecords') => Array.from({ length: kind === 'records' ? 3100 : 510 }, (_, offset) => {
        const group = offset < 50 ? '单场纪录（常规赛）' : offset < 100 ? '单场纪录（季后赛）'
            : kind === 'teamRecords' ? offset < 110 ? '单赛季纪录'
                : offset >= 300 && offset < 310 ? '生涯累计纪录' : '分类待核对'
            : offset >= 2850 ? '分类待核对'
            : offset < 350 ? '单赛季纪录' : '生涯累计纪录';
        const index = offset + (kind === 'teamRecords' ? 4120 : 0);
        return { ...items[0], key: `${kind}:${index}`, kind, index, recordGroup: group,
            category: group, metric: '得分', notice: group === '分类待核对' ? 'Boundary unresolved' : null };
    });
    const league = makeRows('records'), team = makeRows('teamRecords');
    const visibleLeague = confirmedHistoryItems(league), visibleTeam = confirmedHistoryItems(team);
    assert.equal(visibleLeague.length, 2850);
    assert.equal(visibleTeam.length, 120);
    assert.equal(visibleLeague.at(-1)?.index, 2849);
    assert.equal(visibleTeam[0].index, 4120);
    assert.equal(visibleTeam[100].index, 4220);
    assert.equal(visibleTeam[109].index, 4229);
    assert.equal(visibleTeam[110].index, 4420);
    assert.equal(visibleTeam.at(-1)?.index, 4429);
    assert.deepEqual([...new Set(visibleTeam.map((row) => row.recordGroup))], [
        '单场纪录（常规赛）', '单场纪录（季后赛）', '单赛季纪录', '生涯累计纪录',
    ]);
    assert.equal(league.length, 3100);
    assert.equal(team.length, 510);
    assert.ok(visibleLeague.every((row) => league[row.index] === row));
    assert.ok(visibleTeam.every((row) => team[row.index - 4120] === row));
    assert.deepEqual(confirmedHistoryItems([
        { ...team[100], metric: '投篮命中' }, { ...team[300], metric: '犯规' },
    ]), []);
    assert.deepEqual(confirmedHistoryItems([{ ...league[0], notice: 'Unresolved' },
        { ...items[0], category: '未验证分类' }]), []);
});

test('history hides technical fields and unresolved team choices but retains meaningful names and values', () => {
    for (const key of ['TYPE', 'POSITION', 'SIGNATURE', 'SIGNATUREID', 'DAY', 'MONTH', 'FILENAME']) {
        assert.equal(isHistoryFieldVisible({ key, value: 1 }), false, key);
    }
    for (const key of ['DATA', 'FIRSTNAME', 'LASTNAME', 'YEAR', 'WINS', 'SEASON', 'COMPACTYEAR']) {
        assert.equal(isHistoryFieldVisible({ key, value: 1 }), true, key);
    }
    assert.equal(isHistoryFieldVisible({ key: 'TEAM', value: '保留现有关联' }), false);
    assert.equal(isHistoryFieldVisible({ key: 'TEAM', value: '0 · Boston Celtics' }), true);
    assert.equal(isHistoryFieldVisible({ key: 'TEAM', value: null }), false);
    assert.equal(isHistoryFieldVisible({ key: 'JERSEYNUMBER', value: 65535 }), false);
    assert.equal(isHistoryFieldVisible({ key: 'JERSEYNUMBER', value: 23 }), true);
    const detail = { fields: [
        { key: 'DAY', value: 1, writable: true }, { key: 'POSITION', value: 1, writable: true },
        { key: 'TEAM', value: '保留现有关联', writable: true }, { key: 'DATA', value: 10, writable: true },
    ] } as HistoryDetail;
    assert.deepEqual(historyChanges(detail, { DAY: 2, POSITION: 2, TEAM: '0 · Boston Celtics', DATA: 11 }), { DATA: 11 });
});

test('browse mode never produces history writes from retained drafts', () => {
    const detail = { fields: [{ key: 'DATA', value: 10, writable: true }] } as HistoryDetail;
    assert.deepEqual(historyChanges(detail, { DATA: 12 }, false), {});
    assert.deepEqual(historyChanges(detail, { DATA: 12 }, true), { DATA: 12 });
});

test('history preview exposes separate league record scopes and team classification limits', () => {
    const teams = [{ index: 0, label: 'Fixture Team' }] as TeamItem[];
    const league = previewHistory('historyList', { kind: 'records' }, teams) as HistoryItem[];
    assert.deepEqual([...new Set(league.map((row) => row.recordGroup))],
        ['单场纪录（常规赛）', '单场纪录（季后赛）', '单赛季纪录', '生涯累计纪录', '分类待核对']);
    assert.deepEqual([...new Set(confirmedHistoryItems(league).map((row) => row.recordGroup))],
        ['单场纪录（常规赛）', '单场纪录（季后赛）', '单赛季纪录', '生涯累计纪录']);
    const detail = previewHistory('historyRead', { key: league[3].key }, teams) as HistoryDetail;
    assert.equal(detail.fields.find((field) => field.key === 'DATA')?.value, 40283);
    assert.equal(detail.fields.find((field) => field.key === 'YEAR')?.writable, false);
    const team = previewHistory('historyList', { kind: 'teamRecords', teamIndex: 0 }, teams) as HistoryItem[];
    assert.equal(team[2].index, 3200);
    assert.equal(team[2].recordGroup, '单赛季纪录');
    assert.equal(team[3].index, 3400);
    assert.equal(team[3].recordGroup, '生涯累计纪录');
    assert.ok(team[4].notice);
    assert.equal(confirmedHistoryItems(team).length, 4);
});
