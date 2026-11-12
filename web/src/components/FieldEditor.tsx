import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react';
import {
    App as AntApp,
    Alert,
    Button,
    Empty,
    Input,
    InputNumber,
    Select,
    Space,
    Switch,
    Table,
    Tag,
    Tooltip,
    Typography,
} from 'antd';
import { applyPlayer, avatarClear, avatarPick, exportPlayer, peekPlayer, previewPlayerImport, readPlayerDisplay, undoSingle } from '../api';
import type { FieldCell, PlayerDetail } from '../types';
import Avatar from './Avatar';
import type { BorrowedPhoto } from '../avatars';
import { estimateOverall, overallFields } from '../overall';
import HeadScanTag from './HeadScanTag';
import { ArchetypeLabel, OverallRing, PercentileTable } from './PlayerProfile';
import BigRadar from './BigRadar';
import { standings , categoryAverages } from '../scouting';
import type { CategoryScores } from '../archetype';
import { archetypeOf, categoryScores } from '../archetype';
import TeamIdentity from './TeamIdentity';
import StatisticsEditor from './StatisticsEditor';
import BadgeIdentity from './BadgeIdentity';
import BadgeTip from './BadgeTip';
import { findBadge } from '../buildCatalog';
import { badgePresentation } from '../badgePresentation';
import { confirmedOptions, displayValue, fieldDescription, fieldGroupLabel, fieldLabel, fieldSectionDescription, isConfirmedField, isRoutineField, optionLabel, sectionLabel } from '../labels';
import { EditingSwitch, useDraftProtection, useEditing } from '../editingContext';
import { useDisplayRead } from '../useDisplayRead';
import { playerImportPreview } from '../playerImport';
import { groupPlayerEditorFields } from '../playerEditorGroups';
import NumericSlider from './NumericSlider';
import PendingChanges, { stageField } from './PendingChanges';

type Props = {
    index: number;
    /** The browsing source is retained even when prospects share the roster table. */
    source?: string;
    teamIndex?: number;
    load: () => Promise<PlayerDetail>;
    onRead?: (detail: PlayerDetail) => void;
    onClose: () => void;
    open?: boolean;
    borrowed?: BorrowedPhoto | null;
    /** League pool for percentiles; the report hides percentiles without it. */
    pool?: { scores: CategoryScores | null; overall: number | null; position: string | null }[];
};

type Draft = Record<string, number | string | boolean>;
const sectionOrder = [
    'Vitals', 'Body', 'Attributes', 'Tendencies', 'Badges', 'Signature', 'Contract', 'Health',
    'Gear', 'Personality', 'Awards', 'Team', 'Roster', 'College', 'Appearance', 'Type', 'ID', 'Stats',
];
const sectionMenus = [
    { label: '资料与数据', keys: ['Vitals', 'statistics', 'Body'] },
    { label: '能力与表现', keys: ['Attributes', 'Tendencies', 'Badges', 'Signature', 'Personality'] },
    { label: '合同与名单', keys: ['Contract', 'Health', 'Team', 'Roster', 'College', 'Awards', 'Stats'] },
    { label: '外貌与身份', keys: ['Gear', 'Appearance', 'Type', 'ID'] },
];
const classifiedSections = new Set(sectionMenus.flatMap((group) => group.keys));

