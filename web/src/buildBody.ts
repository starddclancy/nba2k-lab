import body from './data/build-body.json' with { type: 'json' };

/** Published MyPLAYER body templates (NBA2KLab builder data); see build-body.json sources. */
export type BodyTemplate = {
    position: string; height: number;
    weightMin: number; weightMax: number; wingspanMin: number; wingspanMax: number;
};
export type BodyShape = { position: string; heightInches: number; weightLbs: number; wingspanInches: number };

export const bodySource = { ...body.sources[0], capturedAt: body.capturedAt, updatedAt: body.sourceUpdatedAt };
const templates = body.templates as BodyTemplate[];

export function heightsFor(position: string): number[] {
    return templates.filter((item) => item.position === position).map((item) => item.height).sort((a, b) => a - b);
}

export function templateFor(position: string, height: number): BodyTemplate | null {
    return templates.find((item) => item.position === position && item.height === height) ?? null;
}

const clamp = (value: number, low: number, high: number) => Math.max(low, Math.min(high, value));

/**
 * Move a body onto the nearest legal combination: height within the position's range, then
 * weight and wingspan within that height's template. Returns the shape unchanged when legal.
 */
export function legalBody<T extends BodyShape>(shape: T): T {
    const heights = heightsFor(shape.position);
    if (!heights.length) {
        return shape;
    }
    const heightInches = clamp(Math.round(shape.heightInches), heights[0], heights[heights.length - 1]);
    const template = templateFor(shape.position, heightInches);
    if (!template) {
        return shape;
    }
    const next = {
        ...shape, heightInches,
        weightLbs: clamp(Math.round(shape.weightLbs), template.weightMin, template.weightMax),
        wingspanInches: clamp(Math.round(shape.wingspanInches), template.wingspanMin, template.wingspanMax),
    };
    return next.heightInches === shape.heightInches && next.weightLbs === shape.weightLbs
        && next.wingspanInches === shape.wingspanInches ? shape : next;
}

export function bodyIssues(shape: BodyShape): string[] {
    const heights = heightsFor(shape.position);
    if (!heights.length) {
        return [`没有 ${shape.position} 的身体模板`];
    }
    const template = templateFor(shape.position, shape.heightInches);
    if (!template) {
        return [`${shape.position} 身高只能在 ${heights[0]}–${heights[heights.length - 1]} 英寸之间`];
    }
    const issues: string[] = [];
    if (shape.weightLbs < template.weightMin || shape.weightLbs > template.weightMax) {
        issues.push(`体重应在 ${template.weightMin}–${template.weightMax} 磅之间`);
    }
    if (shape.wingspanInches < template.wingspanMin || shape.wingspanInches > template.wingspanMax) {
        issues.push(`臂展应在 ${template.wingspanMin}–${template.wingspanMax} 英寸之间`);
    }
    return issues;
}

type HeightTier = { badge: string; id: string; category: string | null; tierGroupByHeight: Record<string, number | null> };
const heightTiers = body.badgeHeightTiers as HeightTier[];
const normalize = (value: string) => value.toLowerCase().replace(/[^a-z0-9]/g, '').replace(/badge$/, '');

/** Tier group (1 or 2) of a badge at this height; null = unavailable, undefined = not published. */
export function badgeTierGroup(badgeName: string, heightInches: number): number | null | undefined {
    const key = normalize(badgeName);
    const row = heightTiers.find((item) => normalize(item.badge) === key || normalize(item.id) === key);
    if (!row) {
        return undefined;
    }
    const value = row.tierGroupByHeight[String(heightInches)];
    return value === undefined ? undefined : value;
}
