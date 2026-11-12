import { useCallback, useEffect, useMemo, useState } from 'react';
import {
    App as AntApp,
    Button,
    Input,
    Segmented,
    Space,
    Switch,
    Table,
    Tabs,
    Tag,
    Typography,
} from 'antd';
import {
    applyTeamRecord,
    listTeamRecords,
    readDraftPicks,
    readTeamRecord,
    readTeamRecordDisplay,
    peekTeamRecord,
    readTeams,
    undoTeamRecord,
} from '../api';
import type { DraftPickRow, TeamDetail, TeamField, TeamItem, TeamRow } from '../types';
import { accentOf, teamColor, teamOverview } from '../teamOverview';
import { pickLabel } from '../draftPicks';
import { moneyShort } from '../tradeView';
import { fieldDescription, fieldGroupLabel, fieldLabel, fieldSectionDescription, isConfirmedField, isRoutineTeamField, sectionLabel, teamLabel } from '../labels';
import { FieldInput } from './FieldEditor';
import TeamIdentity from './TeamIdentity';
import TeamRoster from './TeamRoster';
import { useWorkspace } from '../workspaceContext';
import { entityHref } from '../navigation';
import { useEditing } from '../editingContext';
import { useDisplayRead } from '../useDisplayRead';
import EntityLink from './EntityLink';

type Draft = Record<string, number | string | boolean>;

/**
 * Team records use the same field types as players. Edits are staged and only written when the
 * user applies them; every write is journaled and can be undone.
 */
