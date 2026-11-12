import { useEffect, useMemo, useRef, useState } from 'react';
import { Alert, App as AntApp, Button, Empty, Input, Modal, Segmented, Select, Spin, Tag, Tooltip } from 'antd';
import { EditOutlined, ReloadOutlined, UndoOutlined } from '@ant-design/icons';
import { listTeamRecords, scheduleApply, schedulePrepare, scheduleUndo } from '../api';
import type { TeamItem } from '../types';
import { teamLabel } from '../labels';
import { currentDate, scheduleStats } from '../seasonGames';
import type { SchedulePreview, SeasonGameRow } from '../seasonGames';
import { useSeasonGames } from '../useSeasonGames';
import TeamIdentity from './TeamIdentity';
import DataGrid from './DataGrid';
import type { GridColumn } from './DataGrid';
import type { TeamSchedule } from '../seasonGames';
import EntityLink from './EntityLink';
import { useEditing } from '../editingContext';

const weekdays = ['日', '一', '二', '三', '四', '五', '六'];
const fmt = (date: string) => `${Number(date.slice(5, 7))}月${Number(date.slice(8, 10))}日 周${weekdays[new Date(`${date}T00:00:00Z`).getUTCDay()]}`;

/** One game as a row: date, home vs away with logos, score with quarters. */
function GameRow({ game, brand, focus, onEdit }: { game: SeasonGameRow; brand: (index: number) => TeamItem | { label: string }; focus?: number; onEdit: (game: SeasonGameRow) => void }) {
    const { editing } = useEditing();
    const homeWon = game.played && (game.homePoints ?? 0) > (game.awayPoints ?? 0);
    const side = focus === undefined ? null : game.home === focus ? 'home' : 'away';
    const won = side === null ? null : side === 'home' ? homeWon : !homeWon;
    return <div className={`sched-game${game.played ? ' played' : ''}`}>
        <time className="sched-date" dateTime={game.date} title={game.date}>{fmt(game.date)}</time>
        <span className="sched-ha">{side && <Tag color={side === 'home' ? 'blue' : 'default'}>{side === 'home' ? '主' : '客'}</Tag>}</span>
        <span className={`sched-team sched-away${game.played && !homeWon ? ' lost' : ''}`}>
            <TeamIdentity {...brand(game.away)} linkIndex={game.teamsResolved === false ? undefined : game.away} size={22} text={teamLabel(game.awayLabel)} /></span>
        <b className="sched-score">{game.played ? <>{game.awayPoints}<i>:</i>{game.homePoints}</> : <small>@</small>}</b>
        <span className={`sched-team sched-home${game.played && homeWon ? '' : game.played ? ' lost' : ''}`}>
            <TeamIdentity {...brand(game.home)} linkIndex={game.teamsResolved === false ? undefined : game.home} size={22} text={teamLabel(game.homeLabel)} /></span>
        <span className="sched-result">{game.played && won !== null && <i aria-label={won ? '胜' : '负'} className={`wl ${won ? 'w' : 'l'}`} />}</span>
        {game.played && game.homeQuarters && game.awayQuarters && <small className="sched-q muted">
            {game.awayQuarters.map((q, i) => `${q}-${game.homeQuarters![i]}`).join(' · ')}{game.homeQuarters.length > 4 ? ` · 加时 ${game.homeQuarters.length - 4}` : ''}</small>}
        <span className="sched-edit">{!game.played && game.teamsResolved !== false && <Tooltip title="修改比赛日期"><Button type="text" aria-label={`修改 ${game.date} 比赛日期`}
            icon={<EditOutlined />} disabled={!editing} onClick={() => { if (editing) onEdit(game); }} /></Tooltip>}</span>
    </div>;
}

/**
 * Native season schedule, schedule statistics and date-change previews.
 * Applying or undoing a preview also requires the host's explicit write capability.
 */
