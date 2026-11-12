import { useEffect, useMemo, useRef, useState } from 'react';
import {
    Alert,
    App as AntApp,
    Button,
    InputNumber,
    Input,
    Modal,
    Select,
    Switch,
    Space,
    Table,
    Typography,
} from 'antd';
import { applyBatch, readBatch, readPlayer } from '../api';
import type { BatchPreview, PlayerDetail } from '../types';
import { confirmedOptions, displayValue, fieldLabel, isConfirmedField, isRoutineField, optionLabel, sectionLabel } from '../labels';
import { EditingSwitch, useEditing } from '../editingContext';

type Props = {
    indices: number[];
    source: string;
    onClose: () => void;
    onApplied: () => void;
};

/** A batch uses the same snapshots as the current-value preview. */
export default function BatchModal({ indices, source, onClose, onApplied }: Props) {
    const { message } = AntApp.useApp();
    const { editing } = useEditing();
    const editingRef = useRef(editing);
    editingRef.current = editing;
    const [detail, setDetail] = useState<PlayerDetail | null>(null);
    const [fieldKey, setFieldKey] = useState<string | null>(null);
    const [value, setValue] = useState<number | string | boolean | null>(null);
    const [busy, setBusy] = useState(false);
    const [schemaBusy, setSchemaBusy] = useState(true);
    const [previewBusy, setPreviewBusy] = useState(false);
    const [preview, setPreview] = useState<BatchPreview | null>(null);
    const [previewMode, setPreviewMode] = useState<boolean | null>(null);
    const [previewError, setPreviewError] = useState('');
    const [revision, setRevision] = useState(0);

    useEffect(() => {
        setValue(null);
    }, [editing]);

    useEffect(() => {
        let alive = true;
        setDetail(null);
        setFieldKey(null);
        setValue(null);
        setPreview(null);
        setPreviewMode(null);
        setPreviewError('');
        setSchemaBusy(true);
        readPlayer(indices[0], source)
            .then((data) => {
                if (alive) {
                    setDetail(data);
                }
            })
            .catch((error: Error) => {
                if (alive) {
                    setPreviewError(error.message);
                }
            })
            .finally(() => {
                if (alive) {
                    setSchemaBusy(false);
                }
            });
        return () => {
            alive = false;
        };
    }, [indices, source]);

    useEffect(() => {
        let alive = true;
        setPreview(null);
        setPreviewMode(null);
        setPreviewError('');
        setPreviewBusy(false);
        if (!fieldKey) {
            return;
        }
        setPreviewBusy(true);
        readBatch(indices, fieldKey, source)
            .then((data) => {
                if (alive) {
                    setPreview(data);
                    setPreviewMode(editing);
                    if (editing && data.field.kind === 'bool') {
                        setValue((staged) => staged ?? Boolean(data.field.value));
                    }
                }
            })
            .catch((error: Error) => {
                if (alive) {
                    setPreviewError(error.message);
                }
            })
            .finally(() => {
                if (alive) {
                    setPreviewBusy(false);
                }
            });
        return () => {
            alive = false;
        };
    }, [indices, source, fieldKey, revision, editing]);

    const options = useMemo(() => {
        if (!detail) {
            return [];
        }
        return detail.sections.map((section) => ({
            label: sectionLabel(section.name),
            options: section.fields
                .filter((field) => field.writable && isRoutineField(field) && isConfirmedField(field))
                .map((field) => ({ value: field.key, label: `${sectionLabel(field.group)} · ${fieldLabel(field)}` })),
        })).filter((section) => section.options.length > 0);
    }, [detail]);

    const current = preview?.field.key === fieldKey && isConfirmedField(preview.field) ? preview.field : undefined;
    const visiblePlayers = current ? preview?.players.filter((row) =>
        isConfirmedField({ ...current, value: row.value, display: row.display })) ?? [] : [];
    const ready = !!current && !!preview?.readToken && previewMode === editing && !previewBusy && !schemaBusy
        && visiblePlayers.length === indices.length;

    const apply = async () => {
        if (!editingRef.current || busy || !fieldKey || value === null || !preview?.readToken || !ready) {
            return;
        }
        setBusy(true);
        try {
            const result = await applyBatch(indices, { [fieldKey]: value }, source, preview.readToken);
            message.success(`已写入 ${result.players} 名球员，共 ${result.patches} 处改动`);
            onApplied();
            onClose();
        } catch (error) {
            setPreview(null);
            setPreviewError((error as Error).message);
        } finally {
            setBusy(false);
        }
    };

    return (
        <Modal
            open
            width={640}
            title={<Space><span>批量编辑 {indices.length} 名球员</span><EditingSwitch /></Space>}
            onCancel={onClose}
            closable={!busy}
            maskClosable={!busy}
            keyboard={!busy}
            footer={
                <Space>
                    <Button onClick={onClose} disabled={busy}>取消</Button>
                    <Button type="primary" loading={busy} disabled={!editing || !ready || value === null} onClick={apply}>
                        应用到全部
                    </Button>
                </Space>
            }
        >
            <Space direction="vertical" style={{ width: '100%' }} size="middle">
                <Select
                    showSearch
                    optionFilterProp="label"
                    style={{ width: '100%' }}
                    placeholder="选择要批量修改的项目"
                    value={fieldKey}
                    options={options}
                    loading={schemaBusy}
                    disabled={schemaBusy || busy}
                    onChange={(next) => {
                        setFieldKey(next);
                        setPreview(null);
                        setValue(null);
                    }}
                />
                <Space wrap>
                    <Typography.Text type="secondary">
                        {previewBusy ? '读取当前值中…' : `已读取 ${preview?.players.length ?? 0} 名球员`}
                    </Typography.Text>
                    <Button
                        size="small"
                        loading={previewBusy}
                        disabled={!fieldKey || busy || schemaBusy}
                        onClick={() => {
                            setPreview(null);
                            setPreviewBusy(true);
                            setRevision((previous) => previous + 1);
                        }}
                    >
                        刷新预览
                    </Button>
                </Space>
                {previewError && <Alert type="error" showIcon message={previewError} />}
                {current && (
                    <Space wrap>
                        <Typography.Text>新值</Typography.Text>
                        {!editing ? <span className="field-value">{displayValue(current)}</span> : current.kind === 'bool' ? (
                            <Switch
                                checked={value === true}
                                disabled={busy || !ready}
                                onChange={(checked) => { if (editingRef.current) setValue(checked); }}
                            />
                        ) : current.kind === 'choice' ? (
                            <Select
                                showSearch
                                optionFilterProp="label"
                                style={{ width: 240 }}
                                value={(value as string) ?? undefined}
                                options={confirmedOptions(current).map((option) => ({ value: option, label: optionLabel(option, current) }))}
                                disabled={busy || !ready}
                                onChange={(next) => { if (editingRef.current) setValue(next); }}
                            />
                        ) : current.kind === 'text' ? (
                            <Input
                                style={{ width: 240 }}
                                value={(value as string) ?? ''}
                                disabled={busy || !ready}
                                onChange={(event) => { if (editingRef.current) setValue(event.target.value); }}
                            />
                        ) : (
                            <InputNumber
                                style={{ width: 160 }}
                                min={current.min}
                                max={current.max}
                                value={(value as number) ?? null}
                                disabled={busy || !ready}
                                onChange={(next) => { if (editingRef.current) setValue(next ?? null); }}
                            />
                        )}
                    </Space>
                )}
                {preview && (
                    <Table
                        className="batch-preview"
                        size="small"
                        rowKey="index"
                        dataSource={visiblePlayers}
                        columns={[
                            { title: '球员', dataIndex: 'name', ellipsis: true },
                            { title: '当前值', width: 130, render: (_: unknown, row: BatchPreview['players'][number]) =>
                                current ? displayValue(current, row.value) : row.display },
                            { title: '修改为', width: 130, render: () => !editing || value === null || !current ? '' : displayValue(current, value) },
                        ]}
                        pagination={{ pageSize: 8, size: 'small', showSizeChanger: false, hideOnSinglePage: true }}
                        scroll={{ x: 420 }}
                    />
                )}
            </Space>
        </Modal>
    );
}
