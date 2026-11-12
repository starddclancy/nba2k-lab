import { useCallback, useEffect, useMemo, useState } from 'react';
import { App as AntApp, Button, Drawer, Input, InputNumber, Segmented, Select, Table } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { ReloadOutlined } from '@ant-design/icons';
import { listPlayers, listTeamRecords, readPlayer, readPlayerRatings, readProspects, readRookies } from '../api';
import type { PlayerDetail, TeamItem } from '../types';
import { teamLabel } from '../labels';
import { borrowPhotos } from '../avatars';
import { draftRows, selectSameSource, sourceRows, batchSelection } from '../playerSources';
import type { BrowserPlayer } from '../playerSources';
import { poolOf, statsByIndex } from '../scouting';
import type { RatingTable } from '../scouting';
import { categories, categoryLabels, playerTitle } from '../archetype';
import { moneyShort } from '../tradeView';
import Avatar from './Avatar';
import FieldEditor from './FieldEditor';
import BatchModal from './BatchModal';
import TeamIdentity from './TeamIdentity';
import { ratingColor } from './PlayerProfile';
import InsightPanel from './InsightPanel';
import EntityLink from './EntityLink';
import { useWorkspace } from '../workspaceContext';
import { entityHref } from '../navigation';
import { useEditing } from '../editingContext';

/** Which players the table lists. Draft players are one filter value, not a separate page. */
type Scope = 'all' | 'team' | 'freeAgent' | 'prospects' | 'draft' | 'resources';

const scopeOptions = [
    { value: 'all', label: '全部' },
    { value: 'team', label: '现役' },
    { value: 'freeAgent', label: '自由球员' },
    { value: 'prospects', label: '待选秀' },
    { value: 'draft', label: '新秀名单' },
    { value: 'resources', label: '历史资源' },
];

function inScope(row: BrowserPlayer, scope: Scope): boolean {
    if (scope === 'resources') return row.source === 'roster';
    if (scope === 'prospects' || scope === 'draft') {
        return row.source === scope;
    }
    if (row.source !== 'roster') {
        return scope === 'all';
    }
    return scope === 'all' || row.category === scope;
}

/** Rating cell: green when elite (85+), dimmed below 60. */
const rating = (value: number | null | undefined) => value === null || value === undefined
    ? <span className="rating-cell lo">—</span>
    : <span className={`rating-cell${value >= 85 ? ' hi' : value < 60 ? ' lo' : ''}`}>{value}</span>;

const numeric = (left: number | null | undefined, right: number | null | undefined) =>
    (left ?? -Infinity) - (right ?? -Infinity);

/**
 * Player workspace: one full-width sortable table over roster, free agents and draft classes.
 * Clicking a row opens the editor in a side drawer; nothing is written until the editor applies.
 */
