import { useMemo, useState } from 'react';
import { Button, Input, InputNumber, Popover, Select } from 'antd';
import { FilterOutlined, DownloadOutlined } from '@ant-design/icons';

export type GridColumn<T> = {
    key: string;
    label: string;
    group?: string;
    /** Number columns get range filters and right alignment; text columns get a text filter. */
    numeric?: boolean;
    value: (row: T) => number | string | null;
    render?: (row: T) => React.ReactNode;
    digits?: number;
    lowerBetter?: boolean;
    width?: number;
};

type Filter = { text?: string; min?: number | null; max?: number | null };

/**
 * Spreadsheet-style table: click a header to sort, the funnel on each header filters that
 * column (text contains / numeric range), a quick search over text columns, choose visible
 * columns, highlight the best value per column, and export what is shown to CSV.
 */
export default function DataGrid<T>({ title, hint, columns, rows, rowKey, defaultColumns, onRowClick, activeKey, height = 520 }: {
    title: string;
    hint?: string;
    columns: GridColumn<T>[];
    rows: T[];
    rowKey: (row: T) => string;
    defaultColumns?: string[];
    onRowClick?: (row: T) => void;
    activeKey?: string | null;
    height?: number;
}) {
    const [sort, setSort] = useState<{ key: string; desc: boolean } | null>(null);
    const [filters, setFilters] = useState<Record<string, Filter>>({});
    const [query, setQuery] = useState('');
    const [visible, setVisible] = useState<string[]>(defaultColumns ?? columns.map((column) => column.key));
    const shown = columns.filter((column) => visible.includes(column.key) || column === columns[0]);

    const filtered = useMemo(() => {
        const needle = query.trim().toLowerCase();
        let list = rows.filter((row) => {
            if (needle && !columns.some((column) => !column.numeric && String(column.value(row) ?? '').toLowerCase().includes(needle))) return false;
            for (const [key, filter] of Object.entries(filters)) {
                const column = columns.find((item) => item.key === key);
                if (!column) continue;
                const value = column.value(row);
                if (filter.text && !String(value ?? '').toLowerCase().includes(filter.text.toLowerCase())) return false;
                if (filter.min != null && (typeof value !== 'number' || value < filter.min)) return false;
                if (filter.max != null && (typeof value !== 'number' || value > filter.max)) return false;
            }
            return true;
        });
        if (sort) {
            const column = columns.find((item) => item.key === sort.key);
            if (column) {
                list = [...list].sort((a, b) => {
                    const x = column.value(a);
                    const y = column.value(b);
                    if (x === null || x === undefined) return 1;
                    if (y === null || y === undefined) return -1;
                    const order = typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y), 'zh');
                    return sort.desc ? -order : order;
                });
            }
        }
        return list;
    }, [rows, columns, filters, sort, query]);

    const best = useMemo(() => {
        const result: Record<string, number | null> = {};
        for (const column of shown) {
            if (!column.numeric) continue;
            const values = filtered.map((row) => column.value(row)).filter((value): value is number => typeof value === 'number' && Number.isFinite(value));
            result[column.key] = values.length > 1 ? (column.lowerBetter ? Math.min(...values) : Math.max(...values)) : null;
        }
        return result;
    }, [filtered, shown]);

    const activeFilters = Object.values(filters).filter((filter) => filter.text || filter.min != null || filter.max != null).length;
    const format = (column: GridColumn<T>, value: number | string | null) =>
        value === null || value === undefined ? '—' : typeof value === 'number' ? value.toFixed(column.digits ?? 0) : value;
    const exportCsv = () => {
        const escape = (text: string) => (/[",\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text);
        const lines = [shown.map((column) => escape(column.label)).join(','),
            ...filtered.map((row) => shown.map((column) => escape(format(column, column.value(row)))).join(','))];
        const blob = new Blob(['\ufeff' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' });
        const link = document.createElement('a');
        link.href = URL.createObjectURL(blob);
        link.download = `${title}.csv`;
        link.click();
        URL.revokeObjectURL(link.href);
    };
    const groups = [...new Set(columns.map((column) => column.group ?? '数据'))];

    const filterBox = (column: GridColumn<T>) => {
        const filter = filters[column.key] ?? {};
        const set = (next: Filter) => setFilters({ ...filters, [column.key]: next });
        return <div className="grid-filter">
            {column.numeric ? <>
                <span>范围</span>
                <InputNumber size="small" placeholder="最小" value={filter.min ?? null} onChange={(value) => set({ ...filter, min: value })} />
                <InputNumber size="small" placeholder="最大" value={filter.max ?? null} onChange={(value) => set({ ...filter, max: value })} />
            </> : <Input size="small" allowClear placeholder="包含文字" value={filter.text ?? ''} onChange={(event) => set({ ...filter, text: event.target.value })} />}
            <Button size="small" type="link" onClick={() => { const next = { ...filters }; delete next[column.key]; setFilters(next); }}>清除</Button>
        </div>;
    };

    return <section className="chart-card wide data-grid">
        <h3>{title}{hint && <span className="muted">{hint}</span>}
            <span className="metric-tools">
                <Input size="small" allowClear placeholder="搜索" style={{ width: 150 }} value={query} onChange={(event) => setQuery(event.target.value)} />
                <Select size="small" mode="multiple" maxTagCount={0} maxTagPlaceholder={() => `显示 ${shown.length}/${columns.length} 列`} style={{ width: 150 }}
                    value={visible} onChange={setVisible} popupMatchSelectWidth={false}
                    options={groups.map((group) => ({ label: group, options: columns.filter((column) => (column.group ?? '数据') === group).map((column) => ({ value: column.key, label: column.label })) }))} />
                {activeFilters > 0 && <Button size="small" onClick={() => setFilters({})}>清除 {activeFilters} 个筛选</Button>}
                <Button size="small" icon={<DownloadOutlined />} onClick={exportCsv}>导出 CSV</Button>
            </span>
        </h3>
        <div className="grid-meta muted">{filtered.length} / {rows.length} 行 · 点击表头排序 · 漏斗图标筛选</div>
        <div className="grid-scroll" style={{ maxHeight: height }}>
            <table className="grid-table">
                <thead><tr>{shown.map((column) => {
                    const filter = filters[column.key];
                    const on = !!filter && (!!filter.text || filter.min != null || filter.max != null);
                    return <th key={column.key} className={`${column.numeric ? 'num' : ''} ${sort?.key === column.key ? 'sorted' : ''}`} style={{ minWidth: column.width }}>
                        <span className="grid-head" onClick={() => setSort(sort?.key === column.key ? (sort.desc ? { key: column.key, desc: false } : null) : { key: column.key, desc: column.numeric !== false })}>
                            {column.label}{sort?.key === column.key ? (sort.desc ? ' ▼' : ' ▲') : ''}</span>
                        <Popover trigger="click" content={filterBox(column)} placement="bottom">
                            <FilterOutlined className={`grid-funnel ${on ? 'on' : ''}`} />
                        </Popover>
                    </th>;
                })}</tr></thead>
                <tbody>{filtered.map((row) => {
                    const key = rowKey(row);
                    return <tr key={key} className={`${activeKey === key ? 'active' : ''} ${onRowClick ? 'clickable' : ''}`} onClick={() => onRowClick?.(row)}>
                        {shown.map((column) => {
                            const value = column.value(row);
                            return <td key={column.key} className={`${column.numeric ? 'num' : ''} ${best[column.key] !== null && best[column.key] !== undefined && value === best[column.key] ? 'best' : ''}`}>
                                {column.render ? column.render(row) : format(column, value)}</td>;
                        })}
                    </tr>;
                })}</tbody>
            </table>
        </div>
    </section>;
}