export default function SchedulePage() {
    const { message } = AntApp.useApp();
    const { editing: editingEnabled } = useEditing();
    const editingRef = useRef(editingEnabled);
    editingRef.current = editingEnabled;
    const { data: source, error, loading, reload } = useSeasonGames();
    const [competition, setCompetition] = useState<'league' | 'other'>('league');
    const data = useMemo(() => source && ({
        ...source, games: competition === 'league' ? source.games : source.otherGames ?? [],
    }), [source, competition]);
    const [teams, setTeams] = useState<TeamItem[]>([]);
    const [view, setView] = useState<'team' | 'day' | 'stats'>('team');
    const [team, setTeam] = useState<number | null>(null);
    const [day, setDay] = useState<string | null>(null);
    const [filter, setFilter] = useState<'all' | 'left' | 'played'>('all');
    const [editing, setEditing] = useState<SeasonGameRow | null>(null);
    const [date, setDate] = useState('');
    const [plan, setPlan] = useState<SchedulePreview | null>(null);
    const [busy, setBusy] = useState(false);
    const [editError, setEditError] = useState('');
    function openEdit(game: SeasonGameRow) {
        if (!editingRef.current || busy) return;
        setEditing(game); setDate(game.date); setPlan(null); setEditError('');
    }
    async function prepare() {
        if (!editingRef.current || busy || !editing) return;
        setBusy(true); setEditError(''); setPlan(null);
        try { setPlan(await schedulePrepare(editing.id, date)); }
        catch (failure) { setEditError((failure as Error).message); }
        finally { setBusy(false); }
    }
    async function apply() {
        if (!editingRef.current || busy || !plan?.writesEnabled || !data?.writesEnabled) return;
        setBusy(true); setEditError('');
        try {
            await scheduleApply(plan.token);
            message.success('比赛日期已修改');
            setEditing(null); setPlan(null); reload();
        } catch (failure) { setEditError((failure as Error).message); setPlan(null); reload(); }
        finally { setBusy(false); }
    }
    async function undo() {
        if (!editingRef.current || busy || !data?.writesEnabled || !data.undoDepth) return;
        setBusy(true);
        try { await scheduleUndo(); message.success('已撤销上次赛程修改'); reload(); }
        catch (failure) { message.error((failure as Error).message); reload(); }
        finally { setBusy(false); }
    }
    useEffect(() => { void listTeamRecords().then(setTeams).catch(() => setTeams([])); }, []);
    const brand = (index: number) => teams.find((t) => t.index === index) ?? { label: data?.games.find((g) => g.home === index)?.homeLabel ?? data?.games.find((g) => g.away === index)?.awayLabel ?? '' };
    const stats = useMemo(() => scheduleStats(data), [data]);
    const today = currentDate(data);
    useEffect(() => { if (team === null && stats.length) setTeam(stats.find((s) => s.label === 'Houston Rockets')?.team ?? stats[0].team); }, [stats, team]);
    useEffect(() => {
        if (day !== null || !data) return;
        const next = data.games.find((g) => !g.played)?.date ?? today;
        setDay(next);
    }, [data, day, today]);

    if (loading && !data) return <Spin style={{ display: 'block', margin: 48 }} />;
    if (error || !data) return <Alert type="warning" showIcon message="读取赛程失败" description={error ?? '当前存档没有赛季赛程'} action={<Button onClick={reload}>重试</Button>} />;

    const selected = stats.find((s) => s.team === team) ?? null;
    const teamGames = data.games.filter((g) => g.home === team || g.away === team)
        .filter((g) => filter === 'all' || (filter === 'played') === g.played)
        .sort((a, b) => a.date.localeCompare(b.date) || a.slot - b.slot);
    const dayGames = data.games.filter((g) => g.date === day).sort((a, b) => a.slot - b.slot);
    const playedCount = data.games.filter((g) => g.played).length;

    const columns: GridColumn<TeamSchedule>[] = [
        { key: 'label', label: '球队', value: (r) => teamLabel(r.label), render: (r) => <TeamIdentity {...brand(r.team)} linkIndex={r.team} size={20} text={teamLabel(r.label)} />, width: 170 },
        { key: 'record', label: '战绩', value: (r) => `${r.wins}-${r.losses}` },
        { key: 'pct', label: '胜率%', numeric: true, digits: 1, value: (r) => (r.played ? (r.wins / r.played) * 100 : null) },
        { key: 'played', label: '已赛', numeric: true, digits: 0, value: (r) => r.played },
        { key: 'remaining', label: '剩余', numeric: true, digits: 0, value: (r) => r.remaining },
        { key: 'homeLeft', label: '剩余主场', numeric: true, digits: 0, value: (r) => r.homeLeft },
        { key: 'awayLeft', label: '剩余客场', numeric: true, digits: 0, value: (r) => r.awayLeft },
        { key: 'b2b', label: '已打背靠背', numeric: true, digits: 0, lowerBetter: true, value: (r) => r.backToBacks },
        { key: 'b2bLeft', label: '剩余背靠背', numeric: true, digits: 0, lowerBetter: true, value: (r) => r.backToBacksLeft },
        { key: 'sosPlayed', label: '已赛对手胜率%', numeric: true, digits: 1, lowerBetter: true, value: (r) => r.strengthPlayed },
        { key: 'sosLeft', label: '剩余赛程强度%', numeric: true, digits: 1, lowerBetter: true, value: (r) => r.strengthLeft },
    ];

    return <div className="schedule-page">
        <div className="analytics-bar">
            <Segmented value={competition} onChange={(value) => { setCompetition(value as typeof competition); setTeam(null); setDay(null); }}
                options={[{ value: 'league', label: 'NBA' }, { value: 'other', label: '其他比赛' }]} />
            <Segmented value={view} onChange={(v) => setView(v as typeof view)}
                options={[{ value: 'team', label: '球队赛程' }, { value: 'day', label: '每日赛程' }, { value: 'stats', label: '赛程统计' }]} />
            {view === 'team' && <Select className="sched-team-select" showSearch optionFilterProp="label" value={team} onChange={setTeam}
                options={stats.map((s) => ({ value: s.team, label: teamLabel(s.label) }))}
                optionRender={(o) => <TeamIdentity {...brand(Number(o.value))} size={20} text={String(o.label)} />}
                labelRender={(o) => <TeamIdentity {...brand(Number(o.value))} size={20} text={String(o.label)} />} />}
            {view === 'team' && <Segmented value={filter} onChange={(v) => setFilter(v as typeof filter)}
                options={[{ value: 'all', label: '全部' }, { value: 'left', label: '未赛' }, { value: 'played', label: '已赛' }]} />}
            {view === 'day' && <Select showSearch style={{ width: 200 }} value={day} onChange={setDay}
                options={[...new Set(data.games.map((g) => g.date))].sort().map((d) => ({ value: d, label: `${d.slice(0, 4)} ${fmt(d)}` }))} />}
            <span className="sched-meta muted">
                共 {data.games.length} 场 · 已赛 {playedCount} · 最近比赛日 {today ?? '—'}</span>
            <Tooltip title="撤销上次赛程修改"><Button icon={<UndoOutlined />} aria-label="撤销上次赛程修改"
                disabled={!editingEnabled || !data.writesEnabled || !data.undoDepth || busy} onClick={() => void undo()} /></Tooltip>
            <Button icon={<ReloadOutlined />} onClick={reload} loading={loading}>刷新</Button>
        </div>
        {competition === 'other' && !!source?.unmappedGameCount && <Alert type="warning" showIcon
            message={`${source.unmappedGameCount} 场比赛的球队名称未能读取，已保留原始球队编号；这些场次不可改期。`} />}
        {view === 'team' && selected && <div className="sched-team-view">
            <section className="chart-card sched-summary">
                <TeamIdentity {...brand(selected.team)} size={56} text="" />
                <div><strong><EntityLink page="teams" index={selected.team}>{teamLabel(selected.label)}</EntityLink></strong><span className="muted">{selected.wins} 胜 {selected.losses} 负 · 剩余 {selected.remaining} 场</span></div>
                <div className="kpi-row">
                    <div className="kpi"><span>剩余主 / 客</span><b>{selected.homeLeft}<small> / {selected.awayLeft}</small></b></div>
                    <div className="kpi"><span>背靠背 已打 / 剩余</span><b>{selected.backToBacks}<small> / {selected.backToBacksLeft}</small></b></div>
                    <div className="kpi"><span>剩余赛程强度</span><b>{selected.strengthLeft ?? '—'}<small>%</small></b><em className="muted">对手当前平均胜率</em></div>
                    <div className="kpi"><span>已赛对手胜率</span><b>{selected.strengthPlayed ?? '—'}<small>%</small></b></div>
                </div>
            </section>
            <section className="chart-card wide">
                <h3>赛程<span className="muted">客队 · 比分 · 主队</span></h3>
                {teamGames.length === 0 ? <Empty /> : <div className="sched-list">{teamGames.map((g) => <GameRow key={g.id} game={g} brand={brand} focus={selected.team} onEdit={openEdit} />)}</div>}
            </section>
        </div>}
        {view === 'day' && <section className="chart-card wide">
            <h3>{day ? fmt(day) : '—'}<span className="muted">{dayGames.length} 场</span></h3>
            {dayGames.length === 0 ? <Empty description="这一天没有比赛" /> : <div className="sched-list">{dayGames.map((g) => <GameRow key={g.id} game={g} brand={brand} onEdit={openEdit} />)}</div>}
        </section>}
        {view === 'stats' && <DataGrid<TeamSchedule> title="赛程统计" hint="剩余赛程强度 = 剩余对手当前胜率的平均值；背靠背 = 相邻两天连赛" columns={columns} rows={stats}
            rowKey={(r) => String(r.team)} onRowClick={(r) => { setTeam(r.team); setView('team'); }} />}
        <Modal title="修改比赛日期" open={editing !== null} onCancel={() => { if (!busy) setEditing(null); }}
            footer={<>
                <Button disabled={busy} onClick={() => setEditing(null)}>关闭</Button>
                <Button loading={busy} disabled={!editingEnabled || !date || date === editing?.date} onClick={() => void prepare()}>预览</Button>
                <Button type="primary" loading={busy} disabled={!editingEnabled || !plan?.writesEnabled} onClick={() => void apply()}>应用</Button>
            </>}>
            {editing && <p>{teamLabel(editing.awayLabel)} @ {teamLabel(editing.homeLabel)}</p>}
            <label>比赛日期{editingEnabled ? <Input type="date" value={date} disabled={busy}
                onChange={(event) => { if (editingRef.current) { setDate(event.target.value); setPlan(null); } }} />
                : <span className="field-value">{editing?.date ?? date}</span>}</label>
            {!data.writesEnabled && <Alert style={{ marginTop: 16 }} type="warning" showIcon
                message="当前只读" description={data.writeReason ?? '赛程写入须先经过实机验收确认。'} />}
            {editError && <Alert style={{ marginTop: 16 }} type="error" showIcon message={editError} />}
            {plan && <p>{plan.beforeDate} → <strong>{plan.afterDate}</strong></p>}
        </Modal>
    </div>;
}
