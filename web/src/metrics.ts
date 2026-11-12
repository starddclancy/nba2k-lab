import type { PlayerSeason, TeamSeason } from './seasonTrends.ts';
import type { PlayerGame, TeamGame } from './gameLog.ts';

/**
 * Metric catalog: every stat the analytics pages can chart or tabulate. Counting stats come
 * from the raw season totals and are shown per game, as season totals, or per 36 minutes;
 * rates and advanced numbers ignore the mode. Values keep their decimals (no integer rounding).
 */
export type Mode = 'perGame' | 'total' | 'per36';
export const modeLabels: Record<Mode, string> = { perGame: '场均', total: '总计', per36: '每36分钟' };

export type Metric<T> = {
    key: string; label: string; group: string; unit?: string; digits: number;
    /** Counting stats scale with the mode; others are fixed. */
    counting?: boolean;
    lowerBetter?: boolean;
    value: (row: T, mode: Mode) => number | null;
};

const r = (value: number, digits: number) => Math.round(value * 10 ** digits) / 10 ** digits;
const ratio = (made: number, attempted: number) => (attempted > 0 ? (made / attempted) * 100 : null);

function counting<T>(key: string, label: string, group: string, total: (row: T) => number, games: (row: T) => number,
    minutes: (row: T) => number, lowerBetter = false): Metric<T> {
    return {
        key, label, group, digits: 1, counting: true, lowerBetter,
        value: (row, mode) => {
            const sum = total(row);
            if (mode === 'total') return sum;
            if (mode === 'per36') {
                const played = minutes(row);
                return played > 0 ? r((sum / played) * 36, 1) : null;
            }
            const count = games(row);
            return count > 0 ? r(sum / count, 1) : null;
        },
    };
}
const fixed = <T,>(key: string, label: string, group: string, value: (row: T) => number | null, digits = 1, unit = '', lowerBetter = false): Metric<T> =>
    ({ key, label, group, digits, unit, lowerBetter, value: (row) => { const v = value(row); return v === null ? null : r(v, digits); } });

// ----- Player seasons -----
type P = PlayerSeason;
const t = (row: P, name: string) => row.totals?.[name] ?? 0;
const pg = (row: P) => row.games;
const pm = (row: P) => t(row, 'MINUTES');
const pc = (key: string, label: string, group: string, total: (row: P) => number, lowerBetter = false) => counting<P>(key, label, group, total, pg, pm, lowerBetter);

