import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, App as AntApp, Button, Empty, Input, Segmented, Switch, Table, Tag, Tooltip } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { EditOutlined, ReloadOutlined, UndoOutlined } from '@ant-design/icons';
import { matchRead, matchStatus, matchUndo } from '../api';
import { BridgeError, isBrowserLive, isPreview } from '../bridge';
import { matchClock, matchFieldLabel, matchPeriodLabel, matchShooting, matchValues, stableOrder } from '../match';
import type { MatchPlayer, MatchRow, MatchSnapshot, MatchStatus, MatchTeam } from '../match';
import Avatar from './Avatar';
import TeamIdentity from './TeamIdentity';
import MatchEditor from './MatchEditor';
import { loadLog, recordSnapshot, saveLog } from '../gameLog';
import { useEditing } from '../editingContext';

type PlayerRow = MatchPlayer & { values: MatchRow };

/** Gap after one sample completes before the next starts; samples never overlap. */
const LIVE_GAP_MS = 250;
/** Consecutive "game changed during this sample" results tolerated before live refresh stops. */
const MAX_CHANGED_IN_A_ROW = 40;

export default function MatchPanel() {
    const { message } = AntApp.useApp();
    const { editing: editingEnabled } = useEditing();
    const editingRef = useRef(editingEnabled);
    editingRef.current = editingEnabled;
    const [snapshot, setSnapshot] = useState<MatchSnapshot | null>(null);
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);
    const [writing, setWriting] = useState(false);
    const [status, setStatus] = useState<MatchStatus | null>(null);
    const [live, setLive] = useState(true);
    const [side, setSide] = useState(0);
    const [slot, setSlot] = useState(0);
    const [query, setQuery] = useState('');
    const [editing, setEditing] = useState<{ side: number; playerId?: number } | null>(null);
    const [visible, setVisible] = useState(!document.hidden);
    const requestId = useRef(0);
    const identity = useRef('');
    const inFlight = useRef<Promise<boolean> | null>(null);
    const changedInARow = useRef(0);
    const order = useRef<{ key: string; entities: number[] }>({ key: '', entities: [] });
    const snapshotRef = useRef<MatchSnapshot | null>(null);
    const logged = useRef('');
    snapshotRef.current = snapshot;
    const readsPaused = status?.readsEnabled !== true;

    /** One sample. Background samples never flash a loading state or clear the table. */
    const refresh = useCallback((options: { foreground?: boolean } = {}) => {
        if (readsPaused || document.hidden) {
            return Promise.resolve(false);
        }
        if (inFlight.current) {
            return inFlight.current;
        }
        const request = ++requestId.current;
        if (options.foreground) {
            setLoading(true);
        }
        const operation = (async () => {
            try {
                const result = await matchRead();
                if (request !== requestId.current) {
                    return false; // Page left or hidden meanwhile: discard the stale result.
                }
                const nextIdentity = `${result.pid}:${result.processCreatedUtc}:${result.teams.map((item) => item.label).join('|')}`;
                if (nextIdentity !== identity.current) {
                    identity.current = nextIdentity;
                    order.current = { key: '', entities: [] };
                    setSide(0);
                    setSlot(0);
                    setQuery('');
                }
                changedInARow.current = 0;
                // Keep a per-game box score for the analytics pages; store only when the line changes.
                const line = result.teams.map((team) => `${team.label}:${team.stats.total.points}`).join('|') + `@${result.period}`;
                if (!isPreview && result.issues.length === 0 && line !== logged.current) {
                    logged.current = line;
                    try { saveLog(recordSnapshot(loadLog(), result)); } catch { /* storage full: analytics only */ }
                }
                snapshotRef.current = result;
                setSnapshot(result);
                setError('');
                return true;
            } catch (failure) {
                if (request !== requestId.current) {
                    return false;
                }
                if (failure instanceof BridgeError && failure.code === 'matchChanged') {
                    // The game updated stats mid-sample. Keep the last consistent sample and retry.
                    changedInARow.current += 1;
                    if (changedInARow.current < MAX_CHANGED_IN_A_ROW) {
                        return true;
                    }
                    setLive(false);
                    setError('比赛数据持续变化，连续多次未能取得一致快照，实时更新已暂停');
                    return false;
                }
                // A real failure must not leave another match's values on screen.
                snapshotRef.current = null;
                setSnapshot(null);
                setError((failure as Error).message);
                setLive(false);
                return false;
            } finally {
                inFlight.current = null;
                if (options.foreground && request === requestId.current) {
                    setLoading(false);
                }
            }
        })();
        inFlight.current = operation;
        return operation;
    }, [readsPaused]);

    const reloadStatus = useCallback(async () => {
        try {
            setStatus(await matchStatus());
        } catch (failure) {
            setError((failure as Error).message);
        }
    }, []);

    useEffect(() => {
        void reloadStatus();
        const visibilityChanged = () => {
            setVisible(!document.hidden);
            if (document.hidden) {
                ++requestId.current; // Any sample still in flight is discarded on arrival.
            }
        };
        document.addEventListener('visibilitychange', visibilityChanged);
        return () => {
            ++requestId.current;
            document.removeEventListener('visibilitychange', visibilityChanged);
        };
    }, [reloadStatus]);

    useEffect(() => {
        if (!editingEnabled) setEditing(null);
    }, [editingEnabled]);

    // Live loop: starts as soon as the page is visible, runs one sample at a time, and stops when
    // the page is hidden, unmounted, paused, or an editor is open.
    useEffect(() => {
        if (readsPaused || !live || !visible || editing !== null || writing) {
            return;
        }
        let stopped = false;
        let timer: number | undefined;
        const tick = async (first: boolean) => {
            const keepGoing = await refresh({ foreground: first && snapshotRef.current === null });
            if (!stopped && keepGoing && !document.hidden) {
                timer = window.setTimeout(() => void tick(false), LIVE_GAP_MS);
            }
        };
        void tick(true);
        return () => {
            stopped = true;
            window.clearTimeout(timer);
        };
    }, [readsPaused, live, visible, editing, writing, refresh]);

    async function openEditor(side: number, playerId?: number) {
        if (!editingRef.current || loading || writing || !snapshot?.readToken || snapshot.issues.length > 0) {
            return;
        }
        await inFlight.current;
        const previous = snapshotRef.current;
        if (!editingRef.current || !await refresh({ foreground: true })) return;
        const current = snapshotRef.current;
        if (!editingRef.current || current === previous || !current?.readToken || current.issues.length > 0) return;
        setEditing({ side, playerId });
    }

    async function undoLast() {
        if (!editingRef.current || writing || !status?.writesEnabled) {
            return;
        }
        setWriting(true);
        try {
            const result = await matchUndo();
            message.success(`已撤销上次场内修改 · 恢复 ${result.patches} 处计数器`);
            setError('');
        } catch (failure) {
            message.error((failure as Error).message);
        } finally {
            setWriting(false);
            await reloadStatus();
        }
    }

    const team = snapshot?.teams.find((item) => item.side === side);
    const players = (team?.players ?? []).map((player) => ({ ...player, values: matchValues(player.stats, slot) }));
    const orderKey = `${identity.current}:${side}:${slot}`;
    const initialSort = (first: PlayerRow, second: PlayerRow) => Number(second.onCourt) - Number(first.onCourt)
        || second.values.points - first.values.points || first.name.localeCompare(second.name);
    const ordered = stableOrder(order.current.key === orderKey ? order.current.entities : [],
        players, (player) => player.entity, initialSort);
    order.current = { key: orderKey, entities: ordered.map((player) => player.entity) };
    const rows: PlayerRow[] = ordered
        .filter((player) => player.name.toLowerCase().includes(query.toLowerCase()));
    const busy = loading || writing;
    const pair = (row: MatchRow, made: string, attempted: string) => `${row[made]}/${row[attempted]}`;
    const columns: ColumnsType<PlayerRow> = [
        { title: '球员', key: 'player', width: 230, fixed: 'left', render: (_, player) =>
            <span className="match-player">
                <Avatar name={player.name} team={team?.label ?? ''} photoId={player.photoId} size={44} />
                <span>{player.name}{player.onCourt && <span className="match-oncourt">场上</span>}</span>
            </span> },
        ...[
            ['得分', 'points'], ['篮板', 'rebounds'], ['助攻', 'assists'],
            ['抢断', 'steals'], ['盖帽', 'blocks'], ['失误', 'turnovers'],
        ].map(([title, field]) => ({
            title, key: field, width: 64, align: 'right' as const,
            render: (_: unknown, player: PlayerRow) => player.values[field],
        })),
        { title: '两分', key: 'two', width: 76, align: 'right',
            render: (_, player) => pair(player.values, 'twoPointMade', 'twoPointAttempted') },
        { title: '三分', key: 'three', width: 76, align: 'right',
            render: (_, player) => pair(player.values, 'threePointMade', 'threePointAttempted') },
        { title: '罚球', key: 'free', width: 76, align: 'right',
            render: (_, player) => pair(player.values, 'freeThrowMade', 'freeThrowAttempted') },
        { title: '前板', key: 'offensive', width: 64, align: 'right',
            render: (_, player) => player.values.offensiveRebounds },
        { title: '后板', key: 'defensive', width: 64, align: 'right',
            render: (_, player) => player.values.defensiveRebounds },
        ...(['field', 'three', 'free'] as const).map((kind, index) => ({
            title: ['投篮%', '三分%', '罚球%'][index], key: `${kind}Percentage`, width: 80, align: 'right' as const,
            render: (_: unknown, player: PlayerRow) => matchShooting(player.values, kind).percentage,
        })),
        { title: '', key: 'edit', width: 44, fixed: 'right', render: (_, player) =>
            <Tooltip title="编辑球员本场数据">
                <Button type="text" size="small" icon={<EditOutlined />} aria-label={`编辑${player.name}本场数据`}
                    disabled={!editingEnabled || busy || !snapshot?.readToken || (snapshot?.issues.length ?? 0) > 0}
                    onClick={() => void openEditor(side, player.id)} />
            </Tooltip> },
    ];
    const periodColumns: ColumnsType<MatchTeam> = [
        { title: '球队', key: 'team', width: 220, render: (_, item) => <TeamIdentity {...item} size={30} /> },
        ...['一', '二', '三', '四', '加时合计'].map((title, index) => ({
            title: index < 4 ? `${title}节` : title, key: String(index), align: 'right' as const,
            render: (_: unknown, item: MatchTeam) => item.stats.periods[index].points,
        })),
        { title: '总分', key: 'total', align: 'right', render: (_, item) => <strong>{item.stats.total.points}</strong> },
    ];

    return <section className="match-workspace" aria-label="场内数据" data-pid={snapshot?.pid}
        data-period={snapshot?.period} data-side={side} data-slot={slot} data-captured={snapshot?.capturedUtc}
        data-reads-enabled={status?.readsEnabled ?? false}>
        <div className="match-toolbar">
            <strong>场内数据</strong>
            {isPreview && <Tag color="gold">示例比赛</Tag>}
            {isBrowserLive && <Tag color="blue">本机开发连接</Tag>}
            <label className="match-auto-refresh">
                <Switch size="small" checked={live && !readsPaused} disabled={readsPaused}
                    onChange={(next) => {
                        changedInARow.current = 0;
                        if (next) {
                            setError('');
                        }
                        setLive(next);
                    }} aria-label="实时更新场内数据" />
                实时更新
            </label>
            {snapshot && <span className="match-read-meta">
                {isPreview ? '示例数据' : `PID ${snapshot.pid}`} · {new Date(snapshot.capturedUtc).toLocaleTimeString()} ·
                读取 {Math.round(snapshot.elapsedMs)} ms
                {editing !== null ? ' · 编辑中已暂停' : live && visible ? ' · 实时' : ' · 已暂停'}
            </span>}
            <Tooltip title={(status?.undoDepth ?? 0) > 0 ? '恢复上次写入前的数值' : '本次连接没有可撤销的场内修改'}>
                <Button icon={<UndoOutlined />} loading={writing}
                    disabled={!editingEnabled || !status?.writesEnabled || busy || editing !== null || (status?.undoDepth ?? 0) === 0}
                    onClick={() => void undoLast()}>撤销上次修改</Button>
            </Tooltip>
            <Button className="match-refresh" icon={<ReloadOutlined />} loading={loading}
                disabled={readsPaused || editing !== null || writing}
                onClick={() => {
                    changedInARow.current = 0;
                    setError('');
                    setLive(true);
                    void refresh({ foreground: snapshot === null });
                }}>{snapshot ? '立即刷新' : '读取比赛'}</Button>
        </div>
        {isPreview && <Alert type="info" showIcon message="界面预览 · 示例比赛，不连接游戏" />}
        {status?.reason && <Alert type="warning" showIcon message={status.reason} />}
        {error && <Alert type="error" showIcon message={error} />}
        {snapshot ? <>
            <div className="match-scoreboard">
                <div className="match-score-team">
                    <TeamIdentity {...snapshot.teams[0]} size={52} />
                    <strong>{snapshot.teams[0].stats.total.points}</strong>
                </div>
                <div className="match-clock">
                    <span>{matchPeriodLabel(snapshot.period)}</span>
                    <Tooltip title="计时器内存读值；特殊模式的显示规则仍需核验">
                        <strong>{matchClock(snapshot.clockValue)}</strong>
                    </Tooltip>
                </div>
                <div className="match-score-team">
                    <strong>{snapshot.teams[1].stats.total.points}</strong>
                    <TeamIdentity {...snapshot.teams[1]} size={52} />
                </div>
            </div>
            <Table className="match-periods" size="small" rowKey="side" columns={periodColumns}
                dataSource={snapshot.teams} pagination={false} scroll={{ x: 640 }} />
            {snapshot.issues.length > 0 && <Alert type="warning" showIcon
                message={`本次有 ${snapshot.issues.length} 项合计差异`}
                description={snapshot.issues.slice(0, 5).map((issue) =>
                    `${issue.owner} · ${matchFieldLabel(issue.field)}：${issue.total} / ${issue.sum}`).join('；')} />}
            <div className="match-table-toolbar">
                <Segmented value={side} onChange={(value) => setSide(Number(value))}
                    options={snapshot.teams.map((item) => ({
                        value: item.side, label: <TeamIdentity {...item} size={24} />,
                    }))} />
                <Segmented size="small" value={slot} onChange={(value) => setSlot(Number(value))}
                    options={['全场', '第1节', '第2节', '第3节', '第4节', '加时合计']
                        .map((label, value) => ({ label, value }))} />
                <Input aria-label="搜索本场球员" placeholder="搜索球员" allowClear value={query}
                    onChange={(event) => setQuery(event.target.value)} />
                <Button size="small" icon={<EditOutlined />} disabled={!editingEnabled || busy || !snapshot.readToken || snapshot.issues.length > 0}
                    onClick={() => void openEditor(side)}>编辑本场数据</Button>
                <span>{team?.players.length ?? 0} 名球员</span>
            </div>
            <div className="match-player-table">
                <Table size="small" rowKey="entity" columns={columns} dataSource={rows}
                    pagination={false} scroll={{ x: 1434 }} summary={() => {
                        if (!team) {
                            return null;
                        }
                        const values = matchValues(team.stats, slot);
                        const totals = [
                            values.points, values.rebounds, values.assists, values.steals, values.blocks, values.turnovers,
                            pair(values, 'twoPointMade', 'twoPointAttempted'),
                            pair(values, 'threePointMade', 'threePointAttempted'),
                            pair(values, 'freeThrowMade', 'freeThrowAttempted'),
                            values.offensiveRebounds, values.defensiveRebounds,
                            ...(['field', 'three', 'free'] as const).map((kind) => matchShooting(values, kind).percentage),
                        ];
                        return <Table.Summary fixed="bottom">
                            <Table.Summary.Row>
                                <Table.Summary.Cell index={0}>球队合计</Table.Summary.Cell>
                                {totals.map((value, index) => <Table.Summary.Cell index={index + 1}
                                    key={index} align="right">{value}</Table.Summary.Cell>)}
                                <Table.Summary.Cell index={totals.length + 1} />
                            </Table.Summary.Row>
                        </Table.Summary>;
                    }} />
            </div>
            <div className="match-status">
                <span>{snapshot.issues.length === 0 ? '球队 / 球员 / 分节合计一致' : '合计存在差异'}</span>
                <Tooltip title="得分已有原生记分函数和实机值依据；其他统计仍需更多非零样本交叉核验">
                    <span>部分统计含义待核验</span>
                </Tooltip>
                <span>写入 {snapshot.writes}</span>
            </div>
        </> : !error && !status?.reason && <Empty description={busy ? '正在读取当前比赛…'
            : status ? '尚未读取当前比赛' : '正在检查读取状态…'} />}
        {editing && snapshot && status && <MatchEditor key={snapshot.readToken} snapshot={snapshot} status={status}
            initialSide={editing.side} initialPlayerId={editing.playerId} initialSlot={slot}
            onClose={() => setEditing(null)} onApplied={async () => {
                // Closing the editor resumes the live loop, which immediately reads the new values.
                setEditing(null);
                await reloadStatus();
            }} />}
    </section>;
}
