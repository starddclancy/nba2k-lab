import { Segmented, Select } from 'antd';
import type { Metric, Mode } from '../metrics';
import { metricOptions, modeLabels, show } from '../metrics';
import LineChart from './LineChart';

export const seriesColors = ['var(--primary)', 'var(--warning)', 'var(--info)', 'var(--physical)', 'var(--success)', 'var(--danger)'];

/**
 * A chart card where the user picks which stats to draw (any metric in the catalog, up to four)
 * and, for counting stats, per game / total / per 36. With several entities (comparison) only
 * the first picked metric is drawn, one line per entity.
 */
export default function MetricChart<T>({ title, hint, metrics, picked, onPick, mode, onMode, labels, rows, entities, height = 230, max = 4, compare, onRemove, highlight, onHighlight }: {
    title: string;
    hint?: string;
    metrics: Metric<T>[];
    picked: string[];
    onPick: (keys: string[]) => void;
    mode?: Mode;
    onMode?: (mode: Mode) => void;
    labels: string[];
    /** Single entity: rows aligned with labels. */
    rows?: (T | null)[];
    /** Comparison: one row list per entity, each aligned with labels. */
    entities?: { key: string; label: string; color: string; rows: (T | null)[] }[];
    height?: number;
    max?: number;
    /** Extra control in the header, e.g. a picker of other players/teams to compare on this chart. */
    compare?: React.ReactNode;
    /** Show a remove button. */
    onRemove?: () => void;
    /** Entity key to emphasize (comparison). */
    highlight?: string | null;
    onHighlight?: (key: string | null) => void;
}) {
    const chosen = picked.map((key) => metrics.find((metric) => metric.key === key)).filter(Boolean) as Metric<T>[];
    const active = mode ?? 'perGame';
    const anyCounting = chosen.some((metric) => metric.counting);
    const value = (metric: Metric<T>, row: T | null) => (row ? metric.value(row, active) : null);
    const series = entities
        ? (chosen[0] ? entities.map((entity) => ({ key: entity.key, label: entity.label, color: entity.color,
            values: entity.rows.map((row) => value(chosen[0], row)), unit: chosen[0].unit })) : [])
        : chosen.map((metric, order) => ({ key: metric.key, label: `${metric.label}${metric.counting ? `（${modeLabels[active]}）` : ''}`,
            color: seriesColors[order], values: (rows ?? []).map((row) => value(metric, row)), unit: metric.unit }));
    const digits = chosen[0] ? (chosen[0].counting && active === 'total' ? 0 : chosen[0].digits) : 1;
    return <section className="chart-card metric-chart">
        <h3>{title}{hint && <span className="muted">{hint}</span>}
            <span className="metric-tools">
                {onMode && anyCounting && <Segmented size="small" value={active} onChange={(next) => onMode(next as Mode)}
                    options={(Object.keys(modeLabels) as Mode[]).map((key) => ({ value: key, label: modeLabels[key] }))} />}
                <Select size="small" mode={entities ? undefined : 'multiple'} maxCount={entities ? undefined : max} showSearch optionFilterProp="label"
                    style={{ minWidth: entities ? 150 : 240, maxWidth: 420 }} value={entities ? picked[0] : picked} placeholder="选择数据项"
                    options={metricOptions(metrics)} popupMatchSelectWidth={false}
                    onChange={(next: string | string[]) => onPick(Array.isArray(next) ? next : [next])} />
                {compare}
                {onRemove && <button type="button" className="chart-remove" title="删除这张图" aria-label="删除这张图" onClick={onRemove}>✕</button>}
            </span>
        </h3>
        {series.length === 0 ? <div className="chart-empty">选择要绘制的数据项</div>
            : <LineChart labels={labels} series={series} height={height} highlight={highlight} onHighlight={onHighlight}
                format={(v) => (chosen[0] ? show({ ...chosen[0], unit: '' }, v, active) : v.toFixed(digits))} />}
    </section>;
}
