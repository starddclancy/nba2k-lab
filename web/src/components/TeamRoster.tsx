import { useCallback, useEffect, useMemo, useState } from 'react';
import { App as AntApp, Empty, Segmented, Tooltip } from 'antd';
import { listPlayers, readPlayerRatings } from '../api';
import type { PlayerRow } from '../types';
import { archetypeOf, categories, categoryLabels, playerTitle } from '../archetype';
import { categoryAverages, poolOf, positions, statsByIndex } from '../scouting';
import type { RatingTable } from '../scouting';
import Avatar from './Avatar';
import BigRadar from './BigRadar';
import { ratingColor } from './PlayerProfile';
import { entityHref } from '../navigation';

type Sort = 'overall' | 'position' | 'age';

/**
 * Players of one team: who is on it, what the roster looks like (overall, age, positions, team
 * six-category profile against the league), and click a player to open the full editor.
 */
export default function TeamRoster({ teamLabel: label, teamIndex, accent, scope = 'league' }: {
    teamLabel: string; teamIndex: number; accent?: string | null; scope?: 'league' | 'all';
}) {
    const { message } = AntApp.useApp();
    const [players, setPlayers] = useState<PlayerRow[]>([]);
    const [ratings, setRatings] = useState<RatingTable | null>(null);
    const [sort, setSort] = useState<Sort>('overall');

    const load = useCallback(async () => {
        try {
            const [rows, table] = await Promise.all([listPlayers('', null, scope), readPlayerRatings().catch(() => null)]);
            setPlayers(rows);
            setRatings(table);
        } catch (error) {
            message.error((error as Error).message);
        }
    }, [message, scope]);
    useEffect(() => { void load(); }, [load]);

    const stats = useMemo(() => statsByIndex(ratings), [ratings]);
    const league = useMemo(() => categoryAverages(poolOf(players.filter((row) => stats.has(row.index)), stats)), [players, stats]);
    const roster = useMemo(() => {
        const mine = players.filter((row) => row.teamIndices?.includes(teamIndex) || (!row.teamIndices && row.team === label));
        const order = (row: PlayerRow) => positions.indexOf((row.summary?.position ?? 'C') as typeof positions[number]);
        return [...mine].sort((a, b) => sort === 'position' ? order(a) - order(b) || (b.summary?.overall ?? 0) - (a.summary?.overall ?? 0)
            : sort === 'age' ? (a.summary?.age ?? 99) - (b.summary?.age ?? 99)
                : (b.summary?.overall ?? b.overall ?? 0) - (a.summary?.overall ?? a.overall ?? 0));
    }, [players, teamIndex, label, sort]);

    const overalls = roster.flatMap((row) => (row.summary?.overall ?? row.overall) != null ? [(row.summary?.overall ?? row.overall)!] : []);
    const ages = roster.flatMap((row) => row.summary?.age != null ? [row.summary.age] : []);
    const mean = (values: number[]) => values.length ? values.reduce((sum, value) => sum + value, 0) / values.length : null;
    const top8 = [...overalls].sort((a, b) => b - a).slice(0, 8);
    const team = useMemo(() => categoryAverages(poolOf(roster, stats)).league, [roster, stats]);
    const byPosition = positions.map((position) => ({ position, count: roster.filter((row) => row.summary?.position === position).length }));

    if (roster.length === 0) {
        return <Empty className="team-roster-empty" image={Empty.PRESENTED_IMAGE_SIMPLE} description={players.length ? '这支球队名单里没有球员' : '正在读取名单'} />;
    }
    return <section className="team-roster" style={accent ? { '--team': accent } as React.CSSProperties : undefined}>
        <div className="team-roster-main">
            <div className="team-roster-facts">
                <div className="stat-tile"><span>平均总评</span><b className="stat-value">{mean(overalls)?.toFixed(1) ?? '—'}</b>
                    <small className="muted">前 8 人 {mean(top8)?.toFixed(1) ?? '—'} · 最高 {overalls.length ? Math.max(...overalls) : '—'}</small></div>
                <div className="stat-tile"><span>平均年龄</span><b className="stat-value">{mean(ages)?.toFixed(1) ?? '—'}</b>
                    <small className="muted">最年轻 {ages.length ? Math.min(...ages) : '—'} · 最年长 {ages.length ? Math.max(...ages) : '—'}</small></div>
                <div className="stat-tile"><span>位置分布</span>
                    <div className="pos-bars">{byPosition.map(({ position, count }) => <span key={position} title={`${position} ${count} 人`}>
                        <i style={{ height: `${Math.min(100, count * 25)}%` }} /><b>{count}</b><small>{position}</small></span>)}</div></div>
            </div>
            <div className="team-roster-head">
                <h3>球队阵容 <small className="muted">{roster.length} 人</small></h3>
                <Segmented size="small" value={sort} onChange={(value) => setSort(value as Sort)}
                    options={[{ value: 'overall', label: '按总评' }, { value: 'position', label: '按位置' }, { value: 'age', label: '按年龄' }]} />
            </div>
            <div className="team-roster-list">
                {roster.map((row, order) => {
                    const stat = stats.get(row.index);
                    const overall = row.summary?.overall ?? row.overall ?? null;
                    const title = playerTitle(row.summary, stat?.scores ? archetypeOf(stat.scores).name : null);
                    return <a key={row.key} className="roster-card" href={entityHref('players', row.index)}
                        style={{ animationDelay: `${Math.min(order, 20) * 22}ms` }}>
                        <Avatar name={row.name} team={row.team} photoId={row.photoId} size={40} loadImage />
                        <span className="roster-card-name"><b>{row.name}</b>
                            <small>{[row.summary?.position, row.summary?.secondaryPosition].filter(Boolean).join('/') || '—'} · {row.summary?.age ?? '—'} 岁{title ? ` · ${title.name}` : ''}</small></span>
                        {stat?.scores ? <Tooltip title={categories.map((category) => `${categoryLabels[category]} ${stat.scores![category]}`).join(' · ')}>
                            <span className="roster-card-bars">{categories.map((category) => <i key={category}
                                style={{ height: `${Math.max(8, (stat.scores![category] - 25) / 74 * 100)}%`, background: `var(--${category})` }} />)}</span>
                        </Tooltip> : <span />}
                        <span className="roster-card-ovr" style={{ color: overall !== null ? ratingColor(overall) : undefined }}>{overall ?? '—'}</span>
                    </a>;
                })}
            </div>
        </div>
        <div className="team-roster-radar">
            {team ? <BigRadar size={230} averages={league} layers={[{ key: 'team', label: `${label} 平均`, color: accent ?? 'var(--primary)', scores: team }]} />
                : <span className="muted">读取能力值后显示球队六维</span>}
        </div>
    </section>;
}