export const playerMetrics: Metric<P>[] = [
    pc('pts', '得分', '基础', (row) => t(row, 'POINTS')),
    pc('reb', '篮板', '基础', (row) => t(row, 'OFFENSIVEREBOUNDS') + t(row, 'DEFENSEREBOUNDS')),
    pc('ast', '助攻', '基础', (row) => t(row, 'ASSISTS')),
    pc('stl', '抢断', '基础', (row) => t(row, 'STEALS')),
    pc('blk', '盖帽', '基础', (row) => t(row, 'BLOCKS')),
    pc('tov', '失误', '基础', (row) => t(row, 'TURNOVERS'), true),
    pc('pf', '犯规', '基础', (row) => t(row, 'FOULS'), true),
    pc('min', '分钟', '基础', (row) => t(row, 'MINUTES')),
    pc('fgm', '投篮命中', '投篮', (row) => t(row, 'FIELDGOALSMADE')),
    pc('fga', '投篮出手', '投篮', (row) => t(row, 'FIELDGOALSATTEMPTED')),
    pc('tpm', '三分命中', '投篮', (row) => t(row, '3POINTERSMADE')),
    pc('tpa', '三分出手', '投篮', (row) => t(row, '3POINTERSATTEMPTED')),
    pc('ftm', '罚球命中', '投篮', (row) => t(row, 'FREETHROWMADE')),
    pc('fta', '罚球出手', '投篮', (row) => t(row, 'FREETHROWATTEMPTED')),
    pc('oreb', '前场篮板', '篮板', (row) => t(row, 'OFFENSIVEREBOUNDS')),
    pc('dreb', '后场篮板', '篮板', (row) => t(row, 'DEFENSEREBOUNDS')),
    fixed<P>('fgPct', '投篮%', '命中率', (row) => ratio(t(row, 'FIELDGOALSMADE'), t(row, 'FIELDGOALSATTEMPTED')), 1, '%'),
    fixed<P>('tpPct', '三分%', '命中率', (row) => ratio(t(row, '3POINTERSMADE'), t(row, '3POINTERSATTEMPTED')), 1, '%'),
    fixed<P>('ftPct', '罚球%', '命中率', (row) => ratio(t(row, 'FREETHROWMADE'), t(row, 'FREETHROWATTEMPTED')), 1, '%'),
    fixed<P>('twoPct', '两分%', '命中率', (row) => ratio(t(row, 'FIELDGOALSMADE') - t(row, '3POINTERSMADE'), t(row, 'FIELDGOALSATTEMPTED') - t(row, '3POINTERSATTEMPTED')), 1, '%'),
    fixed<P>('ts', 'TS%', '高阶', (row) => row.tsPct, 1, '%'),
    fixed<P>('efg', 'eFG%', '高阶', (row) => row.efgPct, 1, '%'),
    fixed<P>('tpr', '三分出手占比', '高阶', (row) => (row.threeRate === null ? null : row.threeRate * 100), 1, '%'),
    fixed<P>('ftr', '罚球率', '高阶', (row) => row.ftRate, 3),
    fixed<P>('astTo', '助攻失误比', '高阶', (row) => row.astTo, 2),
    fixed<P>('gameScore', 'Game Score', '高阶', (row) => row.gameScore, 1),
    fixed<P>('bpm', '场均正负值', '高阶', (row) => row.boxPlusMinus, 1),
    fixed<P>('ptsShot', '每次出手得分', '高阶', (row) => (t(row, 'FIELDGOALSATTEMPTED') ? t(row, 'POINTS') / t(row, 'FIELDGOALSATTEMPTED') : null), 2),
    fixed<P>('ftPts', '罚球得分占比', '高阶', (row) => ratio(t(row, 'POINTSOFFFREETHROW') || t(row, 'FREETHROWMADE'), t(row, 'POINTS')), 1, '%'),
    fixed<P>('oppFg', '对位命中率', '防守', (row) => row.oppFgPct, 1, '%', true),
    fixed<P>('defRtg', '防守失分/百回合', '防守', (row) => row.defRating, 1, '', true),
    fixed<P>('stocks', '场均抢断+盖帽', '防守', (row) => (row.games ? (t(row, 'STEALS') + t(row, 'BLOCKS')) / row.games : null), 1),
    fixed<P>('gp', '出场', '出勤', (row) => row.games, 0),
    fixed<P>('gs', '首发', '出勤', (row) => row.started, 0),
    fixed<P>('dd', '两双', '里程碑', (row) => row.doubleDoubles, 0),
    fixed<P>('td', '三双', '里程碑', (row) => row.tripleDoubles, 0),
    fixed<P>('p40', '40+ 场次', '里程碑', (row) => row.fortyPoint, 0),
    fixed<P>('ovr', '当季总评', '能力', (row) => row.overall, 0),
];

// ----- Team seasons -----
type S = TeamSeason;
const tt = (row: S, name: string) => row.totals?.[name] ?? 0;
const tg = (row: S) => row.games;
const tm = (row: S) => tt(row, 'MINUTESPLAYED') / 5;
const tc = (key: string, label: string, group: string, total: (row: S) => number, lowerBetter = false) => counting<S>(key, label, group, total, tg, tm, lowerBetter);
const opt = (row: S, name: string) => (tt(row, name) ? tt(row, name) : 0);

