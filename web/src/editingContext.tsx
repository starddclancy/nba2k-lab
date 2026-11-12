import { createContext, useCallback, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { App as AntApp, Switch } from 'antd';
import { setGameEditing } from './editingPolicy';

type DraftProtection = { count: number; busy: boolean };
const EditingContext = createContext({
    editing: false,
    setEditing: (_value: boolean) => {},
    protect: (_owner: object, _state: DraftProtection) => () => {},
    confirmDiscard: async (_count: number) => true,
    requestLeave: async () => true,
});

export function EditingProvider({ children }: { children: ReactNode }) {
    const [editing, updateEditing] = useState(false);
    const { modal, message } = AntApp.useApp();
    const guards = useRef(new Map<object, DraftProtection>());
    const dialog = useRef<Promise<boolean> | null>(null);
    const protect = useCallback((owner: object, state: DraftProtection) => {
        guards.current.set(owner, state);
        return () => { guards.current.delete(owner); };
    }, []);
    const confirmDiscard = useCallback((count: number): Promise<boolean> => {
        if (count === 0) return Promise.resolve(true);
        if (dialog.current) return dialog.current;
        const snapshot = [...guards.current];
        const current = () => snapshot.length === guards.current.size
            && snapshot.every(([key, value]) => guards.current.get(key) === value);
        dialog.current = new Promise<boolean>((resolve) => {
            modal.confirm({
                title: '有未应用的修改',
                content: `${count} 项修改尚未应用，是否放弃草稿？`,
                okText: '放弃草稿',
                cancelText: '保留草稿',
                onOk: () => resolve(current()),
                onCancel: () => resolve(false),
                afterClose: () => resolve(false),
            });
        }).finally(() => { dialog.current = null; });
        return dialog.current;
    }, [modal]);
    const requestLeave = useCallback(async () => {
        const states = [...guards.current.values()];
        if (states.some((state) => state.busy)) {
            message.info('请等待当前操作完成');
            return false;
        }
        return confirmDiscard(states.reduce((sum, state) => sum + state.count, 0));
    }, [confirmDiscard, message]);
    useLayoutEffect(() => {
        const scope = window as unknown as {
            __requestEditorClose?: (requestId: string) => Promise<void>;
            chrome?: { webview?: { postMessage: (value: unknown) => void } };
        };
        const requestClose = async (requestId: string) => {
            let allowed = false;
            try { allowed = await requestLeave(); }
            finally {
                scope.chrome?.webview?.postMessage({ kind: 'editorCloseDecision', requestId, allowed });
            }
        };
        scope.__requestEditorClose = requestClose;
        return () => {
            if (scope.__requestEditorClose === requestClose) delete scope.__requestEditorClose;
        };
    }, [requestLeave]);
    useEffect(() => {
        const unload = (event: BeforeUnloadEvent) => {
            if ([...guards.current.values()].some((state) => state.count > 0 || state.busy)) {
                event.preventDefault();
                event.returnValue = '';
            }
        };
        window.addEventListener('beforeunload', unload);
        return () => window.removeEventListener('beforeunload', unload);
    }, []);
    const setEditing = useCallback((value: boolean) => {
        setGameEditing(value);
        updateEditing(value);
    }, []);
    useEffect(() => () => setGameEditing(false), []);
    const value = useMemo(() => ({ editing, setEditing, protect, confirmDiscard, requestLeave }),
        [editing, setEditing, protect, confirmDiscard, requestLeave]);
    return <EditingContext.Provider value={value}>{children}</EditingContext.Provider>;
}

export const useEditing = () => useContext(EditingContext);

/** Register the player editor once, including its independent statistics draft. */
export function useDraftProtection(count: number, busy: boolean, identity: string) {
    const { editing, protect } = useEditing();
    const owner = useMemo(() => ({}), []);
    const state = useMemo(() => ({ count, busy }), [count, busy, identity, editing]);
    useLayoutEffect(() => protect(owner, state), [owner, state, protect]);
}

export function EditingSwitch() {
    const { editing, setEditing } = useEditing();
    return <span className="editing-switch">
        <span>{editing ? '编辑' : '浏览'}</span>
        <Switch size="small" aria-label="编辑开关" checked={editing} onChange={setEditing} />
    </span>;
}
