import { useEffect, useState } from 'react';
import { call } from './bridge';
import type { SeasonGames } from './seasonGames';

/** Page-local read; a reconnect/remount never reuses another process or roster's data. */
export function useSeasonGames(refreshKey = 0): { data: SeasonGames | null; error: string | null; loading: boolean; reload: () => void } {
    const [state, setState] = useState<{ data: SeasonGames | null; error: string | null; loading: boolean }>({
        data: null, error: null, loading: true,
    });
    const [tick, setTick] = useState(0);
    useEffect(() => {
        let alive = true;
        setState((current) => ({ ...current, loading: true }));
        void call<SeasonGames>('seasonGames')
            .then((data) => { if (alive) setState({ data, error: null, loading: false }); })
            .catch((error: Error) => { if (alive) setState({ data: null, error: error.message, loading: false }); });
        return () => { alive = false; };
    }, [refreshKey, tick]);
    return { ...state, reload: () => setTick((n) => n + 1) };
}
