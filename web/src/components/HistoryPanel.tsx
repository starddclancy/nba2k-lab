import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Alert, App as AntApp, Button, Checkbox, Empty, Input, Segmented, Select, Table, Tag, Typography } from 'antd';
import { ReloadOutlined, SaveOutlined, UndoOutlined } from '@ant-design/icons';
import { call } from '../bridge';
import { listTeamRecords } from '../api';
import { confirmedHistoryItems, filterHistory, historyChanges, isHistoryFieldVisible, seasonLabel } from '../history';
import type { HistoryDetail, HistoryDraft, HistoryItem, HistoryKind } from '../history';
import type { TeamItem } from '../types';
import { FieldInput } from './FieldEditor';
import TeamIdentity from './TeamIdentity';
import { compactSeason } from '../seasonYears';
import { confirmedOptions, displayValue, isConfirmedField, optionLabel } from '../labels';

export default function HistoryPanel({ scope, editable = false }: { scope: 'league' | 'team'; editable?: boolean }) {
    const { message } = AntApp.useApp();
    const [teams, setTeams] = useState<TeamItem[]>([]);
    const [teamIndex, setTeamIndex] = useState<number | null>(null);
    const [kind, setKind] = useState<HistoryKind>(scope === 'league' ? 'league' : 'totals');
    const [items, setItems] = useState<HistoryItem[]>([]);
    const [selected, setSelected] = useState<string | null>(null);
    const [detail, setDetail] = useState<HistoryDetail | null>(null);
    const [readContext, setReadContext] = useState<{ key: string; editable: boolean } | null>(null);
    const [draft, setDraft] = useState<HistoryDraft>({});
    const [shared, setShared] = useState(false);
    const [query, setQuery] = useState('');
    const [category, setCategory] = useState('');
    const [recordGroup, setRecordGroup] = useState('');
    const [year, setYear] = useState<number | null>(null);
    const [listing, setListing] = useState(false);
    const [reading, setReading] = useState(false);
    const [readVersion, setReadVersion] = useState(0);
    const [writing, setWriting] = useState(false);
    const [error, setError] = useState('');
    const listId = useRef(0);
    const readId = useRef(0);
    const busy = listing || reading || writing;
    const records = kind === 'records' || kind === 'teamRecords';
    const editingReady = editable && readContext?.editable === true && readContext.key === selected;

    useEffect(() => {
        if (!editable) {
            setDraft({});
            setShared(false);
        }
    }, [editable]);

    useEffect(() => {
        if (scope !== 'team') {
            return;
        }
        let cancelled = false;
        void listTeamRecords('all').then((rows) => {
            if (!cancelled) {
                setTeams(rows);
                setTeamIndex(rows[0]?.index ?? null);
            }
        }).catch((error: Error) => { if (!cancelled) { setError(error.message); } });
        return () => { cancelled = true; };
    }, [scope]);

    const loadList = useCallback(async (retain = false) => {
        if (scope === 'team' && teamIndex === null) {
            return;
        }
        const request = ++listId.current;
        ++readId.current;
        setListing(true);
        setError('');
        if (!retain) {
            setItems([]);
            setSelected(null);
            setDetail(null);
            setDraft({});
            setShared(false);
            setCategory('');
            setRecordGroup('');
            setYear(null);
            setQuery('');
        }
        try {
            const rows = confirmedHistoryItems(await call<HistoryItem[]>('historyList', { kind, teamIndex }));
            if (request === listId.current) {
                setItems(rows);
                setSelected((previous) => retain && rows.some((row) => row.key === previous)
                    ? previous : rows[0]?.key ?? null);
                if (!retain) {
                    setReadVersion((version) => version + 1);
                }
            }
        } catch (error) {
            if (request === listId.current) {
                setError((error as Error).message);
            }
        } finally {
            if (request === listId.current) {
                setListing(false);
                setReading(false);
            }
        }
    }, [kind, scope, teamIndex]);

    useEffect(() => {
        void loadList();
        return () => { ++listId.current; ++readId.current; };
    }, [loadList]);

    const loadDetail = useCallback(async (key: string) => {
        const request = ++readId.current;
        setReading(true);
        setReadContext(null);
        setDetail(null);
        setDraft({});
        setShared(false);
        setError('');
        try {
            const read = await call<HistoryDetail>('historyRead', { key });
            if (request === readId.current) {
                setDetail(read);
                setReadContext({ key, editable });
            }
        } catch (error) {
            if (request === readId.current) {
                setError((error as Error).message);
            }
        } finally {
            if (request === readId.current) {
                setReading(false);
            }
        }
    }, [editable]);

    useEffect(() => {
        if (selected) {
            void loadDetail(selected);
        } else {
            setReading(false);
        }
        return () => { ++readId.current; };
    }, [selected, readVersion, loadDetail]);

    const visible = useMemo(() => filterHistory(items, query, category, year, recordGroup), [items, query, category, year, recordGroup]);
    useEffect(() => {
        if (!listing && !writing && !visible.some((item) => item.key === selected)) {
            setSelected(visible[0]?.key ?? null);
            setDetail(null);
            setDraft({});
            setShared(false);
        }
    }, [visible, selected, listing, writing]);
    const fields = detail?.fields.filter((field) => isHistoryFieldVisible(field) && isConfirmedField(field)) ?? [];
    const changes = historyChanges(detail ? { ...detail, fields } : null, draft, editable);
    const count = Object.keys(changes).length;
    const current = items.find((item) => item.key === selected);
    const chosenTeam = teams.find((team) => team.index === teamIndex);
    const groups = [...new Set(items.flatMap((item) => item.recordGroup ? [item.recordGroup] : []))];
    const categories = [...new Set(items.filter((item) => !recordGroup || item.recordGroup === recordGroup).map((item) => item.category))];
    const years = [...new Set(items.flatMap((item) => item.year === null ? [] : [item.year]))].sort((a, b) => b - a);

    const transact = async (undo: boolean) => {
        if (!editingReady || !detail || busy || detail.key !== selected) {
            return;
        }
        const key = detail.key;
        setWriting(true);
        try {
            const result = await call<{ patches?: number; warning?: string }>(undo ? 'historyUndo' : 'historyApply', undo
                ? { key }
                : { key, readToken: detail.readToken, changes, acknowledgeShared: shared });
            message.success(undo ? '已撤销历史修改' : result.patches === 0 ? '数值相同，未写入' : '历史记录已更新');
            if (result.warning) {
                message.warning(result.warning);
            }
            await loadList(true);
            await loadDetail(key);
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setWriting(false);
        }
    };

    return <div className="history-workspace">
        <div className="history-toolbar">
            {scope === 'team' && <>
                <Select aria-label="历史所属球队" className="history-team-select" showSearch
                    optionFilterProp="search" disabled={busy} value={teamIndex}
                    onChange={setTeamIndex} options={teams.map((team) => ({
                        value: team.index, search: `${team.label} ${team.index}`,
                        label: <TeamIdentity {...team} size={24} text={`#${team.index} ${team.label}`} />,
                    }))} />
                <Segmented value={kind} disabled={busy} onChange={(value) => setKind(value as HistoryKind)}
                    options={[{ value: 'totals', label: '累计战绩与荣誉' },
                        { value: 'season', label: '逐赛季统计' }, { value: 'franchise', label: '球队沿革' },
                        { value: 'jersey', label: '退役球衣' }, { value: 'teamRecords', label: '队史纪录' }]} />
            </>}
            {scope === 'league' && <Segmented value={kind} disabled={busy}
                onChange={(value) => setKind(value as HistoryKind)}
                options={[{ value: 'league', label: '荣誉与冠军' }, { value: 'records', label: 'NBA 纪录' }]} />}
            <Button icon={<ReloadOutlined />} title="刷新历史记录" aria-label="刷新历史记录"
                disabled={busy} onClick={() => void loadList()} />
            <Tag>{items.length} 条记录</Tag>
        </div>
        <div className="history-filters">
            {groups.length > 0 && <Select aria-label="纪录范围" value={recordGroup || undefined}
                allowClear placeholder="全部纪录范围" onChange={(value) => { setRecordGroup(value ?? ''); setCategory(''); }}
                options={groups.map((value) => ({ value, label: value }))} />}
            {categories.length > 1 && <Select aria-label="历史分类" value={category || undefined}
                allowClear placeholder="全部分类" onChange={(value) => setCategory(value ?? '')}
                options={categories.map((value) => ({ value, label: value }))} />}
            {years.length > 0 && <Select aria-label="历史年份" value={year} allowClear placeholder="全部年份"
                onChange={(value) => setYear(value ?? null)} options={years.map((value) => ({
                    value, label: kind === 'league' ? seasonLabel(value)
                        : kind === 'season' ? compactSeason(value - 1900) : String(value),
                }))} />}
            <Input.Search aria-label="搜索历史" placeholder="球员、球队、记录" allowClear
                value={query} onChange={(event) => setQuery(event.target.value)} />
        </div>
        {error && <Alert type="error" message={error} showIcon />}
        <div className="history-body">
            <div className="history-list">
                <Table<HistoryItem> size="small" rowKey="key" loading={listing} dataSource={visible}
                    scroll={{ x: scope === 'league' ? 640 : 380, y: 'calc(100vh - 330px)' }}
                    pagination={{ defaultPageSize: 30, showSizeChanger: true, pageSizeOptions: [30, 60, 100],
                        showTotal: (total) => `${total} 条` }}
                    rowClassName={(row) => row.key === selected ? 'history-row-selected' : ''}
                    onRow={(row) => ({ onClick: () => { if (!busy) { setSelected(row.key); } } })}
                    locale={{ emptyText: kind === 'franchise' ? '当前球队未找到已关联沿革记录' : kind === 'jersey' ? '这支球队没有退役球衣' : '当前存档没有对应记录' }}
                    columns={[
                        ...(scope === 'league' ? [
                            { title: records ? '年份' : '赛季', key: 'year', width: 86, render: (_: unknown, row: HistoryItem) => records ? row.year ?? '-' : seasonLabel(row.year) },
                            { title: '类别', dataIndex: 'category', width: 140 },
                        ] : [{ title: '类别', dataIndex: 'category', width: 100 }]),
                        ...(records ? [
                            { title: '排名', dataIndex: 'rank', width: 60, render: (rank: number | null) => rank ?? '-' },
                        ] : []),
                        { title: records ? '纪录保持者' : scope === 'league' ? '获奖者 / 球队' : '记录', dataIndex: 'label', width: 150 },
                        ...(scope === 'league' ? [{ title: '球队', dataIndex: 'team', width: 160,
                            render: (_: unknown, row: HistoryItem) => row.team
                                ? <TeamIdentity label={row.team} historicYearRaw={row.year === null ? null : row.year + (records ? 0 : 1) - 1900} size={24} />
                                : '-',
                        }] : []),
                        { title: '数据', dataIndex: 'summary', width: 105 },
                    ]} />
            </div>
            <div className="history-editor" aria-busy={reading}>
                {detail && current ? <>
                    <div className="history-editor-heading">
                        <div>
                            <Typography.Title level={5}>{current.category}</Typography.Title>
                            {kind === 'franchise' ? <TeamIdentity label={current.label} size={30}
                                logoReference={String(detail.fields.find((field) => field.key === 'FILENAME')?.value ?? '')}
                                historicYearRaw={current.year === null ? null : current.year + 1 - 1900} />
                                : chosenTeam && <TeamIdentity {...chosenTeam} linkIndex={chosenTeam.index} size={30} />}
                            <Typography.Text type="secondary">
                                {current.year !== null && kind === 'league' ? `${seasonLabel(current.year)} · ` : ''}
                                {detail.label}
                            </Typography.Text>
                        </div>
                        {editable && <div className="history-actions">
                            <Button size="small" icon={<UndoOutlined />} disabled={busy || detail.undoKey !== detail.key}
                                onClick={() => void transact(true)}>撤销</Button>
                            <Button size="small" type="primary" icon={<SaveOutlined />} loading={writing}
                                disabled={!editingReady || busy || count === 0 || detail.sharedTeams.length > 0 && !shared}
                                onClick={() => void transact(false)}>应用{count > 0 ? ` (${count})` : ''}</Button>
                        </div>}
                    </div>
                    {detail.sharedTeams.length > 0 && <div className="history-sharing">
                        <Alert type="warning" showIcon message={`此统计还被以下球队记录共用：${detail.sharedTeams.join('、')}`} />
                        {editable && <Checkbox checked={shared} disabled={busy} onChange={(event) => setShared(event.target.checked)}>
                            确认同时影响上述球队记录
                        </Checkbox>}
                    </div>}
                    <div className="history-field-grid">
                        {fields.map((field) => <div className="history-field" key={field.key}>
                            <label>{field.key === 'JERSEYNUMBER' ? '球衣号码' : field.label}</label>
                            {!editable ? <Typography.Text>{displayValue(field)}</Typography.Text>
                                : field.kind === 'choice' ? <Select size="small" showSearch optionFilterProp="label"
                                aria-label={field.label} disabled={!editingReady || busy || !field.writable}
                                value={String(draft[field.key] ?? field.value)}
                                options={confirmedOptions(field).filter((value) => value !== '保留现有关联')
                                    .map((value) => ({ value, label: optionLabel(value, field) }))}
                                onChange={(value) => setDraft({ ...draft, [field.key]: value })} />
                                : <FieldInput cell={field} draft={draft} setDraft={setDraft}
                                    disabled={!editingReady || busy} editable={editable} />}
                        </div>)}
                    </div>
                </> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={reading ? '读取中' : '选择历史记录'} />}
            </div>
        </div>
    </div>;
}
