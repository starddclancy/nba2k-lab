import { playerTitle } from '../archetype';
import { useEffect, useMemo, useState } from 'react';
import { Alert, App as AntApp, Empty, Segmented, Select, Spin, Tag } from 'antd';
import { call } from '../bridge';
import { listPlayers, listTeamRecords, readPlayerRatings, readPlayerStats } from '../api';
import type { PlayerRow, TeamItem } from '../types';
import { teamLabel } from '../labels';
import { categoryAverages, poolOf, statsByIndex } from '../scouting';
import type { RatingTable } from '../scouting';
import { playerSeasons, seasonLabel, teamSeasons } from '../seasonTrends';
import type { PlayerSeason, TeamSeason } from '../seasonTrends';
import { attributeRows } from '../comparison';
import BigRadar from './BigRadar';
import Avatar from './Avatar';
import TeamIdentity from './TeamIdentity';
import EntityLink from './EntityLink';
import ChartBoard from './ChartBoard';
import { emptyLog, playerGames, teamGames } from '../gameLog';
import type { PlayerGame, TeamGame } from '../gameLog';
import { seasonLog } from '../seasonGames';
import { useSeasonGames } from '../useSeasonGames';
import { gameCount, lastGames } from '../seasonTracker';
import { bestIndex, careerRow, modeLabels, playerGameMetrics, playerMetrics, show, teamGameMetrics, teamMetrics } from '../metrics';
import type { Metric, Mode } from '../metrics';

/** Distinct, theme-aware colors for up to four compared items. */
const palette = ['var(--primary)', 'var(--warning)', 'var(--info)', 'var(--physical)'];

/** Rows of every metric in a catalog, one column per compared entity, best value marked. */
function CompareTable<T>({ title, hint, heads, metrics, rows, mode, extra, tools, focus = -1, onFocus }: {
    title: string; hint?: string; heads: React.ReactNode[]; metrics: Metric<T>[]; rows: (T | null)[]; mode: Mode;
    extra?: { label: string; values: (string | number | null)[] }[]; tools?: React.ReactNode;
    /** Column to emphasize (clicked subject); -1 for none. */
    focus?: number;
    onFocus: (order: number) => void;
}) {
    const groups = [...new Set(metrics.map((metric) => metric.group))];
    const [group, setGroup] = useState('全部');
    const shown = group === '全部' ? metrics : metrics.filter((metric) => metric.group === group);
    return <section className="chart-card compare-card"><h3>{title}{hint && <span className="muted">{hint}</span>}
        <span className="metric-tools">{tools}<Segmented size="small" value={group} onChange={(value) => setGroup(String(value))} options={['全部', ...groups]} /></span></h3>
        <div className="metric-scroll">
            <table className="compare-table"><thead><tr><th>数据</th>{heads.map((head, order) => <th key={order} onClick={() => onFocus(order)} className={focus === order ? 'focus' : focus >= 0 ? 'unfocus' : ''} style={{ color: palette[order] }}>{head}</th>)}</tr></thead>
                <tbody>
                    {extra?.map((line) => <tr key={line.label}><td>{line.label}</td>{line.values.map((value, order) => <td key={order}
                        onClick={() => onFocus(order)} className={focus === order ? 'focus' : focus >= 0 ? 'unfocus' : ''}>
                        <button type="button" className="compare-value" aria-label={`${line.label}，聚焦第 ${order + 1} 列`}
                            aria-pressed={focus === order}><b>{value ?? '—'}</b></button></td>)}</tr>)}
                    {shown.map((metric) => {
                        const values = rows.map((row) => (row ? metric.value(row, mode) : null));
                        const best = bestIndex(values, metric.lowerBetter);
                        return <tr key={metric.key}><td>{metric.label}{metric.counting && mode !== 'total' ? <small className="muted"> · {modeLabels[mode]}</small> : null}</td>
                            {values.map((value, order) => <td key={order} onClick={() => onFocus(order)}
                                className={`${best === order ? 'best' : ''} ${focus === order ? 'focus' : focus >= 0 ? 'unfocus' : ''}`}>
                                <button type="button" className="compare-value" aria-label={`${metric.label}，聚焦第 ${order + 1} 列`}
                                    aria-pressed={focus === order}><b>{show(metric, value, mode)}</b></button></td>)}</tr>;
                    })}
                </tbody></table>
        </div>
    </section>;
}

