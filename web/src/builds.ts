export const attributes = [
    { id: 'closeShot', label: '近距离投篮', group: '终结' },
    { id: 'drivingLayup', label: '突破上篮', group: '终结' },
    { id: 'drivingDunk', label: '突破扣篮', group: '终结' },
    { id: 'standingDunk', label: '原地扣篮', group: '终结' },
    { id: 'postControl', label: '背身控制', group: '终结' },
    { id: 'midRange', label: '中距离', group: '投射' },
    { id: 'threePoint', label: '三分', group: '投射' },
    { id: 'freeThrow', label: '罚球', group: '投射' },
    { id: 'passAccuracy', label: '传球准确性', group: '组织' },
    { id: 'ballHandle', label: '控球', group: '组织' },
    { id: 'speedWithBall', label: '持球速度', group: '组织' },
    { id: 'interiorDefense', label: '内线防守', group: '防守' },
    { id: 'perimeterDefense', label: '外线防守', group: '防守' },
    { id: 'steal', label: '抢断', group: '防守' },
    { id: 'block', label: '盖帽', group: '防守' },
    { id: 'offensiveRebound', label: '进攻篮板', group: '篮板' },
    { id: 'defensiveRebound', label: '防守篮板', group: '篮板' },
    { id: 'speed', label: '速度', group: '身体' },
    { id: 'agility', label: '敏捷', group: '身体' },
    { id: 'strength', label: '力量', group: '身体' },
    { id: 'vertical', label: '弹跳', group: '身体' },
] as const;

/**
 * Attributes the roster editor has but the MyPLAYER builder does not. They are kept in a separate
 * optional field so badge/animation requirements and share-code v1 keep using the 21 above.
 */
export const extraAttributes = [
    { id: 'drawFoul', label: '造犯规', group: '其他' },
    { id: 'hands', label: '手感', group: '其他' },
    { id: 'shotIq', label: '投篮意识', group: '其他' },
    { id: 'passIq', label: '传球意识', group: '其他' },
    { id: 'passVision', label: '传球视野', group: '其他' },
    { id: 'postFade', label: '背身后仰', group: '其他' },
    { id: 'postHook', label: '勾手', group: '其他' },
    { id: 'stamina', label: '体能', group: '其他' },
    { id: 'hustle', label: '拼劲', group: '其他' },
    { id: 'helpDefense', label: '协防意识', group: '其他' },
    { id: 'passPerception', label: '传球预判', group: '其他' },
    { id: 'reactionTime', label: '反应时间', group: '其他' },
    { id: 'offensiveConsistency', label: '进攻稳定性', group: '其他' },
    { id: 'defensiveConsistency', label: '防守稳定性', group: '其他' },
    { id: 'intangibles', label: '无形价值', group: '其他' },
    { id: 'potential', label: '潜力', group: '其他' },
] as const;
export type ExtraAttributeId = typeof extraAttributes[number]['id'];
export type ExtraRatings = Record<ExtraAttributeId, number>;
export const defaultExtraRating = 50;
export const defaultExtraRatings = (): ExtraRatings =>
    Object.fromEntries(extraAttributes.map(({ id }) => [id, defaultExtraRating])) as ExtraRatings;

export type AttributeId = typeof attributes[number]['id'];
export type Ratings = Record<AttributeId, number>;
export const tiers = ['bronze', 'silver', 'gold', 'hof'] as const;
export type Tier = typeof tiers[number];
export const positions = ['PG', 'SG', 'SF', 'PF', 'C'] as const;

export type RequirementTerm = {
    attribute: AttributeId;
    minimum: number;
    minHeight?: number;
    maxHeight?: number;
};
export type Requirement = {
    logic: 'all' | 'any' | 'unverified';
    terms: RequirementTerm[];
};
export type BadgeRequirement = {
    id: string;
    name: string;
    category: string;
    tiers: Record<Tier, Requirement>;
};
export type AnimationRequirement = {
    id: string;
    name: string;
    category: string;
    group: 'finishing' | 'dribbling' | 'shooting' | 'motionStyles' | 'jumpers';
    minHeight: number;
    maxHeight: number;
    rule: Requirement;
    scope: 'package' | 'baseOnly';
};
export type RequirementCatalog = {
    schemaVersion: 1;
    game: 'nba2k27';
    capturedAt: string;
    sources: { url: string; sha256: string; buildId: string; title: string }[];
    notes: string[];
    badges: BadgeRequirement[];
    animations: AnimationRequirement[];
};

