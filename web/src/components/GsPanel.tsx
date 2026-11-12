import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react';
import { App as AntApp, Alert, Button, Empty, Input, Segmented, Select, Space, Spin, Typography } from 'antd';
import {
    gsApply,
    gsRead,
    gsUndo,
    inMatchApply,
    inMatchRead,
    inMatchUndo,
    presetDelete,
    presetList,
    presetLoad,
    presetSave,
} from '../api';
import type { GsFieldRow, GsPayload, PresetFile } from '../types';
import { EditingSwitch, useEditing } from '../editingContext';
import NumericSlider from './NumericSlider';

type Source = 'general' | 'inMatch';
const groupColors: Record<string, string> = {
    offense: 'var(--finishing)', defense: 'var(--defense)', attributes: 'var(--physical)',
    tendencies: 'var(--playmaking)', fouls: 'var(--rebounding)', movement: 'var(--shooting)',
};

/**
 * Both GS arrays are editable from one panel: general GS is the settings-menu copy, in-match GS
 * is the copy the in-game menu reads. They expose the same 70 sliders and the same payload shape.
 */
export default function GsPanel() {
    const { message } = AntApp.useApp();
    const { editing } = useEditing();
    const editingRef = useRef(editing);
    editingRef.current = editing;
    const [source, setSource] = useState<Source>('general');
    const [payload, setPayload] = useState<GsPayload | null>(null);
    const [draft, setDraft] = useState<Record<string, number>>({});
    const [group, setGroup] = useState<string>('all');
    const [search, setSearch] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState('');
    const [presets, setPresets] = useState<PresetFile[]>([]);
    const [presetDir, setPresetDir] = useState('');
    const [presetName, setPresetName] = useState('');
    const [chosen, setChosen] = useState<string | null>(null);
    const [loadedFor, setLoadedFor] = useState('');
    const readId = useRef(0);
    const available = payload?.fields.some((field) => field.user !== null || field.cpu !== null) ?? false;
    const editReady = editing && available && loadedFor === `${source}:true`;
    const ready = loadedFor === `${source}:${editing}`;

    const adopt = useCallback((data: GsPayload) => {
        setPayload(data);
        const next: Record<string, number> = {};
        for (const field of data.fields) {
            if (field.user !== null) {
                next[`user.${field.id}`] = field.user;
            }
            if (field.cpu !== null) {
                next[`cpu.${field.id}`] = field.cpu;
            }
        }
        setDraft(next);
    }, []);

    const load = useCallback(
        async (next: Source) => {
            const request = ++readId.current;
            const mode = editingRef.current;
            setLoadedFor('');
            setError('');
            if (!mode) setDraft({});
            setBusy(true);
            try {
                const data = next === 'general' ? await gsRead() : await inMatchRead();
                if (request !== readId.current) return;
                adopt(data);
                setLoadedFor(`${next}:${mode}`);
            } catch (error) {
                if (request === readId.current) setError((error as Error).message);
            } finally {
                if (request === readId.current) setBusy(false);
            }
        },
        [adopt, message],
    );

    useEffect(() => {
        void load(source);
        return () => { ++readId.current; };
    }, [source, editing, load]);

    const refreshPresets = useCallback(async () => {
        try {
            const data = await presetList();
            setPresets(data.files);
            setPresetDir(data.directory);
        } catch {
            // A missing presets folder is not an error; the list simply stays empty.
        }
    }, []);

    useEffect(() => {
        void refreshPresets();
    }, [refreshPresets]);

    const loadChosen = async () => {
        if (!editingRef.current || !editReady || busy) return;
        if (!chosen) {
            message.warning('请选择一个预设文件');
            return;
        }
        const request = readId.current;
        try {
            const data = await presetLoad(chosen);
            if (!editingRef.current || request !== readId.current) return;
            setDraft((current) => {
                const next = { ...current };
                for (const row of data.values) {
                    next[row.key] = row.value;
                }
                return next;
            });
            message.success(`已载入 ${data.count} 项，确认无误后再点「应用改动」`);
        } catch (error) {
            message.error((error as Error).message);
        }
    };

    const saveCurrent = async () => {
        if (busy || !payload || !available || loadedFor !== `${source}:${editingRef.current}`) return;
        const name = presetName.trim() || chosen || '';
        if (!name) {
            message.warning('请输入预设名称');
            return;
        }
        try {
            const saved = await presetSave(name, draft);
            message.success(`已保存预设 ${saved.name}（${saved.count} 项）`);
            setPresetName('');
            setChosen(saved.name);
            await refreshPresets();
        } catch (error) {
            message.error((error as Error).message);
        }
    };

    const removeChosen = async () => {
        if (!chosen) {
            return;
        }
        try {
            await presetDelete(chosen);
            message.success(`已删除 ${chosen}`);
            setChosen(null);
            await refreshPresets();
        } catch (error) {
            message.error((error as Error).message);
        }
    };

    const changed = useMemo(() => {
        const delta: Record<string, number> = {};
        if (!payload) {
            return delta;
        }
        for (const field of payload.fields) {
            const user = draft[`user.${field.id}`];
            const cpu = draft[`cpu.${field.id}`];
            if (field.user !== null && user !== undefined && user !== field.user) {
                delta[`user.${field.id}`] = user;
            }
            if (field.cpu !== null && cpu !== undefined && cpu !== field.cpu) {
                delta[`cpu.${field.id}`] = cpu;
            }
        }
        return delta;
    }, [payload, draft]);

    const changedCount = Object.keys(changed).length;

    const apply = async () => {
        if (!editingRef.current || !editReady || busy || changedCount === 0) {
            return;
        }
        const request = ++readId.current;
        setBusy(true);
        try {
            const data = source === 'general' ? await gsApply(changed) : await inMatchApply(changed);
            if (request === readId.current) adopt(data);
            message.success(`已写入 ${changedCount} 项`);
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            if (request === readId.current) setBusy(false);
        }
    };

    const undo = async () => {
        if (!editingRef.current || !editReady || busy) return;
        const request = ++readId.current;
        setBusy(true);
        try {
            const data = source === 'general' ? await gsUndo() : await inMatchUndo();
            if (request === readId.current) adopt(data);
            message.success('已撤销上一笔');
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            if (request === readId.current) setBusy(false);
        }
    };

    const groups = useMemo(() => {
        const needle = search.trim().toLowerCase();
        return (payload?.groups ?? []).map((item) => {
            const fields = (payload?.fields ?? []).filter((field) => field.group === item.id);
            return { ...item, count: fields.length,
                dirty: fields.reduce((count, field) => count + Number(`user.${field.id}` in changed)
                    + Number(`cpu.${field.id}` in changed), 0),
                fields: fields.filter((field) => !needle
                    || `${item.label} ${field.label} ${field.id}`.toLowerCase().includes(needle)) };
        });
    }, [payload, search, changed]);
    const visible = groups.filter((item) => (group === 'all' || group === item.id) && item.fields.length > 0);
    const menu = [{ id: 'all', label: '全部', count: payload?.fields.length ?? 0, dirty: changedCount }, ...groups];

    function sideControl(field: GsFieldRow, side: 'user' | 'cpu') {
        const key = `${side}.${field.id}`;
        const label = side === 'user' ? '用户' : '电脑';
        const value = field[side];
        return <div className={`gs-side-value${key in changed ? ' gs-value-dirty' : ''}`} key={side}>
            <span className="gs-side-label">{label}</span>
            {editing && value !== null ? <NumericSlider label={`${field.label} ${label}`}
                min={0} max={100} value={draft[key] ?? value} disabled={busy || !editReady}
                onChange={(next) => {
                    if (editingRef.current && editReady) setDraft((current) => ({ ...current, [key]: next }));
                }} /> : <span className="field-value">{value ?? '未加载'}</span>}
        </div>;
    }

    return (
        <div className="gs-panel">
            <div className="toolbar">
                <Segmented
                    value={source}
                    onChange={(value) => setSource(value as Source)}
                    options={[
                        { label: '通用比赛设置', value: 'general' },
                        { label: '当前比赛设置', value: 'inMatch' },
                    ]}
                />
                <EditingSwitch />
                <Input size="small" allowClear className="gs-search" placeholder="搜索 GS 项目"
                    value={search} onChange={(event) => setSearch(event.target.value)} />
                <span className="spacer" />
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {ready && payload ? `${payload.fields.length} 项 · 可撤销 ${payload.undoDepth} 次` : busy ? '读取中' : '尚未读取'}
                </Typography.Text>
                <Space>
                    <Button size="small" onClick={() => void load(source)} loading={busy}>
                        重新读取
                    </Button>
                    <Button size="small" onClick={undo} disabled={!editReady || busy || (payload?.undoDepth ?? 0) === 0}>
                        撤销
                    </Button>
                    <Button
                        size="small"
                        type="primary"
                        aria-label="应用 GS 改动"
                        onClick={apply}
                        loading={busy}
                        disabled={!editReady || changedCount === 0}
                    >
                        应用改动{changedCount > 0 ? ` (${changedCount})` : ''}
                    </Button>
                </Space>
            </div>
            <div className="toolbar">
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    预设
                </Typography.Text>
                <Select
                    allowClear
                    size="small"
                    style={{ width: 220 }}
                    placeholder={presets.length > 0 ? '选择预设文件' : '还没有预设'}
                    value={chosen}
                    options={presets.map((file) => ({
                        value: file.name,
                        label: `${file.name}（${file.modified}）`,
                    }))}
                    onChange={(value) => setChosen(value ?? null)}
                />
                <Button size="small" onClick={loadChosen} disabled={!editReady || busy || !chosen}>
                    载入到草稿
                </Button>
                <Input
                    size="small"
                    style={{ width: 200 }}
                    placeholder="预设名称"
                    value={presetName}
                    onChange={(event) => setPresetName(event.target.value)}
                />
                <Button size="small" onClick={saveCurrent} disabled={busy || !available || loadedFor !== `${source}:${editing}`}>
                    保存当前值
                </Button>
                <Button size="small" danger onClick={removeChosen} disabled={!chosen}>
                    删除
                </Button>
                <span className="spacer" />
                <Typography.Text type="secondary" className="gs-preset-directory" style={{ fontSize: 12 }} ellipsis>
                    {presetDir}
                </Typography.Text>
            </div>
            {error && <Alert type="error" showIcon message={error} />}
            {ready && payload?.notice && <Alert type="info" showIcon message={payload.notice} />}
            <div className="gs-workspace">
                <nav className="gs-nav" aria-label="GS 分类">
                    {menu.map((item) => <button type="button" key={item.id}
                        className={`gs-nav-item${group === item.id ? ' on' : ''}`}
                        aria-pressed={group === item.id} onClick={() => setGroup(item.id)}>
                        <span>{item.label}</span><small>{item.count}</small>
                        {editing && item.dirty > 0 && <i title={`${item.dirty} 项未应用`}>{item.dirty}</i>}
                    </button>)}
                </nav>
                <main className="gs-content">
                    <Select className="gs-group-select" aria-label="GS 分类" value={group} onChange={setGroup}
                        options={menu.map((item) => ({ value: item.id, label: `${item.label} · ${item.count} 项` }))} />
                    {!ready ? <div className="empty-hint">{busy ? <Spin /> : <Empty description="尚未读取当前设置" />}</div>
                        : visible.length === 0 ? <Empty description="没有匹配的 GS 项目" />
                        : visible.map((item) => <section className="gs-field-group" key={item.id}
                            aria-label={item.label} style={{ '--category': groupColors[item.id] ?? 'var(--primary)' } as CSSProperties}>
                            <header className="gs-group-heading"><div className="gs-group-title">
                                <h3>{item.label}</h3><small>{item.fields.length} 项</small>
                                {editing && item.dirty > 0 && <span className="gs-group-dirty">{item.dirty} 项未应用</span>}</div>
                                <span className="gs-column-heading">用户</span><span className="gs-column-heading">电脑</span>
                            </header>
                            {item.fields.map((field) => <div key={field.id} data-gs-field={field.id}
                                className={`gs-field-row${`user.${field.id}` in changed || `cpu.${field.id}` in changed ? ' gs-field-dirty' : ''}`}>
                                <span className="gs-field-label">{field.label}</span>
                                {sideControl(field, 'user')}{sideControl(field, 'cpu')}
                            </div>)}
                        </section>)}
                </main>
            </div>
        </div>
    );
}
