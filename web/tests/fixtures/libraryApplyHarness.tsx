import React, { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { flushSync } from 'react-dom';
import { App as AntApp, ConfigProvider } from 'antd';
import LibraryPanel from '../../src/components/LibraryPanel';
import { EditingProvider, useEditing } from '../../src/editingContext';
import type { FieldCell } from '../../src/types';

type Request = { id: string; command: string; payload: Record<string, unknown> };
const listeners: ((event: { data: unknown }) => void)[] = [];
const calls: Request[] = [];
const held: Request[] = [];
let hold: 'readPlayer' | 'libraryPreview' | null = null;
let failRead = false;
let reads = 0;
let change!: (patch: { editing?: boolean; shown?: boolean }) => void;
const entry = { id: 'library-entry', name: 'Test Entry', kind: 'player',
    created: '2026-11-10T10:00:00+08:00', sourcePlayer: 'Source Player', fieldCount: 1, notes: '' };
const field = (value: string): FieldCell => ({
    key: 'Vitals/General/POSITION', label: 'POSITION', section: 'Vitals', group: 'General',
    kind: 'choice', value, display: value, writable: true, options: ['PG', 'SG'],
});

function reply(request: Request) {
    const { command, payload } = request;
    if (command === 'readPlayer' && failRead) {
        for (const listener of listeners) {
            listener({ data: { id: request.id, ok: false, error: 'Simulated fresh read failed' } });
        }
        return;
    }
    let data: unknown;
    if (command === 'libraryList') {
        data = [entry];
    } else if (command === 'listPlayers') {
        data = [2, 3].map((index) => ({ key: String(index), index, name: `Player ${index}`, team: '' }));
    } else if (command === 'readPlayer') {
        data = { index: payload.index, name: `Player ${payload.index}`, team: '', address: '0x100',
            readToken: `read:roster:${payload.index}:${++reads}`,
            sections: [{ name: 'Vitals', fields: [field('PG')] }] };
    } else if (command === 'libraryApply') {
        data = { entry, index: payload.index, player: `Player ${payload.index}`, scope: payload.scope,
            changeCount: 1, skipped: 0, applied: payload.preview === false,
            changes: [{ field: field('PG').key, label: 'POSITION', before: 0, after: 1,
                beforeField: field('PG'), afterField: field('SG') }] };
    } else if (command === 'undoPlayer') {
        data = { changes: [], patches: 1 };
    } else {
        throw new Error(`Unexpected simulated command: ${command}`);
    }
    for (const listener of listeners) listener({ data: { id: request.id, ok: true, data } });
}

Object.assign(window, { chrome: { webview: {
    addEventListener: (_type: string, listener: (event: { data: unknown }) => void) => listeners.push(listener),
    postMessage: (request: Request) => {
        calls.push(request);
        if (hold === request.command || hold === 'libraryPreview'
            && request.command === 'libraryApply' && request.payload.preview === true) {
            held.push(request);
        } else {
            queueMicrotask(() => reply(request));
        }
    },
} } });

function Probe() {
    const [shown, setShown] = useState(true);
    const { setEditing } = useEditing();
    change = ({ editing, shown: nextShown }) => {
        flushSync(() => {
            if (editing !== undefined) setEditing(editing);
            if (nextShown !== undefined) setShown(nextShown);
        });
    };
    return shown ? <LibraryPanel /> : <span>Closed</span>;
}

const root = createRoot(document.getElementById('root')!);
flushSync(() => root.render(<ConfigProvider button={{ autoInsertSpace: false }}>
    <AntApp><EditingProvider><Probe /></EditingProvider></AntApp>
</ConfigProvider>));
Object.assign(window, { libraryApplyHarness: {
    configure: change,
    hold: (value: typeof hold) => { hold = value; },
    failRead: (value: boolean) => { failRead = value; },
    resolve: () => { const request = held.shift(); if (request) reply(request); },
    snapshot: () => ({
        calls: calls.map((request) => ({ command: request.command, payload: request.payload })),
        pending: held.map((request) => ({ command: request.command, payload: request.payload })),
    }),
} });
