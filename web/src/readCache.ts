export type CachedDisplayRead<T> = {
    value: T;
    readAt: number;
    stale: boolean;
};

type CacheOptions = {
    capacity?: number;
    freshMs?: number;
    retainMs?: number;
    now?: () => number;
};

type Entry = { value: unknown; readAt: number };
type PendingRead = { promise: Promise<unknown> };

/** An obsolete read must not update a panel, even if the transport eventually succeeds. */
export class ReadInvalidatedError extends Error {
    constructor() {
        super('Display read invalidated');
        this.name = 'ReadInvalidatedError';
    }
}

/**
 * Display data only. Cached read tokens retain their original meaning and lifetime;
 * callers must invalidate and read again before enabling a new editing session.
 * This cache owns no polling, prefetching or transport cancellation.
 */
export class DisplayReadCache {
    private readonly entries = new Map<string, Entry>();
    private readonly pending = new Map<string, PendingRead>();
    private readonly capacity: number;
    private readonly freshMs: number;
    private readonly retainMs: number;
    private readonly now: () => number;
    private generation = 0;
    private identity: string | null = null;

    constructor(options: CacheOptions = {}) {
        this.capacity = options.capacity ?? 96;
        this.freshMs = options.freshMs ?? 15_000;
        this.retainMs = options.retainMs ?? 300_000;
        this.now = options.now ?? Date.now;
        if (!Number.isInteger(this.capacity) || this.capacity <= 0
            || !Number.isFinite(this.freshMs) || this.freshMs < 0
            || !Number.isFinite(this.retainMs) || this.retainMs <= 0 || this.retainMs < this.freshMs) {
            throw new RangeError('Invalid display cache capacity or lifetime');
        }
    }

    get connectionIdentity(): string | null {
        return this.identity;
    }

    /** Call for every connection event, including reconnects to the same game process. */
    setConnection(identity: string | null): number {
        this.identity = identity;
        ++this.generation;
        this.invalidate();
        return this.generation;
    }

    /** Retained stale data can be shown immediately while the visible panel revalidates. */
    peek<T>(key: string): CachedDisplayRead<T> | undefined {
        const entry = this.entries.get(key);
        if (!entry) return undefined;
        const age = this.now() - entry.readAt;
        if (age >= this.retainMs) {
            this.entries.delete(key);
            return undefined;
        }
        this.entries.delete(key);
        this.entries.set(key, entry);
        return { value: entry.value as T, readAt: entry.readAt, stale: age >= this.freshMs };
    }

    /**
     * Force bypasses a saved value and still shares a current in-flight read.
     * Invalidate the key first when a new read must supersede an earlier request.
     */
    read<T>(key: string, loader: () => Promise<T>, options: { force?: boolean } = {}): Promise<T> {
        const existing = this.pending.get(key);
        if (existing) return existing.promise as Promise<T>;
        const cached = this.peek<T>(key);
        if (cached && !cached.stale && !options.force) return Promise.resolve(cached.value);

        const generation = this.generation;
        let flight: PendingRead;
        const current = () => generation === this.generation && this.pending.get(key) === flight;
        const promise = Promise.resolve().then(() => {
            if (!current()) throw new ReadInvalidatedError();
            return loader();
        }).then((value) => {
            if (!current()) throw new ReadInvalidatedError();
            const readAt = this.now();
            for (const [savedKey, entry] of this.entries) {
                if (readAt - entry.readAt >= this.retainMs) this.entries.delete(savedKey);
            }
            this.entries.delete(key);
            this.entries.set(key, { value, readAt });
            while (this.entries.size > this.capacity) {
                this.entries.delete(this.entries.keys().next().value!);
            }
            return value;
        }).catch((error: unknown) => {
            if (!current()) throw new ReadInvalidatedError();
            throw error;
        }).finally(() => {
            if (current()) this.pending.delete(key);
        });
        flight = { promise };
        this.pending.set(key, flight);
        return promise;
    }

    /** Invalidate values and pending reads after writes, undo, import or session changes. */
    invalidate(selection?: string | ((key: string) => boolean)): void {
        const matches = typeof selection === 'function' ? selection
            : (key: string) => selection === undefined || key === selection;
        for (const key of this.entries.keys()) {
            if (matches(key)) this.entries.delete(key);
        }
        for (const key of this.pending.keys()) {
            if (matches(key)) this.pending.delete(key);
        }
    }
}

export const displayReadCache = new DisplayReadCache();