export type BuildPlan = {
    id: string;
    name: string;
    position: typeof positions[number];
    heightInches: number;
    weightLbs: number;
    wingspanInches: number;
    ratings: Ratings;
    /** Optional for older plans; missing values read as defaultExtraRating. */
    extraRatings?: ExtraRatings;
    badgeGoals: { id: string; tier: Tier }[];
    animationGoals: string[];
    tags: string[];
    favorite: boolean;
    notes: string;
    createdAt: string;
    updatedAt: string;
};
export type PlanLibrary = {
    schemaVersion: 1;
    game: 'nba2k27';
    plans: BuildPlan[];
};

export function createBuild(id: string = crypto.randomUUID(), now = new Date().toISOString()): BuildPlan {
    return {
        id, name: '新建方案', position: 'SF', heightInches: 78, weightLbs: 210,
        wingspanInches: 82,
        ratings: Object.fromEntries(attributes.map(({ id: key }) => [key, 25])) as Ratings,
        extraRatings: defaultExtraRatings(),
        badgeGoals: [], animationGoals: [], tags: [], favorite: false, notes: '',
        createdAt: now, updatedAt: now,
    };
}

export type TermResult = RequirementTerm & {
    current: number | null;
    missing: number | null;
    heightMatches: boolean | null;
    status: 'eligible' | 'blocked' | 'unknown';
};
export type Eligibility = {
    status: 'eligible' | 'blocked' | 'unknown' | 'unavailable';
    terms: TermResult[];
    logic: Requirement['logic'];
    heightMatches: boolean | null;
};

function validHeight(value: number): boolean {
    return Number.isInteger(value) && value >= 69 && value <= 88;
}

export function evaluateRequirement(
    rule: Requirement,
    ratings: Partial<Ratings>,
    heightInches: number,
): Eligibility {
    const terms: TermResult[] = rule.terms.map((term) => {
        const raw = ratings[term.attribute];
        const current = typeof raw === 'number' && Number.isInteger(raw) && raw >= 25 && raw <= 99
            ? raw : null;
        const heightMatches = !validHeight(heightInches) ? null
            : heightInches >= (term.minHeight ?? 69) && heightInches <= (term.maxHeight ?? 88);
        const missing = current === null ? null : Math.max(0, term.minimum - current);
        const status = heightMatches === false || (missing !== null && missing > 0) ? 'blocked'
            : heightMatches === null || current === null ? 'unknown' : 'eligible';
        return { ...term, current, missing, heightMatches, status };
    });
    const all = terms.every((term) => term.status === 'eligible') ? 'eligible'
        : terms.some((term) => term.status === 'blocked') ? 'blocked' : 'unknown';
    const any = terms.some((term) => term.status === 'eligible') ? 'eligible'
        : terms.every((term) => term.status === 'blocked') ? 'blocked' : 'unknown';
    const status = terms.length === 0 ? 'unavailable'
        : rule.logic === 'all' ? all : rule.logic === 'any' ? any
            : all === any ? all : 'unknown';
    const heightAll = terms.every((term) => term.heightMatches === true) ? true
        : terms.some((term) => term.heightMatches === false) ? false : null;
    const heightAny = terms.some((term) => term.heightMatches === true) ? true
        : terms.every((term) => term.heightMatches === false) ? false : null;
    return {
        status, terms, logic: rule.logic,
        heightMatches: rule.logic === 'all' ? heightAll : rule.logic === 'any' ? heightAny
            : heightAll === heightAny ? heightAll : null,
    };
}

export function evaluateAnimation(
    animation: AnimationRequirement, ratings: Partial<Ratings>, heightInches: number,
): Eligibility {
    const heightMatches = validHeight(heightInches)
        ? heightInches >= animation.minHeight && heightInches <= animation.maxHeight : null;
    const result = evaluateRequirement(animation.rule, ratings, heightInches);
    // Empty animation columns mean no attribute prerequisite, unlike an absent badge tier.
    const status = heightMatches === false ? 'blocked' : heightMatches === null ? 'unknown'
        : animation.rule.terms.length === 0 ? 'eligible' : result.status;
    return { ...result, status, heightMatches };
}

export function evaluateBadge(badge: BadgeRequirement, ratings: Partial<Ratings>, heightInches: number) {
    const levels = tiers.map((tier) => ({
        tier, result: evaluateRequirement(badge.tiers[tier], ratings, heightInches),
    }));
    const current = [...levels].reverse().find((level) => level.result.status === 'eligible')?.tier ?? null;
    const index = current === null ? -1 : tiers.indexOf(current);
    const next = levels.slice(index + 1).find((level) => level.result.status !== 'unavailable') ?? null;
    return { id: badge.id, current, next, levels };
}

