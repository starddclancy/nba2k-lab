import type { FieldCell } from './types';
import type { Tier } from './builds';

export type BadgeVisualState = Tier | 'legend' | 'unequipped' | 'equipped' | 'unknown';
export const badgeStateNames: Record<BadgeVisualState, string> = {
    bronze: '铜', silver: '银', gold: '金', hof: '名人堂', legend: '传奇',
    unequipped: '未装备', equipped: '已装备', unknown: '未知',
};
const levelStates: Record<string, BadgeVisualState> = {
    UNEQUIPPED: 'unequipped', BRONZE: 'bronze', SILVER: 'silver', GOLD: 'gold',
    HALL_OF_FAME: 'hof', LEGEND: 'legend',
};

export function badgePresentation(cell: FieldCell, value = cell.value): {
    mode: 'level' | 'equipment'; state: BadgeVisualState;
} | null {
    if (cell.section !== 'Badges' || /^Is.+Boostable$/i.test(cell.label)) return null;
    const authored = typeof value === 'number' && Number.isInteger(value) && cell.options[value] !== undefined
        ? cell.options[value] : value;
    if (cell.options.includes('BRONZE')) {
        return { mode: 'level', state: levelStates[String(authored)] ?? 'unknown' };
    }
    const binary = cell.options.length === 2
        && cell.options.every((item) => /^(no|yes|false|true|unequipped|equipped)$/i.test(item));
    if (binary || (cell.kind === 'bool' && cell.group === 'Personality')) {
        const enabled = authored === true || authored === 1 || /^(yes|true|equipped)$/i.test(String(authored));
        const disabled = authored === false || authored === 0 || /^(no|false|unequipped)$/i.test(String(authored));
        return { mode: 'equipment', state: enabled ? 'equipped' : disabled ? 'unequipped' : 'unknown' };
    }
    return null;
}