export const teamMetrics: Metric<S>[] = [
    fixed<S>('winPct', '胜率', '战绩', (row) => row.winPct, 1, '%'),
    fixed<S>('wins', '胜场', '战绩', (row) => row.wins, 0),
    fixed<S>('losses', '负场', '战绩', (row) => row.losses, 0, '', true),
    tc('pts', '得分', '基础', (row) => tt(row, 'TOTALPOINTS')),
    { ...tc('opp', '失分', '基础', (row) => tt(row, 'TOTALPOINTSALLOWED'), true), value: (row, mode) => (tt(row, 'TOTALPOINTSALLOWED') ? tc('opp', '', '', (x) => tt(x, 'TOTALPOINTSALLOWED')).value(row, mode) : null) },
    fixed<S>('margin', '净胜分', '基础', (row) => row.margin, 1),
    tc('reb', '篮板', '基础', (row) => opt(row, 'OFFENSIVEREBOUNDS') + opt(row, 'DEFENSIVEREBOUNDS')),
    tc('ast', '助攻', '基础', (row) => opt(row, 'ASSISTS')),
    tc('stl', '抢断', '基础', (row) => opt(row, 'STEALS')),
    tc('blk', '盖帽', '基础', (row) => opt(row, 'BLOCKS')),
    tc('tov', '失误', '基础', (row) => opt(row, 'TURNOVERS'), true),
    tc('pf', '犯规', '基础', (row) => opt(row, 'FOULS'), true),
    tc('fgm', '投篮命中', '投篮', (row) => tt(row, 'FIELDGOALSMADE')),
    tc('fga', '投篮出手', '投篮', (row) => tt(row, 'FIELDGOALSATTEMPTED')),
    tc('tpm', '三分命中', '投篮', (row) => tt(row, 'THREEPOINTSHOTSMADE')),
    tc('tpa', '三分出手', '投篮', (row) => tt(row, 'THREEPOINTSHOTSATTEMPTED')),
    tc('ftm', '罚球命中', '投篮', (row) => opt(row, 'FREETHROWMADE')),
    tc('fta', '罚球出手', '投篮', (row) => opt(row, 'FREETHROWATTEMPTED')),
    fixed<S>('fgPct', '投篮%', '命中率', (row) => row.fgPct, 1, '%'),
    fixed<S>('tpPct', '三分%', '命中率', (row) => row.threePct, 1, '%'),
    fixed<S>('ftPct', '罚球%', '命中率', (row) => ratio(opt(row, 'FREETHROWMADE'), opt(row, 'FREETHROWATTEMPTED')), 1, '%'),
    fixed<S>('ortg', '进攻效率', '高阶', (row) => row.offRating, 1),
    fixed<S>('drtg', '防守效率', '高阶', (row) => row.defRating, 1, '', true),
    fixed<S>('net', '净效率', '高阶', (row) => row.netRating, 1),
    fixed<S>('pace', '节奏（回合/场）', '高阶', (row) => row.pace, 1),
    fixed<S>('efg', 'eFG%', '高阶', (row) => row.efgPct, 1, '%'),
    fixed<S>('oppEfg', '对手 eFG%', '高阶', (row) => row.oppEfgPct, 1, '%', true),
    fixed<S>('tovPct', '失误率', '高阶', (row) => row.tovPct, 1, '%', true),
    fixed<S>('oppFgPct', '对手投篮%', '防守', (row) => ratio(opt(row, 'OPPONENTFIELDGOALSMADE'), opt(row, 'OPPONENTFIELDGOALSATTEMPTED')), 1, '%', true),
    fixed<S>('oppTpPct', '对手三分%', '防守', (row) => ratio(opt(row, 'OPPONENTTHREEPOINTSHOTSMADE'), opt(row, 'OPPONENTTHREEPOINTSHOTSATTEMPTED')), 1, '%', true),
    tc('forced', '制造失误', '防守', (row) => opt(row, 'TURNOVERSFORCED')),
    tc('paint', '内线得分', '得分构成', (row) => opt(row, 'POINTSINPAINT')),
    tc('fast', '快攻得分', '得分构成', (row) => opt(row, 'FASTBREAKPOINTS')),
    tc('bench', '替补得分', '得分构成', (row) => opt(row, 'BENCHPOINTS')),
    tc('second', '二次进攻得分', '得分构成', (row) => opt(row, 'SECONDCHANCEPOINTS')),
    tc('offTov', '利用失误得分', '得分构成', (row) => opt(row, 'POINTSOFFTURNOVERS')),
];

// ----- Recorded games (one row per game; mode does not apply) -----
type G = PlayerGame;
const g = (key: string, label: string, group: string, value: (row: G) => number | null, digits = 0, unit = '', lowerBetter = false) =>
    fixed<G>(key, label, group, value, digits, unit, lowerBetter);