export function evaluateGoals(plan: BuildPlan, catalog: RequirementCatalog) {
    return {
        badges: plan.badgeGoals.map((goal) => {
            const badge = catalog.badges.find((item) => item.id === goal.id);
            return {
                ...goal,
                result: badge ? evaluateRequirement(badge.tiers[goal.tier], plan.ratings, plan.heightInches) : null,
            };
        }),
        animations: plan.animationGoals.map((id) => {
            const animation = catalog.animations.find((item) => item.id === id);
            return {
                id, result: animation ? evaluateAnimation(animation, plan.ratings, plan.heightInches) : null,
            };
        }),
    };
}

export function compareBuilds(left: BuildPlan, right: BuildPlan, catalog: RequirementCatalog) {
    const badges = catalog.badges.map((badge) => {
        const before = evaluateBadge(badge, left.ratings, left.heightInches).current;
        const after = evaluateBadge(badge, right.ratings, right.heightInches).current;
        return { id: badge.id, name: badge.name, before, after };
    });
    const animations = catalog.animations.map((animation) => ({
        id: animation.id, name: animation.name,
        before: evaluateAnimation(animation, left.ratings, left.heightInches).status,
        after: evaluateAnimation(animation, right.ratings, right.heightInches).status,
    }));
    return {
        attributes: attributes.map(({ id, label, group }) => ({
            id, label, group, before: left.ratings[id], after: right.ratings[id],
            delta: right.ratings[id] - left.ratings[id],
        })),
        body: {
            heightInches: right.heightInches - left.heightInches,
            weightLbs: right.weightLbs - left.weightLbs,
            wingspanInches: right.wingspanInches - left.wingspanInches,
        },
        badges: badges.filter((item) => item.before !== item.after),
        animations: animations.filter((item) => item.before !== item.after),
    };
}

function object(value: unknown, context: string): Record<string, unknown> {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) {
        throw new Error(`${context}: expected object`);
    }
    return value as Record<string, unknown>;
}

function text(value: unknown, context: string, limit = 120): string {
    if (typeof value !== 'string' || !value.trim() || value.length > limit) {
        throw new Error(`${context}: invalid text`);
    }
    return value.trim();
}

function integer(value: unknown, context: string, min: number, max: number): number {
    if (typeof value !== 'number' || !Number.isInteger(value) || value < min || value > max) {
        throw new Error(`${context}: expected integer ${min}..${max}`);
    }
    return value;
}

function array(value: unknown, context: string, limit: number): unknown[] {
    if (!Array.isArray(value) || value.length > limit) {
        throw new Error(`${context}: invalid array`);
    }
    return value;
}

function unique<T>(items: T[], key: (item: T) => string, context: string): T[] {
    if (new Set(items.map(key)).size !== items.length) {
        throw new Error(`${context}: duplicate identity`);
    }
    return items;
}

function date(value: unknown, context: string): string {
    const result = text(value, context, 40);
    const format = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,6})?(?:Z|[+-]\d{2}:\d{2})$/;
    const calendar = Date.parse(`${result.slice(0, 19)}Z`);
    if (!format.test(result) || !Number.isFinite(Date.parse(result)) || !Number.isFinite(calendar)
        || new Date(calendar).toISOString().slice(0, 19) !== result.slice(0, 19)) {
        throw new Error(`${context}: invalid timestamp`);
    }
    return result;
}

