import { useEffect, useRef, useState } from 'react';

const reduced = () => typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;

/** easeOutCubic, the curve used by every count-up so numbers settle softly. */
export const ease = (t: number) => 1 - (1 - t) ** 3;

/**
 * Animate a number from its previous value to the new one in about 600 ms with
 * requestAnimationFrame. Returns the integer to display. Reduced-motion users get the value as is.
 */
export function useCountUp(target: number | null | undefined, duration = 600): number | null {
    const [shown, setShown] = useState<number | null>(target ?? null);
    const from = useRef<number>(0);
    useEffect(() => {
        if (target === null || target === undefined) {
            setShown(null);
            return;
        }
        if (reduced()) {
            setShown(target);
            from.current = target;
            return;
        }
        const start = performance.now();
        const origin = from.current;
        let frame = 0;
        const step = (now: number) => {
            const t = Math.min(1, (now - start) / duration);
            setShown(Math.round(origin + (target - origin) * ease(t)));
            if (t < 1) {
                frame = requestAnimationFrame(step);
            } else {
                from.current = target;
            }
        };
        frame = requestAnimationFrame(step);
        return () => cancelAnimationFrame(frame);
    }, [target, duration]);
    return shown;
}