/** Edits are staged locally and only sent when the user applies them. */
export default function FieldEditor({ index, source = 'roster', teamIndex, load, onRead, onClose, open = true, borrowed, pool }: Props) {
    const { message, modal } = AntApp.useApp();
    const { editing, confirmDiscard, requestLeave } = useEditing();
    const [draft, setDraft] = useState<Draft>({});
    const [writing, setBusy] = useState(false);
    const [filter, setFilter] = useState('');
    const [avatarVersion, setAvatarVersion] = useState(0);
    const [diagnostics, setDiagnostics] = useState(false);
    const [activeTab, setActiveTab] = useState('Vitals');
    const [statisticsToolbar, setStatisticsToolbar] = useState<HTMLSpanElement | null>(null);
    const [statisticsState, setStatisticsState] = useState({ dirty: 0, busy: false });
    const reader = useMemo(() => ({
        peek: () => peekPlayer(index, source),
        read: (force: boolean) => readPlayerDisplay(index, source, { force }),
        fresh: load,
    }), [index, source, load]);
    const reads = useDisplayRead({ identity: `player:${source}:${index}`, reader, editing,
        dirty: Object.keys(draft).length > 0, enabled: activeTab !== 'statistics', onRead });
    const detail = reads.data;
    const busy = writing || reads.busy || statisticsState.busy;
    useDraftProtection(open ? Object.keys(draft).length + statisticsState.dirty : 0,
        open && (writing || statisticsState.busy),
        `${source}:${index}:${detail?.readToken ?? ''}`);
    const importContext = useMemo(() => ({}), [index, source, editing, detail?.readToken]);
    const importState = useRef<object | null>(importContext);
    importState.current = importContext;
    const importDialog = useRef<{ context: object; cancel: () => void } | null>(null);
    useEffect(() => {
        importState.current = importContext;
        return () => {
            if (importDialog.current?.context === importContext) importDialog.current.cancel();
            if (importState.current === importContext) importState.current = null;
        };
    }, [importContext]);
    const reload = async () => {
        setDraft({});
        await reads.reload();
    };
    const refresh = async () => {
        if (!busy && await confirmDiscard(Object.keys(draft).length)) await reload();
    };
    const close = async () => {
        if (await requestLeave()) onClose();
    };
    useEffect(() => { setDraft({}); }, [editing, index, source]);

    const averages = useMemo(() => (pool && pool.length > 0 ? categoryAverages(pool) : null), [pool]);
    const dirtyCount = Object.keys(draft).length;
    // Current decoded values of the overall model inputs, so drafts can be scored against them.
    const modelValues = useMemo(() => {
        const values: Record<string, unknown> = {};
        const wanted = new Set(overallFields);
        for (const section of detail?.sections ?? []) {
            for (const cell of section.fields) {
                if (wanted.has(cell.key)) {
                    values[cell.key] = cell.value;
                }
            }
        }
        return values;
    }, [detail]);
    const estimated = estimateOverall(detail?.summary?.position, detail?.summary?.overall, modelValues, draft);
    // Category scores follow unsaved edits so the profile previews the change.
    const profile = useMemo(() => {
        const current: Record<string, unknown> = {};
        for (const section of detail?.sections ?? []) {
            for (const cell of section.fields) {
                if (cell.key.startsWith('Attributes/')) {
                    current[cell.key] = cell.key in draft ? draft[cell.key] : cell.value;
                }
            }
        }
        const scores = categoryScores(current);
        return scores ? { scores, archetype: archetypeOf(scores) } : null;
    }, [detail, draft]);

    const apply = async () => {
        if (!editing || !reads.editReady || busy || !detail?.readToken || dirtyCount === 0) {
            return;
        }
        setBusy(true);
        try {
            const result = await applyPlayer(detail.index, draft, source, detail.readToken);
            message.success(`已应用 ${result.changes.length} 项修改`);
            await reload();
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    };

    const undo = async () => {
        if (!editing || !reads.editReady || busy || !detail || dirtyCount > 0 || statisticsState.dirty > 0) {
            return;
        }
        setBusy(true);
        try {
            await undoSingle(detail.index);
            message.success('已撤销最近一次写入');
            await reload();
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    };

    const exportBackup = async () => {
        setBusy(true);
        try {
            const saved = await exportPlayer(index, source);
            message.success(`已导出 ${saved.count} 个字段：${saved.name}`);
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    };

    const importBackup = async () => {
        if (!editing || !reads.editReady || busy || !detail?.readToken || dirtyCount > 0 || statisticsState.dirty > 0) {
            return;
        }
        const context = importContext;
        const isCurrent = () => importState.current === context;
        setBusy(true);
        try {
            const result = await previewPlayerImport(index, source, detail.readToken);
            if (result.cancelled || !isCurrent()) {
                return;
            }
            const presentation = playerImportPreview(result);
            if (result.index !== index || result.source !== source || !result.readToken || presentation.blocked > 0) {
                message.error('备份包含当前未开放的项目，未导入任何修改。');
                return;
            }
            if (result.changeCount === 0) {
                message.info(`备份与当前值一致，未改动（跳过 ${result.skipped} 项）`);
                return;
            }
            const confirmed = await new Promise<boolean>((resolve) => {
                let settled = false;
                const finish = (accepted: boolean) => {
                    if (settled) return;
                    settled = true;
                    importDialog.current = null;
                    resolve(accepted);
                };
                const dialog = modal.confirm({
                    title: `导入「${result.fileName}」到 ${detail.name}`,
                    width: 760,
                    okText: `应用 ${result.changeCount} 项`,
                    cancelText: '取消',
                    content: <Table size="small" rowKey="field" dataSource={presentation.rows}
                        pagination={{ pageSize: 8, size: 'small', showSizeChanger: false, hideOnSinglePage: true }}
                        columns={[
                            { title: '项目', dataIndex: 'label' },
                            { title: '当前值', dataIndex: 'before' },
                            { title: '修改为', dataIndex: 'after' },
                        ]} />,
                    onOk: () => { finish(isCurrent()); },
                    onCancel: () => { finish(false); },
                    afterClose: () => { finish(false); },
                });
                importDialog.current = { context, cancel: () => { finish(false); dialog.destroy(); } };
            });
            if (!confirmed || !isCurrent()) return;
            const applied = await applyPlayer(index, result.requestChanges, source, result.readToken);
            if (!isCurrent()) return;
            message.success(`已导入 ${applied.changes.length} 项（${result.fileName}），共 ${applied.patches} 处改动`);
            await reload();
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    };

    const pickAvatar = async () => {
        if (!editing || busy || !detail) {
            return;
        }
        try {
            const next = await avatarPick(detail.name);
            if (next.cancelled) {
                return;
            }
            setAvatarVersion((current) => current + 1);
            message.success(next.found ? '头像已更新' : '头像已清除');
        } catch (error) {
            message.error((error as Error).message);
        }
    };

    const clearAvatar = async () => {
        if (!editing || busy || !detail) {
            return;
        }
        try {
            await avatarClear(detail.name);
            setAvatarVersion((current) => current + 1);
            message.success('头像已恢复为首字母');
        } catch (error) {
            message.error((error as Error).message);
        }
    };

    const columns = [
        { title: '项目', dataIndex: 'label', width: '46%',
            render: (_: unknown, cell: FieldCell) => <div className="field-label" title={fieldDescription(cell)}>
                {cell.section === 'Badges' && (findBadge(cell.label)
                    || (cell.group === 'Personality' && badgePresentation(cell)?.mode === 'equipment'))
                    ? <BadgeIdentity name={cell.label}
                        personality={cell.group === 'Personality' && badgePresentation(cell)?.mode === 'equipment'}
                        state={badgePresentation(cell, draft[cell.key] ?? cell.value)?.state} compact /> : fieldLabel(cell)}
                {cell.group !== cell.section && <div className="field-group">{fieldGroupLabel(cell)}</div>}
            </div> },
        {
            title: '当前值',
            dataIndex: 'display',
            width: '42%',
            render: (_: unknown, cell: FieldCell) =>
                <FieldInput cell={cell} draft={draft} setDraft={setDraft} disabled={busy || editing && !reads.editReady} />,
        },
        {
            title: '状态',
            dataIndex: 'key',
            width: '12%',
            render: (_: unknown, cell: FieldCell) =>
                cell.writable ? null : <Tag color="default">只读</Tag>,
        },
    ];

    const sections = useMemo(() => {
        if (!detail) {
            return [];
        }
        const needle = filter.trim().toLowerCase();
        return detail.sections
            .filter((section) => diagnostics || !['Stats', 'Awards'].includes(section.name))
            .map((section) => {
                const candidates = section.fields.filter((field) =>
                    isConfirmedField(field) && (diagnostics || isRoutineField(field)));
                const groups = groupPlayerEditorFields(section.name, candidates, needle);
                const matchingKeys = new Set(groups.flatMap((group) => group.fields.map((field) => field.key)));
                const fields = needle ? candidates.filter((field) => matchingKeys.has(field.key)) : candidates;
                return { ...section, fields, groups };
            })
            .filter((section) => section.fields.length > 0 || (!needle && section.state === 'absent'))
            .sort((first, second) => {
                const rank = (name: string) => {
                    const index = sectionOrder.indexOf(name);
                    return index < 0 ? sectionOrder.length : index;
                };
                return rank(first.name) - rank(second.name);
            });
    }, [detail, filter, diagnostics]);

    if (!detail) {
        return <div className="empty-hint">{busy ? '读取中…' : reads.error || '没有数据'}</div>;
    }

    const tabItems = [{
            key: 'statistics',
            label: '数据与荣誉', name: '数据与荣誉', count: null as number | null, dirty: statisticsState.dirty,
            children: null,
        }, ...sections.map((section) => ({
            key: section.name,
            label: sectionLabel(section.name), name: sectionLabel(section.name),
            count: section.state === 'absent' ? null : section.fields.length as number | null,
            dirty: section.fields.filter((field) => draft[field.key] !== undefined).length,
            children: section.state === 'absent' ? (
                <Alert
                    className="absent-child-object"
                    data-section={section.name}
                    type="info"
                    showIcon
                    message={section.message ?? '该球员无自定义外貌/纹身数据'}
                    style={{ marginBottom: 12 }}
                />
            ) : diagnostics ? (
                <Table<FieldCell>
                    size="small"
                    rowKey="key"
                    dataSource={section.fields}
                    columns={columns}
                    pagination={false}
                    scroll={{ x: 360 }}
                    expandable={diagnostics ? {
                        expandedRowRender: (field) => <div className="diagnostic-data">
                            <div>原始项目：{field.label}</div>
                            <div>内部字段：{field.key}</div>
                            <div>原始值：{String(field.value ?? '')}</div>
                        </div>,
                    } : undefined}
                    rowClassName={(field) => draft[field.key] !== undefined ? 'gs-row-dirty' : ''}
                />
            ) : (
                <div className="field-sections">
                    {fieldSectionDescription(section.name) && <p className="field-description">
                        {fieldSectionDescription(section.name)}</p>}
                    {section.groups.map(({ key, label, color, fields }) => (
                        <section className={`field-section${fields.length > 8 ? ' fe-field-group-wide' : ''}`}
                            key={key} aria-label={label} style={{ '--category': color } as CSSProperties}>
                            <h3 className="field-section-title">
                                <span>{label}</span><small>{fields.length} 项</small>
                                {fields.some((field) => draft[field.key] !== undefined) && <small className="fe-group-dirty">
                                    {fields.filter((field) => draft[field.key] !== undefined).length} 项未应用</small>}
                            </h3>
                            <div className="field-grid">
                                {fields.map((field) => {
                                    const id = `player-${index}-${encodeURIComponent(field.key)}`;
                                    const dirty = draft[field.key] !== undefined;
                                    return (
                                        <div key={field.key} data-row-key={field.key}
                                            className={`field-row${dirty ? ' field-row-dirty' : ''}${
                                                editing && ['Attributes', 'Tendencies'].includes(section.name)
                                                    && field.kind === 'number' && field.writable ? ' field-row-slider' : ''}`}>
                                            <label className="field-label" htmlFor={id} title={fieldDescription(field) ?? fieldLabel(field)}>
                                                {section.name === 'Badges' && (findBadge(field.label)
                                                    || (field.group === 'Personality' && badgePresentation(field)?.mode === 'equipment'))
                                                    ? <Tooltip placement="right" mouseEnterDelay={0.15} overlayClassName="badge-tip-overlay"
                                                        title={<BadgeTip name={field.label} current={(['bronze', 'silver', 'gold', 'hof'] as const).find((tier) => tier === badgePresentation(field, draft[field.key] ?? field.value)?.state) ?? null} />}>
                                                        <span><BadgeIdentity name={field.label}
                                                        personality={field.group === 'Personality' && badgePresentation(field)?.mode === 'equipment'}
                                                        state={badgePresentation(field, draft[field.key] ?? field.value)?.state} compact plain /></span></Tooltip>
                                                    : fieldLabel(field)}
                                                {!field.writable && <span className="field-readonly">只读</span>}
                                            </label>
                                            <div className="field-control">
                                                <FieldInput cell={field} draft={draft} setDraft={setDraft}
                                                    disabled={busy || editing && !reads.editReady} id={id}
                                                    slider={['Attributes', 'Tendencies'].includes(section.name)} />
                                            </div>
                                        </div>
                                    );
                                })}
                            </div>
                        </section>
                    ))}
                </div>
            ),
    }))].sort((a, b) => {
        const rank = (key: string) => key === 'Vitals' ? 0 : key === 'statistics' ? 1 : 2;
        return rank(a.key) - rank(b.key);
    });
    // Keep the field reader active when a search no longer matches the selected section.
    const current = tabItems.find((item) => item.key === activeTab)
        ?? tabItems.find((item) => item.key !== 'statistics');
    const menuGroups = sectionMenus.map((group, index) => ({
        label: group.label,
        items: tabItems.filter((item) => group.keys.includes(item.key)
            || index === sectionMenus.length - 1 && !classifiedSections.has(item.key)),
    })).filter((group) => group.items.length > 0);
    const overall = detail.summary?.overall;
    const pendingOverall = estimated !== null && estimated !== overall;

    return (
        <div className="field-editor fe">
            <aside className="fe-side">
                <div className="fe-overview">
                    <div className="fe-hero">
                        <Avatar name={detail.name} team={detail.team} photoId={detail.photoId} borrowed={borrowed} size={76} loadImage
                            version={avatarVersion} onClick={editing ? pickAvatar : undefined} title={editing ? '点击选择本地图片作为头像' : detail.name} />
                        <div className="fe-hero-text">
                            <div className="scout-name" title={detail.name}>{detail.name}</div>
                            <div className="scout-archetype"><ArchetypeLabel archetype={profile?.archetype ?? null} summary={detail.summary} /></div>
                            <div className="fe-team">{detail.team
                                ? <TeamIdentity {...(detail.teamBrand ?? { label: detail.team, logoReference: detail.teamLogoReference })} linkIndex={teamIndex} size={22} />
                                : <span className="muted">{source === 'roster' ? '未在球队名单中' : source === 'prospects' ? '待选秀' : '新秀名单'}</span>}</div>
                        </div>
                        <OverallRing large value={estimated ?? overall} label={pendingOverall ? '预估' : '总评'}
                            title={pendingOverall
                                ? `当前总评 ${overall}，修改后预估 ${estimated}（按名单 4,396 名球员拟合，约九成误差在 ±1 以内；应用后由游戏重新计算）`
                                : '游戏缓存的总评'} />
                    </div>
                    <div className="scout-meta">
                        <span>位置 <b>{[detail.summary?.position, detail.summary?.secondaryPosition].filter(Boolean).join(' / ') || '—'}</b></span>
                        <span>年龄 <b>{detail.summary?.age ?? '—'}</b></span>
                        <span>身高 <b>{detail.summary?.heightCm ? `${detail.summary.heightCm.toFixed(0)} cm` : '—'}</b></span>
                        <span>体重 <b>{detail.summary?.weightKg ? `${detail.summary.weightKg.toFixed(1)} kg` : '—'}</b></span>
                    </div>
                    {pendingOverall && <div className="scout-pending">
                        总评 {overall} → <b className={estimated! > (overall ?? 0) ? 'up' : 'down'}>{estimated}</b> · {dirtyCount} 项未应用</div>}
                    <div className="fe-tags">
                        <Button size="small" type="link" disabled={!editing || busy} onClick={clearAvatar}>清除头像</Button>
                        {diagnostics && <HeadScanTag info={detail.headScan} />}
                        {diagnostics && (detail.unavailableCount ?? 0) > 0 && <Tag color="default"
                            title={Object.entries(detail.unavailableBySection ?? {}).map(([name, count]) => `${sectionLabel(name)}：${count}`).join('\n')}>
                            {detail.unavailableCount} 项关联数据未加载</Tag>}
                        {(detail.errorCount ?? 0) - (detail.unavailableCount ?? 0) > 0 && <Tag color="orange"
                            title={Object.entries(detail.errorsBySection ?? {}).map(([name, count]) => `${sectionLabel(name)}：${count}`).join('\n')}>
                            {(detail.errorCount ?? 0) - (detail.unavailableCount ?? 0)} 项读取失败</Tag>}
                    </div>
                </div>
                <section className="fe-chart" aria-label="能力分布">
                    <h4 className="fe-side-title">能力分布</h4>
                    {profile ? <BigRadar size={280} compact averages={averages} position={detail.summary?.position ?? null}
                        layers={[{ key: 'p', label: detail.name, color: 'var(--primary)', scores: profile.scores }]} />
                        : <span className="muted">没有可读的能力值</span>}
                </section>
                <section className="fe-ranks" aria-label="联盟排位">
                    <h4 className="fe-side-title">联盟排位</h4>
                    {profile && pool && pool.length > 0
                        ? <PercentileTable position={detail.summary?.position ?? null} rows={standings(
                            { scores: profile.scores, overall: estimated ?? overall ?? null, position: detail.summary?.position ?? null }, pool)} />
                        : <span className="muted">读取全名单能力后显示联盟百分位</span>}
                </section>
                <div className="fe-diag">
                    <Space size={6}><Switch size="small" checked={diagnostics} onChange={setDiagnostics} />
                        <Typography.Text type="secondary">诊断资料</Typography.Text></Space>
                    {diagnostics && <div className="diagnostic-data">球员记录：{detail.index} · 内存地址：{detail.address}</div>}
                </div>
            </aside>
            <main className="fe-main">
                <div className="fe-bar">
                    <Input allowClear size="small" placeholder="搜索球员资料" style={{ width: 200 }} value={filter}
                        onChange={(event) => setFilter(event.target.value)} />
                    <EditingSwitch />
                    {reads.error && <Tag color="orange" title={reads.error}>更新失败</Tag>}
                    <span className="fe-bar-gap" />
                    <span ref={setStatisticsToolbar} style={{ display: 'contents' }} />
                    {activeTab !== 'statistics' && <>
                        <Button size="small" aria-label="刷新球员资料" onClick={() => void refresh()} loading={busy}>重新读取</Button>
                        <Button size="small" onClick={undo} loading={busy}
                            disabled={!editing || !reads.editReady || dirtyCount > 0 || statisticsState.dirty > 0}>撤销</Button>
                        <Button size="small" onClick={exportBackup} loading={busy}>导出</Button>
                        <Button size="small" onClick={importBackup} loading={busy}
                            disabled={!editing || !reads.editReady || dirtyCount > 0 || statisticsState.dirty > 0}>导入</Button>
                        <Button size="small" type="primary" aria-label="应用球员修改"
                            disabled={!editing || !reads.editReady || dirtyCount === 0 || !detail.readToken} loading={busy} onClick={apply}>
                            应用{dirtyCount > 0 ? ` (${dirtyCount})` : ''}</Button>
                        <Button size="small" type="text" onClick={() => void close()} disabled={busy} aria-label="关闭">✕</Button>
                    </>}
                </div>
                <PendingChanges fields={detail.sections.flatMap((section) => section.fields)}
                    draft={draft} label="球员待应用修改" disabled={busy} onReset={(key) => setDraft((previous) => {
                        const next = { ...previous }; delete next[key]; return next;
                    })} onResetAll={() => setDraft({})} onJump={(cell) => {
                        setFilter('');
                        if (['Stats', 'Awards'].includes(cell.section)) setDiagnostics(true);
                        setActiveTab(detail.sections.find((section) => section.fields.some((field) => field.key === cell.key))?.name ?? cell.section);
                    }} />
                <div className="fe-workspace">
                    <nav className="fe-nav" role="tablist" aria-label="球员资料分类" aria-orientation="vertical">
                        {menuGroups.map((group) => <div className="fe-nav-group" key={group.label}>
                            <div className="fe-nav-group-title">{group.label}</div>
                            {group.items.map((item) => <button key={item.key} type="button" role="tab"
                                id={`player-tab-${index}-${encodeURIComponent(item.key)}`}
                                aria-selected={item.key === current?.key} aria-controls={`player-fields-${index}`}
                                className={`fe-tab ${item.key === current?.key ? 'on' : ''}`} onClick={() => setActiveTab(item.key)}>
                                <span className="fe-tab-label">{item.name}</span>
                                {item.count !== null && <small>{item.count}</small>}
                                {item.dirty > 0 && <i className="fe-dirty" title={`${item.dirty} 项未应用`}>{item.dirty}</i>}
                            </button>)}
                        </div>)}
                    </nav>
                    <div className="fe-section-pane">
                        <header className="fe-section-heading">
                            <h2>{current?.name}</h2>
                            <Select className="fe-section-select" size="small" aria-label="球员资料分类"
                                value={current?.key} onChange={setActiveTab}
                                options={menuGroups.map((group) => ({ label: group.label,
                                    options: group.items.map((item) => ({ value: item.key,
                                        label: `${item.name}${item.count !== null ? ` · ${item.count} 项` : ''}${item.dirty ? ` · ${item.dirty} 项未应用` : ''}` })) }))} />
                            {current && current.count !== null && <span className="muted">{current.count} 项</span>}
                            {!!current?.dirty && <span className="fe-group-dirty">{current.dirty} 项未应用</span>}
                        </header>
                        <div className="fe-content field-tabs" role="tabpanel"
                            id={`player-fields-${index}`}
                            aria-labelledby={`player-tab-${index}-${encodeURIComponent(current?.key ?? '')}`}>
                            {current?.children}
                            {!current && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="没有匹配的编辑项目" />}
                            <StatisticsEditor index={index} source={source} toolbarTarget={statisticsToolbar}
                                active={current?.key === 'statistics'} onStateChange={setStatisticsState}
                                otherDirty={dirtyCount} otherBusy={writing} onClose={() => void close()} />
                        </div>
                    </div>
                </div>
            </main>
        </div>
    );
}

export function FieldInput({
    cell,
    draft,
    setDraft,
    disabled,
    id,
    editable,
    slider = false,
}: {
    cell: FieldCell;
    draft: Draft;
    setDraft: (next: Draft) => void;
    disabled: boolean;
    id?: string;
    editable?: boolean;
    slider?: boolean;
}) {
    const { editing } = useEditing();
    const enabled = editable ?? editing;
    if (!isConfirmedField(cell)) return null;
    const staged = draft[cell.key];
    const value = staged !== undefined ? staged : cell.value;
    const stage = (next: Draft[string]) => setDraft(stageField(draft, cell, next));
    const badge = badgePresentation(cell, value);

    if (!cell.writable || !enabled) {
        return <Typography.Text id={id} type={cell.writable ? undefined : 'secondary'}
            title={fieldDescription(cell)}>{badge?.mode === 'equipment'
                ? badge.state === 'equipped' ? '已装备' : '未装备' : displayValue(cell, enabled ? value : cell.value)}</Typography.Text>;
    }
    if (cell.kind === 'bool') {
        return (
            <Switch
                id={id}
                aria-label={fieldLabel(cell)}
                size="small"
                disabled={disabled}
                checked={Boolean(value)}
                className={badge?.mode === 'equipment' ? 'badge-equipment-control' : undefined}
                checkedChildren={badge?.mode === 'equipment' ? '已装备' : undefined}
                unCheckedChildren={badge?.mode === 'equipment' ? '未装备' : undefined}
                onChange={stage}
            />
        );
    }
    if (cell.kind === 'choice' && cell.options.length > 0) {
        return (
            <Select
                id={id}
                aria-label={fieldLabel(cell)}
                size="small"
                showSearch
                optionFilterProp="label"
                disabled={disabled}
                style={{ width: '100%' }}
                className={badge ? `badge-${badge.mode}-control badge-state-${badge.state}` : undefined}
                value={String(value ?? '')}
                options={confirmedOptions(cell).map((option) => ({
                    value: option,
                    label: badge?.mode === 'equipment'
                        ? badgePresentation(cell, option)?.state === 'equipped' ? '已装备' : '未装备'
                        : optionLabel(option, cell),
                }))}
                onChange={stage}
            />
        );
    }
    if (cell.kind === 'number') {
        if (slider && Number.isFinite(cell.min) && Number.isFinite(cell.max) && cell.min! < cell.max!
            && typeof value === 'number' && Number.isFinite(value)) {
            return <NumericSlider id={id} label={fieldLabel(cell)} value={value}
                min={cell.min!} max={cell.max!} disabled={disabled}
                onChange={stage} />;
        }
        return (
            <Space.Compact style={{ width: '100%' }}>
                <InputNumber
                    id={id}
                    aria-label={fieldLabel(cell)}
                    size="small"
                    disabled={disabled}
                    style={{ width: '100%' }}
                    min={cell.min}
                    max={cell.max}
                    value={typeof value === 'number' ? value : Number(value)}
                    onChange={(next) => {
                        if (next === null) {
                            return;
                        }
                        stage(next);
                    }}
                />
            </Space.Compact>
        );
    }
    return (
        <Input
            id={id}
            aria-label={fieldLabel(cell)}
            size="small"
            disabled={disabled}
            value={String(value ?? '')}
            onChange={(event) => stage(event.target.value)}
        />
    );
}
