import { useMemo } from 'react';
import { Button } from 'antd';
import { EditOutlined } from '@ant-design/icons';
import { archetypeOf, categoryLabels, playerTitle } from '../archetype';
import { archetypeShare, leaderCategories, leaders, overallHistogram } from '../insights';
import { categoryAverages, standings } from '../scouting';
import type { PlayerStats } from '../scouting';
import type { BrowserPlayer } from '../playerSources';
import type { BorrowedPhoto } from '../avatars';
import { teamLabel } from '../labels';
import { money } from '../tradeView';
import Avatar from './Avatar';
import { OverallRing, PercentileTable, ratingColor } from './PlayerProfile';
import BigRadar from './BigRadar';
import TeamIdentity from './TeamIdentity';
import EntityLink from './EntityLink';

type Pool = { scores: PlayerStats['scores']; overall: number | null; position: string | null }[];

/**
 * Right-hand panel of the player page. With a player selected it is a compact scouting report;
 * otherwise it summarizes the rows currently listed so the space always carries data.
 */
export default function InsightPanel({ rows, stats, pool, selected, borrowed, onEdit }: {
    rows: BrowserPlayer[];
    stats: Map<number, PlayerStats>;
    pool: Pool;
    selected: BrowserPlayer | null;
    borrowed?: BorrowedPhoto | null;
    onEdit: () => void;
}) {
    const averages = useMemo(() => categoryAverages(pool), [pool]);
    if (selected) {
        const stat = selected.source === 'roster' ? stats.get(selected.index) : undefined;
        const overall = selected.summary?.overall ?? selected.overall ?? null;
        const summary = selected.summary;
        const title = playerTitle(summary, stat?.scores ? archetypeOf(stat.scores).name : null);
        return <aside className="insight" key={selected.key}>
            <div className="insight-hero">
                <Avatar name={selected.name} team={selected.team} photoId={selected.photoId} borrowed={borrowed} size={64} loadImage />
                <div className="insight-hero-text">
                    <strong><EntityLink page="players" index={selected.index} source={selected.source}>{selected.name}</EntityLink></strong>
                    <span className="insight-archetype" title={title?.detail}>{title?.name ?? '—'}</span>
                    <span className="muted">{selected.team ? <TeamIdentity label={selected.team} linkIndex={selected.teamIndices?.[0]} size={18} text={teamLabel(selected.team)} /> : (selected.source === 'roster' ? '自由球员' : selected.source === 'prospects' ? '待选秀' : '新秀名单')}</span>
                </div>
                <OverallRing value={overall} />
            </div>
            <div className="insight-facts">
                <span>位置<b>{[summary?.position, summary?.secondaryPosition].filter(Boolean).join('/') || '—'}</b></span>
                <span>年龄<b>{summary?.age ?? '—'}</b></span>
                <span>身高<b>{summary?.heightCm ? `${Math.round(summary.heightCm)}cm` : '—'}</b></span>
                <span>体重<b>{summary?.weightKg ? `${summary.weightKg.toFixed(0)}kg` : '—'}</b></span>
                <span>潜力<b>{stat?.potential ?? '—'}</b></span>
                <span>合同<b>{stat?.yearsLeft != null ? `${stat.yearsLeft}年` : '—'}</b></span>
            </div>
            {stat?.salary ? <div className="insight-money"><span>本季薪资</span><b>{money(stat.salary)}</b></div> : null}
            {stat?.scores ? <>
                <div className="insight-title">能力分布 <span className="muted">虚线为联盟与同位置平均</span></div>
                <BigRadar size={280} compact averages={averages} position={summary?.position ?? null} layers={[{ key: 'p', label: selected.name, color: 'var(--primary)', scores: stat.scores }]} />
                <div className="insight-title">联盟排位</div>
                <PercentileTable position={summary?.position ?? null}
                    rows={standings({ scores: stat.scores, overall, position: summary?.position ?? null }, pool)} />
            </> : <div className="muted insight-empty">这名球员没有读取到能力值（选秀名单在完整编辑中查看）</div>}
            <Button type="primary" block icon={<EditOutlined />} onClick={onEdit} className="insight-edit">完整资料</Button>
        </aside>;
    }

    const listed = rows.map((row) => ({ index: row.source === 'roster' ? row.index : -1, name: row.name,
        overall: row.summary?.overall ?? row.overall ?? null }));
    const histogram = overallHistogram(listed);
    const peak = Math.max(1, ...histogram.map((bucket) => bucket.count));
    const values = listed.flatMap((row) => (row.overall === null ? [] : [row.overall]));
    const average = values.length ? (values.reduce((sum, value) => sum + value, 0) / values.length).toFixed(1) : '—';
    const stars = values.filter((value) => value >= 80).length;
    const salaries = rows.flatMap((row) => (row.source === 'roster' && stats.get(row.index)?.salary ? [stats.get(row.index)!.salary!] : []));
    const shares = archetypeShare(listed, stats);
    return <aside className="insight">
        <div className="insight-title">当前列表概览</div>
        <div className="insight-kpis">
            <div><span>球员</span><b>{rows.length}</b></div>
            <div><span>平均总评</span><b>{average}</b></div>
            <div><span>80+ 球星</span><b>{stars}</b></div>
            <div><span>薪资合计</span><b>{salaries.length ? `$${(salaries.reduce((s, v) => s + v, 0) / 1e6).toFixed(1)}M` : '—'}</b></div>
        </div>
        <div className="insight-title">总评分布</div>
        <div className="histogram" role="img" aria-label="总评分布">
            {histogram.map((bucket, order) => <div key={bucket.from} className="histogram-col" title={`${bucket.from}–${bucket.from + 4}：${bucket.count} 人`}>
                <i style={{ height: `${(bucket.count / peak) * 100}%`, '--ring': ratingColor(bucket.from + 2), '--i': order } as React.CSSProperties} />
                <span>{bucket.from}</span>
            </div>)}
        </div>
        <div className="insight-title">各项第一</div>
        <div className="leader-grid">
            {leaderCategories.map((category) => {
                const top = leaders(listed, stats, category, 1)[0];
                return <div key={category} className="leader" style={{ '--bar': `var(--${category})` } as React.CSSProperties}>
                    <span>{categoryLabels[category]}</span>
                    <b>{top ? top.value : '—'}</b>
                    <small title={top?.name}>{top?.name ?? '—'}</small>
                </div>;
            })}
        </div>
        <div className="insight-title">定位构成</div>
        <div className="share-list">
            {shares.length === 0 && <span className="muted">读取能力后显示</span>}
            {shares.map((item) => <div key={item.name} className="share-row">
                <span>{item.name}</span>
                <div className="category-bar-track"><div className="category-bar-fill" style={{ width: `${item.share}%` }} /></div>
                <b className="num">{item.share}%</b>
            </div>)}
        </div>
        <div className="muted insight-hint">点击左侧任意球员查看球探报告；双击打开完整编辑。</div>
    </aside>;
}
