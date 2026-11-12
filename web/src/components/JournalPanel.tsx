import { useCallback, useEffect, useState } from 'react';
import { App as AntApp, Button, Table, Tag, Typography } from 'antd';
import { readJournals, type JournalRow } from '../api';
import { fieldLabel, journalLabel } from '../labels';

const statuses: Record<string, string> = {
    applied: '已应用', undone: '已撤销', prepared: '已准备', failed: '失败', needs_review: '需要核查',
};
const changeLabel = (key: string) => {
    const label = fieldLabel({ key, label: key.split('/').at(-1) ?? key });
    return label.startsWith('内部参数') ? key : label;
};

/** Read-only list of every file this editor wrote: player edits, GS edits, presets, exports. */
export default function JournalPanel() {
    const { message } = AntApp.useApp();
    const [rows, setRows] = useState<JournalRow[]>([]);
    const [busy, setBusy] = useState(false);

    const load = useCallback(async () => {
        setBusy(true);
        try {
            setRows(await readJournals());
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    }, [message]);

    useEffect(() => {
        void load();
    }, [load]);

    const columns = [
        {
            title: '类型',
            dataIndex: 'kind',
            width: 110,
            render: (value: string) => <Tag>{journalLabel(value)}</Tag>,
        },
        { title: '文件', dataIndex: 'name', ellipsis: true },
        {
            title: '大小',
            dataIndex: 'bytes',
            width: 100,
            render: (value: number) => `${value} B`,
        },
        { title: '修改时间', dataIndex: 'modified', width: 150 },
    ];

    return (
        <div className="gs-panel">
            <div className="toolbar">
                <Typography.Text strong>操作记录</Typography.Text>
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    共 {rows.length} 个文件
                </Typography.Text>
                <span className="spacer" />
                <Button size="small" onClick={() => void load()} loading={busy}>
                    刷新
                </Button>
            </div>
            <Table<JournalRow>
                size="small"
                rowKey={(row) => `${row.kind}/${row.name}`}
                loading={busy}
                dataSource={rows}
                columns={columns}
                pagination={false}
                scroll={{ y: 'calc(100vh - 210px)' }}
                expandable={{
                    expandedRowRender: (row) => (
                        <section className="journal-detail" aria-label="事务详情">
                            <Typography.Text type="secondary" style={{ fontSize: 12 }}>{row.directory}</Typography.Text>
                            {(!row.detail || row.detail.state === 'unsupported') && <Typography.Paragraph type="secondary">
                                此文件格式暂不提供事务详情
                            </Typography.Paragraph>}
                            {row.detail?.state === 'invalid' && <Typography.Paragraph type="danger">
                                {row.detail.error || '事务记录无法读取'}
                            </Typography.Paragraph>}
                            {row.detail?.state === 'supported' && <>
                                <div className="journal-meta">
                                    <Tag>{statuses[row.detail.status ?? ''] ?? row.detail.status ?? '状态未记录'}</Tag>
                                    {row.detail.time || row.modified}
                                </div>
                                {row.detail.error && <Typography.Paragraph type="danger">{row.detail.error}</Typography.Paragraph>}
                                <Table size="small" rowKey={(_, index) => `${row.name}-${index}`}
                                    dataSource={row.detail.changes ?? []}
                                    pagination={{ pageSize: 15, size: 'small', hideOnSinglePage: true, showSizeChanger: false }}
                                    columns={[
                                        { title: '对象', dataIndex: 'subject' },
                                        { title: '项目', dataIndex: 'field',
                                            render: (value: string) => <span title={value}>{changeLabel(value)}</span> },
                                        { title: '修改前原值', dataIndex: 'before' },
                                        { title: '修改后原值', dataIndex: 'after' },
                                    ]} />
                            </>}
                        </section>
                    ),
                }}
            />
        </div>
    );
}