export default function PlayersPage({ connected }: { connected: boolean }) {
    const { message } = AntApp.useApp();
    const { route } = useWorkspace();
    const { requestLeave } = useEditing();
    const [rows, setRows] = useState<BrowserPlayer[]>([]);
    const [teams, setTeams] = useState<TeamItem[]>([]);
    const [ratings, setRatings] = useState<RatingTable | null>(null);
    const [ratingsError, setRatingsError] = useState('');
    const [busy, setBusy] = useState(false);
    const [scope, setScope] = useState<Scope>('team');
    const [team, setTeam] = useState<number | null>(null);
    const [position, setPosition] = useState<string | null>(null);
    const [query, setQuery] = useState('');
    const [overallMin, setOverallMin] = useState<number | null>(null);
    const [year, setYear] = useState<number | null>(null);
    const [selected, setSelected] = useState<string | null>(null);
    const [editing, setEditing] = useState(false);
    const [picked, setPicked] = useState<string[]>([]);
    const [batchOpen, setBatchOpen] = useState(false);
    const resources = scope === 'resources';

    const load = useCallback(async () => {
        setBusy(true);
        try {
            const [roster, teamRows, draft] = await Promise.all([
                listPlayers('', null, resources ? 'all' : 'league'), listTeamRecords(),
                Promise.allSettled([readProspects(), readRookies()]).then(draftRows),
            ]);
            setRows([...sourceRows(roster, 'roster'), ...draft.items]);
            setTeams(teamRows);
            // Ratings feed the extra columns and percentiles; the table still works without them.
            try {
                setRatings(await readPlayerRatings());
                setRatingsError('');
            } catch (error) {
                setRatings(null);
                setRatingsError((error as Error).message);
            }
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    }, [message, resources]);

    useEffect(() => {
        if (connected) {
            void load();
        }
    }, [connected, load]);

    useEffect(() => {
        if (route?.page !== 'players') return;
        if (route.index === undefined) {
            setEditing(false);
            return;
        }
        if (busy || rows.length === 0) return;
        const key = `${route.source ?? 'roster'}:${route.index}`;
        const row = rows.find((item) => item.key === key);
        if (!row) {
            if (route.source === 'roster' && !resources) setScope('resources');
            return;
        }
        if (!resources) setScope(row.source === 'roster' ? 'all' : row.source);
        setSelected(key);
        setEditing(true);
    }, [route, rows, busy, resources]);

    const stats = useMemo(() => statsByIndex(ratings), [ratings]);
    const borrowed = useMemo(() => borrowPhotos(rows), [rows]);
    const pool = useMemo(() => poolOf(rows.filter((row) => row.source === 'roster'), stats), [rows, stats]);
    const years = useMemo(() => [...new Set(rows.flatMap((row) => (row.draftYear === undefined ? [] : [row.draftYear])))]
        .sort((a, b) => a - b).map((value) => ({ value, label: `${value} 届` })), [rows]);

    const visible = useMemo(() => {
        const needle = query.trim().toLowerCase();
        return rows.filter((row) => inScope(row, scope)
            && (team === null || (row.source === 'roster' && row.teamIndices?.includes(team)))
            && (!position || row.summary?.position === position || row.summary?.secondaryPosition === position)
            && (overallMin === null || (row.summary?.overall ?? row.overall ?? 0) >= overallMin)
            && (year === null || row.draftYear === year)
            && (!needle || row.name.toLowerCase().includes(needle)));
    }, [rows, scope, team, position, overallMin, year, query]);

    const stat = (row: BrowserPlayer) => (row.source === 'roster' ? stats.get(row.index) : undefined);
    const columns: ColumnsType<BrowserPlayer> = [
        {
            title: '球员', key: 'name', fixed: 'left', width: 230, sorter: (a, b) => a.name.localeCompare(b.name),
            render: (_, row) => <div className="player-name-cell">
                <Avatar name={row.name} team={row.team} photoId={row.photoId} borrowed={borrowed.get(row.key)} size={28} />
                <div><strong title={row.name}><EntityLink page="players" index={row.index} source={row.source}>{row.name}</EntityLink>{row.source !== 'roster'
                    && <span className="source-badge">{row.source === 'prospects' ? '待选秀' : '新秀'}{row.draftYear ? ` ${row.draftYear}` : ''}</span>}</strong>
                <small>{row.source === 'roster' ? <EntityLink page="teams" index={row.teamIndices?.[0]}>{teamLabel(row.team) || (row.category === 'freeAgent' ? '自由球员' : '未在队')}</EntityLink> : ''}</small></div>
            </div>,
        },
        {
            title: 'OVR', key: 'overall', width: 62, align: 'center', defaultSortOrder: 'descend',
            sorter: (a, b) => numeric(a.summary?.overall ?? a.overall, b.summary?.overall ?? b.overall),
            render: (_, row) => {
                const value = row.summary?.overall ?? row.overall ?? null;
                return value === null ? <span className="muted">—</span>
                    : <span className="overall-chip" style={{ '--ring': ratingColor(value) } as React.CSSProperties}>{value}</span>;
            },
        },
        {
            title: '定位', key: 'archetype', width: 150, ellipsis: true,
            render: (_, row) => { const title = playerTitle(row.summary, stat(row)?.archetype); return <span className="archetype-cell" title={title?.detail}>{title?.name ?? '—'}</span>; },
        },
        {
            title: '位置', key: 'position', width: 66,
            render: (_, row) => <span className="num">{[row.summary?.position, row.summary?.secondaryPosition].filter(Boolean).join('/') || '—'}</span>,
        },
        { title: '年龄', key: 'age', width: 54, align: 'right', sorter: (a, b) => numeric(a.summary?.age, b.summary?.age),
            render: (_, row) => <span className="num">{row.summary?.age ?? '—'}</span> },
        { title: '身高', key: 'height', width: 62, align: 'right', sorter: (a, b) => numeric(a.summary?.heightCm, b.summary?.heightCm),
            render: (_, row) => <span className="num">{row.summary?.heightCm ? Math.round(row.summary.heightCm) : '—'}</span> },
        ...categories.map((category) => ({
            title: categoryLabels[category], key: category, width: 54, align: 'right' as const,
            sorter: (a: BrowserPlayer, b: BrowserPlayer) => numeric(stat(a)?.scores?.[category], stat(b)?.scores?.[category]),
            render: (_: unknown, row: BrowserPlayer) => rating(stat(row)?.scores?.[category]),
        })),
        { title: '潜力', key: 'potential', width: 54, align: 'right', sorter: (a, b) => numeric(stat(a)?.potential, stat(b)?.potential),
            render: (_, row) => rating(stat(row)?.potential) },
        { title: '本季薪资', key: 'salary', width: 92, align: 'right', sorter: (a, b) => numeric(stat(a)?.salary, stat(b)?.salary),
            render: (_, row) => <span className="money-cell">{stat(row)?.salary ? moneyShort(stat(row)!.salary) : '—'}</span> },
        { title: '合同', key: 'years', width: 54, align: 'right', sorter: (a, b) => numeric(stat(a)?.yearsLeft, stat(b)?.yearsLeft),
            render: (_, row) => <span className="num">{stat(row)?.yearsLeft != null ? `${stat(row)!.yearsLeft} 年` : '—'}</span> },
    ];

    const selectedRow = rows.find((row) => row.key === selected);
    const closeEditor = () => {
        setEditing(false);
        if (route?.page === 'players' && route.index !== undefined) window.location.hash = '/players';
    };
    const openEditor = (row: BrowserPlayer) => {
        const hash = entityHref('players', row.index, row.source);
        if (window.location.hash === hash) {
            setSelected(row.key);
            setEditing(true);
        } else window.location.hash = hash;
    };
    const batch = batchSelection(rows, picked);
    const loadSelected = useCallback(() => readPlayer(selectedRow!.index, selectedRow!.source),
        // eslint-disable-next-line react-hooks/exhaustive-deps
        [selectedRow?.index, selectedRow?.source]);
    const updateSummary = useCallback((detail: PlayerDetail) => {
        setRows((current) => current.map((row) => row.key === selected
            ? { ...row, name: detail.name, summary: detail.summary } : row));
    }, [selected]);
    const reset = () => {
        setPicked([]);
        setBatchOpen(false);
    };

    // The virtual table needs a pixel height: the window minus shell bars and the filter bar.
    const [tableHeight, setTableHeight] = useState(() => Math.max(240, window.innerHeight - 168));
    useEffect(() => {
        const resize = () => setTableHeight(Math.max(240, window.innerHeight - 168));
        window.addEventListener('resize', resize);
        return () => window.removeEventListener('resize', resize);
    }, []);

    if (!connected) {
        return <div className="empty-hint">连接游戏后读取名单，或使用侧栏中的建模与方案工具</div>;
    }
    return <div className="players-page">
        <div className="players-filterbar">
            <Segmented size="small" value={scope} options={scopeOptions}
                onChange={(value) => {
                    void requestLeave().then((accepted) => {
                        if (!accepted) return;
                        closeEditor();
                        setScope(value as Scope); setTeam(null); setYear(null); reset();
                    });
                }} />
            <Select allowClear showSearch size="small" style={{ width: 210 }} placeholder="全部球队" optionFilterProp="label"
                disabled={scope === 'prospects' || scope === 'draft' || scope === 'freeAgent'} value={team}
                options={teams.map((row) => ({ value: row.index, label: teamLabel(row.label), brand: row }))}
                optionRender={(option) => <TeamIdentity {...option.data.brand} text={option.data.label} size={20} />}
                onChange={(value) => { setTeam(value ?? null); if (value !== undefined && scope !== 'team') setScope('team'); reset(); }} />
            <Select allowClear size="small" style={{ width: 96 }} placeholder="位置" value={position}
                options={['PG', 'SG', 'SF', 'PF', 'C'].map((value) => ({ value, label: value }))}
                onChange={(value) => { setPosition(value ?? null); reset(); }} />
            {(scope === 'prospects' || scope === 'draft' || scope === 'all') && years.length > 0
                && <Select allowClear size="small" style={{ width: 104 }} placeholder="届次" value={year} options={years}
                    onChange={(value) => { setYear(value ?? null); reset(); }} />}
            <InputNumber size="small" style={{ width: 104 }} min={25} max={99} placeholder="总评 ≥" value={overallMin}
                onChange={(value) => { setOverallMin(value); reset(); }} />
            <Input allowClear size="small" style={{ width: 180 }} placeholder="搜索姓名" value={query}
                onChange={(event) => { setQuery(event.target.value); reset(); }} />
            {picked.length > 1 && batch && <Button size="small" type="primary" onClick={() => setBatchOpen(true)}>批量编辑 {picked.length} 人</Button>}
            <span className="count"><b>{visible.length}</b> / {rows.length} 名球员
                {ratingsError && <span title={ratingsError}> · 能力列未读取</span>}</span>
            <Button size="small" icon={<ReloadOutlined />} loading={busy} onClick={() => void load()}>刷新</Button>
        </div>
        <div className="players-body">
        <div className="players-table">
            <Table<BrowserPlayer>
                size="small" rowKey="key" loading={busy} dataSource={visible} columns={columns}
                pagination={false} virtual scroll={{ x: 1290, y: tableHeight }} showSorterTooltip={false}
                rowSelection={{
                    columnWidth: 32, fixed: true, selectedRowKeys: picked,
                    onChange: (keys) => setPicked((current) => selectSameSource(visible, keys.map(String), current)),
                    getCheckboxProps: (row) => ({ disabled: !!batch && row.source !== batch.source }),
                }}
                onRow={(row) => ({ onClick: () => setSelected(row.key), onDoubleClick: () => openEditor(row),
                    style: { cursor: 'pointer' } })}
                rowClassName={(row) => (row.key === selected ? 'player-row-active' : '')}
                locale={{ emptyText: busy ? '正在读取名单' : '没有符合条件的球员' }}
            />
        </div>
        <InsightPanel rows={visible} stats={stats} pool={pool} selected={selectedRow ?? null}
            borrowed={selectedRow ? borrowed.get(selectedRow.key) : null} onEdit={() => { if (selectedRow) openEditor(selectedRow); }} />
        </div>
        <Drawer className="player-drawer" open={!!selectedRow && editing} width="min(1180px, 88vw)" destroyOnClose
            onClose={() => { void requestLeave().then((accepted) => { if (accepted) closeEditor(); }); }}
            closable={false} styles={{ body: { padding: 0 } }}>
            {selectedRow && <FieldEditor key={selectedRow.key} index={selectedRow.index} source={selectedRow.source}
                open={editing}
                load={loadSelected} borrowed={borrowed.get(selectedRow.key)} onRead={updateSummary}
                onClose={closeEditor} pool={pool}
                teamIndex={selectedRow.teamIndices?.length === 1 ? selectedRow.teamIndices[0] : undefined} />}
        </Drawer>
        {batchOpen && batch && <BatchModal indices={batch.indices} source={batch.source}
            onClose={() => setBatchOpen(false)} onApplied={() => void load()} />}
    </div>;
}
