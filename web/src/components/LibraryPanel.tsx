import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Alert, App as AntApp, Button, Empty, Input, Modal, Popconfirm, Segmented, Select, Space, Table, Tag, Tooltip, Typography } from 'antd';
import { DeleteOutlined, ImportOutlined, ReloadOutlined, SaveOutlined, UserSwitchOutlined } from '@ant-design/icons';
import { libraryApply, libraryDelete, libraryImport, libraryList, librarySave, listPlayers, readPlayer, undoSingle } from '../api';
import type { LibraryApplyResult, LibraryEntry, LibraryKind, PlayerRow } from '../types';
import { kindLabel, libraryFilter, libraryPreview, savedAt } from '../library';
import { useEditing } from '../editingContext';

/**
 * Custom player and face library. Saving only reads the game. Applying previews the field
 * changes first and then writes through the journaled player edit, so it can be undone.
 */
export default function LibraryPanel() {
    const { message } = AntApp.useApp();
    const { editing } = useEditing();
    const editingRef = useRef(editing);
    editingRef.current = editing;
    const editSession = useMemo(() => ({}), [editing]);
    const sessionRef = useRef<object | null>(editSession);
    sessionRef.current = editSession;
    const [entries, setEntries] = useState<LibraryEntry[]>([]);
    const [players, setPlayers] = useState<PlayerRow[]>([]);
    const [busy, setBusy] = useState(false);
    const [kind, setKind] = useState<'all' | LibraryKind>('all');
    const [query, setQuery] = useState('');
    const [saveFrom, setSaveFrom] = useState<number | null>(null);
    const [saveKind, setSaveKind] = useState<LibraryKind>('face');
    const [saveName, setSaveName] = useState('');
    const [applying, setApplying] = useState<LibraryEntry | null>(null);
    const [target, setTarget] = useState<number | null>(null);
    const [scope, setScope] = useState<LibraryKind>('face');
    const [preview, setPreview] = useState<LibraryApplyResult | null>(null);
    const [lastApplied, setLastApplied] = useState<{ index: number; name: string } | null>(null);
    const lastAppliedRef = useRef(lastApplied);
    lastAppliedRef.current = lastApplied;
    const previewId = useRef(0);
    useEffect(() => {
        sessionRef.current = editSession;
        ++previewId.current;
        setPreview(null);
        setApplying(null);
        return () => {
            if (sessionRef.current === editSession) sessionRef.current = null;
            ++previewId.current;
        };
    }, [editSession]);

    const run = useCallback(async <T,>(work: () => Promise<T>) => {
        setBusy(true);
        try {
            return await work();
        } catch (error) {
            message.error((error as Error).message);
            return undefined;
        } finally {
            setBusy(false);
        }
    }, [message]);

    const load = useCallback(() => run(async () => {
        const [nextEntries, nextPlayers] = await Promise.all([libraryList(), listPlayers('', null)]);
        setEntries(nextEntries);
        setPlayers(nextPlayers);
    }), [run]);

    useEffect(() => {
        void load();
    }, [load]);

    const playerOptions = useMemo(() => players.map((row) => ({
        value: row.index, label: `${row.name}${row.team ? ` · ${row.team}` : ''}`,
    })), [players]);
    const shown = useMemo(() => libraryFilter(entries, kind, query), [entries, kind, query]);
    const presentation = useMemo(() => preview ? libraryPreview(preview) : null, [preview]);
    const previewReady = !!preview && preview.index === target && preview.entry.id === applying?.id
        && preview.scope === scope && presentation?.blocked === 0;

    const save = () => run(async () => {
        if (saveFrom === null) {
            message.warning('请选择要保存的球员');
            return;
        }
        const row = players.find((item) => item.index === saveFrom);
        const name = saveName.trim() || `${row?.name ?? '球员'}${saveKind === 'face' ? ' 面部' : ''}`;
        const saved = await librarySave(saveFrom, saveKind, name);
        message.success(`已保存「${saved.name}」（${saved.fieldCount} 项）`);
        setSaveName('');
        setEntries(await libraryList());
    });

    const openApply = (entry: LibraryEntry) => {
        if (!editingRef.current || busy) return;
        ++previewId.current;
        setApplying(entry);
        setTarget(null);
        setScope(entry.kind === 'face' ? 'face' : 'player');
        setPreview(null);
    };

    const loadPreview = (index: number, nextScope: LibraryKind) => run(async () => {
        if (!editingRef.current || sessionRef.current !== editSession || !applying) {
            return;
        }
        const request = ++previewId.current;
        setPreview(null);
        const result = await libraryApply(applying.id, index, nextScope, true);
        if (editingRef.current && sessionRef.current === editSession && request === previewId.current) setPreview(result);
    });

    const apply = () => run(async () => {
        if (!editingRef.current || sessionRef.current !== editSession || !applying || target === null || !previewReady || !preview
            || preview.changeCount === 0) {
            return;
        }
        const request = previewId.current;
        const detail = await readPlayer(target);
        if (!editingRef.current || sessionRef.current !== editSession || request !== previewId.current
            || detail.index !== target || !detail.readToken) return;
        const fresh = await libraryApply(applying.id, target, scope, true, detail.readToken);
        if (!editingRef.current || sessionRef.current !== editSession || request !== previewId.current) return;
        if (libraryPreview(fresh).blocked > 0 || JSON.stringify(fresh.changes) !== JSON.stringify(preview.changes)) {
            setPreview(fresh);
            message.warning('条目或目标数据已变化，请重新核对预览');
            return;
        }
        const result = await libraryApply(applying.id, target, scope, false, detail.readToken);
        if (!editingRef.current || sessionRef.current !== editSession || request !== previewId.current) return;
        if (result.applied) {
            message.success(`已把「${applying.name}」应用到 ${result.player}（${result.changeCount} 项），可撤销`);
            setLastApplied({ index: target, name: result.player });
        } else {
            message.info('没有需要修改的字段');
        }
        setApplying(null);
    });

    const undo = () => run(async () => {
        if (!editingRef.current || sessionRef.current !== editSession || !lastApplied) {
            return;
        }
        const record = lastApplied;
        const request = ++previewId.current;
        const detail = await readPlayer(record.index);
        if (!editingRef.current || sessionRef.current !== editSession || request !== previewId.current
            || lastAppliedRef.current !== record || detail.index !== record.index || !detail.readToken) return;
        await undoSingle(record.index);
        if (!editingRef.current || sessionRef.current !== editSession || request !== previewId.current
            || lastAppliedRef.current !== record) return;
        message.success(`已撤销 ${record.name} 的上一次修改`);
        setLastApplied(null);
    });

    return (
        <div className="library-panel library-layout">
            <section className="library-side">
            <div className="insight-title">保存到库</div>
            <Typography.Paragraph type="secondary" className="library-note">
                面部条目保存外观各项（头型、五官、发型、肤色等）和面部模型编号 GRAPHICID，
                应用后目标球员使用同一张脸，照片和身份不变。球员条目保存全部可写字段，
                但不复制身份编号、合同、球队和统计。保存只读取游戏；应用前会列出变化，写入后可撤销。
            </Typography.Paragraph>
            <div className="library-form">
                <Select showSearch optionFilterProp="label" style={{ width: '100%' }} placeholder="从名单选择球员"
                    value={saveFrom} options={playerOptions} disabled={busy} onChange={setSaveFrom} />
                <Segmented block value={saveKind} disabled={busy} onChange={(value) => setSaveKind(value as LibraryKind)}
                    options={[{ value: 'face', label: '保存面部' }, { value: 'player', label: '保存整个球员' }]} />
                <Input placeholder="名称（可选）" maxLength={80} value={saveName}
                    onChange={(event) => setSaveName(event.target.value)} />
                <Button type="primary" block icon={<SaveOutlined />} loading={busy} onClick={() => void save()}>保存到库</Button>
                <Button icon={<ImportOutlined />} disabled={busy} onClick={() => void run(async () => {
                    const imported = await libraryImport();
                    if (!imported.cancelled) {
                        message.success(`已导入「${imported.name}」`);
                        setEntries(await libraryList());
                    }
                })}>导入文件</Button>
                <Button icon={<ReloadOutlined />} disabled={busy} onClick={() => void load()}>刷新</Button>
                {lastApplied && <Button danger disabled={!editing || busy} onClick={() => void undo()}>撤销对 {lastApplied.name} 的应用</Button>}
            </div>
            <div className="insight-kpis">
                <div><span>条目</span><b>{entries.length}</b></div>
                <div><span>面部</span><b>{entries.filter((entry) => entry.kind === 'face').length}</b></div>
            </div>
            </section>
            <section className="library-main">
            <Space wrap style={{ marginBottom: 10 }}>
                <Segmented value={kind} onChange={(value) => setKind(value as typeof kind)}
                    options={[{ value: 'all', label: '全部' }, { value: 'face', label: '面部' }, { value: 'player', label: '球员' }]} />
                <Input.Search allowClear style={{ width: 260 }} placeholder="搜索名称或来源球员"
                    value={query} onChange={(event) => setQuery(event.target.value)} />
            </Space>
            <Table size="small" rowKey="id" dataSource={shown} loading={busy} pagination={{ pageSize: 20 }}
                locale={{ emptyText: <Empty description="库里还没有条目。选择一名球员，保存面部或整个球员。" /> }}
                columns={[
                    { title: '名称', dataIndex: 'name' },
                    { title: '类型', key: 'kind', width: 90, render: (_, entry) => <Tag color={entry.kind === 'face' ? 'purple' : 'blue'}>{kindLabel(entry.kind)}</Tag> },
                    { title: '来源球员', dataIndex: 'sourcePlayer' },
                    { title: '字段', dataIndex: 'fieldCount', width: 80 },
                    { title: '保存时间', key: 'created', width: 150, render: (_, entry) => savedAt(entry.created) },
                    {
                        title: '', key: 'actions', width: 180, render: (_, entry) => <Space>
                            <Button size="small" disabled={!editing || busy} icon={<UserSwitchOutlined />} onClick={() => openApply(entry)}>应用</Button>
                            <Popconfirm title="删除这个条目？游戏里的球员不受影响。" okText="删除" cancelText="取消"
                                onConfirm={() => void run(async () => {
                                    await libraryDelete(entry.id);
                                    setEntries(await libraryList());
                                })}>
                                <Button size="small" danger icon={<DeleteOutlined />} />
                            </Popconfirm>
                        </Space>,
                    },
                ]} />
            <Modal open={applying !== null} width={760} title={applying ? `应用「${applying.name}」` : ''}
                okText={preview ? `写入 ${preview.changeCount} 项` : '写入'} cancelText="取消"
                okButtonProps={{ disabled: !editing || !previewReady || !preview || preview.changeCount === 0, loading: busy }}
                onCancel={() => { ++previewId.current; setApplying(null); }} onOk={() => void apply()}>
                <Space wrap style={{ marginBottom: 12 }}>
                    <Select showSearch optionFilterProp="label" style={{ width: 340 }} placeholder="目标球员"
                        value={target} options={playerOptions} disabled={busy}
                        onChange={(value) => { setTarget(value); void loadPreview(value, scope); }} />
                    {applying?.kind === 'player' && <Tooltip title="球员条目也可以只应用其中的面部部分">
                        <Segmented value={scope} disabled={busy}
                            onChange={(value) => {
                                ++previewId.current;
                                setPreview(null);
                                setScope(value as LibraryKind);
                                if (target !== null) void loadPreview(target, value as LibraryKind);
                            }}
                            options={[{ value: 'player', label: '整个球员' }, { value: 'face', label: '只换面部' }]} />
                    </Tooltip>}
                </Space>
                {!!presentation?.blocked && <Alert type="error" showIcon
                    message="条目包含当前未开放的项目，请选择其他条目。" />}
                {preview && (preview.changeCount === 0
                    ? <Empty description="目标球员已经与条目一致" />
                    : <Table size="small" rowKey="field" pagination={false} scroll={{ y: 360 }} dataSource={presentation?.rows ?? []}
                        columns={[
                            { title: '字段', dataIndex: 'label' },
                            { title: '当前', dataIndex: 'before', width: 130 },
                            { title: '条目', dataIndex: 'after', width: 130 },
                        ]} />)}
            </Modal>
            </section>
        </div>
    );
}
