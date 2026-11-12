import { Tooltip } from 'antd';
import { categories, categoryLabels, leagueNorms, playerTitle } from '../archetype';
import type { Standing } from '../scouting';
import { useCountUp } from '../motion';
import type { Archetype, CategoryScores } from '../archetype';

/** Ring color steps with the rating, like the game's overall badge. */
export function ratingColor(value: number): string {
    return value >= 90 ? 'var(--hof)' : value >= 80 ? 'var(--primary)' : value >= 70 ? 'var(--info)' : value >= 60 ? 'var(--warning)' : 'var(--muted)';
}

export function OverallRing({ value, label = '总评', large = false, title }: {
    value: number | null | undefined; label?: string; large?: boolean; title?: string;
}) {
    const shown = useCountUp(value ?? null);
    const ring = <div className={`overall-ring${large ? ' large' : ''}`} role="img" aria-label={`${label} ${shown ?? '未知'}`}
        style={{ '--value': shown ?? 0, '--ring': ratingColor(shown ?? 0) } as React.CSSProperties}>
        <span><strong>{shown ?? '—'}</strong><small>{label}</small></span>
        <i className="overall-ring-glow" aria-hidden />
    </div>;
    return title ? <Tooltip title={title}>{ring}</Tooltip> : ring;
}

export function CategoryBars({ scores }: { scores: CategoryScores }) {
    return <>{categories.map((category, order) => <div className="category-bar" key={category} style={{ '--i': order } as React.CSSProperties}>
        <span>{categoryLabels[category]}</span>
        <div className="category-bar-track"><div className="category-bar-fill"
            style={{ width: `${Math.max(0, Math.min(99, scores[category]))}%`, '--bar': `var(--${category})` } as React.CSSProperties} /></div>
        <b>{scores[category]}</b>
    </div>)}</>;
}

export const archetypeHint = '游戏未提供球员类型时，按六维能力中相对联盟最突出的两项估算';

/** Player title: the game's own player type, or the six-category estimate when it is missing. */
export function ArchetypeLabel({ archetype, summary }: { archetype: Archetype | null; summary?: { playerType?: string | null; offensiveType?: string | null } | null }) {
    const title = playerTitle(summary, archetype?.name);
    if (!title) {
        return null;
    }
    return <Tooltip title={title.detail}><span className={`player-card-archetype ${title.fromGame ? '' : 'estimated'}`}>{title.name}</span></Tooltip>;
}

/** Six-axis radar of the category scores, with the league average as a dashed outline. */
export function Radar({ scores }: { scores: CategoryScores }) {
    const size = 184;
    const center = size / 2;
    const radius = 64;
    const point = (index: number, value: number) => {
        const angle = (Math.PI * 2 * index) / categories.length - Math.PI / 2;
        const r = (Math.max(25, Math.min(99, value)) - 25) / 74 * radius;
        return [center + Math.cos(angle) * r, center + Math.sin(angle) * r];
    };
    const outline = (values: number[]) => values.map((value, index) => point(index, value).join(',')).join(' ');
    return <svg className="radar" key={categories.map((category) => scores[category]).join('-')} viewBox={`0 0 ${size} ${size}`} role="img"
        aria-label={categories.map((category) => `${categoryLabels[category]} ${scores[category]}`).join('，')}>
        {[50, 75, 99].map((level) => <polygon key={level} className="grid" points={outline(categories.map(() => level))} />)}
        {categories.map((category, index) => {
            const [x, y] = point(index, 99);
            return <line key={category} className="axis" x1={center} y1={center} x2={x} y2={y} />;
        })}
        <polygon className="league" points={outline(categories.map((category) => leagueNorms[category].mean))} />
        <polygon className="shape" points={outline(categories.map((category) => scores[category]))} />
        {categories.map((category, index) => {
            const angle = (Math.PI * 2 * index) / categories.length - Math.PI / 2;
            const x = center + Math.cos(angle) * (radius + 16);
            const y = center + Math.sin(angle) * (radius + 14);
            return <text key={category} x={x} y={y} textAnchor="middle" dominantBaseline="middle">
                {categoryLabels[category]} <tspan className="value">{scores[category]}</tspan></text>;
        })}
    </svg>;
}

/** League and same-position percentile bars; the marker is the league median. */
export function PercentileTable({ rows, position }: { rows: Standing[]; position: string | null }) {
    return <table className="pct-table">
        <thead><tr><th>项目</th><th>值</th><th>联盟百分位</th><th>全联盟</th><th>{position ?? '同位置'}</th></tr></thead>
        <tbody>{rows.map((row) => <tr key={row.category}>
            <td>{row.category === 'overall' ? '总评' : categoryLabels[row.category]}</td>
            <td className="v">{row.value}</td>
            <td><div className="pct-bar"><i style={{ width: `${row.league}%`,
                '--bar': row.category === 'overall' ? 'var(--primary)' : `var(--${row.category})` } as React.CSSProperties} />
                <em style={{ left: '50%' }} /></div></td>
            <td className={`pct${row.league >= 90 ? ' top' : ''}`}>{row.league}%</td>
            <td className={`pct${(row.position ?? 0) >= 90 ? ' top' : ''}`}>{row.position === null ? '—' : `${row.position}%`}</td>
        </tr>)}</tbody>
    </table>;
}