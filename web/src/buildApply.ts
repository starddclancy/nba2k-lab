import { attributes, defaultExtraRating, extraAttributes } from './builds.ts';
import type { BuildPlan } from './builds.ts';
import { scoreOverall } from './overall.ts';

/** Builder attribute id -> roster field key, in the units the editor reads and writes. */
export const ratingFields: Record<string, string> = {
    closeShot: 'Attributes/Offense/CLOSESHOT', drivingLayup: 'Attributes/Offense/DRIVINGLAYUP',
    drivingDunk: 'Attributes/Offense/DRIVINGDUNK', standingDunk: 'Attributes/Offense/STANDINGDUNK',
    postControl: 'Attributes/Offense/POSTCONTROL', midRange: 'Attributes/Offense/MIDRANGE',
    threePoint: 'Attributes/Offense/3POINT', freeThrow: 'Attributes/Offense/FREETHROW',
    passAccuracy: 'Attributes/Offense/PASSACCURACY', ballHandle: 'Attributes/Offense/BALLCONTROL',
    speedWithBall: 'Attributes/Athleticism/SPEEDWITHBALL', interiorDefense: 'Attributes/Defense/INTERIORDEFENSE',
    perimeterDefense: 'Attributes/Defense/PERIMETERDEFENSE', steal: 'Attributes/Defense/STEAL',
    block: 'Attributes/Defense/BLOCK', offensiveRebound: 'Attributes/Offense/OFFENSIVEREBOUND',
    defensiveRebound: 'Attributes/Defense/DEFENSEREBOUND', speed: 'Attributes/Athleticism/SPEED',
    agility: 'Attributes/Athleticism/AGILITY', strength: 'Attributes/Athleticism/STRENGTH',
    vertical: 'Attributes/Athleticism/VERTICAL',
    drawFoul: 'Attributes/Offense/DRAWFOUL', hands: 'Attributes/Offense/HANDS',
    shotIq: 'Attributes/Offense/IQSHOT', passIq: 'Attributes/Offense/PASSIQ',
    passVision: 'Attributes/Offense/PASSVISION', postFade: 'Attributes/Offense/POSTFADE',
    postHook: 'Attributes/Offense/POSTHOOK', stamina: 'Attributes/Athleticism/STAMINA',
    hustle: 'Attributes/Athleticism/HUSTLE', helpDefense: 'Attributes/Mental/HELPDEFENSE',
    passPerception: 'Attributes/Mental/PASSPERCEPTION', reactionTime: 'Attributes/Misc/REACTIONTIME',
    offensiveConsistency: 'Attributes/Mental/OFFENSIVECONSISTENCY',
    defensiveConsistency: 'Attributes/Mental/DEFENSECONSISTENCY',
    intangibles: 'Attributes/Misc/INTANGIBLES', potential: 'Attributes/Misc/POTENTIAL',
};
export const bodyFields = {
    position: 'Vitals/Vitals/POSITION', height: 'Vitals/Body/HEIGHT',
    weight: 'Vitals/Body/WEIGHT', wingspan: 'Vitals/Body/WINGSPANINTENTHSOFMM',
};
const CM_PER_INCH = 2.54;
const KG_PER_POUND = 0.45359237;

/** The plan as roster field values: centimetres, kilograms and rating display values. */
export function planValues(plan: BuildPlan): Record<string, number | string> {
    const values: Record<string, number | string> = {
        [bodyFields.position]: plan.position,
        [bodyFields.height]: Number((plan.heightInches * CM_PER_INCH).toFixed(2)),
        [bodyFields.weight]: Number((plan.weightLbs * KG_PER_POUND).toFixed(3)),
        [bodyFields.wingspan]: Number((plan.wingspanInches * CM_PER_INCH).toFixed(2)),
    };
    for (const { id } of attributes) {
        values[ratingFields[id]] = plan.ratings[id];
    }
    for (const { id } of extraAttributes) {
        values[ratingFields[id]] = plan.extraRatings?.[id] ?? defaultExtraRating;
    }
    return values;
}

/** Overall estimated from all 37 attributes plus height; see overall-model.json for accuracy. */
export function planOverall(plan: BuildPlan): number | null {
    const score = scoreOverall(plan.position, planValues(plan));
    return score === null ? null : Math.max(25, Math.min(99, Math.round(score)));
}

export type PlanChange = { key: string; before: unknown; after: number | string };

/**
 * Fields that differ between the player's current values and the plan. Body values compare with
 * a tolerance of half a stored unit so a round trip through inches does not rewrite them.
 */
export function planChanges(plan: BuildPlan, current: Record<string, unknown>): PlanChange[] {
    const tolerance: Record<string, number> = {
        [bodyFields.height]: CM_PER_INCH / 2, [bodyFields.wingspan]: CM_PER_INCH / 2,
        [bodyFields.weight]: KG_PER_POUND / 2,
    };
    return Object.entries(planValues(plan)).flatMap(([key, after]) => {
        const before = current[key];
        if (!(key in current)) {
            return [];
        }
        const same = typeof after === 'number'
            ? Math.abs(Number(before) - after) < (tolerance[key] ?? 0.5)
            : String(before) === after;
        return same ? [] : [{ key, before, after }];
    });
}
