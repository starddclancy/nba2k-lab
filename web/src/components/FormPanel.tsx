import { useState } from 'react';
import { Segmented } from 'antd';
import type { PlayerForm, TeamForm, WindowStats } from '../form';
import { chronological, heat, mean, rankingLabels, streakText, verdict, windowStats } from '../form';
import type { PlayerGame, TeamGame } from '../gameLog';
import type { PlayerSeason, TeamSeason } from '../seasonTrends';
import { teamLabel } from '../labels';
import LineChart from './LineChart';
import TeamIdentity from './TeamIdentity';

const toneClass = (tone: string) => `heat heat-${tone}`;

/** Window table rows: label, key in WindowStats, unit, lower is better. */
const windowRows: [string, keyof WindowStats, string, boolean][] = [
    ['得分', 'pts', '', false], ['篮板', 'reb', '', false], ['助攻', 'ast', '', false], ['抢断', 'stl', '', false], ['盖帽', 'blk', '', false],
    ['失误', 'tov', '', true], ['三分命中', 'tpm', '', false], ['三分出手', 'tpa', '', false], ['投篮%', 'fgPct', '%', false],
    ['三分%', 'tpPct', '%', false], ['罚球%', 'ftPct', '%', false], ['TS%', 'ts', '%', false], ['eFG%', 'efg', '%', false],
    ['助攻失误比', 'astTo', '', false], ['Game Score', 'gameScore', '', false], ['球权使用', 'usage', '', false],
];

/** Season line in WindowStats shape, so the season can be a column next to the game windows. */
function seasonWindow(season: PlayerSeason | null): Partial<WindowStats> | null {
    if (!season || !season.games) return null;
    const t = (key: string) => season.totals?.[key] ?? 0;
    const per = (key: string) => Math.round((t(key) / season.games) * 10) / 10;
    return {
        games: season.games, pts: per('POINTS'), reb: per('OFFENSIVEREBOUNDS') + per('DEFENSEREBOUNDS'), ast: per('ASSISTS'), stl: per('STEALS'),
        blk: per('BLOCKS'), tov: per('TURNOVERS'), tpm: per('3POINTERSMADE'), tpa: per('3POINTERSATTEMPTED'),
        fgPct: t('FIELDGOALSATTEMPTED') ? Math.round((t('FIELDGOALSMADE') / t('FIELDGOALSATTEMPTED')) * 1000) / 10 : null,
        tpPct: t('3POINTERSATTEMPTED') ? Math.round((t('3POINTERSMADE') / t('3POINTERSATTEMPTED')) * 1000) / 10 : null,
        ftPct: t('FREETHROWATTEMPTED') ? Math.round((t('FREETHROWMADE') / t('FREETHROWATTEMPTED')) * 1000) / 10 : null,
        ts: season.tsPct, efg: season.efgPct, astTo: season.astTo, gameScore: season.gameScore,
        usage: per('FIELDGOALSATTEMPTED') + Math.round(0.44 * per('FREETHROWATTEMPTED') * 10) / 10 + per('TURNOVERS'),
    };
}

/** Last 5 / 10 / 20 recorded games next to each other and the season, with a written verdict. */
function WindowTable({ games, season, windows = [5, 10, 20] }: { games: Parameters<typeof windowStats>[0]; season: Partial<WindowStats> | null; windows?: number[] }) {
    // Windows longer than the record collapse to the same games; keep the first that reaches the end.
    const counts = windows.filter((count, order) => count <= games.length || order === 0 || windows[order - 1] < games.length).map((count) => Math.min(count, games.length));
    const columns = [...new Set(counts)].map((count) => ({ count, stats: windowStats(games.slice(-count)) }));
    const all = windowStats(games);
    const head = [...columns.map((column) => `近 ${column.count} 场`), ...(season ? ['赛季'] : []), ...(games.length > 20 ? [`全部 ${games.length} 场`] : [])];
    const cells = (key: keyof WindowStats) => [...columns.map((column) => column.stats?.[key] ?? null), ...(season ? [season[key] ?? null] : []), ...(games.length > 20 ? [all?.[key] ?? null] : [])];
    return <table className="compare-table form-table window-table">
        <thead><tr><th>数据</th>{head.map((label) => <th key={label}>{label}</th>)}<th>近 5 场对比</th></tr></thead>
        <tbody>{windowRows.map(([label, key, unit, lowerBetter]) => {
            const values = cells(key);
            const recent = values[0];
            const base = values[columns.length] ?? values[values.length - 1];
            const delta = typeof recent === 'number' && typeof base === 'number' ? Math.round((recent - base) * 10) / 10 : null;
            const tone = delta === null || delta === 0 ? '' : (delta > 0) !== lowerBetter ? 'up' : 'down';
            return <tr key={key}><td>{label}</td>
                {values.map((value, order) => <td key={order}>{typeof value === 'number' ? `${value.toFixed(key === 'astTo' ? 2 : 1)}${unit}` : '—'}</td>)}
                <td className={tone}>{delta === null ? '—' : `${delta > 0 ? '▲' : delta < 0 ? '▼' : '■'} ${Math.abs(delta).toFixed(1)}`}</td></tr>;
        })}</tbody>
    </table>;
}

