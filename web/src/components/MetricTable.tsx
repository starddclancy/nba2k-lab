import { useState } from 'react';
import { Segmented } from 'antd';
import type { Metric, Mode } from '../metrics';
import { modeLabels, show } from '../metrics';

/**
 * Season/game table over the whole metric catalog. Columns come in groups (tabs); clicking a
 * column header sends that stat to the chart above (onChart). Picked columns are highlighted.
 */
export default function MetricTable<T>({ title, hint, metrics, rows, rowLabel, rowClass, mode, onMode, picked, onChart, lead }: {
    title: string;
    hint?: string;
    metrics: Metric<T>[];
    rows: T[];
    rowLabel: (row: T) => React.ReactNode;
    rowClass?: (row: T) => string;
    mode?: Mode;
    onMode?: (mode: Mode) => void;
    picked: string[];
    onChart: (key: string) => void;
    /** Extra fixed columns after the row label (e.g. opponent, result). */
    lead?: { label: string; cell: (row: T) => React.ReactNode }[];
}) {
    const groups = [...new Set(metrics.map((metric) => metric.group))];
    const [group, setGroup] = useState<string>('全部');
    const active = mode ?? 'perGame';
    const columns = group === '全部' ? metrics : metrics.filter((metric) => metric.group === group);
    return <section className="chart-card wide metric-table">
        <h3>{title}{hint && <span className="muted">{hint}</span>}
            <span className="metric-tools">
                <Segmented size="small" value={group} onChange={(value) => setGroup(String(value))} options={['全部', ...groups]} />
                {onMode && <Segmented size="small" value={active} onChange={(value) => onMode(value as Mode)}
                    options={(Object.keys(modeLabels) as Mode[]).map((key) => ({ value: key, label: modeLabels[key] }))} />}
            </span>
        </h3>
        <div className="metric-scroll">
            <table className="season-table">
                <thead><tr><th className="sticky">{rows.length} 行</th>{lead?.map((column) => <th key={column.label}>{column.label}</th>)}
                    {columns.map((metric) => <th key={metric.key} className={`pickable ${picked.includes(metric.key) ? 'on' : ''}`}
                        title={`点击绘制「${metric.label}」折线图`} onClick={() => onChart(metric.key)}>{metric.label}</th>)}</tr></thead>
                <tbody>{rows.map((row, index) => <tr key={index} className={rowClass?.(row) ?? ''}>
                    <td className="sticky">{rowLabel(row)}</td>
                    {lead?.map((column) => <td key={column.label}>{column.cell(row)}</td>)}
                    {columns.map((metric) => <td key={metric.key} className={picked.includes(metric.key) ? 'hl' : ''}>{show({ ...metric, unit: '' }, metric.value(row, active), active)}</td>)}
                </tr>)}</tbody>
            </table>
        </div>
    </section>;
}
