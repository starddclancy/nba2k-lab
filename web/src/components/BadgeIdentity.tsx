import { useState } from 'react';
import { badgeIcon, badgeName, findBadge, tierNames } from '../buildCatalog';
import type { Tier } from '../builds';
import { badgeStateNames, type BadgeVisualState } from '../badgePresentation';

export default function BadgeIdentity({ name, tier, state, personality = false, compact = false, plain = false }: {
    name: string;
    tier?: Tier | null;
    state?: BadgeVisualState;
    personality?: boolean;
    compact?: boolean;
    /** Leave out the native title (a richer tooltip wraps the badge). */
    plain?: boolean;
}) {
    const [failed, setFailed] = useState<string | null>(null);
    const image = badgeIcon(name, personality);
    const chinese = badgeName(name);
    const visual = state ?? (tier === null ? 'unequipped' : tier);
    const label = visual ? badgeStateNames[visual] : null;
    return <span className={`badge-identity${compact ? ' compact' : ''}`}
        data-badge-state={visual}
        title={plain ? undefined : `${findBadge(name)?.name ?? name}${label ? ` · ${label}` : ''}${personality ? ' · 性格类别共用原版图标' : ''}`}>
        {image && failed !== image
            ? <img src={image} className={visual ? `badge-visual-${visual}` : undefined}
                alt={`${chinese}${label ? `·${label}` : ''}徽章图标`} loading="lazy" onError={() => setFailed(image)} />
            : <span className="badge-image-missing" title="原版图标对应关系待核对">待核对</span>}
        <span className="badge-identity-text"><strong>{chinese}</strong>
            {!compact && <small>{findBadge(name)?.name ?? name}</small>}</span>
        {tier && <span className={`badge-tier tier-${tier}`}>{tierNames[tier]}</span>}
    </span>;
}
