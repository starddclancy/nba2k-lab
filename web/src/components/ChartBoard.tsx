import { useEffect, useState } from 'react';
import { Segmented, Select } from 'antd';
import type { Metric, Mode } from '../metrics';
import { seasonLabel } from '../seasonTrends';
import MetricChart, { seriesColors } from './MetricChart';

export type Subject<S, G> = { key: string; label: string; seasons: (S & { year: number })[]; games: G[] };
type Spec = { id: number; source: 'season' | 'game'; metrics: string[]; compare: string[] };

const comparePalette = ['var(--primary)', 'var(--warning)', 'var(--info)', 'var(--physical)', 'var(--success)', 'var(--danger)'];
let nextId = 1;

/**
 * A board of line charts the user builds: add or remove charts, pick the stats of each chart,
 * and in the second dropdown add other players or teams to draw the first picked stat for them
 * too. Each chart reads season records or recorded games. Comparison focus is shared with
 * the page; individual metric focus stays within its chart.
 */
export default function ChartBoard<S, G>({ base, subject, options, onNeed, seasonMetrics, gameMetrics, mode, onMode, defaults, highlight, onHighlight, metricRequest, selectedMetric, onSelectedMetric, lockedCompare }: {
    /** The page's own subject; always drawn. Null on pages where every subject is optional. */
    base: string | null;
    subject: (key: string) => Subject<S, G> | null;
    /** Subjects that can be added for comparison. */
    options: { value: string; label: string }[];
    /** Ask the page to load these subjects' data. */
    onNeed: (keys: string[]) => void;
    seasonMetrics: Metric<S>[];
    gameMetrics: Metric<G>[];
    mode: Mode;
    onMode: (mode: Mode) => void;
    defaults: { source: 'season' | 'game'; metrics: string[] }[];
    highlight?: string | null;
    onHighlight?: (key: string | null) => void;
    selectedMetric?: string | null;
    metricRequest?: { key: string } | null;
    onSelectedMetric?: (key: string | null) => void;
    /** Comparison subjects every chart starts with (comparison page). */
    lockedCompare?: string[];
}) {
    const [specs, setSpecs] = useState<Spec[]>(() => defaults.map((item) => ({ id: nextId++, ...item, compare: lockedCompare ?? [] })));
    useEffect(() => {
        if (!metricRequest) return;
        setSpecs((list) => list.map((spec, index) => index === 0 ? { ...spec, metrics: [metricRequest.key],
            source: spec.source === 'game' && !gameMetrics.some((metric) => metric.key === metricRequest.key) ? 'season' : spec.source } : spec));
    }, [metricRequest]); // eslint-disable-line react-hooks/exhaustive-deps
    useEffect(() => {
        if (lockedCompare) setSpecs((list) => list.map((spec) => ({ ...spec, compare: lockedCompare })));
    }, [lockedCompare?.join('|')]); // eslint-disable-line react-hooks/exhaustive-deps
    useEffect(() => {
        const wanted = [...new Set(specs.flatMap((spec) => spec.compare))];
        if (wanted.length) onNeed(wanted);
    }, [specs.map((spec) => spec.compare.join(',')).join('|')]); // eslint-disable-line react-hooks/exhaustive-deps

    const update = (id: number, change: Partial<Spec>) => setSpecs((list) => list.map((spec) => (spec.id === id ? { ...spec, ...change } : spec)));
    const add = () => setSpecs((list) => [...list, { id: nextId++, source: list[list.length - 1]?.source ?? 'season', metrics: ['pts'], compare: lockedCompare ?? [] }]);

    const chart = (spec: Spec) => {
        const keys = [...new Set([...(base ? [base] : []), ...spec.compare])];
        const subjects = keys.map(subject).filter(Boolean) as Subject<S, G>[];
        const comparing = subjects.length > 1 || lockedCompare !== undefined;
        const primary = spec.id === specs[0]?.id;
        const colorOf = (key: string) => comparePalette[keys.indexOf(key) % comparePalette.length];
        const sourcePicker = <Segmented size="small" value={spec.source} onChange={(value) => {
            update(spec.id, { source: value as Spec['source'] });
            if (primary) onSelectedMetric?.(null);
        }}
            options={[{ value: 'season', label: '逐季' }, { value: 'game', label: '逐场' }]} />;
        const compare = <Select size="small" mode="multiple" maxCount={5} showSearch optionFilterProp="label" placeholder="＋ 对比球员/球队" style={{ minWidth: 170, maxWidth: 360 }}
            value={spec.compare} onChange={(value: string[]) => update(spec.id, { compare: value })} popupMatchSelectWidth={false}
            options={options.filter((option) => option.value !== base)} maxTagCount="responsive" />;
        const common = { key: spec.id, picked: spec.metrics, onPick: (metrics: string[]) => {
            update(spec.id, { metrics });
            if (primary) onSelectedMetric?.(null);
        }, compare: <>{sourcePicker}{compare}</>,
            onRemove: specs.length > 1 ? () => {
                setSpecs((list) => list.filter((item) => item.id !== spec.id));
                if (primary) onSelectedMetric?.(null);
            } : undefined,
            highlight: comparing ? highlight : primary ? selectedMetric : undefined,
            onHighlight: comparing ? onHighlight : primary ? onSelectedMetric : undefined };
        if (spec.source === 'season') {
            const years = [...new Set(subjects.flatMap((item) => item.seasons.map((row) => row.year)))].sort((a, b) => a - b);
            const rowsOf = (item: Subject<S, G>) => years.map((year) => item.seasons.find((row) => row.year === year) ?? null);
            return comparing
                ? <MetricChart<S> {...common} title="逐季对比" hint={`第一个数据项 · ${subjects.length} 个对象`} metrics={seasonMetrics} mode={mode} onMode={onMode}
                    labels={years.map(seasonLabel)} entities={subjects.map((item) => ({ key: item.key, label: item.label, color: colorOf(item.key), rows: rowsOf(item) }))} />
                : <MetricChart<S> {...common} title="逐季走势" metrics={seasonMetrics} mode={mode} onMode={onMode}
                    labels={years.map(seasonLabel)} rows={subjects[0] ? rowsOf(subjects[0]) : []} />;
        }
        const length = Math.max(0, ...subjects.map((item) => item.games.length));
        // Align games on the most recent one so "the last N games" line up across subjects.
        const rowsOf = (item: Subject<S, G>) => Array.from({ length }, (_, i) => item.games[item.games.length - length + i] ?? null);
        const labels = Array.from({ length }, (_, i) => (length - i === 1 ? '最近一场' : `前 ${length - i - 1} 场`));
        return comparing
            ? <MetricChart<G> {...common} title="逐场对比" hint="按最近一场对齐" metrics={gameMetrics} labels={labels}
                entities={subjects.map((item) => ({ key: item.key, label: item.label, color: colorOf(item.key), rows: rowsOf(item) }))} />
            : <MetricChart<G> {...common} title="逐场走势" hint={length ? `游戏记录 ${length} 场` : '当前赛季暂无逐场记录'} metrics={gameMetrics} labels={labels}
                rows={subjects[0] ? rowsOf(subjects[0]) : []} />;
    };

    return <div className="chart-board">
        <div className="chart-grid-2">{specs.map(chart)}</div>
        <button type="button" className="chart-add" onClick={add}>＋ 添加一张折线图</button>
    </div>;
}

export { seriesColors };
