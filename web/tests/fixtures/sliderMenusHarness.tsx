import React, { useMemo, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { flushSync } from 'react-dom';
import { App as AntApp, ConfigProvider } from 'antd';
import FieldEditor from '../../src/components/FieldEditor';
import GsPanel from '../../src/components/GsPanel';
import JournalPanel from '../../src/components/JournalPanel';
import Shell from '../../src/App';
import { EditingProvider, useEditing } from '../../src/editingContext';
import { readPlayer } from '../../src/api';
import { antdTheme, applyThemeMode } from '../../src/theme';
import type { FieldCell, GsPayload } from '../../src/types';
import '../../src/index.css';
import '../../src/workspace.css';
import '../../src/polish.css';

type Request = { id: string; command: string; payload: Record<string, unknown> };
const listeners: ((event: { data: unknown }) => void)[] = [];
const calls: Request[] = [];
const held: Request[] = [];
let hold = false;
let change!: (patch: { editing?: boolean; view?: string }) => void;
let reads = 0;
let closes = 0;
const playerChanges: Record<string, number | string | boolean> = {};
const field = (key: string, label: string, value: number, min: number, max: number): FieldCell => ({
    key, label, section: key.split('/')[0], group: key.split('/')[1],
    value, raw: value, display: String(value), writable: true, kind: 'number', options: [], min, max,
});
const gs: GsPayload = {
    fields: [
        { id: 'three_point', label: '三分命中率', group: 'offense', user: 50, cpu: 60 },
        { id: 'close_shot', label: '近距离投篮命中率', group: 'offense', user: 45, cpu: 55 },
        { id: 'steal_success', label: '抢断成功率', group: 'defense', user: 40, cpu: 65 },
        { id: 'block', label: '盖帽', group: 'attributes', user: null, cpu: 70 },
    ],
    groups: [
        { id: 'offense', label: '进攻' }, { id: 'defense', label: '防守' },
        { id: 'attributes', label: '属性' },
    ],
    sides: [{ id: 'user', label: '用户' }, { id: 'cpu', label: '电脑' }],
    undoDepth: 0, writeAttempts: 0,
};
let general = structuredClone(gs);
let inMatch: GsPayload = { ...structuredClone(gs), fields: gs.fields.map((row) =>
    ({ ...row, user: row.user === null ? null : row.user + 5, cpu: row.cpu === null ? null : row.cpu + 5 })) };

function reply(request: Request) {
    let data: unknown;
    const { command } = request;
    if (command === 'readPlayer') {
        data = { index: request.payload.index, name: `Slider Sample ${request.payload.index}`, address: '0x100', team: '', photoId: 0,
            readToken: `slider-test:${++reads}`,
            sections: [
                { name: 'Vitals', fields: [field('Vitals/General/JERSEYNUMBER', 'Jersey Number', 7, 0, 99)] },
                { name: 'Body', fields: [{ ...field('Vitals/Body/HEIGHT', 'Height', 198, 150, 250), section: 'Body' }] },
                { name: 'Contract', fields: [field('Contract/Salary/SALARYYEAR1', 'Salary Year 1', 1000000, 0, 100000000)] },
                { name: 'Attributes', fields: [
                    field('Attributes/Offense/3POINT', '3pt Shot', 85, 25, 110),
                    { ...field('Attributes/Offense/FREETHROW', 'Free Throw', 80, 25, 110), writable: false },
                ] },
                { name: 'Tendencies', fields: [field('Tendencies/Shooting/SHOT3PT', 'Shot 3pt', 50, 0, 100)] },
            ].map((section) => ({ ...section, fields: section.fields.map((cell) => cell.key in playerChanges
                ? { ...cell, value: playerChanges[cell.key], raw: playerChanges[cell.key] } : cell) })) };
    } else if (command === 'readPlayerStats') {
        const stats = (key: string, label: string, value: number) => {
            const cell = field(key, label, value, 0, 100000);
            return key in playerChanges ? { ...cell, value: playerChanges[key], raw: playerChanges[key] } : cell;
        };
        data = { index: 2, name: 'Slider Sample', readToken: `statistics-test:${++reads}`,
            direct: [
                stats('Stats/Career High Stats/POINTS', 'Points', 52),
                { ...stats('Vitals/Awards/PLAYEROFMONTH', 'Player of Month', 3), section: 'Awards' },
            ],
            seasons: [{ selector: 'CURRENTYEARSTATID', id: 101, address: '0x200',
                fields: [
                    stats('SeasonStats/CURRENTYEARSTATID/Participation/GAMESPLAYED', 'Games Played', 40),
                    stats('SeasonStats/CURRENTYEARSTATID/Scoring & Impact/POINTS', 'Points', 1000),
                ] }], warnings: [] };
    } else if (command === 'applyPlayer') {
        Object.assign(playerChanges, request.payload.changes);
        data = { changes: Object.keys(request.payload.changes as object).map((field) => ({ field })), patches: 1 };
    } else if (command === 'undoPlayer') {
        data = {};
    } else if (command === 'connect') {
        data = { pid: 1, roster: 2, teams: 0, seasonEndYear: 2009 };
    } else if (command === 'listTeamRecords' || command === 'listTeams') {
        data = [];
    } else if (command === 'listPlayers') {
        data = [2, 3].map((index) => ({ key: String(index), index, name: `Slider Sample ${index}`,
            team: '', category: 'team', teamIndices: [], summary: {} }));
    } else if (command === 'prospects' || command === 'rookies') {
        data = { items: [], status: 'empty' };
    } else if (command === 'playerRatings') {
        data = { fields: [], rows: [] };
    } else if (command === 'leagueSeason') {
        data = {};
    } else if (command === 'journalList') {
        data = [
            { kind: '球员事务', name: 'edit.json', directory: 'test-journals', bytes: 500, modified: '2026-11-13 12:00',
                detail: { state: 'supported', format: 'nba2k27-player-edit/1', status: 'applied',
                    mode: 'single', time: '2026-11-13T12:00:00+08:00', error: '',
                    changes: [{ subject: 'Slider Sample', index: 2, field: 'Vitals/General/JERSEYNUMBER', before: '7', after: '8' }] } },
            { kind: '预设', name: 'preset.json', directory: 'test-journals', bytes: 40, modified: '2026-11-13 11:00',
                detail: { state: 'unsupported', format: 'nba2k27-player-preset/1', error: '' } },
            { kind: '球员事务', name: 'broken.json', directory: 'test-journals', bytes: 1, modified: '2026-11-13 10:00',
                detail: { state: 'invalid', format: '', error: 'Invalid JSON' } },
        ];
    } else if (command === 'avatarGet') data = { found: false };
    else if (command === 'presetList') data = { directory: '', files: [] };
    else if (['gsRead', 'inMatchRead', 'gsApply', 'inMatchApply', 'gsUndo', 'inMatchUndo'].includes(command)) {
        const current = command.startsWith('gs') ? general : inMatch;
        if (command.endsWith('Apply')) {
            const values = request.payload.changes as Record<string, number>;
            for (const [key, value] of Object.entries(values)) {
                const [side, id] = key.split('.');
                const row = current.fields.find((item) => item.id === id)!;
                row[side as 'user' | 'cpu'] = value;
            }
        }
        data = structuredClone(current);
    } else throw new Error(`Unexpected slider test command: ${command}`);
    for (const listener of listeners) listener({ data: { id: request.id, ok: true, data } });
}
Object.assign(window, { chrome: { webview: {
    addEventListener: (_type: string, listener: (event: { data: unknown }) => void) => listeners.push(listener),
    postMessage: (request: Request) => {
        calls.push(request);
        if (hold && ['gsRead', 'inMatchRead', 'readPlayerStats'].includes(request.command)) held.push(request);
        else queueMicrotask(() => reply(request));
    },
} } });
function Probe() {
    const [view, setView] = useState('player');
    const { setEditing } = useEditing();
    change = (patch) => flushSync(() => {
        if (patch.view !== undefined) setView(patch.view);
        if (patch.editing !== undefined) setEditing(patch.editing);
    });
    const load = useMemo(() => () => readPlayer(2, 'roster'), []);
    return <div style={{ height: '100vh' }}>
        {view === 'shell' ? <Shell /> : view === 'gs' ? <GsPanel /> : view === 'journals' ? <JournalPanel />
            : <FieldEditor index={2} load={load} onClose={() => { closes++; }} />}
    </div>;
}
applyThemeMode('dark');
flushSync(() => createRoot(document.getElementById('root')!).render(
    <ConfigProvider theme={antdTheme('dark')} button={{ autoInsertSpace: false }}>
        <AntApp><EditingProvider><Probe /></EditingProvider></AntApp>
    </ConfigProvider>,
));
Object.assign(window, { sliderMenusHarness: {
    configure: change,
    hold: (value: boolean) => { hold = value; },
    resolve: () => { const request = held.shift(); if (request) reply(request); },
    inMatchUnavailable: () => {
        inMatch = { ...inMatch, fields: inMatch.fields.map((row) => ({ ...row, user: null, cpu: null })),
            notice: '当前比赛设置尚未加载，请进入比赛后重新读取。' };
    },
    snapshot: () => ({ calls: calls.map(({ command, payload }) => ({ command, payload })), pending: held.length, closes }),
} });
