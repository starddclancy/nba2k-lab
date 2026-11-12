import React, { useMemo, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { flushSync } from 'react-dom';
import { App as AntApp, ConfigProvider } from 'antd';
import FieldEditor from '../../src/components/FieldEditor';
import { EditingProvider, useEditing } from '../../src/editingContext';
import { readPlayer } from '../../src/api';
import type { FieldCell } from '../../src/types';

type Request = { id: string; command: string; payload: Record<string, unknown> };
type Settings = { index: number; source: string; shown: boolean };
const listeners: ((event: { data: unknown }) => void)[] = [];
const calls: Request[] = [];
const held: Request[] = [];
let hold = false;
let unknown = false;
let reads = 0;
let change!: (patch: Partial<Settings> & { editing?: boolean }) => void;
const field = (value: string): FieldCell => ({
    key: 'Vitals/General/POSITION', label: 'POSITION', section: 'Vitals', group: 'General',
    kind: 'choice', value, display: value, writable: true, options: ['PG', 'SG', 'UNKNOWN_POSITION'],
});

function reply(request: Request) {
    const { command, payload } = request;
    let data: unknown;
    if (command === 'readPlayer') {
        data = { index: payload.index, name: `Player ${payload.index}`, team: '', photoId: 0,
            address: '0x100', readToken: `read:${payload.source}:${payload.index}:${++reads}`,
            sections: [{ name: 'Vitals', fields: [field('PG')] }] };
    } else if (command === 'importPlayer') {
        const value = unknown ? 'UNKNOWN_POSITION' : 'SG';
        data = { cancelled: false, index: payload.index, source: payload.source,
            fileName: 'backup.json', readToken: `import:${payload.source}:${payload.index}`,
            changeCount: 1, skipped: 0, requestChanges: { 'Vitals/General/POSITION': value },
            changes: [{ field: 'Vitals/General/POSITION', label: 'POSITION', before: 0, after: 1,
                beforeField: field('PG'), afterField: field(value) }] };
    } else if (command === 'applyPlayer') {
        data = { changes: [{ field: 'Vitals/General/POSITION', before: 'PG', after: 'SG' }], patches: 1 };
    } else if (command === 'avatarGet') {
        data = { found: false };
    } else {
        throw new Error(`Unexpected simulated command: ${command}`);
    }
    for (const listener of listeners) listener({ data: { id: request.id, ok: true, data } });
}

Object.assign(window, { chrome: { webview: {
    addEventListener: (_type: string, listener: (event: { data: unknown }) => void) => listeners.push(listener),
    postMessage: (request: Request) => {
        calls.push(request);
        if (hold && request.command === 'importPlayer') held.push(request);
        else queueMicrotask(() => reply(request));
    },
} } });

function Probe() {
    const [settings, setSettings] = useState<Settings>({ index: 2, source: 'roster', shown: true });
    const { setEditing } = useEditing();
    change = ({ editing, ...patch }) => {
        flushSync(() => {
            setSettings((previous) => ({ ...previous, ...patch }));
            if (editing !== undefined) setEditing(editing);
        });
    };
    const load = useMemo(() => () => readPlayer(settings.index, settings.source), [settings.index, settings.source]);
    return settings.shown ? <FieldEditor index={settings.index} source={settings.source} load={load}
        onClose={() => setSettings((previous) => ({ ...previous, shown: false }))} /> : <span>Closed</span>;
}

const root = createRoot(document.getElementById('root')!);
flushSync(() => root.render(<ConfigProvider button={{ autoInsertSpace: false }}>
    <AntApp><EditingProvider><Probe /></EditingProvider></AntApp>
</ConfigProvider>));
Object.assign(window, { playerImportHarness: {
    configure: change,
    hold: (value: boolean) => { hold = value; },
    unknown: (value: boolean) => { unknown = value; },
    resolve: () => { const request = held.shift(); if (request) reply(request); },
    snapshot: () => ({ calls: calls.map((request) => ({ command: request.command, payload: request.payload })),
        pending: held.length }),
} });