export function PlayerFormPanel({ form, season, games }: { form: PlayerForm | null; season: PlayerSeason | null; games: PlayerGame[] }) {
    const recentGames = games.slice(-5).map((game) => ({
        date: game.date, minutes: game.minutes ?? 0, gameScore: windowStats([game])!.gameScore,
    }));
    const recentScore = mean(recentGames.map((game) => game.gameScore));
    const seasonScore = season?.gameScore ?? null;
    const state = heat(recentScore, seasonScore);
    const seasonLine = seasonWindow(season);
    const baseline = games.length >= 10 ? windowStats(games.slice(-20)) : seasonLine;
    const report = verdict(windowStats(games.slice(-5)), baseline, games.length >= 10 ? `近 ${Math.min(20, games.length)} 场` : '赛季');
    const rankings = Object.entries(form?.rankings ?? {}).filter(([, value]) => value > 0);
    return <section className="chart-card wide form-card">
        <h3>近期状态<span className="muted">游戏赛季逐场记录 · {games.length} 场</span></h3>
        <div className="form-grid three">
            <div className="form-badge">
                <span className={toneClass(games.length ? report.tone : state.tone)}>{games.length ? report.label : state.label}</span>
                <b>{recentScore === null ? '—' : recentScore.toFixed(1)}</b>
                <small>游戏记录近 {recentGames.length} 场 Game Score</small>
                <em>赛季 {seasonScore === null ? '—' : seasonScore.toFixed(1)}{state.delta !== null ? ` · ${state.delta > 0 ? '+' : ''}${state.delta.toFixed(1)}` : ''}</em>
                {rankings.length > 0 && <div className="rank-chips">{rankings.map(([key, value]) => <span key={key}>{rankingLabels[key] ?? key}<b>#{value}</b></span>)}</div>}
            </div>
            <div className="form-verdict">
                <h4>状态评价</h4>
                <ul>{(games.length ? report.lines : [state.delta === null ? '游戏里还没有这名球员的近期比赛记录。'
                    : `近 ${recentGames.length} 场 Game Score ${recentScore} ，比赛季场均${state.delta >= 0 ? '高' : '低'} ${Math.abs(state.delta)}。`,
                    '当前赛季尚无足够的逐场记录。']).map((text) => <li key={text}>{text}</li>)}</ul>
            </div>
            <div className="form-chart">
                {recentGames.length === 0 ? <div className="chart-empty">游戏里还没有这名球员的近期比赛记录</div>
                    : <LineChart labels={recentGames.map((game) => game.date.slice(5))} height={170} format={(v) => v.toFixed(1)} series={[
                        { key: 'gs', label: 'Game Score', color: 'var(--primary)', values: recentGames.map((game) => game.gameScore) },
                        { key: 'min', label: '上场分钟', color: 'var(--warning)', values: recentGames.map((game) => game.minutes) },
                        { key: 'avg', label: '赛季场均', color: 'var(--muted)', values: recentGames.map(() => seasonScore) },
                    ]} />}
            </div>
        </div>
        {games.length > 0 && <div className="form-compare"><WindowTable games={games} season={seasonLine} /></div>}
    </section>;
}

export function TeamFormPanel({ form, season, games, teams }: { form: TeamForm | null; season: TeamSeason | null; games: TeamGame[];
    teams?: { index: number; label: string; logoReference?: string | null; logoReferences?: string[]; historicYearRaw?: number | null }[] }) {
    const [count, setCount] = useState(10);
    if (!form) return <section className="chart-card wide form-card"><h3>近期状态</h3><div className="chart-empty">读取中或游戏未提供近期战绩</div></section>;
    const results = chronological(form.last10);
    const recentPct = form.last10.length ? Math.round((form.wins / form.last10.length) * 1000) / 10 : null;
    const state = heat(recentPct, season?.winPct ?? null);
    const boxes = games.map((game) => game.box);
    const report = verdict(windowStats(boxes.slice(-5)), games.length >= 10 ? windowStats(boxes.slice(-20)) : null, `近 ${Math.min(20, games.length)} 场`);
    const last = games.slice(-count);
    const brand = (label: string) => {
        const matches = teams?.filter((team) => team.label === label) ?? [];
        return matches.length === 1 ? { ...matches[0], linkIndex: matches[0].index } : { label };
    };
    const dots = (text: string) => [...text].reverse().map((result, index) => <i key={index} className={`wl ${result === 'W' ? 'w' : 'l'}`} />);
    return <section className="chart-card wide form-card">
        <h3>近期状态<span className="muted">游戏记录的最近 10 场胜负（绿胜红负，左旧右新）</span></h3>
        <div className="form-grid three">
            <div className="form-badge">
                <span className={toneClass(state.tone)}>{state.label}</span>
                <b>{form.wins}-{form.losses}</b>
                <small>近 {form.last10.length} 场 · 胜率 {recentPct ?? '—'}%</small>
                <em>赛季胜率 {season ? `${season.winPct.toFixed(1)}%` : '—'} · {streakText(form.streak)}</em>
                <div className="wl-strip big" title="从左到右：较早 → 最近">{results.map((result, index) => <i key={index} className={`wl ${result === 'W' ? 'w' : 'l'}`}
                    style={{ animationDelay: `${index * 50}ms` }} />)}</div>
            </div>
            <div className="form-verdict">
                <h4>状态评价</h4>
                <ul>
                    <li>近 {form.last10.length} 场 {form.wins} 胜 {form.losses} 负，{state.delta === null ? '' : `胜率比赛季${state.delta >= 0 ? '高' : '低'} ${Math.abs(state.delta)} 个百分点，`}当前{streakText(form.streak)}。</li>
                    {games.length >= 10 ? report.lines.slice(0, 5).map((text) => <li key={text}>{text}</li>)
                        : <li className="muted">游戏赛季记录 {games.length} 场，满 10 场后显示长期状态对比。</li>}
                </ul>
                <div className="rank-chips">
                    <span>主场近况 {dots(form.last10Home)}</span>
                    <span>客场近况 {dots(form.last10Away)}</span>
                    <span>主场战绩<b>{form.homeWins}-{form.homeLosses}</b></span>
                    <span>最长连胜<b>{form.longestWin}</b></span><span>最长连败<b>{form.longestLoss}</b></span>
                </div>
            </div>
            <div className="form-games">
                {last.length === 0 ? <div className="chart-empty">当前赛季还没有这支球队的已赛记录</div> : <>
                    <Segmented size="small" value={count} onChange={(value) => setCount(Number(value))} options={[5, 10, 20].map((n) => ({ value: n, label: `近 ${n} 场` }))} />
                    <div className="game-list">{[...last].reverse().map((game) => <div key={game.game} className="game-line">
                        <i className={`wl ${game.won ? 'w' : 'l'}`} />
                        <TeamIdentity {...brand(game.opponent)} size={18} text={teamLabel(game.opponent)} />
                        <b className={game.won ? 'up' : 'down'}>{game.points}-{game.allowed}</b>
                        <small className="muted">{game.date.slice(5)}</small>
                    </div>)}</div>
                </>}
            </div>
        </div>
        {games.length > 0 && <div className="form-compare"><WindowTable games={boxes} season={null} /></div>}
    </section>;
}
