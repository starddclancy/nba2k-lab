import { useEffect, useMemo, useState } from 'react';
import { App as AntApp, Empty, InputNumber, Segmented, Select, Spin } from 'antd';
import { call } from '../bridge';
import { listPlayers, listTeamRecords } from '../api';
import type { PlayerRow, TeamItem } from '../types';
import { teamLabel } from '../labels';
import { leagueSeasons, leagueTeams, minimumGames, rank } from '../leaders';
import type { LeagueTable } from '../leaders';
import { metricOptions, modeLabels, playerMetrics, show, teamMetrics } from '../metrics';
import type { Metric, Mode } from '../metrics';
import type { PlayerSeason, TeamSeason } from '../seasonTrends';
import Avatar from './Avatar';
import TeamIdentity from './TeamIdentity';
import EntityLink from './EntityLink';
import { useWorkspace } from '../workspaceContext';

/** Stats shown on the leader wall (first place of each). */
const wallPlayer = ['pts', 'reb', 'ast', 'stl', 'blk', 'tpm', 'tpa', 'ftm', 'fgPct', 'tpPct', 'ftPct', 'ts', 'efg', 'gameScore', 'bpm', 'min', 'oreb', 'dd', 'td', 'tov'];
const wallTeam = ['winPct', 'pts', 'opp', 'margin', 'tpm', 'tpa', 'tpPct', 'fgPct', 'ortg', 'drtg', 'net', 'pace', 'efg', 'oppEfg', 'paint', 'fast', 'bench', 'tovPct'];
/** Rate stats need a games minimum to qualify. */
const isRate = (metric: Metric<unknown>) => !metric.counting && !['gp', 'gs', 'dd', 'td', 'p40', 'ovr', 'wins', 'losses'].includes(metric.key);

type PlayerEntry = { row: PlayerRow; season: PlayerSeason };
type TeamEntry = { index: number; label: string; season: TeamSeason; brand?: TeamItem };

/**
 * League leaderboards from the game's current-season records: a wall with the No. 1 in each
 * stat and a full ranked list for any stat, per game / total / per 36. Read-only.
 */
