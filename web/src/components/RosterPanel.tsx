import { playerTitle } from '../archetype';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { App as AntApp, Button, Checkbox, Empty, Segmented, Select, Space, Tag, Tooltip } from 'antd';
import { SwapOutlined, ArrowRightOutlined, UndoOutlined, ReloadOutlined, ClearOutlined } from '@ant-design/icons';
import Avatar from './Avatar';
import { ratingColor } from './PlayerProfile';
import { money, moneyShort } from '../tradeView';
import {
    applyMove, applyPackage, listPlayers, planMove, planPackage, readDraftPicks, readPlacement, readPlayerRatings,
    readTeams, undoMove,
} from '../api';
import type { TradePackage } from '../api';
import type { DraftPickRow, MoveResult, Placement, PlayerRow, TeamRow } from '../types';
import { detailLabel, pickLabel, tradablePicks } from '../draftPicks';
import { journalLabel, teamLabel } from '../labels';
import { belongsTo, rosterGroups } from '../roster';
import { statsByIndex } from '../scouting';
import type { PlayerStats } from '../scouting';
import { salaryRatio, sideSummary, toggle } from '../tradeMachine';
import type { BasketPlayer } from '../tradeMachine';
import TeamIdentity from './TeamIdentity';
import { isPreview } from '../bridge';
import { useEditing } from '../editingContext';

type Side = { team: number | null; players: number[]; picks: number[] };
const emptySide: Side = { team: null, players: [], picks: [] };

/**
 * Trade machine: pick two teams, click players and draft picks on either side to put them in the
 * deal, read the ledger, preview, then execute. A single-player move to another team is the
 * second view. Planning is read-only; executing asks for confirmation and is journaled.
 */