export default function TeamPanel() {
    const { message } = AntApp.useApp();
    const { editing, setEditing } = useEditing();
    const { route } = useWorkspace();
    const [resourceScope, setResourceScope] = useState(false);
    const [teamQuery, setTeamQuery] = useState('');
    const [view, setView] = useState<'roster' | 'fields'>('roster');
    const [teams, setTeams] = useState<TeamItem[]>([]);
    const [selected, setSelected] = useState<number | null>(null);
    const [draft, setDraft] = useState<Draft>({});
    const [filter, setFilter] = useState('');
    const [working, setBusy] = useState(false);
    const [diagnostics, setDiagnostics] = useState(false);
    const [slots, setSlots] = useState<TeamRow[]>([]);
    const [picks, setPicks] = useState<DraftPickRow[]>([]);
    const reader = useMemo(() => ({
        peek: () => selected === null ? undefined : peekTeamRecord(selected),
        read: (force: boolean) => readTeamRecordDisplay(selected!, { force }),
        fresh: () => readTeamRecord(selected!),
    }), [selected]);
    const reads = useDisplayRead<TeamDetail>({ identity: `team:${selected}`, reader, editing,
        dirty: Object.keys(draft).length > 0, enabled: selected !== null });
    const detail = selected === null ? null : reads.data;
    const busy = working || reads.busy;
    const reload = async () => { setDraft({}); await reads.reload(); };

    const load = useCallback(async () => {
        setBusy(true);
        try {
            const rows = await listTeamRecords(resourceScope ? 'all' : 'league');
            setTeams(rows);
            // Overview extras are optional; the field editor works without them.
            const [nextSlots, nextPicks] = await Promise.allSettled([readTeams(), readDraftPicks()]);
            setSlots(nextSlots.status === 'fulfilled' ? nextSlots.value : []);
            setPicks(nextPicks.status === 'fulfilled' ? nextPicks.value : []);
            if (rows.length > 0) {
                setSelected((current) => rows.some((row) => row.index === current) ? current : rows[0].index);
            }
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    }, [message, resourceScope]);

    useEffect(() => {
        void load();
    }, [load]);

    useEffect(() => {
        if (route?.page !== 'teams' || route.index === undefined || teams.length === 0) return;
        if (!teams.some((team) => team.index === route.index)) {
            if (!resourceScope) setResourceScope(true);
            return;
        }
        setSelected(route.index);
    }, [route, teams, resourceScope]);

    useEffect(() => {
        setDraft({});
        setEditing(false);
    }, [selected, setEditing]);
    useEffect(() => { setDraft({}); }, [editing]);

    const changed = useMemo(() => {
        const delta: Draft = {};
        if (!detail) {
            return delta;
        }
        for (const section of detail.sections) {
            for (const field of section.fields) {
                if (!field.writable || field.value === null) {
                    continue;
                }
                const next = draft[field.key];
                if (next !== undefined && next !== field.value) {
                    delta[field.key] = next;
                }
            }
        }
        return delta;
    }, [detail, draft]);

    const changedCount = Object.keys(changed).length;

    const apply = async () => {
        if (!editing || !reads.editReady || !detail || detail.index !== selected || changedCount === 0 || busy) {
            return;
        }
        setBusy(true);
        try {
            const result = await applyTeamRecord(detail.index, changed);
            message.success(`已应用 ${changedCount} 项球队修改`);
            if (result.note) {
                message.warning(result.note);
            }
            await reload();
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    };

    const undo = async () => {
        if (!editing || !reads.editReady || busy) return;
        setBusy(true);
        try {
            const result = await undoTeamRecord();
            message.success(`已撤销 ${result.restored} 处字节（${result.team}）`);
            if (selected !== null) {
                await reload();
            }
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    };

    const sections = useMemo(() => {
        if (!detail) {
            return [];
        }
        const needle = filter.trim().toLowerCase();
        return detail.sections
            .map((section) => ({
                ...section,
                fields: section.fields.filter((field) =>
                    isConfirmedField(field) && isRoutineTeamField(field)
                    && (!['Team Players', 'Team Lineups'].includes(field.section)
                        || Number(field.value) === 0 || field.playerIndex !== undefined && !!field.playerName)
                    && (!needle || `${fieldLabel(field)} ${sectionLabel(field.group)} ${field.label}`
                        .toLowerCase().includes(needle))),
            }))
            .filter((section) => section.fields.length > 0);
    }, [detail, filter, diagnostics]);

    const overview = useMemo(() => {
        if (!detail) {
            return null;
        }
        const fields: Record<string, unknown> = {};
        for (const section of detail.sections) {
            for (const field of section.fields) {
                fields[field.key] = field.value;
            }
        }
        // The live bridge sends colors as raw numbers in value and as "07 24 41 ff" text in display.
        const colorText = (name: string) => {
            const cell = detail.sections.flatMap((section) => section.fields).find((field) => field.key === `Vitals/Color/${name}`);
            return cell ? teamColor(cell.display) ?? teamColor(cell.value) : null;
        };
        const primary = colorText('TEAMCOLOR1');
        const secondary = colorText('TEAMCOLOR2');
        return { ...teamOverview(detail.index, slots.find((row) => row.index === detail.index), fields, picks),
            primary, secondary, accent: accentOf(primary, secondary) };
    }, [detail, slots, picks]);

    const renderEditor = (field: TeamField) => {
        if (['Team Players', 'Team Lineups'].includes(field.section)) {
            return field.playerIndex !== undefined && field.playerName
                ? <EntityLink page="players" index={field.playerIndex}>{field.playerName}</EntityLink>
                : <span className="muted">空位</span>;
        }
        return <FieldInput cell={field} draft={draft} setDraft={setDraft}
            disabled={busy || editing && !reads.editReady} />;
    };

    const columns = [
        { title: '项目', width: 260, render: (_: unknown, field: TeamField) =>
            <span className="field-label" title={fieldDescription(field)}>{fieldLabel(field)}</span> },
        {
            title: '值',
            dataIndex: 'display',
            render: (_: unknown, field: TeamField) => renderEditor(field),
        },
        {
            title: '分组',
            dataIndex: 'group',
            width: 140,
            render: (_: string, field: TeamField) => <Tag>{fieldGroupLabel(field)}</Tag>,
        },
    ];

    return (
        <div className="team-page" style={overview?.accent ? { '--team': overview.accent, '--team2': overview.secondary ?? overview.accent } as React.CSSProperties : undefined}>
            <div className="team-strip" role="region" aria-label="球队">
                <div className="team-strip-tools">
                    <Segmented size="small" value={resourceScope ? 'all' : 'league'}
                        onChange={(value) => {
                            window.location.hash = '/teams';
                            setSelected(null);
                            setResourceScope(value === 'all');
                        }}
                        options={[{ value: 'league', label: '当前联盟' }, { value: 'all', label: '历史资源' }]} />
                    <Input size="small" allowClear placeholder={`搜索 ${teams.length} 支球队`} value={teamQuery}
                        onChange={(event) => setTeamQuery(event.target.value)} />
                </div>
                {teams.filter((row) => `${row.label} ${teamLabel(row.label)}`.toLowerCase().includes(teamQuery.trim().toLowerCase()))
                    .map((row) => <button key={row.key} type="button" aria-pressed={row.index === selected}
                    className={row.index === selected ? 'active' : ''} disabled={working}
                    onClick={() => { window.location.hash = entityHref('teams', row.index); setSelected(row.index); }} title={row.label}>
                    <TeamIdentity {...row} size={30} text="" />
                    <span>{teamLabel(row.label)}</span>
                </button>)}
            </div>
            <div className="detail-host" style={{ flex: 1, minWidth: 0 }}>
                {!detail ? (
                    <div className="empty-hint">{busy ? '读取中…' : '从左侧选择一支球队'}</div>
                ) : (
                    <div>
                        <div className="field-toolbar">
                            <Typography.Text strong><TeamIdentity {...detail} size={32} /></Typography.Text>
                            {diagnostics && <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                                #{detail.index} · {detail.address}
                            </Typography.Text>}
                            <span style={{ flex: 1 }} />
                            {reads.error && <Tag color="orange" title={reads.error}>更新失败</Tag>}
                            <Input
                                allowClear
                                size="small"
                                placeholder="搜索球队资料"
                                style={{ width: 160 }}
                                value={filter}
                                onChange={(event) => { setFilter(event.target.value); setView('fields'); }}
                            />
                            <Space>
                                <Button size="small" aria-label="刷新球队资料" onClick={() => selected !== null && void reload()} loading={busy}>
                                    重新读取
                                </Button>
                                <Button size="small" onClick={undo} loading={busy} disabled={!editing || !reads.editReady}>
                                    撤销
                                </Button>
                                <Button
                                    size="small"
                                    type="primary"
                                    onClick={apply}
                                    loading={busy}
                                    disabled={!editing || !reads.editReady || changedCount === 0}
                                >
                                    应用{changedCount > 0 ? ` (${changedCount})` : ''}
                                </Button>
                            </Space>
                            <Space size={6}>
                                <Switch size="small" checked={diagnostics} onChange={setDiagnostics} />
                                <Typography.Text type="secondary">诊断资料</Typography.Text>
                            </Space>
                        </div>
                        {overview && <div className="team-overview">
                            <div className="stat-tile"><span>常规名单</span><b className="stat-value">{overview.rosterUsed ?? '—'}<small> / {overview.rosterCapacity ?? '—'}</small></b></div>
                            <div className="stat-tile"><span>薪资总额</span><b className="stat-value">{moneyShort(overview.payroll)}</b>
                                {overview.payroll !== null && overview.budget ? <div className="category-bar-track"><div className="category-bar-fill"
                                    style={{ width: `${Math.min(100, (overview.payroll / overview.budget) * 100)}%`,
                                        '--bar': overview.payroll > overview.budget ? 'var(--danger)' : 'var(--primary)' } as React.CSSProperties} /></div> : null}
                                <small className="muted">预算 {moneyShort(overview.budget)}</small></div>
                            <div className="stat-tile"><span>持有选秀权</span><b className="stat-value">{overview.picksOwned}</b>
                                <small className="muted">首轮 {overview.firstRounders} · 换入 {overview.acquired} · 送出 {overview.traded}</small></div>
                            <div className="stat-tile wide"><span>最近的选秀权</span>
                                <div className="team-next-picks">{overview.nextPicks.length === 0 ? <small className="muted">没有可用选秀权</small>
                                    : overview.nextPicks.map((pick) => <small key={pick.index}>{pickLabel(pick)}</small>)}</div></div>
                        </div>}
                        <div className="team-view-tabs"><Segmented size="small" value={view}
                            onChange={(value) => setView(value as typeof view)}
                            options={[{ value: 'roster', label: '球队阵容' }, { value: 'fields', label: `球队资料${changedCount ? ` (${changedCount})` : ''}` }]} /></div>
                        {view === 'roster' ? <TeamRoster teamIndex={detail.index} teamLabel={detail.label} accent={overview?.accent}
                            scope={resourceScope ? 'all' : 'league'} />
                        : <Tabs
                            size="small"
                            style={{ padding: '0 12px' }}
                            items={sections.map((section) => ({
                                key: section.name,
                                label: `${sectionLabel(section.name)} (${section.fields.length})`,
                                children: <>{fieldSectionDescription(section.name) && <p className="field-description">
                                    {fieldSectionDescription(section.name)}</p>}{diagnostics ? (
                                    <Table<TeamField>
                                        size="small"
                                        rowKey="key"
                                        dataSource={section.fields}
                                        columns={columns}
                                        pagination={false}
                                        scroll={{ x: 480 }}
                                        expandedRowRender={(field) => <div className="diagnostic-data">
                                            原始项目：{field.label} · 内部字段：{field.key}
                                        </div>}
                                        rowClassName={(field) => changed[field.key] !== undefined ? 'gs-row-dirty' : ''}
                                    />
                                ) : (
                                    <div className="team-field-grid">
                                        {section.fields.map((field) => <label key={field.key}
                                            className={`team-field${changed[field.key] !== undefined ? ' dirty' : ''}`}>
                                            <span title={fieldDescription(field) ?? fieldGroupLabel(field)}>{fieldLabel(field)}</span>
                                            {renderEditor(field)}
                                        </label>)}
                                    </div>
                                )}</>,
                            }))}
                        />}
                    </div>
                )}
            </div>
        </div>
    );
}
