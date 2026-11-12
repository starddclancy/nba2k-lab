/**
 * Recent form. The game keeps, per player, the last five games' date, minutes and Game Score
 * (Stats/History/GAMESCOREHISTORY*) and, per team, the last ten results and streaks. Full box scores
 * of recent games come from the native season game table.
 */
import type { TeamGame } from './gameLog.ts';
export type FormGame = { slot: number; date: string; minutes: number; gameScore: number };
export type PlayerForm = { index: number; name: string; games: FormGame[]; rankings: Record<string, number> };
export type TeamForm = {
    index: number; label: string; last10: string; wins: number; losses: number; last10Home: string; last10Away: string;
    streak: number; homeStreak: number; awayStreak: number; previousStreak: number; longestWin: number; longestLoss: number;
    homeWins: number; homeLosses: number;
};

export type Heat = { label: string; tone: 'hot' | 'warm' | 'normal' | 'cool' | 'cold'; delta: number | null };

/** Compare a recent average with the season average; thresholds are relative (±10% / ±25%). */
export function heat(recent: number | null, season: number | null): Heat {
    if (recent === null || season === null) return { label: '数据不足', tone: 'normal', delta: null };
    const delta = Math.round((recent - season) * 10) / 10;
    const scale = Math.max(Math.abs(season), 3);
    const ratio = delta / scale;
    if (ratio >= 0.25) return { label: '火热', tone: 'hot', delta };
    if (ratio >= 0.1) return { label: '上升', tone: 'warm', delta };
    if (ratio <= -0.25) return { label: '低迷', tone: 'cold', delta };
    if (ratio <= -0.1) return { label: '下滑', tone: 'cool', delta };
    return { label: '稳定', tone: 'normal', delta };
}

export const mean = (values: (number | null)[]) => {
    const present = values.filter((value): value is number => value !== null && Number.isFinite(value));
    return present.length ? Math.round((present.reduce((s, v) => s + v, 0) / present.length) * 10) / 10 : null;
};

/** "W3" / "L2" for a signed streak. */
export const streakText = (streak: number) => (streak > 0 ? `${streak} 连胜` : streak < 0 ? `${-streak} 连败` : '—');

/** Results string newest first → oldest first for left-to-right display. */
export const chronological = (results: string) => [...results].reverse();

/** Native games are chronological; result strings match the legacy newest-first shape. */
export function teamRecentForm(games: TeamGame[], index: number, label: string): TeamForm {
    const results = (rows: TeamGame[]) => rows.slice(-10).map((game) => game.won ? 'W' : 'L').reverse().join('');
    const wins = (rows: TeamGame[]) => rows.filter((game) => game.won).length;
    const streak = (rows: TeamGame[]) => {
        const won = rows.at(-1)?.won;
        let count = 0;
        for (let i = rows.length - 1; i >= 0 && rows[i].won === won; i--) count++;
        return won ? count : -count;
    };
    const home = games.filter((game) => game.home === true);
    const away = games.filter((game) => game.home === false);
    const recent = games.slice(-10);
    let longestWin = 0, longestLoss = 0, run = 0;
    for (const game of games) {
        run = game.won ? Math.max(0, run) + 1 : Math.min(0, run) - 1;
        longestWin = Math.max(longestWin, run);
        longestLoss = Math.max(longestLoss, -run);
    }
    const current = streak(games);
    return { index, label, last10: results(games), wins: wins(recent), losses: recent.length - wins(recent),
        last10Home: results(home), last10Away: results(away), streak: current,
        homeStreak: streak(home), awayStreak: streak(away), previousStreak: streak(games.slice(0, games.length - Math.abs(current))),
        longestWin, longestLoss, homeWins: wins(home), homeLosses: home.length - wins(home) };
}

export const rankingLabels: Record<string, string> = {
    LEAGUEOVRRATINGRANKING: '联盟总评排名', LEAGUEOVRRATINGRANKINGBYPOSITION: '同位置总评排名', CONFERENCEOVRRATINGRANKING: '分区总评排名',
    LEAGUEPERSCORERANKING: '联盟效率排名', LEAGUEPERSCORERANKINGBYPOSITION: '同位置效率排名', CONFERENCEPERSCORERANKING: '分区效率排名',
    OFFENSERATINGRANKING: '进攻评分排名', DEFENSERATINGRANKING: '防守评分排名',
};

/** Box-score line of one recorded game (structural, matches gameLog GameLine). */
type Line = { points: number; rebounds: number; assists: number; steals: number; blocks: number; turnovers: number;
    fgMade: number; fgAttempted: number; threeMade: number; threeAttempted: number; ftMade?: number; ftAttempted?: number;
    offensiveRebounds?: number; defensiveRebounds?: number; fouls?: number;
    /** Games this line covers (tracked multi-game segments); 1 when absent. */
    games?: number };

export type WindowStats = {
    games: number; pts: number; reb: number; ast: number; stl: number; blk: number; tov: number; tpm: number; tpa: number;
    fgPct: number | null; tpPct: number | null; ftPct: number | null; ts: number | null; efg: number | null; astTo: number | null;
    gameScore: number; usage: number;
};

