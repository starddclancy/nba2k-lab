import React, { useMemo, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { flushSync } from 'react-dom';
import { DisplayReadCache } from '../../src/readCache';
import { useDisplayRead } from '../../src/useDisplayRead';

type Value = { name: string; readToken: string };
let time = 0;
let visible = true;
const cache = new DisplayReadCache({ now: () => time });
const timers = new Map<number, () => void>();
let nextTimer = 0;
window.setInterval = ((callback: () => void, delay: number) => {
    if (delay !== 5_000) throw new Error(`Unexpected refresh interval: ${delay}`);
    timers.set(++nextTimer, callback);
    return nextTimer;
}) as typeof window.setInterval;
window.clearInterval = (id) => { timers.delete(id); };
Object.defineProperty(document, 'visibilityState', { get: () => visible ? 'visible' : 'hidden' });

const requests: { identity: string; resolve: (value: Value) => void; reject: (error: Error) => void }[] = [];
const requestLog: string[] = [];
const references = new WeakMap<object, number>();
let nextReference = 0;
let latest: ReturnType<typeof useDisplayRead<Value>>;
let change!: (patch: Partial<Settings>) => void;
type Settings = { identity: string; editing: boolean; enabled: boolean; dirty: boolean };
const frames: { editing: boolean; editReady: boolean; token: string | null }[] = [];

function Probe() {
    const [settings, setSettings] = useState<Settings>({ identity: 'A', editing: false,
        enabled: !new URLSearchParams(location.search).has('disabled'), dirty: false });
    change = (patch) => { flushSync(() => setSettings((before) => ({ ...before, ...patch }))); };
    const reader = useMemo(() => {
        const load = () => {
            requestLog.push(settings.identity);
            return new Promise<Value>((resolve, reject) => requests.push({ identity: settings.identity, resolve, reject }));
        };
        return {
            peek: () => cache.peek<Value>(settings.identity),
            read: (force: boolean) => cache.read(settings.identity, load, { force }),
            fresh: () => { cache.invalidate(settings.identity); return cache.read(settings.identity, load, { force: true }); },
        };
    }, [settings.identity]);
    latest = useDisplayRead({ ...settings, reader });
    frames.push({ editing: settings.editing, editReady: latest.editReady, token: latest.data?.readToken ?? null });
    return <output>{latest.data?.name ?? 'empty'}</output>;
}

await cache.read('A', async () => ({ name: 'Alpha', readToken: 'cached-A' }));
await cache.read('B', async () => ({ name: 'Beta', readToken: 'cached-B' }));
const root = createRoot(document.getElementById('root')!);
flushSync(() => root.render(<Probe />));

Object.assign(window, {
    displayReadHarness: {
        configure: change,
        reload: () => { void latest.reload(); },
        tick: (elapsed = 5_000) => {
            time += elapsed;
            for (const callback of timers.values()) callback();
        },
        visible: (value: boolean) => {
            visible = value;
            document.dispatchEvent(new Event('visibilitychange'));
        },
        resolve: (value: Value, index = 0) => { requests.splice(index, 1)[0].resolve(value); },
        fail: () => { requests.shift()!.reject(new Error('read unavailable')); },
        snapshot: () => {
            if (latest.data && !references.has(latest.data)) references.set(latest.data, ++nextReference);
            return { data: latest.data, reference: latest.data ? references.get(latest.data) : null,
                readAt: latest.readAt, busy: latest.busy, refreshing: latest.refreshing,
                error: latest.error, editReady: latest.editReady, requestLog: [...requestLog],
                pending: requests.map((request) => request.identity), frames: [...frames] };
        },
    },
});
