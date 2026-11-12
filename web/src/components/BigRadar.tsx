import { useState } from 'react';
import { Segmented } from 'antd';
import { categories, categoryLabels, leagueNorms } from '../archetype';
import type { CategoryScores } from '../archetype';
import { positions } from '../scouting';
import type { Averages } from '../scouting';

export type RadarLayer = { key: string; label: string; color: string; scores: CategoryScores };

const leagueColor = 'var(--muted)';
const positionColor = 'var(--warning)';

/**
 * Large six-axis chart for one or more players: filled glowing shapes and value labels at every
 * vertex. Optional dashed reference outlines: the league average and a position average (the
 * player's own position by default, switchable to any position). Without averages it falls back
 * to the fixed league norms.
 */
export default function BigRadar({ layers, size = 360, compact = false, averages, position, highlight = null, onHighlight }: {
    layers: RadarLayer[];
    size?: number;
    compact?: boolean;
    averages?: Averages | null;
    /** Position of the (first) player, preselected for the position average. */
    position?: string | null;
    highlight?: string | null;
    onHighlight?: (key: string | null) => void;
}) {
    const [shown, setShown] = useState<'both' | 'league' | 'position' | 'none'>('both');
    const [chosen, setChosen] = useState<string | null>(null);
    const center = size / 2;
    // Reserve space for the existing label sizes in the compact player chart.
    const radius = compact ? Math.min(size * 0.33, center - 54) : size * 0.33;
    const point = (index: number, value: number) => {
        const angle = (Math.PI * 2 * index) / categories.length - Math.PI / 2;
        const r = (Math.max(25, Math.min(99, value)) - 25) / 74 * radius;
        return [center + Math.cos(angle) * r, center + Math.sin(angle) * r] as const;
    };
    const outline = (values: number[]) => values.map((value, index) => point(index, value).join(',')).join(' ');
    const single = layers.length === 1;
    const fallback = Object.fromEntries(categories.map((category) => [category, leagueNorms[category].mean])) as CategoryScores;
    const league = averages?.league ?? fallback;
    const pos = chosen ?? (position && (positions as readonly string[]).includes(position) ? position : null);
    const positionAvg = pos ? averages?.byPosition[pos] ?? null : null;
    const showLeague = shown === 'both' || shown === 'league';
    const showPosition = (shown === 'both' || shown === 'position') && positionAvg !== null;
    const references = [
        showLeague ? { key: 'league', label: `联盟平均${averages?.counts.league ? `（${averages.counts.league} 人）` : ''}`, color: leagueColor, scores: league, dash: '4 4' } : null,
        showPosition ? { key: 'pos', label: `${pos} 平均（${averages?.counts[pos!] ?? 0} 人）`, color: positionColor, scores: positionAvg!, dash: '7 3' } : null,
    ].filter(Boolean) as { key: string; label: string; color: string; scores: CategoryScores; dash: string }[];

    return <div className="big-radar-wrap">
        {averages && <div className="radar-tools">
            <Segmented block size="small" value={shown} onChange={(value) => setShown(value as typeof shown)}
                options={[{ value: 'both', label: '联盟+位置' }, { value: 'league', label: '联盟' }, { value: 'position', label: '位置' }, { value: 'none', label: '隐藏' }]} />
            <Segmented block size="small" value={pos ?? ''} onChange={(value) => setChosen(String(value) || null)}
                options={positions.map((item) => ({ value: item, label: item, disabled: !averages.byPosition[item] }))} />
        </div>}
        <svg className="big-radar" viewBox={`0 0 ${size} ${size}`} role="img"
            aria-label={[...layers, ...references].map((layer) => `${layer.label}：${categories.map((c) => `${categoryLabels[c]} ${layer.scores[c]}`).join('，')}`).join('；')}>
            <defs>
                {layers.map((layer) => <radialGradient key={layer.key} id={`br-${layer.key}`}>
                    <stop offset="0%" stopColor={layer.color} stopOpacity=".05" />
                    <stop offset="100%" stopColor={layer.color} stopOpacity={single ? '.45' : '.22'} />
                </radialGradient>)}
            </defs>
            {[40, 60, 80, 99].map((level) => <g key={level}>
                <polygon className="br-ring" points={outline(categories.map(() => level))} />
                <text className="br-level" x={center + 3} y={point(0, level)[1] - 2}>{level}</text>
            </g>)}
            {categories.map((category, index) => {
                const [x, y] = point(index, 99);
                return <line key={category} className="br-axis" x1={center} y1={center} x2={x} y2={y} />;
            })}
            {references.map((reference) => <polygon key={reference.key} className="br-reference" points={outline(categories.map((category) => reference.scores[category]))}
                style={{ stroke: reference.color, strokeDasharray: reference.dash }} />)}
            {layers.map((layer, order) => <g key={layer.key} className={`br-layer ${highlight !== null && highlight !== layer.key ? 'dimmed' : ''}`}
                onClick={onHighlight ? () => onHighlight(highlight === layer.key ? null : layer.key) : undefined}
                style={{ '--c': layer.color, animationDelay: `${order * 140}ms`, cursor: onHighlight ? 'pointer' : undefined } as React.CSSProperties}>
                <polygon className="br-shape" points={outline(categories.map((category) => layer.scores[category]))} fill={`url(#br-${layer.key})`} />
                {categories.map((category, index) => {
                    const [x, y] = point(index, layer.scores[category]);
                    return <circle key={category} className="br-dot" cx={x} cy={y} r={single ? 4 : 3.2} />;
                })}
            </g>)}
            {categories.map((category, index) => {
                const angle = (Math.PI * 2 * index) / categories.length - Math.PI / 2;
                const x = center + Math.cos(angle) * (radius + 34);
                const y = center + Math.sin(angle) * (radius + 26);
                const lines = references.length > 0 ? 3 : 2;
                return <g key={category} className="br-label">
                    <text x={x} y={y - (lines === 3 ? 12 : 4)} textAnchor="middle" style={{ fill: `var(--${category})` }}>{categoryLabels[category]}</text>
                    <text x={x} y={y + (lines === 3 ? 4 : 12)} textAnchor="middle" className="br-values">
                        {layers.map((layer, order) => <tspan key={layer.key} style={{ fill: layer.color }}>{order ? ' / ' : ''}{layer.scores[category]}</tspan>)}
                    </text>
                    {references.length > 0 && <text x={x} y={y + 18} textAnchor="middle" className="br-refs">
                        {references.map((reference, order) => <tspan key={reference.key} style={{ fill: reference.color }}>{order ? ' · ' : ''}{reference.key === 'league' ? '联' : pos}{reference.scores[category].toFixed(0)}</tspan>)}
                    </text>}
                </g>;
            })}
        </svg>
        {references.length > 0 && <div className="chart-legend radar-legend">
            {single && <span><i style={{ background: layers[0].color }} />{layers[0].label}</span>}
            {references.map((reference) => <span key={reference.key}><i className="dash" style={{ borderColor: reference.color }} />{reference.label}</span>)}
        </div>}
        {single && references.length > 0 && <table className="radar-diff"><thead><tr><th />{categories.map((category) => <th key={category} style={{ color: `var(--${category})` }}>{categoryLabels[category]}</th>)}</tr></thead>
            <tbody>{references.map((reference) => <tr key={reference.key}><td style={{ color: reference.color }}>比{reference.key === 'league' ? '联盟' : pos}</td>
                {categories.map((category) => {
                    const delta = Math.round((layers[0].scores[category] - reference.scores[category]) * 10) / 10;
                    return <td key={category} className={delta > 0 ? 'up' : delta < 0 ? 'down' : ''}>{delta > 0 ? '+' : ''}{delta.toFixed(1)}</td>;
                })}</tr>)}</tbody></table>}
    </div>;
}
