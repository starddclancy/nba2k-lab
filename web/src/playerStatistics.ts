import type { FieldCell, PlayerStatistics } from './types';

export type StatisticsDraft = Record<string, number | string | boolean>;
export const statName = (field: Pick<FieldCell, 'key'>) => field.key.split('/').at(-1) ?? '';

const names: Record<string, string> = {
    GAMESPLAYED: '出场次数', GAMESSTARTED: '首发次数', MINUTES: '出场分钟',
    MINUTESPLAYED: '出场分钟', POINTS: '得分', ASSISTS: '助攻', TURNOVERS: '失误',
    BLOCKS: '盖帽', FOULS: '犯规', STEALS: '抢断', DEFENSEREBOUNDS: '防守篮板',
    OFFENSIVEREBOUNDS: '进攻篮板', REBOUNDS: '篮板',
    FIELDGOALSATTEMPTED: '投篮出手', FIELDGOALSMADE: '投篮命中',
    FREETHROWATTEMPTED: '罚球出手', FREETHROWMADE: '罚球命中',
    '3POINTERSATTEMPTED': '三分出手', '3POINTERSMADE': '三分命中',
    DOUBLEDOUBLES: '两双次数', TRIPLEDOUBLES: '三双次数', FORTYPOINTGAMES: '40分场次',
    FIFTYPOINTGAMES: '50分场次', SIXTYPOINTGAMES: '60分场次',
    ALLNBATEAM: '最佳阵容编码', ALLDEFENSETEAM: '最佳防守阵容编码', ALLROOKIETEAM: '最佳新秀阵容编码',
    CHAMPIONSHIP: '总冠军', ALLSTARRESERVE: '全明星替补', ALLSTARSTARTER: '全明星首发',
    CLUTCHPLAYER: '最佳关键球员', DEFENSEPLAYEROFTHEYEAR: '最佳防守球员',
    FINALSMVP: '总决赛 MVP', ISTCHAMPION: '季中锦标赛冠军', MOSTIMPROVEDPLAYER: '进步最快球员',
    MOSTVALUABLEPLAYER: '常规赛 MVP', ROOKIEOFTHEYEAR: '最佳新秀', SIXTHMAN: '最佳第六人',
    PLAYEROFMONTH: '月最佳次数', PLAYEROFWEEK: '周最佳次数',
    ROOKIEOFMONTH: '月最佳新秀次数', ROOKIEOFWEEK: '周最佳新秀次数',
    HALLOFFAMERATING: '名人堂评分', MVPAWARDSHARES: 'MVP 选票份额（原始值）',
    COMPACTYEAR: '赛季', JERSEYNUMBER: '当季球衣号码', OVERALL: '当季总评',
    POINTSOFFFREETHROW: '罚球得分（原始值）', 'BOX+-': '正负值（原始编码）',
    DEFENSIVEFIELDGOALSATTEMPTED: '防守投篮出手', DEFENSIVEFIELDGOALSMADE: '防守投篮命中',
    POSSESSIONS: '回合数', TOTALPOINTSALLOWED: '总失分', REFERENCECOUNT: '记录引用数',
};

export function statisticCell(field: FieldCell): FieldCell {
    return { ...field, label: names[statName(field)] ?? field.label };
}

export function statValue(field: FieldCell | undefined, draft: StatisticsDraft = {}): number {
    if (!field) {
        return 0;
    }
    const value = draft[field.key] ?? field.raw ?? field.value;
    if (value === true || value === 'Yes') {
        return 1;
    }
    if (value === false || value === 'No' || value === 'None') {
        return 0;
    }
    return Number(value);
}

export const perGameNames = new Set([
    'MINUTES', 'POINTS', 'ASSISTS', 'TURNOVERS', 'BLOCKS', 'FOULS', 'STEALS',
    'DEFENSEREBOUNDS', 'OFFENSIVEREBOUNDS', 'FIELDGOALSATTEMPTED', 'FIELDGOALSMADE',
    'FREETHROWATTEMPTED', 'FREETHROWMADE', '3POINTERSATTEMPTED', '3POINTERSMADE',
]);

export function average(total: number, games: number): number | null {
    return games > 0 && Number.isFinite(total) ? total / games : null;
}

export function totalFromAverage(value: number, games: number): number {
    if (!Number.isFinite(value) || value < 0 || !Number.isInteger(games) || games <= 0) {
        throw new Error('修改场均前需要有效的出场次数');
    }
    return Math.round(value * games);
}

export function cumulativeAwards(seasons: PlayerStatistics['seasons'], draft: StatisticsDraft) {
    const counts = new Map<string, { label: string; count: number }>();
    const seen = new Set<number>();
    for (const season of seasons) {
        if (seen.has(season.id)) {
            continue;
        }
        seen.add(season.id);
        for (const field of season.fields.filter((field) =>
            field.group === 'Awards' || field.group === 'Awards & Honors')) {
            const name = statName(field);
            const value = statValue(field, draft);
            const category = name.startsWith('ALL') && name.endsWith('TEAM');
            const key = category ? `${name}:${value}` : name;
            if (category && value === 0) {
                continue;
            }
            const label = `${names[name] ?? field.label}${category ? ` ${value}` : ''}`;
            const previous = counts.get(key)?.count ?? 0;
            counts.set(key, { label, count: previous + (value > 0 ? 1 : 0) });
        }
    }
    return [...counts.values()];
}