export function parseBuild(value: unknown): BuildPlan {
    const plan = object(value, 'build');
    const rawRatings = object(plan.ratings, 'ratings');
    if (Object.keys(rawRatings).length !== attributes.length) {
        throw new Error('ratings: expected all 21 attributes');
    }
    const ratings = Object.fromEntries(attributes.map(({ id }) => [
        id, integer(rawRatings[id], id, 25, 99),
    ])) as Ratings;
    let extraRatings: ExtraRatings | undefined;
    if (plan.extraRatings !== undefined) {
        const rawExtra = object(plan.extraRatings, 'extraRatings');
        if (Object.keys(rawExtra).some((key) => !extraAttributes.some(({ id }) => id === key))) {
            throw new Error('extraRatings: unknown attribute');
        }
        extraRatings = Object.fromEntries(extraAttributes.map(({ id }) => [
            id, rawExtra[id] === undefined ? defaultExtraRating : integer(rawExtra[id], id, 25, 99),
        ])) as ExtraRatings;
    }
    if (!positions.includes(plan.position as BuildPlan['position'])) {
        throw new Error('position: invalid value');
    }
    if (typeof plan.favorite !== 'boolean' || typeof plan.notes !== 'string' || plan.notes.length > 10_000) {
        throw new Error('favorite/notes: invalid value');
    }
    const badgeGoals = unique(array(plan.badgeGoals, 'badgeGoals', 100).map((item) => {
        const goal = object(item, 'badge goal');
        if (!tiers.includes(goal.tier as Tier)) {
            throw new Error('badge goal: unsupported tier');
        }
        return { id: text(goal.id, 'badge id'), tier: goal.tier as Tier };
    }), (item) => item.id, 'badgeGoals');
    const createdAt = date(plan.createdAt, 'createdAt');
    const updatedAt = date(plan.updatedAt, 'updatedAt');
    if (Date.parse(updatedAt) < Date.parse(createdAt)) {
        throw new Error('updatedAt precedes createdAt');
    }
    return {
        id: text(plan.id, 'id'), name: text(plan.name, 'name'),
        position: plan.position as BuildPlan['position'],
        // Broad input bounds, not certified game body limits or attribute caps.
        heightInches: integer(plan.heightInches, 'height', 69, 88),
        weightLbs: integer(plan.weightLbs, 'weight', 130, 400),
        wingspanInches: integer(plan.wingspanInches, 'wingspan', 60, 110),
        ratings, ...(extraRatings ? { extraRatings } : {}), badgeGoals,
        animationGoals: unique(array(plan.animationGoals, 'animationGoals', 3000)
            .map((item) => text(item, 'animation goal', 300)), (item) => item, 'animationGoals'),
        tags: unique(array(plan.tags, 'tags', 20).map((item) => text(item, 'tag', 40)), (item) => item, 'tags'),
        favorite: plan.favorite, notes: plan.notes, createdAt, updatedAt,
    };
}

export function parseLibrary(value: unknown): PlanLibrary {
    const library = object(value, 'library');
    if (library.schemaVersion !== 1 || library.game !== 'nba2k27') {
        throw new Error('Unsupported library version or game');
    }
    return {
        schemaVersion: 1, game: 'nba2k27',
        plans: unique(array(library.plans, 'plans', 2000).map(parseBuild), (item) => item.id, 'plans'),
    };
}

export function importLibrary(content: string): PlanLibrary {
    if (content.length > 8_000_000 || new TextEncoder().encode(content).byteLength > 8_000_000) {
        throw new Error('Library exceeds 8 MB');
    }
    return parseLibrary(JSON.parse(content));
}

export function exportLibrary(library: PlanLibrary): string {
    return JSON.stringify(parseLibrary(library), null, 2);
}

export function saveBuild(library: PlanLibrary, plan: BuildPlan): PlanLibrary {
    const checked = parseBuild(plan);
    const items = parseLibrary(library).plans;
    return parseLibrary({
        ...library, plans: [...items.filter((item) => item.id !== checked.id), checked],
    });
}

export function deleteBuild(library: PlanLibrary, id: string): PlanLibrary {
    return { ...parseLibrary(library), plans: library.plans.filter((item) => item.id !== id).map(parseBuild) };
}

export function duplicateBuild(plan: BuildPlan, id: string = crypto.randomUUID(), now = new Date().toISOString()): BuildPlan {
    if (id === plan.id) {
        throw new Error('Copy needs a new identity');
    }
    return parseBuild({
        ...plan, id, name: `${plan.name.slice(0, 115)} 副本`, createdAt: now, updatedAt: now,
    });
}

export function mergeLibraries(current: PlanLibrary, incoming: PlanLibrary, policy: 'reject' | 'replace' = 'reject') {
    if (policy !== 'reject' && policy !== 'replace') {
        throw new Error('Unknown import conflict policy');
    }
    const before = parseLibrary(current);
    const next = parseLibrary(incoming);
    const collisions = next.plans.filter((item) => before.plans.some((saved) => saved.id === item.id));
    if (collisions.length > 0 && policy === 'reject') {
        throw new Error(`Import has ${collisions.length} conflicting identities`);
    }
    const incomingIds = new Set(next.plans.map((item) => item.id));
    return parseLibrary({
        ...before, plans: [...before.plans.filter((item) => !incomingIds.has(item.id)), ...next.plans],
    });
}

