import rawCatalog from './data/build-requirements.json';
import rawIcons from './data/badge-icons.json';
import rawEffects from './data/badge-effects.json';
import { parseRequirementCatalog, type Tier } from './builds';
import { translated } from './labels';

export const buildCatalog = parseRequirementCatalog(rawCatalog);
export const tierNames: Record<Tier, string> = {
    bronze: '铜', silver: '银', gold: '金', hof: '名人堂',
};
export const categoryNames: Record<string, string> = {
    Defense: '防守', 'Inside Scoring': '终结', 'Outside Scoring': '投射',
    Playmaking: '组织', Rebounding: '篮板', 'General Offense': '进攻通用',
    'General': '通用', 'General Offense & Defense': '攻防通用', Physicals: '身体',
};
const normalize = (value: string) => value.toLowerCase().replace(/[^a-z0-9]/g, '').replace(/badge$/, '');
const badgesByName = new Map(buildCatalog.badges.flatMap((badge) => [
    [normalize(badge.id), badge], [normalize(badge.name), badge],
]));
const icons = rawIcons.entries as Record<string, { file: string; displayName?: string; name?: string }>;
const additionalBadges = new Map(Object.entries(icons).flatMap(([id, entry]) => {
    const badge = { id, name: entry.displayName ?? entry.name ?? id };
    return [[normalize(id), badge], [normalize(badge.name), badge]];
}));

export function findBadge(name: string) {
    return badgesByName.get(normalize(name)) ?? additionalBadges.get(normalize(name));
}

export function badgeName(name: string): string {
    const badge = findBadge(name);
    return translated(badge?.name ?? name) ?? translated(`${badge?.id ?? name}Badge`)
        ?? badge?.name ?? name;
}

export function badgeIcon(name: string, personality = false): string | null {
    const badge = findBadge(name);
    const file = (badge && icons[badge.id]?.file)
        ?? (personality ? rawIcons.categories.Personality.file : null);
    return file ? `${import.meta.env.BASE_URL}badge-icons/${file}` : null;
}


const effects = rawEffects.effects as Record<string, string>;
export const badgeEffectSource = rawEffects.source;

/** Confirmed effect text (Chinese) of a badge, from data/badge-effects.json. */
export function badgeEffect(name: string): string | null {
    const badge = findBadge(name);
    return (badge && effects[badge.id]) ?? null;
}

/** Category and per-tier unlock requirements of a badge, when the catalog has them. */
export function badgeDetails(name: string) {
    const badge = findBadge(name);
    const full = badge ? buildCatalog.badges.find((item) => item.id === badge.id) : undefined;
    return full ? { category: categoryNames[full.category] ?? full.category, tiers: full.tiers } : null;
}