export default function RosterPanel() {
    const { message, modal } = AntApp.useApp();
    const { editing } = useEditing();
    const editingRef = useRef(editing);
    editingRef.current = editing;
    const [teams, setTeams] = useState<TeamRow[]>([]);
    const [players, setPlayers] = useState<PlayerRow[]>([]);
    const [stats, setStats] = useState<Map<number, PlayerStats>>(new Map());
    const [picks, setPicks] = useState<DraftPickRow[]>([]);
    const [picksError, setPicksError] = useState<string | null>(null);
    const [payroll, setPayroll] = useState<Map<number, number>>(new Map());
    const [a, setA] = useState<Side>(emptySide);
    const [b, setB] = useState<Side>(emptySide);
    const [syncPayroll, setSyncPayroll] = useState(true);
    const [result, setResult] = useState<MoveResult | null>(null);
    const [busy, setBusy] = useState(false);
    const [view, setView] = useState<'trade' | 'move'>('trade');

    const load = useCallback(async () => {
        setBusy(true);
        try {
            const [nextTeams, nextPlayers] = await Promise.all([readTeams(), listPlayers('', null)]);
            setTeams(nextTeams);
            setPlayers(nextPlayers);
            setA((side) => ({ ...side, players: [], picks: [] }));
            setB((side) => ({ ...side, players: [], picks: [] }));
            const [ratings, pickRows] = await Promise.allSettled([readPlayerRatings(), readDraftPicks()]);
            setStats(ratings.status === 'fulfilled' ? statsByIndex(ratings.value) : new Map());
            setPicks(pickRows.status === 'fulfilled' ? pickRows.value : []);
            setPicksError(pickRows.status === 'rejected' ? (pickRows.reason as Error).message : null);
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    }, [message]);

    useEffect(() => {
        void load();
    }, [load]);

    // Preview-only deep link (?preview&page=roster&trade=1) fills both sides for screenshots.
    useEffect(() => {
        if (!isPreview || !new URLSearchParams(window.location.search).has('trade') || teams.length < 2 || players.length === 0 || a.team !== null) {
            return;
        }
        const first = teams[0].index;
        const second = teams[1].index;
        setA({ team: first, players: players.filter((row) => row.teamIndices?.[0] === first).slice(0, 2).map((row) => row.index), picks: [] });
        setB({ team: second, players: players.filter((row) => row.teamIndices?.[0] === second).slice(0, 1).map((row) => row.index), picks: [] });
    }, [teams, players, a.team]);

    // Team payroll comes with any player's placement; read it once per selected team.
    useEffect(() => {
        for (const team of [a.team, b.team]) {
            if (team === null || payroll.has(team)) {
                continue;
            }
            const member = players.find((row) => row.teamIndices?.[0] === team);
            if (member) {
                void readPlacement(member.index).then((place) => {
                    if (place.teamPayroll !== undefined) {
                        setPayroll((current) => new Map(current).set(team, place.teamPayroll!));
                    }
                }).catch(() => undefined);
            }
        }
    }, [a.team, b.team, players, payroll]);

    const roster = (team: number | null) => (team === null ? [] : players
        .filter((row) => row.teamIndices?.[0] === team)
        .sort((x, y) => (y.summary?.overall ?? y.overall ?? 0) - (x.summary?.overall ?? x.overall ?? 0)));
    const basket = (side: Side): BasketPlayer[] => side.players.flatMap((index) => {
        const row = players.find((item) => item.index === index);
        return row ? [{ index, name: row.name, overall: row.summary?.overall ?? row.overall ?? null,
            salary: stats.get(index)?.salary ?? null, teamIndex: side.team ?? -1 }] : [];
    });
    const teamRow = (team: number | null) => teams.find((row) => row.index === team);
    const summaryA = sideSummary(basket(a), basket(b), { payroll: a.team === null ? null : payroll.get(a.team), used: teamRow(a.team)?.used, capacity: teamRow(a.team)?.capacity });
    const summaryB = sideSummary(basket(b), basket(a), { payroll: b.team === null ? null : payroll.get(b.team), used: teamRow(b.team)?.used, capacity: teamRow(b.team)?.capacity });
    const ratio = salaryRatio(summaryA);
    const itemCount = a.players.length + b.players.length + a.picks.length + b.picks.length;
    const ready = a.team !== null && b.team !== null && itemCount > 0;
    const trade = (): TradePackage => ({ firstTeam: a.team!, secondTeam: b.team!, firstPlayers: a.players, secondPlayers: b.players,
        firstPicks: a.picks, secondPicks: b.picks, syncPayroll });

    const run = async (apply: boolean) => {
        if (busy || !ready || (apply && !editingRef.current)) return;
        setBusy(true);
        try {
            const next = apply ? await applyPackage(trade()) : await planPackage(trade());
            setResult(next);
            if (apply) {
                message.success('交易已执行');
                setPayroll(new Map());
                await load();
            }
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    };
    const confirm = () => {
        if (!editingRef.current || busy || !ready) return;
        modal.confirm({
            title: '确认执行交易', okText: '执行', cancelText: '取消',
            content: `将立即修改联赛名单：${itemCount} 项资产换队${syncPayroll ? '，并同步两队薪资总额' : ''}。可以用“撤销上一笔”恢复。`,
            onOk: () => run(true),
        });
    };
    const revert = async () => {
        if (!editingRef.current || busy) return;
        setBusy(true);
        try {
            const next = await undoMove();
            setResult(next);
            message.success('已撤销上一笔');
            setPayroll(new Map());
            await load();
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    };

    const teamSelect = (side: Side, set: (next: Side) => void, other: number | null) => (
        <Select showSearch optionFilterProp="label" placeholder="选择球队" value={side.team} disabled={busy}
            options={teams.filter((row) => row.index !== other).map((row) => ({ value: row.index, label: teamLabel(row.label), brand: row }))}
            optionRender={(option) => <TeamIdentity {...option.data.brand} text={option.data.label} size={20} />}
            labelRender={(option) => { const row = teamRow(Number(option.value)); return row ? <TeamIdentity {...row} text={teamLabel(row.label)} size={20} /> : option.label; }}
            onChange={(team) => { set({ team, players: [], picks: [] }); setResult(null); }} />
    );

    const column = (side: Side, set: (next: Side) => void, other: Side, summary: ReturnType<typeof sideSummary>, label: string) => {
        const list = roster(side.team);
        const owned = tradablePicks(picks, side.team);
        return <section className="tm-col" aria-label={label}>
            <div className="tm-head"><b className="num" style={{ color: 'var(--muted)' }}>{label}</b>{teamSelect(side, set, other.team)}</div>
            <div className="tm-stats">
                <div><span>名单</span><b>{summary.rosterBefore ?? '—'}{summary.rosterAfter !== summary.rosterBefore && summary.rosterAfter !== null
                    && <small className={summary.overRoster ? 'down' : ''}> → {summary.rosterAfter}</small>}<small className="muted"> / {summary.capacity ?? '—'}</small></b></div>
                <div><span>薪资总额</span><b>{moneyShort(summary.payrollBefore)}</b></div>
                <div><span>交易后</span><b className={summary.payrollAfter !== null && summary.payrollBefore !== null
                    ? summary.payrollAfter > summary.payrollBefore ? 'down' : summary.payrollAfter < summary.payrollBefore ? 'up' : '' : ''}>
                    {syncPayroll ? moneyShort(summary.payrollAfter) : '不变'}</b></div>
                <div><span>选秀权</span><b>{owned.length}</b></div>
            </div>
            <div className="tm-list">
                {side.team === null ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="先选择球队" /> : <>
                    <div className="tm-section">球员 · 点击加入交易</div>
                    {list.map((row) => {
                        const on = side.players.includes(row.index);
                        const value = row.summary?.overall ?? row.overall ?? null;
                        const stat = stats.get(row.index);
                        return <div key={row.index} className={`tm-row${on ? ' on' : ''}`} role="checkbox" aria-checked={on} tabIndex={0}
                            onClick={() => { set({ ...side, players: toggle(side.players, row.index) }); setResult(null); }}
                            onKeyDown={(event) => { if (event.key === ' ' || event.key === 'Enter') { event.preventDefault(); set({ ...side, players: toggle(side.players, row.index) }); setResult(null); } }}>
                            <Checkbox checked={on} tabIndex={-1} />
                            <Avatar name={row.name} team={row.team} photoId={row.photoId} size={26} />
                            <span className="nm">{row.name}<small>{[row.summary?.position, row.summary?.age != null ? `${row.summary.age}岁` : null, playerTitle(row.summary, stat?.archetype)?.name].filter(Boolean).join(' · ')}</small></span>
                            <span className="overall-chip" style={{ '--ring': ratingColor(value ?? 0) } as React.CSSProperties}>{value ?? '—'}</span>
                            <span className="tm-money">{stat?.salary ? moneyShort(stat.salary) : '—'}</span>
                            <span className="yrs num">{stat?.yearsLeft != null ? `${stat.yearsLeft}年` : ''}</span>
                        </div>;
                    })}
                    <div className="tm-section">选秀权{picksError ? ' · 读取失败' : ''}</div>
                    {owned.length === 0 && <div className="tm-row pick"><span /><span className="muted">没有可交易的选秀权</span><span /></div>}
                    {owned.map((pick) => {
                        const on = side.picks.includes(pick.index);
                        return <div key={pick.index} className={`tm-row pick${on ? ' on' : ''}`} role="checkbox" aria-checked={on} tabIndex={0}
                            onClick={() => { set({ ...side, picks: toggle(side.picks, pick.index) }); setResult(null); }}
                            onKeyDown={(event) => { if (event.key === ' ' || event.key === 'Enter') { event.preventDefault(); set({ ...side, picks: toggle(side.picks, pick.index) }); setResult(null); } }}>
                            <Checkbox checked={on} tabIndex={-1} />
                            <span className="nm">{pickLabel(pick)}</span>
                            <span className="muted" style={{ textAlign: 'right', fontSize: 11.5 }}>
                                {pick.originalIndex !== null && pick.originalIndex !== side.team ? <>来自 <TeamIdentity {...(teams.find((t) => t.index === pick.originalIndex) ?? { label: pick.original ?? '' })} size={16} text={teamLabel(pick.original ?? '')} /></> : '自有'}</span>
                        </div>;
                    })}
                </>}
            </div>
        </section>;
    };

    const ledger = (side: Side, summary: ReturnType<typeof sideSummary>, title: string) => {
        const row = teamRow(side.team);
        return <div>
            <div className="lbl">{row ? teamLabel(row.label) : title} 送出</div>
            <div className="items">
                {side.players.length + side.picks.length === 0 && <span className="muted">—</span>}
                {basket(side).map((player) => <Tag key={player.index} closable onClose={() => (side === a ? setA : setB)({ ...side, players: side.players.filter((value) => value !== player.index) })}>
                    {player.name} <b className="num">{player.overall ?? ''}</b></Tag>)}
                {side.picks.map((index) => { const pick = picks.find((item) => item.index === index); return pick
                    ? <Tag key={index} closable onClose={() => (side === a ? setA : setB)({ ...side, picks: side.picks.filter((value) => value !== index) })}>{pickLabel(pick)}</Tag> : null; })}
            </div>
            <div className="tm-line"><span>送出薪资</span><b>{money(summary.outgoingSalary)}</b></div>
            <div className="tm-line"><span>收到薪资</span><b>{money(summary.incomingSalary)}</b></div>
        </div>;
    };

    if (view === 'move') {
        return <div className="tm">
            <div className="tm-bar">{viewSwitch()}<span className="spacer" style={{ flex: 1 }} />{toolButtons()}</div>
            <div className="page-pad" style={{ overflow: 'auto' }}><MoveForm teams={teams} players={players} onDone={load} /></div>
            <div />
        </div>;
    }
    function viewSwitch() {
        return <Segmented size="small" value={view} onChange={(value) => { setView(value as 'trade' | 'move'); setResult(null); }}
            options={[{ value: 'trade', label: '交易', icon: <SwapOutlined /> }, { value: 'move', label: '单人调队', icon: <ArrowRightOutlined /> }]} />;
    }
    function toolButtons() {
        return <Space size={6}>
            <Button size="small" icon={<UndoOutlined />} disabled={!editing} onClick={revert} loading={busy}>撤销上一笔</Button>
            <Button size="small" icon={<ReloadOutlined />} onClick={() => void load()} loading={busy}>刷新</Button>
        </Space>;
    }

    return <div className="tm">
        <div className="tm-bar">
            {viewSwitch()}
            <span className="muted" style={{ fontSize: 12 }}>选两支球队 → 点击双方要送出的球员和选秀权 → 预览 → 执行</span>
            <span style={{ flex: 1 }} />
            <Button size="small" icon={<ClearOutlined />} disabled={itemCount === 0 || busy}
                onClick={() => { setA({ ...a, players: [], picks: [] }); setB({ ...b, players: [], picks: [] }); setResult(null); }}>清空交易</Button>
            {toolButtons()}
        </div>
        <div className="tm-cols">
            {column(a, setA, b, summaryA, 'A')}
            {column(b, setB, a, summaryB, 'B')}
        </div>
        <div>
            <div className="tm-ledger">
                {ledger(a, summaryA, 'A 方')}
                {ledger(b, summaryB, 'B 方')}
                <div>
                    <div className="tm-line"><span>薪资匹配</span><b className={ratio === null ? '' : ratio <= 125 ? 'up' : 'down'}>
                        {ratio === null ? '—' : ratio === Infinity ? '单方送出' : `${ratio}%`}</b></div>
                    <Tooltip title="球队薪资总额等于现役球员本赛季薪资之和（32 支球队实测一致）。勾选后在同一笔操作里同步两队总额，撤销时一起恢复。合同记录在球员身上，随球员转队。">
                        <Checkbox checked={syncPayroll} disabled={busy} onChange={(event) => { setSyncPayroll(event.target.checked); setResult(null); }}>同步球队薪资总额</Checkbox>
                    </Tooltip>
                    {(summaryA.overRoster || summaryB.overRoster) && <span className="down" style={{ fontSize: 12 }}>交易后名单超过上限，无法执行</span>}
                    <Space>
                        <Button onClick={() => void run(false)} loading={busy} disabled={!ready}>预览</Button>
                        <Button type="primary" danger onClick={confirm} loading={busy}
                            disabled={!editing || !ready || summaryA.overRoster || summaryB.overRoster}>执行交易</Button>
                    </Space>
                </div>
            </div>
            {result && <div className="tm-result">
                <div><Tag color={result.applied ? 'green' : 'blue'}>{result.applied ? '已执行' : '预览'} · {journalLabel(result.kind)}</Tag>
                    <span className="muted">{result.applied ? `写入 ${result.patchCount} 处` : `将写入 ${result.patchCount} 处（${result.playerCount} 名球员、${result.teamCount} 支球队）`}</span></div>
                {result.details?.map((line) => <span key={line}>· {detailLabel(line)}</span>)}
                {result.journal && <span className="muted">日志：{result.journal}</span>}
            </div>}
        </div>
    </div>;
}

/** Single-player move to another team (or free agency), kept from the earlier version. */
function MoveForm({ teams, players, onDone }: { teams: TeamRow[]; players: PlayerRow[]; onDone: () => Promise<void> }) {
    const { message, modal } = AntApp.useApp();
    const { editing } = useEditing();
    const editingRef = useRef(editing);
    editingRef.current = editing;
    const [group, setGroup] = useState<string | null>(null);
    const [player, setPlayer] = useState<number | null>(null);
    const [target, setTarget] = useState<number | null>(null);
    const [placement, setPlacement] = useState<Placement | null>(null);
    const [syncPayroll, setSyncPayroll] = useState(true);
    const [result, setResult] = useState<MoveResult | null>(null);
    const [busy, setBusy] = useState(false);
    useEffect(() => {
        if (player === null) {
            setPlacement(null);
            return;
        }
        let alive = true;
        readPlacement(player).then((next) => { if (alive) setPlacement(next); }).catch(() => { if (alive) setPlacement(null); });
        return () => { alive = false; };
    }, [player]);
    const groups = useMemo(() => rosterGroups(teams, players).map((item) => ({ value: item.value, label: `${item.label}（${item.count} 人）` })), [teams, players]);
    const row = players.find((item) => item.index === player);
    const run = async (apply: boolean) => {
        if (busy || player === null || target === null || (apply && !editingRef.current)) return;
        setBusy(true);
        try {
            const next = apply ? await applyMove(player!, target!, syncPayroll) : await planMove(player!, target!, syncPayroll);
            setResult(next);
            if (apply) {
                message.success('调队已执行');
                await onDone();
            }
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    };
    return <div className="panel-card" style={{ maxWidth: 720 }}>
        <div className="panel-card-title">把一名球员调到另一支球队</div>
        <Space direction="vertical" style={{ width: '100%' }}>
            <Select showSearch optionFilterProp="label" style={{ width: '100%' }} placeholder="所在球队或自由球员" value={group} options={groups}
                onChange={(value) => { setGroup(value); setPlayer(null); setResult(null); }} />
            <Select showSearch optionFilterProp="label" style={{ width: '100%' }} placeholder="选择球员" value={player} disabled={!group}
                options={players.filter((item) => group !== null && belongsTo(item, group)).map((item) => ({ value: item.index, label: item.name }))}
                onChange={(value) => { setPlayer(value); setResult(null); }} />
            {row && <div className="tm-line"><span>{row.name} · {placement ? <TeamIdentity {...(teams.find((t) => t.label === placement.team) ?? { label: placement.team })} size={18} text={teamLabel(placement.team)} /> : '—'}</span>
                <b>本季 {money(placement?.salary)}{placement?.yearsLeft != null ? ` · 剩余 ${placement.yearsLeft} 年` : ''}</b></div>}
            <Select showSearch optionFilterProp="label" style={{ width: '100%' }} placeholder="目标球队" value={target}
                options={teams.map((item) => ({ value: item.index, label: `${teamLabel(item.label)}（${item.used}/${item.capacity} 人）`, brand: item }))}
                optionRender={(option) => <TeamIdentity {...option.data.brand} size={20} text={option.data.label} />}
                labelRender={(option) => { const item = teams.find((t) => t.index === Number(option.value)); return item ? <TeamIdentity {...item} size={20} text={`${teamLabel(item.label)}（${item.used}/${item.capacity} 人）`} /> : option.label; }}
                onChange={(value) => { setTarget(value); setResult(null); }} />
            <Space wrap>
                <Checkbox checked={syncPayroll} onChange={(event) => { setSyncPayroll(event.target.checked); setResult(null); }}>同步两队薪资总额</Checkbox>
                <Button onClick={() => void run(false)} loading={busy} disabled={player === null || target === null}>预览</Button>
                <Button type="primary" loading={busy} disabled={!editing || player === null || target === null}
                    onClick={() => {
                        if (!editingRef.current || busy) return;
                        modal.confirm({ title: '确认调队', content: '这会立即修改联赛名单。', okText: '执行', cancelText: '取消', onOk: () => run(true) });
                    }}>执行调队</Button>
            </Space>
            {result && <div className="tm-result">
                <span>{result.applied ? '已执行' : '预览'}：{result.applied ? `写入 ${result.patchCount} 处` : `将写入 ${result.patchCount} 处`}</span>
                {result.details?.map((line) => <span key={line}>· {detailLabel(line)}</span>)}
            </div>}
        </Space>
    </div>;
}
