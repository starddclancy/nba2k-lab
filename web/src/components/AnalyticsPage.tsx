import { useCallback, useEffect, useMemo, useState } from 'react';
import { Alert, App as AntApp, Empty, Segmented, Select, Spin } from 'antd';
import { call } from '../bridge';
import { listPlayers, listTeamRecords, readPlayerStats } from '../api';
import type { PlayerRow, TeamItem } from '../types';
import { teamLabel } from '../labels';
import { playerSeasons, seasonLabel, teamSeasons } from '../seasonTrends';
import type { PlayerSeason, TeamSeason } from '../seasonTrends';
import Avatar from './Avatar';
import TeamIdentity from './TeamIdentity';
import ChartBoard from './ChartBoard';
import type { Subject } from './ChartBoard';
import DataGrid from './DataGrid';
import type { GridColumn } from './DataGrid';
import { emptyLog, playerGames, teamGames } from '../gameLog';
import type { PlayerGame, TeamGame } from '../gameLog';
import { seasonLog } from '../seasonGames';
import { useSeasonGames } from '../useSeasonGames';
import { gameCount } from '../seasonTracker';
import { playerGameMetrics, playerMetrics, teamGameMetrics, teamMetrics } from '../metrics';
import type { Metric, Mode } from '../metrics';
import type { PlayerForm } from '../form';
import { teamRecentForm } from '../form';
import { PlayerFormPanel, TeamFormPanel } from './FormPanel';
import { useWorkspace } from '../workspaceContext';
import EntityLink from './EntityLink';

/** Big number with its change versus the previous season; keeps the metric's decimals. */
function Kpi<T>({ metric, row, previous, mode, selected, onSelect }: {
    metric: Metric<T>; row: T | null; previous: T | null; mode: Mode; selected: boolean; onSelect: () => void;
}) {
    const value = row ? metric.value(row, mode) : null;
    const before = previous ? metric.value(previous, mode) : null;
    const delta = value !== null && before !== null ? value - before : null;
    const digits = metric.counting && mode === 'total' ? 0 : metric.digits;
    const good = delta === null || delta === 0 ? '' : (delta > 0) !== !!metric.lowerBetter ? 'up' : 'down';
    return <button type="button" className="kpi" aria-pressed={selected} onClick={onSelect}>
        <span>{metric.label}{metric.counting ? ` · ${mode === 'total' ? '总计' : mode === 'per36' ? '每36' : '场均'}` : ''}</span>
        <b>{value === null ? '—' : value.toFixed(digits)}<small>{metric.unit}</small></b>
        {delta !== null && <em className={good}>{delta > 0 ? '▲' : delta < 0 ? '▼' : '■'} {Math.abs(delta).toFixed(digits)}</em>}
    </button>;
}

const pick = <T,>(metrics: Metric<T>[], keys: string[]) => keys.map((key) => metrics.find((metric) => metric.key === key)!).filter(Boolean);

/** Grid columns from a metric catalog (per-game values, numeric). */
function metricColumns<T>(metrics: Metric<T>[], mode: Mode = 'perGame'): GridColumn<T>[] {
    return metrics.map((metric) => ({ key: metric.key, label: `${metric.label}${metric.unit === '%' ? '' : ''}`, group: metric.group, numeric: true,
        digits: metric.counting && mode === 'total' ? 0 : metric.digits, lowerBetter: metric.lowerBetter, value: (row: T) => metric.value(row, mode) }));
}

/**
 * Player and team analytics from game data: season records (readPlayerStats / teamSeasons),
 * the native season's per-game box scores and derived recent form. Charts are added and
 * removed by the user; each can draw any stat and compare other players or teams. Read-only.
 */
