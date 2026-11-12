/**
 * Season trend lines for the analytics charts. Inputs are what the bridge already returns:
 * readPlayerStats seasons (field cells keyed "SeasonStats/<selector>/<group>/<NAME>") and
 * teamSeasons rows (TeamStatsData fields). The game stores seasons as a compact year where
 * 108 is the 2007-08 season (1900 + 108 = 2008 = season end year).
 */
export type PlayerSeason = {
    year: number; games: number; minutes: number; points: number; rebounds: number; assists: number;
    steals: number; blocks: number; fgPct: number | null; threePct: number | null; ftPct: number | null; overall: number | null;
    // Advanced, computed from the same season record.
    started: number; turnovers: number; fouls: number;
    tsPct: number | null; efgPct: number | null; ftRate: number | null; threeRate: number | null; astTo: number | null;
    per36: number | null; gameScore: number; boxPlusMinus: number | null;
    oppFgPct: number | null; defRating: number | null;
    doubleDoubles: number; tripleDoubles: number; fortyPoint: number; fiftyPoint: number; honors: string[];
    /** Every numeric field of the season record by its short name (POINTS, 3POINTERSMADE, ...). */
    totals: Record<string, number>;
};

const honorNames: [string, string][] = [['MOSTVALUABLEPLAYER', 'MVP'], ['FINALSMVP', 'FMVP'], ['CHAMPIONSHIP', '总冠军'],
    ['DEFENSEPLAYEROFTHEYEAR', 'DPOY'], ['ROOKIEOFTHEYEAR', 'ROY'], ['SIXTHMAN', '最佳第六人'], ['MOSTIMPROVEDPLAYER', 'MIP'],
    ['CLUTCHPLAYER', '关键先生'], ['ALLSTARSTARTER', '全明星首发'], ['ALLSTARRESERVE', '全明星替补'], ['ALLNBATEAM', '最佳阵容'],
    ['ALLDEFENSETEAM', '最佳防守阵容'], ['ALLROOKIETEAM', '最佳新秀阵容'], ['ISTCHAMPION', '季中赛冠军']];
const round = (value: number, digits = 1) => Math.round(value * 10 ** digits) / 10 ** digits;

type Cell = { key: string; value: unknown; display?: string };
type SeasonBlock = { selector: string; fields: Cell[] };

const per = (total: number, games: number) => (games > 0 ? Math.round((total / games) * 10) / 10 : 0);
const pct = (made: number, attempted: number) => (attempted > 0 ? Math.round((made / attempted) * 1000) / 10 : null);
export const seasonLabel = (year: number) => `${year - 1}-${String(year % 100).padStart(2, '0')}`;

