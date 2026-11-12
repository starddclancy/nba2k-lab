import { useEffect, useLayoutEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { Alert, App as AntApp, Button, Empty, InputNumber, Segmented, Select, Spin, Tabs } from 'antd';
import { applyPlayer, peekPlayerStats, readPlayerStats, readPlayerStatsDisplay, undoSingle } from '../api';
import type { FieldCell, PlayerStatistics } from '../types';
import {
    average, cumulativeAwards, perGameNames, statName, statisticCell, statValue, totalFromAverage,
    type StatisticsDraft,
} from '../playerStatistics';
import { FieldInput } from './FieldEditor';
import { isConfirmedField } from '../labels';
import { compactSeason } from '../seasonYears';
import { useWorkspace } from '../workspaceContext';
import { useEditing } from '../editingContext';
import { useDisplayRead } from '../useDisplayRead';
import PendingChanges, { stageField } from './PendingChanges';

export default function StatisticsEditor({ index, source, toolbarTarget, onClose, active = true, onStateChange, otherDirty = 0, otherBusy = false }: {
    index: number;
    source: string;
    toolbarTarget?: HTMLElement | null;
    onClose?: () => void;
    active?: boolean;
    onStateChange?: (state: { dirty: number; busy: boolean }) => void;
    otherDirty?: number;
    otherBusy?: boolean;
}) {
    const { message } = AntApp.useApp();
    const { editing, confirmDiscard } = useEditing();
    const { seasonEndYear } = useWorkspace();
    const [draft, setDraft] = useState<StatisticsDraft>({});
    const [selected, setSelected] = useState('');
    const [view, setView] = useState('season');
    const [mode, setMode] = useState('total');
    const [writing, setBusy] = useState(false);
    const reader = useMemo(() => ({
        peek: () => peekPlayerStats(index, source),
        read: (force: boolean) => readPlayerStatsDisplay(index, source, { force }),
        fresh: () => readPlayerStats(index, source),
    }), [index, source]);
    const reads = useDisplayRead<PlayerStatistics>({ identity: `statistics:${source}:${index}`,
        reader, editing, dirty: Object.keys(draft).length > 0, enabled: active });
    const { data, error } = reads;
    const busy = writing || reads.busy || otherBusy;
    const dirty = Object.keys(draft).length;
    useLayoutEffect(() => { onStateChange?.({ dirty, busy: writing }); }, [dirty, writing, onStateChange]);

    async function reload() {
        setDraft({});
        await reads.reload();
    }
    async function refresh() {
        if (!busy && await confirmDiscard(dirty)) await reload();
    }

    useEffect(() => {
        setDraft({});
    }, [index, source, editing]);
    useEffect(() => {
        if (data) setSelected((current) => data.seasons.some((season) => season.selector === current)
            ? current : data.seasons[0]?.selector ?? '');
    }, [data]);

    async function apply() {
        if (!editing || !reads.editReady || busy || !data?.readToken || Object.keys(draft).length === 0) {
            return;
        }
        setBusy(true);
        try {
            const result = await applyPlayer(index, draft, source, data.readToken);
            message.success(`已应用 ${result.changes.length} 项统计修改`);
            await reload();
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    }

    async function undo() {
        if (!editing || !reads.editReady || busy || dirty > 0 || otherDirty > 0) return;
        setBusy(true);
        try {
            await undoSingle(index);
            message.success('已撤销最近一次球员写入');
            await reload();
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    }

    const season = data?.seasons.find((entry) => entry.selector === selected);
    const games = statValue(season?.fields.find((field) => statName(field) === 'GAMESPLAYED'), draft);
    const findValue = (name: string) => statValue(season?.fields.find((field) => statName(field) === name), draft);
    const groups = new Map<string, FieldCell[]>();
    const fields = view === 'high' ? data?.direct.filter((field) => field.section === 'Stats') ?? []
        : view === 'awards' ? [
            ...(data?.direct.filter((field) => field.group === 'Awards') ?? []),
            ...(season?.fields.filter((field) => field.group === 'Awards' || field.group === 'Awards & Honors') ?? []),
        ] : season?.fields.filter((field) =>
            !['Awards', 'Awards & Honors', 'Internal / Links', 'Season Context', 'Ratings'].includes(field.group)
            && !['BOX+-', 'POINTSOFFFREETHROW'].includes(statName(field))) ?? [];
    for (const field of fields) {
        if (!isConfirmedField(field)) continue;
        const key = field.section === 'Awards' ? 'Career Awards' : field.group;
        const bucket = groups.get(key) ?? [];
        bucket.push(statisticCell(field));
        groups.set(key, bucket);
    }
    const groupLabels: Record<string, string> = {
        Participation: '出场', Shooting: '投篮', Playmaking: '组织',
        Rebounding: '篮板', Defense: '防守', 'Scoring & Impact': '得分',
        'Advanced Defense': '防守累计', Milestones: '里程碑', Awards: '年度阵容与冠军',
        'Awards & Honors': '年度奖项', 'Career Awards': '生涯荣誉字段',
        'Career High Stats': '生涯单场最高', 'Season High Stats': '本赛季单场最高',
    };
    const groupOrder = [
        'Participation', 'Scoring & Impact', 'Shooting', 'Rebounding', 'Playmaking', 'Defense',
        'Milestones', 'Advanced Defense', 'Career Awards', 'Awards', 'Awards & Honors',
        'Career High Stats', 'Season High Stats',
    ];

    function renderField(cell: FieldCell) {
        const name = statName(cell);
        const perGame = mode === 'average' && view === 'season' && perGameNames.has(name);
        const value = statValue(cell, draft);
        const mean = average(value, games);
        const id = `stat-${index}-${encodeURIComponent(cell.key)}`;
        return <div className={`field-row${draft[cell.key] !== undefined ? ' field-row-dirty' : ''}`}
            key={cell.key} data-row-key={cell.key}>
            <label className="field-label" htmlFor={id}>
                {perGame ? `场均${cell.label}` : cell.label}
                {!cell.writable && <span className="field-readonly">只读</span>}
            </label>
            <div className="field-control">
                {perGame && !editing ? <span>{mean === null ? '—' : mean.toFixed(2)} <small className="muted">累计 {value}</small></span>
                : perGame ? <div className="stat-average-control">
                    <InputNumber id={id} aria-label={`场均${cell.label}`} size="small" min={0}
                        max={games > 0 && cell.max !== undefined ? cell.max / games : undefined}
                        precision={2} value={mean === null ? null : Number(mean.toFixed(2))}
                        disabled={busy || !reads.editReady || games <= 0 || !cell.writable}
                        onChange={(next) => {
                            if (next !== null) {
                                setDraft(stageField(draft, cell, totalFromAverage(next, games)));
                            }
                        }} />
                    <span>累计 {value}</span>
                </div> : <FieldInput cell={cell} draft={draft} setDraft={setDraft}
                    disabled={busy || editing && !reads.editReady} id={id} />}
            </div>
        </div>;
    }

    // Keep the statistics snapshot and handlers together when rendering actions in the player header.
    const actions = <>
        <Button size="small" aria-label="刷新球员统计" disabled={busy} onClick={() => void refresh()}>重新读取统计</Button>
        <Button size="small" disabled={!editing || !reads.editReady || busy || dirty > 0 || otherDirty > 0} onClick={() => void undo()}>撤销球员写入</Button>
        <Button size="small" aria-label="应用统计修改"
            disabled={!editing || !reads.editReady || busy || dirty === 0 || !data?.readToken} type="primary" onClick={() => void apply()}>
            应用统计{dirty > 0 ? ` (${dirty})` : ''}
        </Button>
        {onClose && <Button size="small" type="text" onClick={onClose} disabled={busy} aria-label="关闭">✕</Button>}
    </>;

    if (!active) return null;
    return <div className="statistics-editor">
        {toolbarTarget && createPortal(actions, toolbarTarget)}
        <div className="statistics-toolbar">
            <Select aria-label="统计赛季" value={selected || undefined} disabled={busy}
                placeholder="没有赛季记录" style={{ minWidth: 210, maxWidth: '100%' }}
                options={data?.seasons.map((season) => {
                    const year = season.fields.find((field) => statName(field) === 'COMPACTYEAR')?.value;
                    return { value: season.selector, label: `${compactSeason(year)}${
                        typeof year === 'number' && year > 0 && 1900 + year === seasonEndYear ? ' · 本季' : ''}` };
                })}
                onChange={setSelected} />
            {toolbarTarget === undefined && actions}
            {dirty > 0 && <Button size="small" disabled={busy} onClick={() => setDraft({})}>放弃草稿</Button>}
        </div>
        {error && <Alert type="error" message={error} showIcon />}
        {data && data.warnings.length > 0 && <Alert type="warning" showIcon
            message={`${data.warnings.length} 项统计不可用`}
            description={data.warnings.join('；')} />}
        {data && <PendingChanges fields={[...data.direct, ...data.seasons.flatMap((season) => season.fields)].map(statisticCell)}
            draft={draft} label="统计待应用修改" disabled={busy}
            onReset={(key) => setDraft((previous) => { const next = { ...previous }; delete next[key]; return next; })}
            onResetAll={() => setDraft({})} onJump={(cell) => {
                const owner = data.seasons.find((season) => season.fields.some((field) => field.key === cell.key));
                if (owner) setSelected(owner.selector);
                setView(cell.section === 'Stats' ? 'high'
                    : ['Awards', 'Awards & Honors'].includes(cell.group) || cell.section === 'Awards' ? 'awards' : 'season');
            }} />}
        {!data ? <Spin spinning={busy}><Empty description={busy ? '读取统计中' : '没有统计数据'} /></Spin> : <>
            <Tabs size="small" activeKey={view} onChange={setView} items={[
                { key: 'season', label: '赛季累计与场均' },
                { key: 'high', label: '单场最高' },
                { key: 'awards', label: '奖项与累计荣誉' },
            ]} />
            {view === 'season' && season && <>
                <div className="statistics-summary">
                    <Segmented size="small" value={mode} onChange={(value) => setMode(String(value))}
                        options={[{ label: '累计', value: 'total' }, { label: '场均', value: 'average' }]} />
                    <span>{games} 场</span>
                    <span>场均得分 {average(findValue('POINTS'), games)?.toFixed(2) ?? '--'}</span>
                    <span>场均篮板 {average(findValue('DEFENSEREBOUNDS') + findValue('OFFENSIVEREBOUNDS'), games)?.toFixed(2) ?? '--'}</span>
                    <span>场均助攻 {average(findValue('ASSISTS'), games)?.toFixed(2) ?? '--'}</span>
                    {[
                        ['投篮', 'FIELDGOALSMADE', 'FIELDGOALSATTEMPTED'],
                        ['三分', '3POINTERSMADE', '3POINTERSATTEMPTED'],
                        ['罚球', 'FREETHROWMADE', 'FREETHROWATTEMPTED'],
                    ].map(([label, made, attempts]) => <span key={made}>
                        {label} {findValue(attempts) > 0
                            ? `${(findValue(made) / findValue(attempts) * 100).toFixed(1)}%` : '--'}
                    </span>)}
                </div>
            </>}
            {view === 'awards' && <section className="field-section">
                <h3 className="field-section-title">已载入 {data.seasons.length} 条赛季记录的累计荣誉</h3>
                <div className="award-totals">{cumulativeAwards(data.seasons, draft).map((award) =>
                    <span key={award.label}>{award.label}<strong>{award.count}</strong></span>)}</div>
            </section>}
            <div className="field-sections">
                {[...groups].sort(([a], [b]) => groupOrder.indexOf(a) - groupOrder.indexOf(b))
                    .map(([group, fields]) => <section className="field-section" key={group}>
                    <h3 className="field-section-title">{groupLabels[group] ?? group}</h3>
                    <div className="field-grid">{fields.map(renderField)}</div>
                </section>)}
            </div>
            {fields.length === 0 && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="该球员没有可用的赛季统计记录" />}
        </>}
    </div>;
}