export default function LeadersPage() {
    const { message } = AntApp.useApp();
    const { seasonEndYear } = useWorkspace();
    const [scope, setScope] = useState<'player' | 'team'>('player');
    const [mode, setMode] = useState<Mode>('perGame');
    const [players, setPlayers] = useState<PlayerRow[]>([]);
    const [teams, setTeams] = useState<TeamItem[]>([]);
    const [table, setTable] = useState<LeagueTable | null>(null);
    const [teamRows, setTeamRows] = useState<Record<string, number | string>[]>([]);
    const [metricKey, setMetricKey] = useState('tpm');
    const [teamMetricKey, setTeamMetricKey] = useState('tpm');
    const [minGames, setMinGames] = useState<number | null>(null);
    const [position, setPosition] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        setBusy(true);
        void Promise.all([listPlayers('', null), listTeamRecords(), call<LeagueTable>('leagueSeason'), call<Record<string, number | string>[]>('leagueTeamSeason')])
            .then(([rows, teamItems, league, teamLines]) => { setPlayers(rows); setTeams(teamItems); setTable(league); setTeamRows(teamLines); })
            .catch((error: Error) => message.error(error.message))
            .finally(() => setBusy(false));
    }, [message]);

    const seasons = useMemo(() => leagueSeasons(table), [table]);
    const entries: PlayerEntry[] = useMemo(() => players.flatMap((row) => {
        const season = seasons.get(row.index);
        return season && row.category === 'team' ? [{ row, season }] : season ? [{ row, season }] : [];
    }), [players, seasons]);
    const teamEntries: TeamEntry[] = useMemo(() => leagueTeams(teamRows, seasonEndYear).map((entry) => ({ ...entry, brand: teams.find((team) => team.index === entry.index) })), [teamRows, teams, seasonEndYear]);
    const autoMin = minimumGames(entries.map((entry) => entry.season.games));
    const games = minGames ?? autoMin;
    const pool = entries.filter((entry) => !position || entry.row.summary?.position === position || entry.row.summary?.secondaryPosition === position);

    const rankPlayers = (metric: Metric<PlayerSeason>) => rank(pool, (entry) => entry.season, metric, mode,
        (entry) => entry.season.games >= (isRate(metric as Metric<unknown>) ? games : 1));
    const rankTeams = (metric: Metric<TeamSeason>) => rank(teamEntries, (entry) => entry.season, metric, mode, () => true);
    const metric = playerMetrics.find((item) => item.key === metricKey) ?? playerMetrics[0];
    const teamMetric = teamMetrics.find((item) => item.key === teamMetricKey) ?? teamMetrics[0];
    const list = scope === 'player' ? rankPlayers(metric).slice(0, 50) : rankTeams(teamMetric);
    const top = list[0]?.value ?? 0;
    const bottom = list[list.length - 1]?.value ?? 0;
    const width = (value: number) => {
        const lowerBetter = scope === 'player' ? metric.lowerBetter : teamMetric.lowerBetter;
        const span = Math.max(Math.abs(top), Math.abs(bottom), 1e-9);
        return Math.max(4, Math.min(100, lowerBetter ? (Math.abs(bottom) / Math.max(Math.abs(value), 1e-9)) * 100 : (Math.abs(value) / span) * 100));
    };
    const label = (m: Metric<unknown>) => `${m.label}${m.counting ? ` · ${modeLabels[mode]}` : ''}`;

    return <div className="analytics leaders">
        <div className="analytics-bar">
            <Segmented value={scope} onChange={(value) => setScope(value as 'player' | 'team')} options={[{ value: 'player', label: '球员榜' }, { value: 'team', label: '球队榜' }]} />
            <Segmented value={mode} onChange={(value) => setMode(value as Mode)} options={(Object.keys(modeLabels) as Mode[]).map((key) => ({ value: key, label: modeLabels[key] }))} />
            {scope === 'player' && <>
                <Select allowClear size="middle" style={{ width: 96 }} placeholder="位置" value={position}
                    options={['PG', 'SG', 'SF', 'PF', 'C'].map((value) => ({ value, label: value }))} onChange={(value) => setPosition(value ?? null)} />
                <span className="muted" style={{ fontSize: 12 }}>命中率等比率榜最少出场</span>
                <InputNumber size="middle" min={1} max={82} style={{ width: 80 }} value={games} onChange={(value) => setMinGames(value)} />
                <span className="muted" style={{ fontSize: 12 }}>场</span>
            </>}
            <span className="muted" style={{ marginLeft: 'auto', fontSize: 12 }}>
                本赛季 · {scope === 'player' ? `${entries.length} 名有出场记录的球员` : `${teamEntries.length} 支球队`} · 游戏数据只读</span>
        </div>
        <Spin spinning={busy}>
            {(scope === 'player' ? entries.length : teamEntries.length) === 0 && !busy ? <Empty style={{ marginTop: 60 }} description="游戏里还没有本赛季数据" /> : <div className="analytics-body">
                <section className="leader-wall">
                    {(scope === 'player' ? wallPlayer.map((key) => playerMetrics.find((item) => item.key === key)!).filter(Boolean) : []).map((m, order) => {
                        const first = rankPlayers(m)[0];
                        const second = rankPlayers(m)[1];
                        return <div key={m.key} className={`leader-tile ${metricKey === m.key ? 'on' : ''}`} style={{ animationDelay: `${order * 25}ms` }}>
                            <button type="button" className="leader-stat" onClick={() => setMetricKey(m.key)}>{label(m as Metric<unknown>)}</button>
                            {first ? <>
                                <span className="leader-who"><Avatar name={first.item.row.name} team={first.item.row.team} photoId={first.item.row.photoId} size={30} loadImage />
                                    <span><b><EntityLink page="players" index={first.item.row.index}>{first.item.row.name}</EntityLink></b><small>{first.item.row.team ? <TeamIdentity {...(teams.find((t) => t.label === first.item.row.team) ?? { label: first.item.row.team })} linkIndex={first.item.row.teamIndices?.[0]} size={14} text={teamLabel(first.item.row.team)} /> : '自由球员'}</small></span></span>
                                <span className="leader-value">{show(m, first.value, mode)}</span>
                                {second && <small className="leader-gap">领先第二名 {show({ ...m, unit: '' }, Math.abs(first.value - second.value), mode)}</small>}
                            </> : <span className="muted">—</span>}
                        </div>;
                    })}
                    {(scope === 'team' ? wallTeam.map((key) => teamMetrics.find((item) => item.key === key)!).filter(Boolean) : []).map((m, order) => {
                        const ranked = rankTeams(m);
                        const first = ranked[0];
                        const second = ranked[1];
                        return <div key={m.key} className={`leader-tile ${teamMetricKey === m.key ? 'on' : ''}`} style={{ animationDelay: `${order * 25}ms` }}>
                            <button type="button" className="leader-stat" onClick={() => setTeamMetricKey(m.key)}>{label(m as Metric<unknown>)}</button>
                            {first ? <>
                                <span className="leader-who">{first.item.brand && <TeamIdentity {...first.item.brand} size={28} text="" />}
                                    <span><b><EntityLink page="teams" index={first.item.index}>{teamLabel(first.item.label)}</EntityLink></b><small>{first.item.season.wins}-{first.item.season.losses}</small></span></span>
                                <span className="leader-value">{show(m, first.value, mode)}</span>
                                {second && <small className="leader-gap">领先第二名 {show({ ...m, unit: '' }, Math.abs(first.value - second.value), mode)}</small>}
                            </> : <span className="muted">—</span>}
                        </div>;
                    })}
                </section>
                <section className="chart-card wide leader-board">
                    <h3>{scope === 'player' ? `${label(metric as Metric<unknown>)} 排行` : `${label(teamMetric as Metric<unknown>)} 排行`}
                        <span className="muted">{scope === 'player' ? `前 ${list.length} 名${isRate(metric as Metric<unknown>) ? ` · 至少出场 ${games} 场` : ''}` : `全部 ${list.length} 支球队`}</span>
                        <span className="metric-tools">
                            {scope === 'player'
                                ? <Select showSearch optionFilterProp="label" size="small" style={{ width: 180 }} value={metricKey} onChange={setMetricKey} options={metricOptions(playerMetrics)} popupMatchSelectWidth={false} />
                                : <Select showSearch optionFilterProp="label" size="small" style={{ width: 180 }} value={teamMetricKey} onChange={setTeamMetricKey} options={metricOptions(teamMetrics)} popupMatchSelectWidth={false} />}
                        </span>
                    </h3>
                    <div className="leader-rows" key={`${scope}-${metricKey}-${teamMetricKey}-${mode}-${position}-${games}`}>
                        {list.map((entry, order) => {
                            const isPlayer = scope === 'player';
                            const p = entry.item as PlayerEntry;
                            const t = entry.item as TeamEntry;
                            return <div key={isPlayer ? p.row.key ?? p.row.index : t.index} className={`leader-row rank-${entry.rank <= 3 ? entry.rank : 'n'}`} style={{ animationDelay: `${Math.min(order, 30) * 18}ms` }}>
                                <span className="leader-rank">{entry.rank}</span>
                                {isPlayer
                                    ? <span className="leader-name"><Avatar name={p.row.name} team={p.row.team} photoId={p.row.photoId} size={24} loadImage />
                                        <b><EntityLink page="players" index={p.row.index}>{p.row.name}</EntityLink></b><small>{p.row.team ? <TeamIdentity {...(teams.find((t) => t.label === p.row.team) ?? { label: p.row.team })} linkIndex={p.row.teamIndices?.[0]} size={14} text={teamLabel(p.row.team)} /> : '自由球员'} · {p.row.summary?.position ?? ''} · {p.season.games} 场</small></span>
                                    : <span className="leader-name">{t.brand && <TeamIdentity {...t.brand} size={22} text="" />}<b><EntityLink page="teams" index={t.index}>{teamLabel(t.label)}</EntityLink></b><small>{t.season.wins}-{t.season.losses}</small></span>}
                                <span className="leader-bar"><i style={{ width: `${width(entry.value)}%` }} /></span>
                                <span className="leader-num">{isPlayer ? show(metric, entry.value, mode) : show(teamMetric, entry.value, mode)}</span>
                            </div>;
                        })}
                    </div>
                </section>
            </div>}
        </Spin>
    </div>;
}