export default function AnalyticsPage() {
    const { message } = AntApp.useApp();
    const { seasonEndYear } = useWorkspace();
    const [mode, setMode] = useState<'player' | 'team'>('player');
    const [players, setPlayers] = useState<PlayerRow[]>([]);
    const [teams, setTeams] = useState<TeamItem[]>([]);
    const [player, setPlayer] = useState<number | null>(null);
    const [team, setTeam] = useState<number | null>(null);
    const [pSeasons, setPSeasons] = useState<Map<number, PlayerSeason[]>>(new Map());
    const [tSeasons, setTSeasons] = useState<Map<number, TeamSeason[]>>(new Map());
    const [busy, setBusy] = useState(false);
    const [statMode, setStatMode] = useState<Mode>('perGame');
    const [selectedMetric, setSelectedMetric] = useState<string | null>(null);
    const [metricRequest, setMetricRequest] = useState<{ key: string; subject: string } | null>(null);
    const subjectKey = `${mode}-${mode === 'player' ? player : team}`;
    const selectMetric = (key: string) => {
        const next = selectedMetric === key ? null : key;
        setSelectedMetric(next);
        if (next) setMetricRequest({ key: next, subject: subjectKey });
    };
    useEffect(() => { setSelectedMetric(null); }, [mode, player, team]);
    const [pForm, setPForm] = useState<PlayerForm | null>(null);
    const [gridSource, setGridSource] = useState<'game' | 'season'>('game');
    const { data: nativeData, error: nativeError, loading: nativeLoading } = useSeasonGames();
    const nativeLog = useMemo(() => seasonLog(nativeData), [nativeData]);
    const log = useMemo(() => nativeLog ?? emptyLog(), [nativeLog]);
    // A native table is authoritative, including players with zero appearances. Never mix sources.
    const gamesOf = (row: PlayerRow) => playerGames(log, row.name, row.index);

    useEffect(() => {
        void Promise.all([listPlayers('', null), listTeamRecords()]).then(([rows, teamRows]) => {
            const ranked = [...rows].sort((a, b) => (b.summary?.overall ?? b.overall ?? 0) - (a.summary?.overall ?? a.overall ?? 0));
            setPlayers(ranked);
            setTeams(teamRows);
            setPlayer((current) => current ?? ranked.find((row) => row.category === 'team')?.index ?? ranked[0]?.index ?? null);
            setTeam((current) => current ?? teamRows[0]?.index ?? null);
        }).catch((error: Error) => message.error(error.message));
    }, [message]);

    const loadPlayers = useCallback((indices: number[]) => {
        const missing = indices.filter((index) => !pSeasons.has(index));
        if (missing.length === 0) return;
        setBusy(true);
        void Promise.all(missing.map((index) => readPlayerStats(index).then((stats) => [index, playerSeasons(stats.seasons)] as const)
            .catch(() => [index, [] as PlayerSeason[]] as const)))
            .then((entries) => setPSeasons((current) => { const next = new Map(current); for (const [k, v] of entries) next.set(k, v); return next; }))
            .finally(() => setBusy(false));
    }, [pSeasons]);
    const loadTeams = useCallback((indices: number[]) => {
        const missing = indices.filter((index) => !tSeasons.has(index));
        if (missing.length === 0) return;
        setBusy(true);
        void Promise.all(missing.map((index) => call<Record<string, number>[]>('teamSeasons', { teamIndex: index })
            .then((rows) => [index, teamSeasons(rows)] as const).catch(() => [index, [] as TeamSeason[]] as const)))
            .then((entries) => setTSeasons((current) => { const next = new Map(current); for (const [k, v] of entries) next.set(k, v); return next; }))
            .finally(() => setBusy(false));
    }, [tSeasons]);

    useEffect(() => {
        if (mode !== 'player' || player === null) return;
        let alive = true;
        setPForm(null);
        call<PlayerForm>('playerForm', { index: player }).then((form) => { if (alive) setPForm(form); }).catch(() => { if (alive) setPForm(null); });
        loadPlayers([player]);
        return () => { alive = false; };
    }, [mode, player]); // eslint-disable-line react-hooks/exhaustive-deps
    useEffect(() => {
        if (mode !== 'team' || team === null) return;
        loadTeams([team]);
    }, [mode, team]); // eslint-disable-line react-hooks/exhaustive-deps

    const playerRow = players.find((row) => row.index === player);
    const teamRow = teams.find((row) => row.index === team);
    const seasonsOf = player !== null ? pSeasons.get(player) ?? [] : [];
    const teamSeasonsOf = team !== null ? tSeasons.get(team) ?? [] : [];
    const games: PlayerGame[] = playerRow ? gamesOf(playerRow) : [];
    const gameTotal = gameCount(games as { games?: number }[]);
    const tGames: TeamGame[] = teamRow ? teamGames(log, teamRow.label) : [];
    const tForm = teamRow ? teamRecentForm(tGames, teamRow.index, teamRow.label) : null;
    const currentPlayerSeason = seasonsOf.find((row) => row.year === seasonEndYear) ?? null;
    const previousPlayerSeason = seasonsOf.find((row) => row.year === (seasonEndYear ?? 0) - 1) ?? null;
    const currentTeamSeason = teamSeasonsOf.find((row) => row.year === seasonEndYear) ?? null;
    const previousTeamSeason = teamSeasonsOf.find((row) => row.year === (seasonEndYear ?? 0) - 1) ?? null;

    const playerOptions = useMemo(() => players.map((row) => ({ value: row.index,
        label: `${row.name}${row.summary?.overall ?? row.overall ? ` · ${row.summary?.overall ?? row.overall}` : ''}${row.team ? ` · ${teamLabel(row.team)}` : ''}` })), [players]);
    const teamOptions = useMemo(() => teams.map((row) => ({ value: row.index, label: teamLabel(row.label), brand: row })), [teams]);
    const playerSubject = (key: string): Subject<PlayerSeason, PlayerGame> | null => {
        const row = players.find((item) => String(item.index) === key);
        return row ? { key, label: row.name, seasons: pSeasons.get(row.index) ?? [], games: gamesOf(row) } : null;
    };
    const teamSubject = (key: string): Subject<TeamSeason, TeamGame> | null => {
        const row = teams.find((item) => String(item.index) === key);
        return row ? { key, label: teamLabel(row.label), seasons: tSeasons.get(row.index) ?? [], games: teamGames(log, row.label) } : null;
    };

    const playerGameColumns: GridColumn<PlayerGame>[] = [
        { key: 'date', label: '日期', value: (row) => row.date, width: 92 },
        { key: 'opponent', label: '对手', value: (row) => teamLabel(row.opponent), render: (row) => row.opponent ? <TeamIdentity {...(teams.find((t) => t.label === row.opponent) ?? { label: row.opponent })} linkIndex={teams.find((t) => t.label === row.opponent)?.index} size={18} text={teamLabel(row.opponent)} /> : <span className="muted">—</span>, width: 150 },
        { key: 'result', label: '结果', value: (row) => row.result || '—', render: (row) => !row.result ? <span className="muted">—</span>
            : row.result.endsWith('合计') ? <span className="muted">{row.result}</span>
                : <span className={row.result.startsWith('胜') ? 'up' : 'down'}><i className={`wl ${row.result.startsWith('胜') ? 'w' : 'l'}`} /> {row.result.split(' ')[1]}</span> },
        ...metricColumns(playerGameMetrics),
    ];
    const teamGameColumns: GridColumn<TeamGame>[] = [
        { key: 'date', label: '日期', value: (row) => row.date, width: 92 },
        { key: 'opponent', label: '对手', value: (row) => teamLabel(row.opponent), render: (row) => <TeamIdentity {...(teams.find((t) => t.label === row.opponent) ?? { label: row.opponent })} linkIndex={teams.find((t) => t.label === row.opponent)?.index} size={18} text={teamLabel(row.opponent)} />, width: 150 },
        { key: 'won', label: '胜负', value: (row) => (row.won ? '胜' : '负'), render: (row) => <i className={`wl ${row.won ? 'w' : 'l'}`} /> },
        ...metricColumns(teamGameMetrics),
    ];
    const playerSeasonColumns: GridColumn<PlayerSeason>[] = [{ key: 'year', label: '赛季', value: (row) => seasonLabel(row.year), width: 80 }, ...metricColumns(playerMetrics, statMode)];
    const teamSeasonColumns: GridColumn<TeamSeason>[] = [{ key: 'year', label: '赛季', value: (row) => seasonLabel(row.year), width: 80 }, ...metricColumns(teamMetrics, statMode)];

    return <div className="analytics">
        {nativeError && <Alert type="warning" showIcon message="逐场数据读取失败" description={nativeError} />}
        <div className="analytics-bar">
            <Segmented value={mode} onChange={(value) => setMode(value as 'player' | 'team')}
                options={[{ value: 'player', label: '球员分析' }, { value: 'team', label: '球队分析' }]} />
            {mode === 'player'
                ? <Select key="player" showSearch optionFilterProp="label" style={{ width: 360 }} placeholder="选择球员" value={player} options={playerOptions} onChange={setPlayer} />
                : <Select key="team" showSearch optionFilterProp="label" style={{ width: 280 }} placeholder="选择球队" value={team} options={teamOptions}
                    optionRender={(option) => <TeamIdentity {...option.data.brand} text={option.data.label} size={20} />}
                    labelRender={() => (teamRow ? <TeamIdentity {...teamRow} text={teamLabel(teamRow.label)} size={20} /> : null)} onChange={setTeam} />}
            <Segmented value={statMode} onChange={(value) => setStatMode(value as Mode)}
                options={[{ value: 'perGame', label: '场均' }, { value: 'total', label: '总计' }, { value: 'per36', label: '每36分钟' }]} />
            <span className="muted" style={{ marginLeft: 'auto', fontSize: 12 }}>折线图可增删 · 每张图可选数据、可加对比对象 · 只读</span>
        </div>
        <Spin spinning={busy || nativeLoading}>
            {mode === 'player' ? <div className="analytics-body" key={`p-${player}`}>
                <section className="analytics-hero">
                    {playerRow && <Avatar name={playerRow.name} team={playerRow.team} photoId={playerRow.photoId} size={56} loadImage />}
                    <div><strong><EntityLink page="players" index={playerRow?.index}>{playerRow?.name ?? '—'}</EntityLink></strong>
                        <span className="muted">{playerRow?.team ? <TeamIdentity {...(teams.find((t) => t.label === playerRow.team) ?? { label: playerRow.team })} linkIndex={playerRow.teamIndices?.[0]} size={16} text={teamLabel(playerRow.team)} /> : null}
                            {' '}{seasonsOf.length} 个赛季{currentPlayerSeason ? ` · 本季 ${currentPlayerSeason.games} 场` : ' · 暂无本季数据'}</span></div>
                    <div className="kpi-row ten">{pick(playerMetrics, ['pts', 'reb', 'ast', 'tpm', 'stl', 'blk', 'fgPct', 'tpPct', 'ts', 'gameScore'])
                        .map((metric) => <Kpi key={metric.key} metric={metric} row={currentPlayerSeason} previous={previousPlayerSeason} mode={statMode}
                            selected={selectedMetric === metric.key} onSelect={() => selectMetric(metric.key)} />)}</div>
                </section>
                <PlayerFormPanel form={pForm} season={currentPlayerSeason} games={games} />
                {player !== null && <ChartBoard<PlayerSeason, PlayerGame> base={String(player)} subject={playerSubject}
                    options={playerOptions.map((option) => ({ value: String(option.value), label: option.label }))}
                    onNeed={(keys) => loadPlayers(keys.map(Number))} seasonMetrics={playerMetrics} gameMetrics={playerGameMetrics}
                    mode={statMode} onMode={setStatMode} selectedMetric={selectedMetric} onSelectedMetric={setSelectedMetric}
                    metricRequest={metricRequest?.subject === subjectKey ? metricRequest : null}
                    defaults={[{ source: games.length ? 'game' : 'season', metrics: ['pts', 'reb', 'ast'] }, { source: 'season', metrics: ['tpm', 'tpPct'] }]} />}
                <div className="grid-switch"><Segmented value={gridSource} onChange={(value) => setGridSource(value as 'game' | 'season')}
                    options={[{ value: 'game', label: `逐场数据表（${gameTotal} 场）` }, { value: 'season', label: `逐季数据表（${seasonsOf.length} 季）` }]} /></div>
                {gridSource === 'game'
                    ? (games.length ? <DataGrid<PlayerGame> title="逐场数据" hint="游戏原生赛季比赛记录" columns={playerGameColumns} rows={[...games].reverse()} rowKey={(row) => row.game} />
                        : <Empty description="当前赛季没有这名球员的出场记录" />)
                    : <DataGrid<PlayerSeason> title="逐季数据" hint="游戏赛季记录" columns={playerSeasonColumns} rows={[...seasonsOf].reverse()} rowKey={(row) => String(row.year)}
                        defaultColumns={['year', 'gp', 'min', 'pts', 'reb', 'ast', 'stl', 'blk', 'tov', 'tpm', 'tpa', 'fgPct', 'tpPct', 'ftPct', 'ts', 'efg', 'gameScore', 'bpm']} />}
            </div> : <div className="analytics-body" key={`t-${team}`}>
                <section className="analytics-hero">
                    {teamRow && <TeamIdentity {...teamRow} size={48} text="" />}
                    <div><strong><EntityLink page="teams" index={teamRow?.index}>{teamRow ? teamLabel(teamRow.label) : '—'}</EntityLink></strong>
                        <span className="muted">{teamSeasonsOf.length} 个赛季记录{currentTeamSeason ? ` · 本季 ${currentTeamSeason.wins} 胜 ${currentTeamSeason.losses} 负` : ' · 暂无本季数据'}</span></div>
                    <div className="kpi-row ten">{pick(teamMetrics, ['winPct', 'pts', 'opp', 'margin', 'tpm', 'tpPct', 'ortg', 'drtg', 'net', 'pace'])
                        .map((metric) => <Kpi key={metric.key} metric={metric} row={currentTeamSeason} previous={previousTeamSeason} mode={statMode}
                            selected={selectedMetric === metric.key} onSelect={() => selectMetric(metric.key)} />)}</div>
                </section>
                <TeamFormPanel form={tForm} season={currentTeamSeason} games={tGames} teams={teams} />
                {team !== null && <ChartBoard<TeamSeason, TeamGame> base={String(team)} subject={teamSubject}
                    options={teamOptions.map((option) => ({ value: String(option.value), label: option.label }))}
                    onNeed={(keys) => loadTeams(keys.map(Number))} seasonMetrics={teamMetrics} gameMetrics={teamGameMetrics}
                    mode={statMode} onMode={setStatMode} selectedMetric={selectedMetric} onSelectedMetric={setSelectedMetric}
                    metricRequest={metricRequest?.subject === subjectKey ? metricRequest : null}
                    defaults={[{ source: tGames.length ? 'game' : 'season', metrics: ['pts', 'opp'] }, { source: 'season', metrics: ['tpm', 'tpPct'] }]} />}
                <div className="grid-switch"><Segmented value={gridSource} onChange={(value) => setGridSource(value as 'game' | 'season')}
                    options={[{ value: 'game', label: `逐场数据表（${tGames.length} 场）` }, { value: 'season', label: `逐季数据表（${teamSeasonsOf.length} 季）` }]} /></div>
                {gridSource === 'game'
                    ? (tGames.length ? <DataGrid<TeamGame> title="逐场数据" hint="游戏原生赛季比赛记录" columns={teamGameColumns} rows={[...tGames].reverse()} rowKey={(row) => row.game} />
                        : <Empty description="当前赛季没有这支球队的已赛记录" />)
                    : <DataGrid<TeamSeason> title="逐季数据" hint="早年赛季游戏未记录失分与回合数，显示为 —" columns={teamSeasonColumns} rows={[...teamSeasonsOf].reverse()} rowKey={(row) => String(row.year)}
                        defaultColumns={['year', 'wins', 'losses', 'winPct', 'pts', 'opp', 'margin', 'tpm', 'tpa', 'tpPct', 'fgPct', 'ortg', 'drtg', 'net', 'pace']} />}
            </div>}
        </Spin>
    </div>;
}
