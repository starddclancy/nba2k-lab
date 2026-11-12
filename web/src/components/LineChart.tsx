import { useEffect, useMemo, useState } from 'react';
import { axis } from '../seasonTrends';

export type Series = { key: string; label: string; color: string; values: (number | null)[]; unit?: string };

/**
 * Animated SVG line chart: lines draw in, points pop, an area gradient fills under the first
 * series, and a crosshair with a value card follows the pointer. No chart library needed.
 */
export default function LineChart({ labels, series: rawSeries, height = 220, floor, format: rawFormat = (value) => String(value), highlight: controlledHighlight, onHighlight }: {
    labels: string[];
    /** Series key to emphasize; the others are dimmed. */
    highlight?: string | null;
    onHighlight?: (key: string | null) => void;
    series: Series[];
    height?: number;
    floor?: number;
    format?: (value: number) => string;
}) {
    const [hover, setHover] = useState<number | null>(null);
    const [localHighlight, setLocalHighlight] = useState<string | null>(null);
    const requested = controlledHighlight === undefined ? localHighlight : controlledHighlight;
    const highlight = rawSeries.some((line) => line.key === requested) ? requested : null;
    useEffect(() => {
        if (localHighlight !== null && !rawSeries.some((line) => line.key === localHighlight)) setLocalHighlight(null);
    }, [rawSeries, localHighlight]);
    const toggle = (key: string) => {
        const next = highlight === key ? null : key;
        if (controlledHighlight === undefined) setLocalHighlight(next);
        onHighlight?.(next);
    };
    // Pad or trim every series to the label count and turn undefined / NaN into gaps, so a
    // series that is shorter than the labels (or data that changed under the pointer) cannot crash.
    const series = rawSeries.map((line) => ({ ...line, values: labels.map((_, index) => {
        const value = line.values[index];
        return typeof value === 'number' && Number.isFinite(value) ? value : null;
    }) }));
    const format = (value: number) => { try { return rawFormat(value); } catch { return String(value); } };
    useEffect(() => { setHover(null); }, [labels.length, rawSeries.length]);
    const width = 640;
    const pad = { left: 40, right: 14, top: 14, bottom: 26 };
    const values = series.flatMap((line) => line.values.filter((value): value is number => value !== null));
    const scale = useMemo(() => axis(values, floor), [values.join(','), floor]); // eslint-disable-line react-hooks/exhaustive-deps
    const x = (index: number) => pad.left + (labels.length <= 1 ? (width - pad.left - pad.right) / 2
        : (index / (labels.length - 1)) * (width - pad.left - pad.right));
    const y = (value: number) => pad.top + (1 - (value - scale.min) / (scale.max - scale.min || 1)) * (height - pad.top - pad.bottom);
    const path = (line: Series) => line.values.map((value, index) => (value === null ? null : `${x(index)},${y(value)}`))
        .reduce<string[]>((parts, point, index, all) => {
            if (point === null) return parts;
            parts.push(`${index === 0 || all[index - 1] === null ? 'M' : 'L'}${point}`);
            return parts;
        }, []).join(' ');
    const id = useMemo(() => `chart-${Math.random().toString(36).slice(2, 8)}`, []);
    if (labels.length === 0 || series.length === 0) {
        return <div className="chart-empty">没有可绘制的赛季数据</div>;
    }
    const first = series[0];
    const firstPoints = first.values.map((value, index) => (value === null ? null : [x(index), y(value)] as const)).filter(Boolean) as (readonly [number, number])[];
    const area = firstPoints.length > 1
        ? `M${firstPoints[0][0]},${height - pad.bottom} ${firstPoints.map(([px, py]) => `L${px},${py}`).join(' ')} L${firstPoints[firstPoints.length - 1][0]},${height - pad.bottom} Z` : '';
    const step = labels.length > 12 ? Math.ceil(labels.length / 10) : 1;

    return <div className="chart">
        <div className="chart-legend">{series.map((line) => <button type="button" key={line.key} aria-pressed={highlight === line.key}
            className={highlight === null ? '' : highlight === line.key ? 'on' : 'off'} onClick={() => toggle(line.key)}><i style={{ background: line.color }} />{line.label}</button>)}</div>
        <svg viewBox={`0 0 ${width} ${height}`} className="chart-svg" role="img"
            aria-label={series.map((line) => `${line.label}：${line.values.map((value) => value ?? '—').join('、')}`).join('；')}
            onMouseLeave={() => setHover(null)}
            onMouseMove={(event) => {
                const box = event.currentTarget.getBoundingClientRect();
                const px = ((event.clientX - box.left) / box.width) * width;
                const index = Math.round(((px - pad.left) / (width - pad.left - pad.right)) * (labels.length - 1));
                setHover(Math.max(0, Math.min(labels.length - 1, index)));
            }}>
            <defs>
                <linearGradient id={`${id}-fill`} x1="0" x2="0" y1="0" y2="1">
                    <stop offset="0%" stopColor={first.color} stopOpacity=".32" />
                    <stop offset="100%" stopColor={first.color} stopOpacity="0" />
                </linearGradient>
            </defs>
            {scale.ticks.map((tick) => <g key={tick} className="chart-grid">
                <line x1={pad.left} x2={width - pad.right} y1={y(tick)} y2={y(tick)} />
                <text x={pad.left - 6} y={y(tick)} textAnchor="end" dominantBaseline="middle">{format(tick)}</text>
            </g>)}
            {labels.map((label, index) => index % step === 0 || index === labels.length - 1
                ? <text key={label + index} className="chart-x" x={x(index)} y={height - 8} textAnchor="middle">{label}</text> : null)}
            {area && <g opacity={highlight !== null && highlight !== first.key ? .18 : 1}>
                <path className="chart-area" d={area} fill={`url(#${id}-fill)`} />
            </g>}
            {series.map((line, order) => <g key={line.key} className={highlight === null ? '' : highlight === line.key ? 'chart-hi' : 'chart-dim'}>
                <path className="chart-line" d={path(line)} stroke={line.color} pathLength={1} style={{ animationDelay: `${order * 120}ms` }} />
                <path className="chart-hit" d={path(line)} onClick={() => toggle(line.key)} />
                {line.values.map((value, index) => value === null ? null : <circle key={index} className="chart-dot"
                    cx={x(index)} cy={y(value)} r={hover === index ? 4.5 : 2.6} fill={line.color}
                    onClick={() => toggle(line.key)}
                    style={{ animationDelay: `${300 + index * 40 + order * 120}ms` }} />)}
            </g>)}
            {hover !== null && hover < labels.length && <line className="chart-cross" x1={x(hover)} x2={x(hover)} y1={pad.top} y2={height - pad.bottom} />}
        </svg>
        {hover !== null && hover < labels.length && <div className={`chart-tip ${x(hover) / width < 0.34 ? 'tip-left' : x(hover) / width > 0.66 ? 'tip-right' : 'tip-center'}`}
            style={{ left: `${(x(hover) / width) * 100}%` }}>
            <b>{labels[hover]}</b>
            {series.map((line) => <span key={line.key}><i style={{ background: line.color }} />{line.label}
                <em>{line.values[hover] === null ? '—' : `${format(line.values[hover]!)}${line.unit ?? ''}`}</em></span>)}
        </div>}
    </div>;
}