/** Align per-entity season lists on the union of years. */
function alignYears<T extends { year: number }>(lists: T[][]) {
    const years = [...new Set(lists.flatMap((list) => list.map((row) => row.year)))].sort((a, b) => a - b);
    return { years, rows: lists.map((list) => years.map((year) => list.find((row) => row.year === year) ?? null)) };
}
/** Average a game metric over recorded games. */
function gameMean<T>(metric: Metric<T>, rows: T[]) {
    // Rows covering several games (tracked segments) count once per game.
    const pairs = rows.flatMap((row) => { const value = metric.value(row, 'perGame'); const weight = (row as { games?: number }).games ?? 1;
        return value === null ? [] : [[value, weight] as const]; });
    const weight = pairs.reduce((s, [, w]) => s + w, 0);
    return weight ? Math.round((pairs.reduce((s, [v, w]) => s + v * w, 0) / weight) * 10) / 10 : null;
}

/**
 * Side-by-side comparison of up to four players or teams: six-dimension radar, attribute table,
 * any stat as a season or per-game line, single season, career and recorded-game averages.
 */
export default function ComparePage() {
    const { message } = AntApp.useApp();
    const [mode, setMode] = useState<'player' | 'team'>('player');
    const [players, setPlayers] = useState<PlayerRow[]>([]);
    const [teams, setTeams] = useState<TeamItem[]>([]);
    const [ratings, setRatings] = useState<RatingTable | null>(null);
    const [picked, setPicked] = useState<number[]>([]);
    const [pickedTeams, setPickedTeams] = useState<number[]>([]);
    const [seasons, setSeasons] = useState<Map<number, PlayerSeason[]>>(new Map());
    const [tSeasons, setTSeasons] = useState<Map<number, TeamSeason[]>>(new Map());
    const [statMode, setStatMode] = useState<Mode>('perGame');
    const [year, setYear] = useState<number | null>(null);
    /** Clicked subject: emphasized in every chart and table. */
    const [focus, setFocus] = useState<string | null>(null);
    const toggleFocus = (key: string) => setFocus((current) => current === key ? null : key);
    const focusPlayer = (order: number) => toggleFocus(String(chosen[order].index));
    const focusTeam = (order: number) => toggleFocus(String(chosenTeams[order].index));
    useEffect(() => { setFocus(null); }, [mode]);
    useEffect(() => {
        if (focus !== null && !(mode === 'player' ? picked : pickedTeams).some((index) => String(index) === focus)) setFocus(null);
    }, [mode, picked, pickedTeams, focus]);
    /** Recent-game window of the per-game table: 5 / 10 / 20 / all (0). */
    const [span, setSpan] = useState(10);
    const [busy, setBusy] = useState(false);
    const playerHeads = picked.map((index) => <EntityLink key={index} page="players" index={index}>
        {players.find((row) => row.index === index)?.name ?? index}</EntityLink>);
    const teamHeads = pickedTeams.map((index) => <EntityLink key={index} page="teams" index={index}>
        {teamLabel(teams.find((row) => row.index === index)?.label ?? '')}</EntityLink>);

    useEffect(() => {
        void Promise.all([listPlayers('', null), listTeamRecords(), readPlayerRatings().catch(() => null)]).then(([rows, teamRows, table]) => {
            const ranked = [...rows].sort((a, b) => (b.summary?.overall ?? b.overall ?? 0) - (a.summary?.overall ?? a.overall ?? 0));
            setPlayers(ranked);
            setTeams(teamRows);
            setRatings(table);
            setPicked((current) => current.length ? current : ranked.filter((row) => row.category === 'team').slice(0, 2).map((row) => row.index));
            setPickedTeams((current) => current.length ? current : teamRows.slice(0, 2).map((row) => row.index));
        }).catch((error: Error) => message.error(error.message));
    }, [message]);

    useEffect(() => {
        const missing = picked.filter((index) => !seasons.has(index));
        if (mode !== 'player' || missing.length === 0) return;
        setBusy(true);
        void Promise.all(missing.map((index) => readPlayerStats(index).then((stats) => [index, playerSeasons(stats.seasons)] as const)
            .catch(() => [index, [] as PlayerSeason[]] as const)))
            .then((entries) => setSeasons((current) => { const next = new Map(current); for (const [k, v] of entries) next.set(k, v); return next; }))
            .finally(() => setBusy(false));
    }, [mode, picked, seasons]);

    useEffect(() => {
        const missing = pickedTeams.filter((index) => !tSeasons.has(index));
        if (mode !== 'team' || missing.length === 0) return;
        setBusy(true);
        void Promise.all(missing.map((index) => call<Record<string, number>[]>('teamSeasons', { teamIndex: index })
            .then((rows) => [index, teamSeasons(rows)] as const).catch(() => [index, [] as TeamSeason[]] as const)))
            .then((entries) => setTSeasons((current) => { const next = new Map(current); for (const [k, v] of entries) next.set(k, v); return next; }))
            .finally(() => setBusy(false));
    }, [mode, pickedTeams, tSeasons]);

    const stats = useMemo(() => statsByIndex(ratings), [ratings]);
    const averages = useMemo(() => categoryAverages(poolOf(players, stats)), [players, stats]);
    const { data: nativeData, error: nativeError } = useSeasonGames();
    const log = useMemo(() => seasonLog(nativeData) ?? emptyLog(), [nativeData]);
    const chosen = picked.map((index) => players.find((row) => row.index === index)).filter(Boolean) as PlayerRow[];
    const chosenTeams = pickedTeams.map((index) => teams.find((row) => row.index === index)).filter(Boolean) as TeamItem[];
    const layers = chosen.flatMap((row, order) => {
        const scores = stats.get(row.index)?.scores;
        return scores ? [{ key: String(row.index), label: row.name, color: palette[order], scores }] : [];
    });
    const attributes = attributeRows(ratings, chosen.map((row) => row.index));
    const playerLists = chosen.map((row) => seasons.get(row.index) ?? []);
    const teamLists = chosenTeams.map((row) => tSeasons.get(row.index) ?? []);
    const allPlayerGames: PlayerGame[][] = chosen.map((row) => playerGames(log, row.name, row.index));
    const playerGameLists: PlayerGame[][] = allPlayerGames.map((list) => (span === 0 ? list : lastGames(list as { games?: number }[], span) as PlayerGame[]));
    const teamGameLists: TeamGame[][] = chosenTeams.map((row) => teamGames(log, row.label));
    const pAligned = alignYears(playerLists);
    const tAligned = alignYears(teamLists);
    const yearsAvailable = mode === 'player' ? pAligned.years : tAligned.years;
    const shownYear = year !== null && yearsAvailable.includes(year) ? year : yearsAvailable[yearsAvailable.length - 1] ?? null;
    const yearPicker = <Select size="small" style={{ width: 110 }} value={shownYear} onChange={setYear}
        options={[...yearsAvailable].reverse().map((value) => ({ value, label: seasonLabel(value) }))} />;
    const modePicker = <Segmented size="small" value={statMode} onChange={(value) => setStatMode(value as Mode)}
        options={(Object.keys(modeLabels) as Mode[]).map((key) => ({ value: key, label: modeLabels[key] }))} />;

    const playerOptions = useMemo(() => players.map((row) => ({ value: row.index,
        label: `${row.name}${row.summary?.overall ?? row.overall ? ` · ${row.summary?.overall ?? row.overall}` : ''}${row.team ? ` · ${teamLabel(row.team)}` : ''}` })), [players]);

    return <div className="analytics compare">
        {nativeError && <Alert type="warning" showIcon message="逐场数据读取失败" description={nativeError} />}
        <div className="analytics-bar">
            <Segmented value={mode} onChange={(value) => setMode(value as 'player' | 'team')}
                options={[{ value: 'player', label: '球员对比' }, { value: 'team', label: '球队对比' }]} />
            {mode === 'player'
                ? <Select key="players" mode="multiple" maxCount={4} showSearch optionFilterProp="label" style={{ minWidth: 520, flex: 1 }} placeholder="选择 2–4 名球员"
                    value={picked} options={playerOptions} onChange={setPicked}
                    tagRender={({ value, closable, onClose }) => { const order = picked.indexOf(Number(value)); const row = players.find((item) => item.index === Number(value));
                        return <Tag closable={closable} onClose={onClose} className="compare-tag" style={{ '--c': palette[order] } as React.CSSProperties}>{row?.name ?? value}</Tag>; }} />
                : <Select key="teams" mode="multiple" maxCount={4} showSearch optionFilterProp="label" style={{ minWidth: 420, flex: 1 }} placeholder="选择 2–4 支球队"
                    value={pickedTeams} options={teams.map((row) => ({ value: row.index, label: teamLabel(row.label), brand: row }))} onChange={setPickedTeams}
                    optionRender={(option) => <TeamIdentity {...option.data.brand} text={option.data.label} size={20} />}
                    tagRender={({ value, closable, onClose }) => { const order = pickedTeams.indexOf(Number(value)); const row = teams.find((item) => item.index === Number(value));
                        return <Tag closable={closable} onClose={onClose} className="compare-tag" style={{ '--c': palette[order] } as React.CSSProperties}>{row ? <TeamIdentity {...row} size={16} text={teamLabel(row.label)} /> : value}</Tag>; }} />}
            {modePicker}
            <span className="muted" style={{ fontSize: 12 }}>数据来自游戏 · 只读</span>
        </div>
        <Spin spinning={busy}>
        {mode === 'player' ? (chosen.length === 0 ? <Empty style={{ marginTop: 60 }} description="选择要对比的球员" /> : <div className="analytics-body">
            <div className="compare-heads" style={{ gridTemplateColumns: `repeat(${chosen.length}, minmax(0, 1fr))` }}>
                {chosen.map((row, order) => {
                    const stat = stats.get(row.index);
                    return <div key={row.index} role="button" tabIndex={0} title="点击高亮这名球员的数据"
                        className={`compare-head ${focus === String(row.index) ? 'focused' : focus ? 'dimmed' : ''}`} style={{ '--c': palette[order] } as React.CSSProperties}
                        aria-pressed={focus === String(row.index)}
                        onKeyDown={(event) => {
                            if (event.target === event.currentTarget && (event.key === 'Enter' || event.key === ' ')) {
                                event.preventDefault();
                                toggleFocus(String(row.index));
                            }
                        }} onClick={() => toggleFocus(String(row.index))}>
                        <Avatar name={row.name} team={row.team} photoId={row.photoId} size={52} loadImage />
                        <div><strong><EntityLink page="players" index={row.index}>{row.name}</EntityLink></strong><span>{playerTitle(row.summary, stat?.archetype)?.name ?? '—'}</span>
                            <small>{[row.summary?.position, row.summary?.age != null ? `${row.summary.age}岁` : null].filter(Boolean).join(' · ')}
                                {row.team && <> · <EntityLink page="teams" index={row.teamIndices?.[0]}>{teamLabel(row.team)}</EntityLink></>}</small></div>
                        <b className="compare-ovr">{row.summary?.overall ?? row.overall ?? '—'}</b>
                    </div>;
                })}
            </div>
            <div className="compare-top">
                <section className="chart-card radar-card"><h3>六维对比 <span className="muted">虚线为联盟与位置平均，可切换</span></h3>
                    {layers.length ? <BigRadar layers={layers} size={440} averages={averages} position={chosen[0]?.summary?.position ?? null} highlight={focus} onHighlight={setFocus} /> : <Empty description="读取能力值后显示" />}
                    <div className="chart-legend radar-legend">{layers.map((layer) => <button type="button" key={layer.key}
                        aria-pressed={focus === layer.key} className={focus === null ? '' : focus === layer.key ? 'on' : 'off'}
                        onClick={() => toggleFocus(layer.key)}><i style={{ background: layer.color }} />{layer.label}</button>)}</div>
                </section>
                <section className="chart-card attr-card"><h3>单项属性 <span className="muted">最高值高亮</span></h3>
                    <div className="metric-scroll"><table className="compare-table"><thead><tr><th>属性</th>{chosen.map((row, order) => <th key={row.index}
                        className={focus === String(row.index) ? 'focus' : focus !== null ? 'unfocus' : ''} onClick={() => focusPlayer(order)}
                        style={{ color: palette[order] }}><EntityLink page="players" index={row.index}>{row.name}</EntityLink></th>)}</tr></thead>
                        <tbody>{attributes.map((attr) => <tr key={attr.key}><td>{attr.label}</td>
                            {attr.values.map((value, order) => <td key={order} onClick={() => focusPlayer(order)}
                                className={`${attr.best === order ? 'best' : ''} ${focus === String(chosen[order].index) ? 'focus' : focus !== null ? 'unfocus' : ''}`}>
                                <span className="cmp-bar" style={{ width: `${value ?? 0}%`, background: palette[order] }} /><button type="button" className="compare-value"
                                    aria-label={`${attr.label}，聚焦第 ${order + 1} 列`} aria-pressed={focus === String(chosen[order].index)}>
                                    <b>{value ?? '—'}</b></button></td>)}</tr>)}</tbody></table></div>
                </section>
            </div>
            <ChartBoard<PlayerSeason, PlayerGame> key="players" base={null} lockedCompare={chosen.map((row) => String(row.index))} highlight={focus} onHighlight={setFocus}
                subject={(key) => { const order = chosen.findIndex((row) => String(row.index) === key); const row = chosen[order];
                    return row ? { key, label: row.name, seasons: playerLists[order] ?? [], games: playerGameLists[order] ?? [] } : null; }}
                options={chosen.map((row) => ({ value: String(row.index), label: row.name }))} onNeed={() => undefined}
                seasonMetrics={playerMetrics} gameMetrics={playerGameMetrics} mode={statMode} onMode={setStatMode}
                defaults={[{ source: 'season', metrics: ['pts'] }, { source: 'game', metrics: ['pts'] }]} />
            <div className="compare-top three">
                <CompareTable onFocus={focusPlayer} focus={chosen.findIndex((row) => String(row.index) === focus)} title="单季对比" heads={playerHeads} metrics={playerMetrics} mode={statMode} tools={yearPicker}
                    rows={playerLists.map((list) => list.find((season) => season.year === shownYear) ?? null)} />
                <CompareTable onFocus={focusPlayer} focus={chosen.findIndex((row) => String(row.index) === focus)} title="生涯对比" hint="生涯总数重新计算" heads={playerHeads} metrics={playerMetrics.filter((m) => m.key !== 'ovr')} mode={statMode}
                    rows={playerLists.map((list) => careerRow(list))} extra={[{ label: '赛季数', values: playerLists.map((list) => list.length) }]} />
                <CompareTable onFocus={focusPlayer} focus={chosen.findIndex((row) => String(row.index) === focus)} title="近期场均" hint="逐场记录的平均（基础与高阶数据）" heads={playerHeads} mode="perGame"
                    tools={<Segmented size="small" value={span} onChange={(value) => setSpan(Number(value))} options={[{ value: 5, label: '近 5 场' }, { value: 10, label: '近 10 场' }, { value: 20, label: '近 20 场' }, { value: 0, label: '全部' }]} />}
                    metrics={playerGameMetrics.map((metric) => ({ ...metric, digits: 1, value: (rows: PlayerGame[]) => gameMean(metric, rows) })) as unknown as Metric<PlayerGame>[]}
                    rows={playerGameLists as unknown as PlayerGame[]} extra={[{ label: '统计场次', values: playerGameLists.map((list) => gameCount(list as { games?: number }[])) }]} />
            </div>
        </div>) : (chosenTeams.length === 0 ? <Empty style={{ marginTop: 60 }} description="选择要对比的球队" /> : <div className="analytics-body">
            <div className="compare-heads" style={{ gridTemplateColumns: `repeat(${chosenTeams.length}, minmax(0, 1fr))` }}>
                {chosenTeams.map((row, order) => {
                    const last = teamLists[order][teamLists[order].length - 1];
                    return <div key={row.index} role="button" tabIndex={0} title="点击高亮这支球队的数据"
                        className={`compare-head ${focus === String(row.index) ? 'focused' : focus ? 'dimmed' : ''}`} style={{ '--c': palette[order] } as React.CSSProperties}
                        aria-pressed={focus === String(row.index)}
                        onKeyDown={(event) => {
                            if (event.target === event.currentTarget && (event.key === 'Enter' || event.key === ' ')) {
                                event.preventDefault();
                                toggleFocus(String(row.index));
                            }
                        }} onClick={() => toggleFocus(String(row.index))}>
                        <TeamIdentity {...row} size={44} text="" />
                        <div><strong><EntityLink page="teams" index={row.index}>{teamLabel(row.label)}</EntityLink></strong><span>{last ? `${last.wins} 胜 ${last.losses} 负` : '—'}</span>
                            <small>{teamLists[order].length} 个赛季记录</small></div>
                        <b className="compare-ovr">{last ? `${last.winPct.toFixed(1)}%` : '—'}</b>
                    </div>;
                })}
            </div>
            <ChartBoard<TeamSeason, TeamGame> key="teams" base={null} lockedCompare={chosenTeams.map((row) => String(row.index))} highlight={focus} onHighlight={setFocus}
                subject={(key) => { const order = chosenTeams.findIndex((row) => String(row.index) === key); const row = chosenTeams[order];
                    return row ? { key, label: teamLabel(row.label), seasons: teamLists[order] ?? [], games: teamGameLists[order] ?? [] } : null; }}
                options={chosenTeams.map((row) => ({ value: String(row.index), label: teamLabel(row.label) }))} onNeed={() => undefined}
                seasonMetrics={teamMetrics} gameMetrics={teamGameMetrics} mode={statMode} onMode={setStatMode}
                defaults={[{ source: 'season', metrics: ['winPct'] }, { source: 'season', metrics: ['net'] }, { source: 'game', metrics: ['margin'] }]} />
            <div className="compare-top three">
                <CompareTable onFocus={focusTeam} focus={chosenTeams.findIndex((row) => String(row.index) === focus)} title="单季对比" heads={teamHeads} metrics={teamMetrics} mode={statMode} tools={yearPicker}
                    rows={teamLists.map((list) => list.find((season) => season.year === shownYear) ?? null)} />
                <CompareTable onFocus={focusTeam} focus={chosenTeams.findIndex((row) => String(row.index) === focus)} title="多季平均" hint="所有已记录赛季" heads={teamHeads} mode={statMode}
                    metrics={teamMetrics.map((metric) => ({ ...metric, value: (list: TeamSeason[], m: Mode) => {
                        const values = list.map((row) => metric.value(row, m)).filter((v): v is number => v !== null);
                        return values.length ? Math.round((values.reduce((s, v) => s + v, 0) / values.length) * 10) / 10 : null; } })) as unknown as Metric<TeamSeason>[]}
                    rows={teamLists as unknown as TeamSeason[]} extra={[{ label: '赛季数', values: teamLists.map((list) => list.length) }]} />
                <CompareTable onFocus={focusTeam} focus={chosenTeams.findIndex((row) => String(row.index) === focus)} title="逐场场均" hint="已记录比赛的平均" heads={teamHeads} mode="perGame"
                    metrics={teamGameMetrics.map((metric) => ({ ...metric, digits: 1, value: (rows: TeamGame[]) => gameMean(metric, rows) })) as unknown as Metric<TeamGame>[]}
                    rows={teamGameLists as unknown as TeamGame[]}
                    extra={[{ label: '已记录场次', values: teamGameLists.map((list) => list.length) }, { label: '胜-负', values: teamGameLists.map((list) => `${list.filter((g) => g.won).length}-${list.filter((g) => !g.won).length}`) }]} />
            </div>
        </div>)}
        </Spin>
    </div>;
}
