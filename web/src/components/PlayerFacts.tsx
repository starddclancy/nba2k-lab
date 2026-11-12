import type { PlayerSummary } from '../types';

export default function PlayerFacts({ summary }: { summary?: PlayerSummary }) {
    const positions = [...new Set([summary?.position, summary?.secondaryPosition].filter(Boolean))];
    const facts = [
        positions.length > 0 ? positions.join(' / ') : '位置未知',
        summary?.age != null ? `${summary.age} 岁` : '年龄未知',
        summary?.heightCm != null ? `${Number(summary.heightCm.toFixed(1))} cm` : null,
        summary?.weightKg != null ? `${Number(summary.weightKg.toFixed(1))} kg` : null,
    ].filter(Boolean).join(' · ');
    return <div className="player-facts" data-summary={JSON.stringify(summary ?? null)} title={facts}>{facts}</div>;
}