const r1 = (value: number) => Math.round(value * 10) / 10;

/** Averages over a window of games; rates come from summed makes and attempts. */
export function windowStats(games: Line[]): WindowStats | null {
    if (games.length === 0) return null;
    const count = games.reduce((total, line) => total + (line.games ?? 1), 0);
    // Rows standing for several games hold per-game values; weight them back by their game count.
    const sum = (pick: (line: Line) => number) => games.reduce((total, line) => total + pick(line) * (line.games ?? 1), 0);
    const avg = (pick: (line: Line) => number) => r1(sum(pick) / count);
    const ratio = (made: number, attempted: number) => (attempted > 0 ? r1((made / attempted) * 100) : null);
    const fga = sum((g) => g.fgAttempted);
    const fta = sum((g) => g.ftAttempted ?? 0);
    const shots = fga + 0.44 * fta;
    const gameScore = (g: Line) => g.points + 0.4 * g.fgMade - 0.7 * g.fgAttempted - 0.4 * ((g.ftAttempted ?? 0) - (g.ftMade ?? 0))
        + 0.7 * (g.offensiveRebounds ?? 0) + 0.3 * (g.defensiveRebounds ?? g.rebounds) + g.steals + 0.7 * g.assists + 0.7 * g.blocks
        - 0.4 * (g.fouls ?? 0) - g.turnovers;
    return {
        games: count, pts: avg((g) => g.points), reb: avg((g) => g.rebounds), ast: avg((g) => g.assists), stl: avg((g) => g.steals),
        blk: avg((g) => g.blocks), tov: avg((g) => g.turnovers), tpm: avg((g) => g.threeMade), tpa: avg((g) => g.threeAttempted),
        fgPct: ratio(sum((g) => g.fgMade), fga), tpPct: ratio(sum((g) => g.threeMade), sum((g) => g.threeAttempted)),
        ftPct: ratio(sum((g) => g.ftMade ?? 0), fta), ts: shots > 0 ? r1((sum((g) => g.points) / (2 * shots)) * 100) : null,
        efg: fga > 0 ? r1(((sum((g) => g.fgMade) + 0.5 * sum((g) => g.threeMade)) / fga) * 100) : null,
        astTo: sum((g) => g.turnovers) > 0 ? Math.round((sum((g) => g.assists) / sum((g) => g.turnovers)) * 100) / 100 : null,
        gameScore: avg(gameScore), usage: avg((g) => g.fgAttempted + 0.44 * (g.ftAttempted ?? 0) + g.turnovers),
    };
}

export type Verdict = { tone: Heat['tone']; label: string; lines: string[] };

/**
 * Plain-language form report: compares the last few games with the longer baseline (more
 * recorded games, else the season line) and names what is going up or down.
 */
export function verdict(recent: WindowStats | null, baseline: Partial<WindowStats> | null, baselineName: string): Verdict {
    if (!recent || !baseline) return { tone: 'normal', label: '数据不足', lines: ['当前赛季尚无足够的逐场记录。'] };
    const lines: string[] = [];
    const change = (key: keyof WindowStats, label: string, unit = '', lowerBetter = false, threshold = 0.12) => {
        const now = recent[key];
        const base = baseline[key];
        if (typeof now !== 'number' || typeof base !== 'number') return 0;
        const delta = now - base;
        const scale = Math.max(Math.abs(base), unit === '%' ? 10 : 2);
        const ratio = delta / scale;
        if (Math.abs(ratio) < threshold) return 0;
        const good = (delta > 0) !== lowerBetter;
        lines.push(`${label}${good ? (delta > 0 ? '上升' : '下降') : (delta > 0 ? '增加' : '下滑')} ${Math.abs(r1(delta))}${unit}（${r1(now)}${unit}，${baselineName} ${r1(base)}${unit}）`);
        return good ? ratio : -ratio;
    };
    let score = 0;
    score += change('gameScore', '综合表现 Game Score', '', false, 0.1) * 2;
    score += change('pts', '得分');
    score += change('ts', '真实命中率', '%', false, 0.06);
    score += change('reb', '篮板');
    score += change('ast', '助攻');
    score += change('tpPct', '三分命中率', '%', false, 0.1);
    score += change('tov', '失误', '', true);
    score += change('stl', '抢断', '', false, 0.25) * 0.5;
    score += change('blk', '盖帽', '', false, 0.25) * 0.5;
    const tone: Heat['tone'] = score >= 0.9 ? 'hot' : score >= 0.3 ? 'warm' : score <= -0.9 ? 'cold' : score <= -0.3 ? 'cool' : 'normal';
    const label = { hot: '状态火热', warm: '状态上升', normal: '状态平稳', cool: '状态下滑', cold: '状态低迷' }[tone];
    if (lines.length === 0) lines.push(`各项数据与${baselineName}基本持平。`);
    return { tone, label, lines };
}
