/**
 * Transport to the C# host.
 *
 * Inside the WebView2 shell the host injects `window.chrome.webview`; requests carry a
 * correlation id and the host answers with `{ id, ok, data | error }`. When the page is opened
 * in a plain browser, development mode can explicitly select sample data or the local match
 * transport. A normal browser launch without either mode rejects host commands.
 */

import { errorLabel } from './labels';
import { assertGameEditing, isGameMutation } from './editingPolicy';
import { displayReadCache, ReadInvalidatedError } from './readCache';

/** Host failure that also carries the machine-readable code sent by the bridge, if any. */
export class BridgeError extends Error {
    constructor(message: string, readonly code?: string) {
        super(message);
    }
}

export const isBrowserLive = import.meta.env.DEV && new URLSearchParams(window.location.search).has('live');
export const isPreview = import.meta.env.DEV && !isBrowserLive && new URLSearchParams(window.location.search).has('preview');
// Entity links use the hash after startup; retain the initial local transport credential.
const browserSession = isBrowserLive ? new URLSearchParams(window.location.hash.slice(1)).get('session') : null;

type Pending = {
    resolve: (value: unknown) => void;
    reject: (reason: Error) => void;
    timer: number;
};

type WebViewHost = {
    postMessage: (message: unknown) => void;
    addEventListener: (type: 'message', handler: (event: { data: unknown }) => void) => void;
};

const pending = new Map<string, Pending>();
let sequence = 0;
let installed = false;
let connectionAttempt = 0;

function host(): WebViewHost | null {
    const candidate = (window as unknown as { chrome?: { webview?: WebViewHost } }).chrome?.webview;
    return candidate && typeof candidate.postMessage === 'function' ? candidate : null;
}

function install(): boolean {
    if (installed) {
        return true;
    }
    const bridge = host();
    if (!bridge) {
        return false;
    }
    bridge.addEventListener('message', (event) => {
        const payload = event.data as { id?: string; ok?: boolean; data?: unknown; error?: string; code?: string } | null;
        if (!payload || typeof payload.id !== 'string') {
            return;
        }
        const entry = pending.get(payload.id);
        if (!entry) {
            return;
        }
        pending.delete(payload.id);
        window.clearTimeout(entry.timer);
        if (payload.ok) {
            entry.resolve(payload.data);
        } else {
            entry.reject(new BridgeError(errorLabel(payload.error ?? '未知错误'), payload.code));
        }
    });
    installed = true;
    return true;
}

export function hasHost(): boolean {
    return install();
}

/** Last transport failure, surfaced in the UI so a failed attach is never silent. */
export function lastError(): string {
    return (window as unknown as { __bridgeError?: string }).__bridgeError ?? '';
}

function remember(message: string) {
    (window as unknown as { __bridgeError?: string }).__bridgeError = message;
}

export async function call<T>(command: string, payload: Record<string, unknown> = {}): Promise<T> {
    assertGameEditing(command, payload);
    const reconnect = command === 'connect';
    const mutation = isGameMutation(command, payload);
    const attempt = reconnect ? ++connectionAttempt : connectionAttempt;
    if (reconnect) displayReadCache.setConnection(null);
    else if (mutation) displayReadCache.invalidate();
    try {
        const result = await request<T>(command, payload);
        if (reconnect) {
            if (attempt !== connectionAttempt) throw new ReadInvalidatedError();
            const session = result as { pid?: number; base?: string };
            displayReadCache.setConnection(`${session.pid ?? 'unknown'}:${session.base ?? 'unknown'}:${attempt}`);
        }
        return result;
    } finally {
        // A failed or partially applied write cannot preserve a pre-write display snapshot.
        if (mutation) displayReadCache.invalidate();
    }
}

async function request<T>(command: string, payload: Record<string, unknown>): Promise<T> {
    if (isBrowserLive) {
        const token = browserSession;
        if (!token) {
            throw new Error('缺少本机开发连接凭证，请使用当前开发连接打开页面');
        }
        try {
            const response = await fetch('http://127.0.0.1:5182/bridge', {
                method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Match-Session': token },
                body: JSON.stringify({ id: `b${++sequence}`, command, payload }),
                signal: AbortSignal.timeout(120_000),
            });
            const reply = await response.json() as { ok?: boolean; data?: T; error?: string; code?: string };
            if (!response.ok || !reply.ok) {
                throw new BridgeError(reply.error ?? '本机开发接口调用失败', reply.code);
            }
            return reply.data as T;
        } catch (error) {
            const message = error instanceof TypeError ? '本机游戏后端未连接，请启动场内开发连接' : (error as Error).message;
            remember(message);
            throw new BridgeError(message, error instanceof BridgeError ? error.code : undefined);
        }
    }
    if (isPreview) {
        return (await import('./preview')).previewCall(command, payload) as Promise<T>;
    }
    if (!install()) {
        const missing = '当前不在桌面宿主中运行：请使用 Nba2k27Editor.exe 打开本界面';
        remember(missing);
        throw new Error(missing);
    }
    const bridge = host();
    if (!bridge) {
        const missing = '桌面宿主不可用';
        remember(missing);
        throw new Error(missing);
    }
    const id = `r${++sequence}`;
    return new Promise<T>((resolve, reject) => {
        const timer = window.setTimeout(() => {
            pending.delete(id);
            remember(`调用超时：${command}`);
            reject(new Error(`调用超时：${command}`));
        }, 120_000);
        pending.set(id, {
            resolve: resolve as (value: unknown) => void,
            reject: (error: Error) => {
                remember(error.message);
                reject(error);
            },
            timer,
        });
        try {
            bridge.postMessage({ id, command, payload });
        } catch (error) {
            window.clearTimeout(timer);
            pending.delete(id);
            remember(`发送失败：${(error as Error).message}`);
            reject(error as Error);
        }
    });
}
