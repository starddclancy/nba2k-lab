import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ReadInvalidatedError } from './readCache';

export type DisplayReader<T> = {
    peek: () => { value: T; readAt: number } | undefined;
    read: (force: boolean) => Promise<T>;
    fresh: () => Promise<T>;
};

type DisplayState<T> = {
    identity: string;
    data: T | null;
    readAt: number | null;
    busy: boolean;
    refreshing: boolean;
    error: string;
    editContext: object | null;
};

function displaySignature(value: unknown): string | undefined {
    return JSON.stringify(value, (key, item: unknown) => key === 'readToken' ? undefined : item);
}

/** Refresh only the visible subject; editing keeps its exact write snapshot until apply or reload. */
export function useDisplayRead<T>({ identity, reader, editing, dirty, enabled = true, onRead }: {
    identity: string;
    reader: DisplayReader<T>;
    editing: boolean;
    dirty: boolean;
    enabled?: boolean;
    onRead?: (value: T) => void;
}) {
    const context = useMemo(() => ({}), [identity, editing]);
    const [state, setState] = useState<DisplayState<T>>(() => {
        const saved = reader.peek();
        return { identity, data: saved?.value ?? null, readAt: saved?.readAt ?? null,
            busy: enabled && !saved, refreshing: false, error: '', editContext: null };
    });
    const editReady = editing && enabled && state.editContext === context && !state.busy;
    const current = useRef({ identity, context, reader, editing, dirty, enabled, editReady, onRead });
    current.current = { identity, context, reader, editing, dirty, enabled, editReady, onRead };
    const requestId = useRef(0);
    const pending = useRef(false);

    const reload = useCallback(async (strict = false, background = false) => {
        const snapshot = current.current;
        if (!snapshot.enabled || background && (snapshot.editing || snapshot.dirty
            || document.visibilityState !== 'visible' || pending.current)) return;
        const request = ++requestId.current;
        pending.current = true;
        const active = () => request === requestId.current
            && current.current.context === snapshot.context && current.current.enabled;
        setState((previous) => {
            const saved = snapshot.reader.peek();
            const data = previous.identity === snapshot.identity ? previous.data : saved?.value ?? null;
            return { ...previous, identity: snapshot.identity, data, busy: strict || data === null,
                refreshing: background, error: '', editContext: strict ? null : previous.editContext };
        });
        try {
            const value = strict ? await snapshot.reader.fresh() : await snapshot.reader.read(background);
            if (!active()) return;
            const saved = snapshot.reader.peek();
            setState((previous) => {
                const unchanged = !strict && !snapshot.editing && previous.identity === snapshot.identity
                    && previous.data !== null && displaySignature(previous.data) === displaySignature(value);
                return { identity: snapshot.identity, data: unchanged ? previous.data : value,
                    readAt: saved?.readAt ?? Date.now(), busy: false, refreshing: false, error: '',
                    editContext: strict && snapshot.editing ? snapshot.context : null };
            });
            snapshot.onRead?.(value);
        } catch (error) {
            if (!active()) return;
            setState((previous) => ({ ...previous, busy: false, refreshing: false,
                error: error instanceof ReadInvalidatedError ? '' : (error as Error).message,
                editContext: null }));
        } finally {
            if (request === requestId.current) pending.current = false;
        }
    }, []);

    useEffect(() => {
        const saved = reader.peek();
        const prepared = editing && state.editContext === context;
        setState((previous) => {
            const same = previous.identity === identity;
            const data = same ? previous.data ?? saved?.value ?? null : saved?.value ?? null;
            return { identity, data, readAt: same && previous.data !== null ? previous.readAt : saved?.readAt ?? null,
                busy: enabled && (data === null || editing && !prepared), refreshing: false,
                error: '', editContext: prepared ? context : null };
        });
        if (enabled && !prepared && document.visibilityState === 'visible') void reload(editing);
        return () => { ++requestId.current; pending.current = false; };
    }, [identity, context, enabled, reload]);

    useEffect(() => {
        const refresh = () => {
            const latest = current.current;
            if (document.visibilityState === 'visible' && latest.enabled
                && !latest.editing && !latest.dirty && !pending.current) {
                void reload(latest.editing, !latest.editing);
            }
        };
        if (!enabled) return;
        const timer = window.setInterval(refresh, 5_000);
        const resume = () => {
            const latest = current.current;
            if (document.visibilityState === 'visible' && latest.enabled && !latest.dirty && !pending.current) {
                if (latest.editing && latest.editReady) return;
                void reload(latest.editing, !latest.editing);
            }
        };
        document.addEventListener('visibilitychange', resume);
        return () => {
            window.clearInterval(timer);
            document.removeEventListener('visibilitychange', resume);
        };
    }, [identity, enabled, reload]);

    const sameIdentity = state.identity === identity;
    const saved = sameIdentity ? undefined : reader.peek();
    return { identity, data: sameIdentity ? state.data : saved?.value ?? null,
        readAt: sameIdentity ? state.readAt : saved?.readAt ?? null,
        busy: sameIdentity ? state.busy : enabled && (!saved || editing),
        refreshing: sameIdentity && state.refreshing, error: sameIdentity ? state.error : '', editReady,
        reload: () => reload(true) };
}