export const libraryStorageKey = 'nba2k27.buildStudio.library.v1';
export type LibraryStorage = Pick<Storage, 'getItem' | 'setItem'>;

export function loadLibrary(storage: LibraryStorage): PlanLibrary {
    const content = storage.getItem(libraryStorageKey);
    // Corrupt or inaccessible storage is surfaced; never quietly overwrite it with an empty library.
    return content === null ? { schemaVersion: 1, game: 'nba2k27', plans: [] } : importLibrary(content);
}

export function persistLibrary(storage: LibraryStorage, library: PlanLibrary, expected?: PlanLibrary): void {
    if (expected && exportLibrary(loadLibrary(storage)) !== exportLibrary(expected)) {
        throw new Error('Library changed in another view; reload before saving');
    }
    storage.setItem(libraryStorageKey, exportLibrary(library));
}

export function parseRequirementCatalog(value: unknown): RequirementCatalog {
    const catalog = object(value, 'catalog');
    if (catalog.schemaVersion !== 1 || catalog.game !== 'nba2k27') {
        throw new Error('Unsupported catalog version or game');
    }
    const rule = (value: unknown): Requirement => {
        const source = object(value, 'rule');
        if (!['all', 'any', 'unverified'].includes(String(source.logic))) {
            throw new Error('Unknown requirement connector');
        }
        const terms = unique(array(source.terms, 'terms', 21).map((value) => {
            const item = object(value, 'term');
            if (!attributes.some(({ id }) => id === item.attribute)) {
                throw new Error('Unknown requirement attribute');
            }
            const minHeight = item.minHeight === undefined ? undefined : integer(item.minHeight, 'minHeight', 69, 88);
            const maxHeight = item.maxHeight === undefined ? undefined : integer(item.maxHeight, 'maxHeight', 69, 88);
            if ((minHeight ?? 69) > (maxHeight ?? 88)) {
                throw new Error('Inverted height interval');
            }
            return {
                attribute: item.attribute as AttributeId, minimum: integer(item.minimum, 'minimum', 25, 99),
                ...(minHeight === undefined ? {} : { minHeight }),
                ...(maxHeight === undefined ? {} : { maxHeight }),
            };
        }), (item) => item.attribute, 'terms');
        return { logic: source.logic as Requirement['logic'], terms };
    };
    return {
        schemaVersion: 1, game: 'nba2k27', capturedAt: date(catalog.capturedAt, 'capturedAt'),
        notes: array(catalog.notes, 'notes', 30).map((item) => text(item, 'note', 1000)),
        sources: array(catalog.sources, 'sources', 20).map((value) => {
            const item = object(value, 'source');
            const url = text(item.url, 'url', 500);
            if (!url.startsWith('https://www.nba2klab.com/')) {
                throw new Error('Unexpected catalog source');
            }
            const hash = text(item.sha256, 'sha256', 64);
            if (!/^[a-f0-9]{64}$/.test(hash)) {
                throw new Error('Invalid source hash');
            }
            return { url, sha256: hash, buildId: text(item.buildId, 'buildId'), title: text(item.title, 'title', 300) };
        }),
        badges: unique(array(catalog.badges, 'badges', 200).map((value) => {
            const item = object(value, 'badge');
            const levels = object(item.tiers, 'tiers');
            return {
                id: text(item.id, 'badge id'), name: text(item.name, 'badge name'),
                category: text(item.category, 'badge category'),
                tiers: Object.fromEntries(tiers.map((tier) => [tier, rule(levels[tier])])) as Record<Tier, Requirement>,
            };
        }), (item) => item.id, 'badges'),
        animations: unique(array(catalog.animations, 'animations', 10000).map((value) => {
            const item = object(value, 'animation');
            if (!['finishing', 'dribbling', 'shooting', 'motionStyles', 'jumpers'].includes(String(item.group))
                || !['package', 'baseOnly'].includes(String(item.scope))) {
                throw new Error('Unknown animation group or scope');
            }
            const minHeight = integer(item.minHeight, 'minHeight', 69, 88);
            const maxHeight = integer(item.maxHeight, 'maxHeight', 69, 88);
            if (minHeight > maxHeight) {
                throw new Error('Inverted animation height interval');
            }
            return {
                id: text(item.id, 'animation id', 300), name: text(item.name, 'animation name'),
                category: text(item.category, 'animation category'),
                group: item.group as AnimationRequirement['group'],
                scope: item.scope as AnimationRequirement['scope'],
                minHeight, maxHeight, rule: rule(item.rule),
            };
        }), (item) => item.id, 'animations'),
    };
}
