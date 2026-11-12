import model from './data/overall-model.json' with { type: 'json' };

/**
 * Estimated overall after editing a roster player. The linear model was fitted to the game's own
 * cached overalls (see overall-model.json source). It is anchored on the player's real overall:
 * estimate = current + model(after) - model(before), so model bias cancels for small edits.
 */
type Position = keyof typeof model.positions;

export const overallFields: readonly string[] = model.fields;
export const overallModelSource = model.source;

/** Raw model score; prefer estimateOverall when a real current overall is known. */
export function scoreOverall(position: string, values: Record<string, unknown>): number | null {
    return position in model.positions ? score(position as Position, values) : null;
}

function score(position: Position, values: Record<string, unknown>): number | null {
    const params = model.positions[position];
    let total = params.intercept;
    for (let index = 0; index < model.fields.length; index += 1) {
        const value = Number(values[model.fields[index]]);
        if (!Number.isFinite(value)) {
            return null;
        }
        total += params.weights[index] * value;
    }
    return total;
}

/** Returns null when the position or any model input is unknown, or nothing relevant changed. */
export function estimateOverall(position: string | null | undefined, current: number | null | undefined,
    before: Record<string, unknown>, after: Record<string, unknown>): number | null {
    if (!position || !(position in model.positions) || current == null) {
        return null;
    }
    if (!model.fields.some((field) => field in after && Number(after[field]) !== Number(before[field]))) {
        return null;
    }
    const oldScore = score(position as Position, before);
    const newScore = score(position as Position, { ...before, ...after });
    if (oldScore === null || newScore === null) {
        return null;
    }
    return Math.max(25, Math.min(99, Math.round(current + newScore - oldScore)));
}