export const playerGameMetrics: Metric<G>[] = [
    g('pts', '得分', '基础', (row) => row.points), g('reb', '篮板', '基础', (row) => row.rebounds), g('ast', '助攻', '基础', (row) => row.assists),
    g('stl', '抢断', '基础', (row) => row.steals), g('blk', '盖帽', '基础', (row) => row.blocks), g('tov', '失误', '基础', (row) => row.turnovers, 0, '', true),
    g('pf', '犯规', '基础', (row) => row.fouls ?? null, 0, '', true),
    g('oreb', '前场篮板', '篮板', (row) => row.offensiveRebounds ?? null), g('dreb', '后场篮板', '篮板', (row) => row.defensiveRebounds ?? null),
    g('min', '上场分钟', '基础', (row) => (row as G & { minutes?: number }).minutes ?? null, 1),
    g('pm', '正负值', '高阶', (row) => (row as G & { plusMinus?: number }).plusMinus ?? null, 1),
    g('efg', 'eFG%', '高阶', (row) => (row.fgAttempted ? ((row.fgMade + 0.5 * row.threeMade) / row.fgAttempted) * 100 : null), 1, '%'),
    g('stocks', '抢断+盖帽', '高阶', (row) => row.steals + row.blocks, 1),
    g('pra', '得分+篮板+助攻', '高阶', (row) => row.points + row.rebounds + row.assists, 1),
    g('fgm', '投篮命中', '投篮', (row) => row.fgMade), g('fga', '投篮出手', '投篮', (row) => row.fgAttempted),
    g('tpm', '三分命中', '投篮', (row) => row.threeMade), g('tpa', '三分出手', '投篮', (row) => row.threeAttempted),
    g('ftm', '罚球命中', '投篮', (row) => row.ftMade ?? null), g('fta', '罚球出手', '投篮', (row) => row.ftAttempted ?? null),
    g('fgPct', '投篮%', '命中率', (row) => ratio(row.fgMade, row.fgAttempted), 1, '%'),
    g('tpPct', '三分%', '命中率', (row) => ratio(row.threeMade, row.threeAttempted), 1, '%'),
    g('ftPct', '罚球%', '命中率', (row) => (row.ftAttempted ? ratio(row.ftMade ?? 0, row.ftAttempted) : null), 1, '%'),
    g('ts', 'TS%', '高阶', (row) => { const shots = row.fgAttempted + 0.44 * (row.ftAttempted ?? 0); return shots ? (row.points / (2 * shots)) * 100 : null; }, 1, '%'),
    g('gameScore', 'Game Score', '高阶', (row) => row.points + 0.4 * row.fgMade - 0.7 * row.fgAttempted - 0.4 * ((row.ftAttempted ?? 0) - (row.ftMade ?? 0))
        + 0.7 * (row.offensiveRebounds ?? 0) + 0.3 * (row.defensiveRebounds ?? row.rebounds) + row.steals + 0.7 * row.assists + 0.7 * row.blocks
        - 0.4 * (row.fouls ?? 0) - row.turnovers, 1),
];
type TG = TeamGame;
export const teamGameMetrics: Metric<TG>[] = [
    fixed<TG>('pts', '得分', '比分', (row) => row.points, 0), fixed<TG>('opp', '失分', '比分', (row) => row.allowed, 0, '', true),
    fixed<TG>('margin', '净胜分', '比分', (row) => row.margin, 0), fixed<TG>('total', '两队总分', '比分', (row) => row.points + row.allowed, 0),
    fixed<TG>('reb', '篮板', '基础', (row) => row.box.rebounds, 0), fixed<TG>('ast', '助攻', '基础', (row) => row.box.assists, 0),
    fixed<TG>('stl', '抢断', '基础', (row) => row.box.steals, 0), fixed<TG>('blk', '盖帽', '基础', (row) => row.box.blocks, 0),
    fixed<TG>('tov', '失误', '基础', (row) => row.box.turnovers, 0, '', true), fixed<TG>('oreb', '前场篮板', '基础', (row) => row.box.offensiveRebounds ?? null, 0),
    fixed<TG>('pf', '犯规', '基础', (row) => row.box.fouls ?? null, 0, '', true),
    fixed<TG>('tpm', '三分命中', '投篮', (row) => row.box.threeMade, 0), fixed<TG>('tpa', '三分出手', '投篮', (row) => row.box.threeAttempted, 0),
    fixed<TG>('fgm', '投篮命中', '投篮', (row) => row.box.fgMade, 0), fixed<TG>('fga', '投篮出手', '投篮', (row) => row.box.fgAttempted, 0),
    fixed<TG>('fgPct', '投篮%', '命中率', (row) => ratio(row.box.fgMade, row.box.fgAttempted), 1, '%'),
    fixed<TG>('tpPct', '三分%', '命中率', (row) => ratio(row.box.threeMade, row.box.threeAttempted), 1, '%'),
    fixed<TG>('ftPct', '罚球%', '命中率', (row) => ratio(row.box.ftMade ?? 0, row.box.ftAttempted ?? 0), 1, '%'),
    fixed<TG>('ts', 'TS%', '高阶', (row) => { const shots = row.box.fgAttempted + 0.44 * (row.box.ftAttempted ?? 0); return shots ? (row.box.points / (2 * shots)) * 100 : null; }, 1, '%'),
    fixed<TG>('efg', 'eFG%', '高阶', (row) => (row.box.fgAttempted ? ((row.box.fgMade + 0.5 * row.box.threeMade) / row.box.fgAttempted) * 100 : null), 1, '%'),
    fixed<TG>('oppFgPct', '对手投篮%', '防守', (row) => ratio(row.oppBox.fgMade, row.oppBox.fgAttempted), 1, '%', true),
    fixed<TG>('oppTpm', '对手三分命中', '防守', (row) => row.oppBox.threeMade, 0, '', true),
    fixed<TG>('rebDiff', '篮板差', '高阶', (row) => row.box.rebounds - row.oppBox.rebounds, 0),
    fixed<TG>('astTo', '助攻失误比', '高阶', (row) => (row.box.turnovers ? row.box.assists / row.box.turnovers : null), 2),
];

