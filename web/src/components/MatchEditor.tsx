import { useEffect, useRef, useState } from 'react';
import { Alert, App as AntApp, Button, Drawer, InputNumber, Select, Table, Tag, Tooltip } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { DeleteOutlined, EyeOutlined, SaveOutlined } from '@ant-design/icons';
import { matchApply, matchPrepare } from '../api';
import {
    matchDraftKey, matchDraftValues, matchEditFields, matchEditRequests, matchFieldLabel, matchSlotLabel, matchValues,
} from '../match';
import type { MatchEditChange, MatchEditDraft, MatchEditPlan, MatchSnapshot, MatchStatus } from '../match';
import Avatar from './Avatar';
import TeamIdentity from './TeamIdentity';
import { clearMatchDrafts, loadMatchDrafts, saveMatchDrafts } from '../matchDraftStorage';
import { EditingSwitch, useEditing } from '../editingContext';

type Props = {
    snapshot: MatchSnapshot;
    status: MatchStatus;
    initialSide: number;
    initialPlayerId?: number;
    initialSlot: number;
    onClose: () => void;
    onApplied: () => Promise<void>;
};

export default function MatchEditor({
    snapshot, status, initialSide, initialPlayerId, initialSlot, onClose, onApplied,
}: Props) {
    const { message, modal } = AntApp.useApp();
    const { editing } = useEditing();
    const editingRef = useRef(editing);
    editingRef.current = editing;
    const [side, setSide] = useState(initialSide);
    const [playerId, setPlayerId] = useState(initialPlayerId
        ?? snapshot.teams.find((team) => team.side === initialSide)?.players[0]?.id);
    const [slot, setSlot] = useState(initialSlot > 0 ? initialSlot : Math.min(snapshot.period, 5));
    const [recovered] = useState(() => {
        try {
            return { drafts: loadMatchDrafts(window.localStorage, snapshot), error: '' };
        } catch (error) {
            return { drafts: {} as Record<string, MatchEditDraft>, error: (error as Error).message };
        }
    });
    const [drafts, setDrafts] = useState<Record<string, MatchEditDraft>>(recovered.drafts);
    const [storageError, setStorageError] = useState(recovered.error);
    const storageBlocked = useRef(!!recovered.error);
    const [plan, setPlan] = useState<MatchEditPlan | null>(null);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState('');
    const requestId = useRef(0);
    useEffect(() => () => { ++requestId.current; }, []);

    const team = snapshot.teams.find((item) => item.side === side)!;
    const players = team.players.filter((player) => player.id === playerId);
    const player = players.length === 1 ? players[0] : undefined;
    const key = matchDraftKey(side, playerId ?? -1, slot);
    const original = player ? matchValues(player.stats, slot) : null;
    const values = original ? matchDraftValues(original, drafts[key]) : null;
    let edits = [] as ReturnType<typeof matchEditRequests>;
    let validation = '';
    try {
        edits = matchEditRequests(snapshot, Object.values(drafts));
    } catch (error) {
        validation = (error as Error).message;
    }
    const dirty = Object.values(drafts).reduce((count, draft) => count + Object.keys(draft.changes).length, 0);
    const columns: ColumnsType<MatchEditChange> = [
        { title: '对象', key: 'owner', width: 170, render: (_, change) =>
            <span>{change.owner}{change.playerId === null && <Tag>球队</Tag>}</span> },
        { title: '分节', dataIndex: 'slot', width: 88, render: matchSlotLabel },
        { title: '统计', dataIndex: 'field', width: 105, render: matchFieldLabel },
        { title: '原值', dataIndex: 'before', width: 65, align: 'right' },
        { title: '新值', dataIndex: 'after', width: 65, align: 'right' },
        { title: '来源', dataIndex: 'derived', width: 65, render: (derived: boolean) => derived ? '联动' : '输入' },
    ];

    function invalidate() {
        ++requestId.current;
        setPlan(null);
        setError('');
    }

    function changeValue(field: string, value: number | null) {
        if (!editingRef.current || !player || !original || busy) {
            return;
        }
        invalidate();
        const changes = { ...drafts[key]?.changes };
        if (value === original[field]) {
            delete changes[field];
        } else {
            changes[field] = value;
        }
        const next = { ...drafts };
        if (Object.keys(changes).length === 0) {
            delete next[key];
        } else {
            next[key] = { side, playerId: player.id, slot, changes };
        }
        setDrafts(next);
        try {
            if (storageBlocked.current) throw new Error('原草稿读取失败，已保留原记录；清空草稿后可重新保存');
            saveMatchDrafts(window.localStorage, snapshot, next);
            setStorageError('');
        } catch (error) {
            setStorageError(`草稿尚未保存：${(error as Error).message}`);
        }
    }

    async function prepare() {
        if (!editingRef.current || busy || validation || edits.length === 0) {
            return;
        }
        const request = ++requestId.current;
        setBusy(true);
        setError('');
        setPlan(null);
        try {
            const result = await matchPrepare(snapshot.readToken, edits);
            if (request === requestId.current) {
                if (result.readToken !== snapshot.readToken) {
                    throw new Error('比赛快照已失效，请重新读取');
                }
                setPlan(result);
            }
        } catch (error) {
            if (request === requestId.current) {
                setError((error as Error).message);
            }
        } finally {
            if (request === requestId.current) {
                setBusy(false);
            }
        }
    }

    const writable = editing && !!plan && status.writesEnabled && plan.liveWriteReady === true;

    function confirmApply() {
        if (!editingRef.current || !writable || busy) {
            return;
        }
        modal.confirm({
            title: '写入游戏？',
            content: `将写入 ${plan!.patches?.length ?? plan!.writes} 处计数器。请先在游戏内暂停比赛；`
                + '写入前会再次核对数据未变化，失败会自动恢复，成功后可在场内页撤销。',
            okText: '写入', cancelText: '取消', onOk: () => void apply(),
        });
    }

    async function apply() {
        if (!editingRef.current || !plan || busy || !writable) {
            return;
        }
        const request = ++requestId.current;
        setBusy(true);
        setError('');
        try {
            const result = await matchApply(plan.readToken, plan.previewToken);
            if (request === requestId.current) {
                message.success(`已写入游戏 · ${result.patches} 处计数器`);
                try {
                    clearMatchDrafts(window.localStorage, snapshot);
                } catch {
                    // The drafts belong to a snapshot that is now obsolete; a stale entry is harmless.
                }
                await onApplied();
            }
        } catch (error) {
            if (request === requestId.current) {
                setError((error as Error).message);
                setPlan(null);
            }
        } finally {
            if (request === requestId.current) {
                setBusy(false);
            }
        }
    }

    function close() {
        if (busy) {
            return;
        }
        if (dirty === 0) {
            onClose();
            return;
        }
        // Drafts belong to this one sample. Once the editor closes, live refresh resumes and the
        // next sample is a new baseline, so the input cannot be restored; say so and drop it.
        modal.confirm({
            title: '关闭场内编辑？',
            content: `${dirty} 项输入尚未写入游戏。关闭后实时刷新会继续，下次打开将基于最新数据，这些输入不会保留。`,
            okText: '放弃并关闭', cancelText: '继续编辑', onOk: () => {
                try {
                    clearMatchDrafts(window.localStorage, snapshot);
                } catch {
                    // Storage is unavailable; nothing was persisted for this sample anyway.
                }
                onClose();
            },
        });
    }

    return <Drawer title="编辑场内数据" extra={<EditingSwitch />} open width="min(860px, 100vw)" onClose={close}
        maskClosable={!busy} keyboard={!busy} closable={!busy} rootClassName="match-edit-drawer"
        footer={<div className="match-edit-actions">
            <Button icon={<DeleteOutlined />} disabled={!editing || busy || (dirty === 0 && !storageBlocked.current)} onClick={() => {
                if (!editingRef.current || busy) return;
                try {
                    clearMatchDrafts(window.localStorage, snapshot);
                    storageBlocked.current = false;
                    setStorageError('');
                    invalidate();
                    setDrafts({});
                } catch (error) {
                    setStorageError(`清空失败：${(error as Error).message}`);
                }
            }}>清空草稿</Button>
            <span>{dirty} 项输入 · {edits.length} 条球员分节记录</span>
            {dirty > 0 && !storageError && <Tag>草稿已保存</Tag>}
            <Button icon={<EyeOutlined />} loading={busy} disabled={!editing || busy || !!validation || edits.length === 0}
                onClick={() => void prepare()}>预览修改</Button>
            <Tooltip title={status.writeReason ?? '场内写入尚未开放'}>
                <Button type="primary" icon={<SaveOutlined />} loading={busy && !!plan}
                    disabled={busy || !writable} onClick={confirmApply}>应用</Button>
            </Tooltip>
        </div>}>
        <div className="match-edit-selectors">
            <Select aria-label="编辑球队" value={side} disabled={busy} options={snapshot.teams.map((item) => ({
                value: item.side, label: <TeamIdentity {...item} size={24} />,
            }))} onChange={(next) => {
                setSide(next);
                setPlayerId(snapshot.teams.find((item) => item.side === next)?.players[0]?.id);
            }} />
            <Select aria-label="编辑球员" value={playerId} disabled={busy} showSearch optionFilterProp="label"
                options={team.players.map((item) => ({
                    value: item.id, label: `${item.name}${item.onCourt ? ' · 场上' : ''}`,
                }))} onChange={setPlayerId} />
            <Select aria-label="编辑分节" value={slot} disabled={busy}
                options={Array.from({ length: Math.min(snapshot.period, 5) }, (_, index) => ({
                    value: index + 1, label: matchSlotLabel(index + 1),
                }))} onChange={setSlot} />
        </div>
        {!status.writesEnabled && <Alert type="warning" showIcon
            message={status.writeReason ?? '场内写入尚未开放'} />}
        {Object.keys(recovered.drafts).length > 0 && <Alert type="info" showIcon message="已恢复当前快照的编辑草稿" />}
        {storageError && <Alert type="error" showIcon message={storageError} />}
        {(error || validation) && <Alert type="error" showIcon message={error || validation} />}
        {!player && <Alert type="error" showIcon message="球员身份不唯一，请重新读取比赛" />}
        {player && original && values && <>
            <div className="match-edit-identity">
                <Avatar name={player.name} team={team.label} photoId={player.photoId} size={48} />
                <strong>{player.name}</strong>
                <Tag>{matchSlotLabel(slot)}</Tag>
                <span>计算得分 <output aria-label="草稿得分">{Number.isFinite(values.points) ? values.points : '--'}</output></span>
                <span>篮板 <output aria-label="草稿篮板">{Number.isFinite(values.rebounds) ? values.rebounds : '--'}</output></span>
            </div>
            <div className="match-edit-fields">
                {matchEditFields.map(({ key: field, label }) => {
                    const id = `match-${key}-${field}`;
                    const changed = drafts[key]?.changes[field] !== undefined;
                    return <div className={`field-row${changed ? ' field-row-dirty' : ''}`} key={field}>
                        <label className="field-label" htmlFor={id}>{label}</label>
                        <div className="field-control">
                            {editing ? <InputNumber id={id} aria-label={label} size="small" min={0} max={65535} precision={0}
                                value={changed ? drafts[key].changes[field] : original[field]} disabled={busy}
                                onChange={(value) => changeValue(field, value)} />
                                : <span className="field-value">{original[field]}</span>}
                        </div>
                    </div>;
                })}
            </div>
        </>}
        {plan && <>
            <div className="match-edit-preview-heading">
                <strong>修改预览</strong>
                <span>{plan.players} 名球员 · {plan.periods} 个分节 · {plan.changes.length} 项关联变化</span>
            </div>
            {plan.patches && <div className="match-edit-preview-heading">
                <span>{plan.patches.length} 处定长计数器 · {plan.patchBytes} 字节 · {plan.liveWriteReady
                    ? '点击“应用”后写入游戏' : '场内写入已暂停，不会写入游戏'}</span>
            </div>}
            <div className="match-edit-scores">
                {snapshot.teams.map((item) => {
                    const change = plan.changes.find((entry) =>
                        entry.side === item.side && entry.playerId === null && entry.slot === 0 && entry.field === 'points');
                    return <div key={item.side}>
                        <TeamIdentity {...item} size={28} />
                        <span>{item.stats.total.points} → <strong>{change?.after ?? item.stats.total.points}</strong></span>
                    </div>;
                })}
            </div>
            <Table className="match-edit-preview" size="small" columns={columns} dataSource={plan.changes}
                rowKey={(change) => `${change.side}:${change.playerId ?? 'team'}:${change.slot}:${change.field}`}
                pagination={{ pageSize: 15, size: 'small', showSizeChanger: false }} scroll={{ x: 560 }} />
        </>}
    </Drawer>;
}