export function playerSeasons(blocks: SeasonBlock[]): PlayerSeason[] {
    const result: PlayerSeason[] = [];
    for (const block of blocks) {
        const value = (name: string) => {
            const cell = block.fields.find((field) => field.key.endsWith(`/${name}`));
            const number = Number(cell?.value);
            return Number.isFinite(number) ? number : 0;
        };
        const compact = value('COMPACTYEAR');
        const games = value('GAMESPLAYED');
        // Compact year 0 rows are playoff/aggregate records without a season; skip empty seasons.
        if (compact <= 0 || games <= 0) continue;
        const overall = value('OVERALL');
        const totals: Record<string, number> = {};
        for (const field of block.fields) {
            const number = Number(field.value);
            if (Number.isFinite(number) && typeof field.value !== 'boolean') totals[field.key.split('/').pop()!] = number;
        }
        const flag = (name: string) => {
            const cell = block.fields.find((field) => field.key.endsWith(`/${name}`));
            const raw = cell?.value;
            return raw === 'Yes' || raw === true || cell?.display === 'Yes' || (typeof raw === 'number' && raw > 0);
        };
        const pts = value('POINTS');
        const fgm = value('FIELDGOALSMADE');
        const fga = value('FIELDGOALSATTEMPTED');
        const tpm = value('3POINTERSMADE');
        const tpa = value('3POINTERSATTEMPTED');
        const ftm = value('FREETHROWMADE');
        const fta = value('FREETHROWATTEMPTED');
        const orb = value('OFFENSIVEREBOUNDS');
        const drb = value('DEFENSEREBOUNDS');
        const ast = value('ASSISTS');
        const stl = value('STEALS');
        const blk = value('BLOCKS');
        const tov = value('TURNOVERS');
        const pf = value('FOULS');
        const minutes = value('MINUTES');
        const defPoss = value('POSSESSIONS');
        const dfga = value('DEFENSIVEFIELDGOALSATTEMPTED');
        const shots = fga + 0.44 * fta;
        // Hollinger Game Score, averaged per game.
        const gameScore = pts + 0.4 * fgm - 0.7 * fga - 0.4 * (fta - ftm) + 0.7 * orb + 0.3 * drb + stl + 0.7 * ast + 0.7 * blk - 0.4 * pf - tov;
        const advanced = {
            started: value('GAMESSTARTED'), turnovers: per(tov, games), fouls: per(pf, games),
            tsPct: shots > 0 ? round((pts / (2 * shots)) * 100) : null,
            efgPct: fga > 0 ? round(((fgm + 0.5 * tpm) / fga) * 100) : null,
            ftRate: fga > 0 ? round(fta / fga, 3) : null,
            threeRate: fga > 0 ? round(tpa / fga, 3) : null,
            astTo: tov > 0 ? round(ast / tov, 2) : null,
            per36: minutes > 0 ? round((pts / minutes) * 36) : null,
            gameScore: round(gameScore / games),
            boxPlusMinus: block.fields.some((field) => field.key.endsWith('/BOX+-')) ? round(value('BOX+-') / games) : null,
            oppFgPct: dfga > 0 ? round((value('DEFENSIVEFIELDGOALSMADE') / dfga) * 100) : null,
            defRating: defPoss > 0 ? round((value('TOTALPOINTSALLOWED') / defPoss) * 100) : null,
            doubleDoubles: value('DOUBLEDOUBLES'), tripleDoubles: value('TRIPLEDOUBLES'),
            fortyPoint: value('FORTYPOINTGAMES'), fiftyPoint: value('FIFTYPOINTGAMES') + value('SIXTYPOINTGAMES'),
            honors: honorNames.filter(([name]) => flag(name)).map(([, label]) => label),
        };
        result.push({
            year: 1900 + compact, games,
            minutes: per(value('MINUTES'), games),
            points: per(value('POINTS'), games),
            rebounds: per(value('OFFENSIVEREBOUNDS') + value('DEFENSEREBOUNDS'), games),
            assists: per(value('ASSISTS'), games),
            steals: per(value('STEALS'), games),
            blocks: per(value('BLOCKS'), games),
            fgPct: pct(value('FIELDGOALSMADE'), value('FIELDGOALSATTEMPTED')),
            threePct: pct(value('3POINTERSMADE'), value('3POINTERSATTEMPTED')),
            ftPct: pct(value('FREETHROWMADE'), value('FREETHROWATTEMPTED')),
            overall: overall > 0 ? overall : null,
            ...advanced,
            totals,
        });
    }
    // One line per season, oldest first (a traded player can have two records in one season: merge by summing is
    // not possible from per-game values, so keep the one with more games).
    const bySeason = new Map<number, PlayerSeason>();
    for (const season of result) {
        const current = bySeason.get(season.year);
        if (!current || season.games > current.games) bySeason.set(season.year, season);
    }
    return [...bySeason.values()].sort((a, b) => a.year - b.year);
}

export type TeamSeason = {
    year: number; wins: number; losses: number; winPct: number; pointsFor: number; pointsAgainst: number | null;
    margin: number | null; fgPct: number | null; threePct: number | null; pace: number | null; current: boolean;
    // Advanced team numbers; null where the season record does not hold the inputs.
    offRating: number | null; defRating: number | null; netRating: number | null; efgPct: number | null; oppEfgPct: number | null;
    tovPct: number | null; paint: number | null; fastBreak: number | null; bench: number | null; secondChance: number | null;
    forcedTurnovers: number | null; assists: number | null; rebounds: number | null;
    games: number; totals: Record<string, number>;
};