/** Display a metric value with its own decimals, so 21.06 shows as 21.1 rather than 21. */
export function show<T>(metric: Metric<T>, value: number | null, mode: Mode = 'perGame'): string {
    if (value === null || !Number.isFinite(value)) return '—';
    const digits = metric.counting && mode === 'total' ? 0 : metric.digits;
    return `${value.toFixed(digits)}${metric.unit ?? ''}`;
}

/** Group metrics for a picker: [{ label: group, options: [...] }]. */
export function metricOptions<T>(metrics: Metric<T>[]) {
    const groups = new Map<string, { value: string; label: string }[]>();
    for (const metric of metrics) {
        if (!groups.has(metric.group)) groups.set(metric.group, []);
        groups.get(metric.group)!.push({ value: metric.key, label: metric.label });
    }
    return [...groups].map(([label, options]) => ({ label, options }));
}

/**
 * Sum several player seasons into one career row: totals add up and the derived rates are
 * recomputed from the summed totals (so career TS% is not an average of season TS%).
 */
export function careerRow(seasons: PlayerSeason[]): PlayerSeason | null {
    if (seasons.length === 0) return null;
    const totals: Record<string, number> = {};
    for (const season of seasons) for (const [key, value] of Object.entries(season.totals ?? {})) totals[key] = (totals[key] ?? 0) + value;
    const games = seasons.reduce((sum, season) => sum + season.games, 0);
    const shots = (totals.FIELDGOALSATTEMPTED ?? 0) + 0.44 * (totals.FREETHROWATTEMPTED ?? 0);
    const fga = totals.FIELDGOALSATTEMPTED ?? 0;
    const at = (key: string) => totals[key] ?? 0;
    const gameScore = at('POINTS') + 0.4 * at('FIELDGOALSMADE') - 0.7 * fga - 0.4 * (at('FREETHROWATTEMPTED') - at('FREETHROWMADE'))
        + 0.7 * at('OFFENSIVEREBOUNDS') + 0.3 * at('DEFENSEREBOUNDS') + at('STEALS') + 0.7 * at('ASSISTS') + 0.7 * at('BLOCKS') - 0.4 * at('FOULS') - at('TURNOVERS');
    const last = seasons[seasons.length - 1];
    return {
        ...last, year: 0, games, totals,
        started: seasons.reduce((sum, season) => sum + season.started, 0),
        tsPct: shots ? r((at('POINTS') / (2 * shots)) * 100, 1) : null,
        efgPct: fga ? r(((at('FIELDGOALSMADE') + 0.5 * at('3POINTERSMADE')) / fga) * 100, 1) : null,
        threeRate: fga ? r(at('3POINTERSATTEMPTED') / fga, 3) : null,
        ftRate: fga ? r(at('FREETHROWATTEMPTED') / fga, 3) : null,
        astTo: at('TURNOVERS') ? r(at('ASSISTS') / at('TURNOVERS'), 2) : null,
        gameScore: games ? r(gameScore / games, 1) : 0,
        boxPlusMinus: games && 'BOX+-' in totals ? r(at('BOX+-') / games, 1) : null,
        oppFgPct: at('DEFENSIVEFIELDGOALSATTEMPTED') ? r((at('DEFENSIVEFIELDGOALSMADE') / at('DEFENSIVEFIELDGOALSATTEMPTED')) * 100, 1) : null,
        defRating: at('POSSESSIONS') ? r((at('TOTALPOINTSALLOWED') / at('POSSESSIONS')) * 100, 1) : null,
        doubleDoubles: seasons.reduce((sum, season) => sum + season.doubleDoubles, 0),
        tripleDoubles: seasons.reduce((sum, season) => sum + season.tripleDoubles, 0),
        fortyPoint: seasons.reduce((sum, season) => sum + season.fortyPoint, 0),
        fiftyPoint: seasons.reduce((sum, season) => sum + season.fiftyPoint, 0),
        honors: [...new Set(seasons.flatMap((season) => season.honors))],
        overall: null,
    };
}

/** Index of the single best value (ties or fewer than two values: -1). */
export function bestIndex(values: (number | null)[], lowerBetter = false): number {
    const present = values.filter((value): value is number => value !== null);
    if (present.length < 2) return -1;
    const target = lowerBetter ? Math.min(...present) : Math.max(...present);
    return present.filter((value) => value === target).length === 1 ? values.indexOf(target) : -1;
}
