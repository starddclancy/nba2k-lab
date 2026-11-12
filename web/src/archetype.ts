/**
 * Player profile: six category scores and a 2K-style archetype name ("攻防俱佳空间威胁").
 * The game stores no archetype for NBA players, so this is our own estimate: the strongest
 * category gives the noun, the second strongest gives the prefix. Independent design, not taken
 * from any third-party site.
 */
export const categories = ['finishing', 'shooting', 'playmaking', 'defense', 'rebounding', 'physical'] as const;
export type Category = typeof categories[number];

export const categoryLabels: Record<Category, string> = {
    finishing: '终结', shooting: '投射', playmaking: '组织', defense: '防守', rebounding: '篮板', physical: '身体',
};

/** Roster field keys per category with weights; the main skill of each category weighs most. */
const weights: Record<Category, Record<string, number>> = {
    finishing: {
        'Attributes/Offense/CLOSESHOT': 1, 'Attributes/Offense/DRIVINGLAYUP': 1.5, 'Attributes/Offense/DRIVINGDUNK': 1.5,
        'Attributes/Offense/STANDINGDUNK': 1, 'Attributes/Offense/POSTCONTROL': 0.75,
    },
    shooting: {
        'Attributes/Offense/MIDRANGE': 1, 'Attributes/Offense/3POINT': 1.5, 'Attributes/Offense/FREETHROW': 0.5,
    },
    playmaking: {
        'Attributes/Offense/PASSACCURACY': 1.25, 'Attributes/Offense/BALLCONTROL': 1.25,
        'Attributes/Athleticism/SPEEDWITHBALL': 0.75,
    },
    defense: {
        'Attributes/Defense/INTERIORDEFENSE': 1, 'Attributes/Defense/PERIMETERDEFENSE': 1.25,
        'Attributes/Defense/STEAL': 0.75, 'Attributes/Defense/BLOCK': 0.75,
    },
    rebounding: { 'Attributes/Offense/OFFENSIVEREBOUND': 1, 'Attributes/Defense/DEFENSEREBOUND': 1.25 },
    physical: {
        'Attributes/Athleticism/SPEED': 1, 'Attributes/Athleticism/AGILITY': 1,
        'Attributes/Athleticism/STRENGTH': 1, 'Attributes/Athleticism/VERTICAL': 1,
    },
};

export type CategoryScores = Record<Category, number>;

/** Weighted category averages, rounded; null when a category has no readable attribute. */
export function categoryScores(values: Record<string, unknown>): CategoryScores | null {
    const scores = {} as CategoryScores;
    for (const category of categories) {
        let sum = 0;
        let weight = 0;
        for (const [key, w] of Object.entries(weights[category])) {
            const value = Number(values[key]);
            if (values[key] !== undefined && values[key] !== null && Number.isFinite(value)) {
                sum += value * w;
                weight += w;
            }
        }
        if (weight === 0) {
            return null;
        }
        scores[category] = Math.round(sum / weight);
    }
    return scores;
}

const nouns: Record<Category, string> = {
    finishing: '内线终结者', shooting: '空间威胁', playmaking: '组织核心',
    defense: '锁防者', rebounding: '篮板悍将', physical: '运动战士',
};
const prefixes: Record<Category, string> = {
    finishing: '冲击型', shooting: '投射型', playmaking: '组织型',
    defense: '攻防俱佳', rebounding: '篮板型', physical: '运动型',
};
/** Pairs with a well-known name of their own. Key: primary + secondary. */
const special: Partial<Record<`${Category}+${Category}`, string>> = {
    'defense+shooting': '3D 锁防者',
    'shooting+playmaking': '组织型神射手',
    'playmaking+shooting': '投射型组织核心',
    'finishing+rebounding': '禁区野兽',
    'rebounding+defense': '护框篮板手',
    'defense+rebounding': '护框锁防者',
    'finishing+playmaking': '突破组织者',
};

export type Archetype = { name: string; primary: Category; secondary: Category; balanced: boolean };

/**
 * League mean and spread of each category over the 4,396-player roster (2026-11-08,
 * scripts/archetype-stats.mts). Ranking by distance from the league norm keeps shooting, whose
 * raw numbers run high for everyone, from naming most players.
 */
export const leagueNorms: Record<Category, { mean: number; sd: number }> = {
    finishing: { mean: 61.8, sd: 9 }, shooting: { mean: 66.2, sd: 12.6 }, playmaking: { mean: 58.8, sd: 15.8 },
    defense: { mean: 55.6, sd: 8.4 }, rebounding: { mean: 53, sd: 15 }, physical: { mean: 65, sd: 8.9 },
};

export const standing = (scores: CategoryScores, category: Category) =>
    (scores[category] - leagueNorms[category].mean) / leagueNorms[category].sd;

/**
 * Name from the two categories that stand out most against the league. When every category
 * sits within 6 points of the best and the best is at least 75, the player is an all-rounder.
 */
export function archetypeOf(scores: CategoryScores): Archetype {
    const ranked = [...categories].sort((a, b) =>
        standing(scores, b) - standing(scores, a) || categories.indexOf(a) - categories.indexOf(b));
    const [primary, secondary] = ranked;
    const best = Math.max(...categories.map((category) => scores[category]));
    const balanced = best >= 75 && categories.every((category) => best - scores[category] <= 6);
    if (balanced) {
        return { name: '全能核心', primary, secondary, balanced };
    }
    const name = special[`${primary}+${secondary}`] ?? `${prefixes[secondary]}${nouns[primary]}`;
    return { name, primary, secondary, balanced };
}

/**
 * The game's own player types (Vitals/Type PLAYERTYPE and OFFENSIVEPLAYERTYPE, enum
 * PLAYERDATA_PLAYERTYPE). Every rated player has both; the title shown everywhere comes from
 * these, and the six-category estimate above is only the fallback when they are unreadable.
 */
export const gameTypeLabels: Record<string, string> = {
    ALL_AROUND: '全能型', TWO_WAY: '攻防兼备', OFFENSIVE: '进攻型', DEFENSIVE: '防守型', ATHLETIC: '运动型',
    REBOUNDING: '篮板型', PLAYMAKING: '组织型', OUTSIDE: '外线型', INSIDE: '内线型',
};

export type PlayerTitle = { name: string; detail: string; fromGame: boolean };

/** Title from the game's type; the estimate (marked) only when the game value is missing. */
export function playerTitle(summary: { playerType?: string | null; offensiveType?: string | null } | null | undefined,
    estimate: string | null | undefined): PlayerTitle | null {
    const type = summary?.playerType ? gameTypeLabels[summary.playerType] : undefined;
    const offense = summary?.offensiveType ? gameTypeLabels[summary.offensiveType] : undefined;
    if (type) {
        return { name: type, detail: `游戏内球员类型：${type}${offense ? ` · 进攻风格：${offense}` : ''}`, fromGame: true };
    }
    return estimate ? { name: estimate, detail: '游戏未提供球员类型，按六维能力估算', fromGame: false } : null;
}