export function teamSeasons(rows: Record<string, number>[]): TeamSeason[] {
    const result: TeamSeason[] = [];
    for (const row of rows) {
        const games = (row.WINS ?? 0) + (row.LOSSES ?? 0);
        if (!row.COMPACTYEAR || games <= 0) continue;
        const pointsFor = per(row.TOTALPOINTS ?? 0, games);
        // Older seasons in the live roster store 0 for points allowed and possessions: not recorded.
        const pointsAgainst = row.TOTALPOINTSALLOWED ? per(row.TOTALPOINTSALLOWED, games) : null;
        result.push({
            year: 1900 + row.COMPACTYEAR, wins: row.WINS ?? 0, losses: row.LOSSES ?? 0,
            winPct: Math.round(((row.WINS ?? 0) / games) * 1000) / 10,
            pointsFor, pointsAgainst, margin: pointsAgainst === null ? null : Math.round((pointsFor - pointsAgainst) * 10) / 10,
            fgPct: pct(row.FIELDGOALSMADE ?? 0, row.FIELDGOALSATTEMPTED ?? 0),
            threePct: pct(row.THREEPOINTSHOTSMADE ?? 0, row.THREEPOINTSHOTSATTEMPTED ?? 0),
            pace: row.POSSESSIONS ? per(row.POSSESSIONS, games) : null,
            current: row.slot === 0,
            ...teamAdvanced(row, games),
            games, totals: Object.fromEntries(Object.entries(row).filter(([key]) => key !== 'slot' && key !== 'id')),
        });
    }
    const byYear = new Map<number, TeamSeason>();
    for (const season of result) {
        const current = byYear.get(season.year);
        if (!current || season.wins + season.losses > current.wins + current.losses) byYear.set(season.year, season);
    }
    return [...byYear.values()].sort((a, b) => a.year - b.year);
}

/** Nice axis bounds and ticks for a series (min/max padded, 4 steps). */
export function axis(values: number[], floor?: number): { min: number; max: number; ticks: number[] } {
    const finite = values.filter((value) => Number.isFinite(value));
    if (finite.length === 0) return { min: 0, max: 1, ticks: [0, 1] };
    let min = Math.min(...finite);
    let max = Math.max(...finite);
    if (floor !== undefined) min = Math.min(min, floor);
    if (min === max) { min -= 1; max += 1; }
    const raw = (max - min) / 4;
    const magnitude = 10 ** Math.floor(Math.log10(raw));
    const step = [1, 2, 2.5, 5, 10].map((m) => m * magnitude).find((s) => s >= raw) ?? raw;
    const lo = Math.floor(min / step) * step;
    const hi = Math.ceil(max / step) * step;
    const ticks: number[] = [];
    for (let tick = lo; tick <= hi + step / 2; tick += step) ticks.push(Math.round(tick * 100) / 100);
    return { min: lo, max: hi, ticks };
}

function teamAdvanced(row: Record<string, number>, games: number) {
    const poss = row.POSSESSIONS ?? 0;
    const fga = row.FIELDGOALSATTEMPTED ?? 0;
    const ofga = row.OPPONENTFIELDGOALSATTEMPTED ?? 0;
    const off = poss && row.TOTALPOINTS ? round((row.TOTALPOINTS / poss) * 100) : null;
    const def = poss && row.TOTALPOINTSALLOWED ? round((row.TOTALPOINTSALLOWED / poss) * 100) : null;
    const optional = (key: string) => (row[key] ? per(row[key], games) : null);
    return {
        offRating: off, defRating: def, netRating: off !== null && def !== null ? round(off - def) : null,
        efgPct: fga ? round((((row.FIELDGOALSMADE ?? 0) + 0.5 * (row.THREEPOINTSHOTSMADE ?? 0)) / fga) * 100) : null,
        oppEfgPct: ofga ? round((((row.OPPONENTFIELDGOALSMADE ?? 0) + 0.5 * (row.OPPONENTTHREEPOINTSHOTSMADE ?? 0)) / ofga) * 100) : null,
        tovPct: poss && row.TURNOVERS ? round((row.TURNOVERS / poss) * 100) : null,
        paint: optional('POINTSINPAINT'), fastBreak: optional('FASTBREAKPOINTS'), bench: optional('BENCHPOINTS'),
        secondChance: optional('SECONDCHANCEPOINTS'), forcedTurnovers: optional('TURNOVERSFORCED'),
        assists: optional('ASSISTS'), rebounds: row.OFFENSIVEREBOUNDS || row.DEFENSIVEREBOUNDS
            ? per((row.OFFENSIVEREBOUNDS ?? 0) + (row.DEFENSIVEREBOUNDS ?? 0), games) : null,
    };
}