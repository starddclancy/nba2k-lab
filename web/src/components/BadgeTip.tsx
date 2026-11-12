import { attributes, tiers } from '../builds';
import type { Tier } from '../builds';
import { badgeDetails, badgeEffect, badgeName, findBadge, tierNames } from '../buildCatalog';

const attributeLabel = new Map<string, string>(attributes.map((item) => [item.id, item.label]));
const inches = (value: number) => `${Math.floor(value / 12)}'${value % 12}"`;

/**
 * Hover card of a badge: what it does in the game, its category, and what each tier needs.
 * The current tier (when known) is highlighted.
 */
export default function BadgeTip({ name, current }: { name: string; current?: Tier | null }) {
    const effect = badgeEffect(name);
    const details = badgeDetails(name);
    return <div className="badge-tip">
        <div className="badge-tip-head"><b>{badgeName(name)}</b><small>{findBadge(name)?.name ?? name}{details ? ` · ${details.category}` : ''}</small></div>
        <p className="badge-tip-effect">{effect ?? '暂无确认的效果说明'}</p>
        {details && <table className="badge-tip-tiers"><tbody>
            {tiers.map((tier) => {
                const requirement = details.tiers[tier];
                const terms = requirement?.terms ?? [];
                const height = terms.find((term) => term.minHeight !== undefined || term.maxHeight !== undefined);
                return <tr key={tier} className={current === tier ? 'on' : ''}>
                    <td><span className={`badge-tier tier-${tier}`}>{tierNames[tier]}</span></td>
                    <td>{terms.length === 0 ? '—' : terms.map((term) => `${attributeLabel.get(term.attribute) ?? term.attribute} ${term.minimum}`)
                        .join(requirement.logic === 'any' ? ' 或 ' : ' + ')}
                        {height && <small> · 身高 {height.minHeight !== undefined ? inches(height.minHeight) : ''}–{height.maxHeight !== undefined ? inches(height.maxHeight) : ''}</small>}
                    </td>
                </tr>;
            })}
        </tbody></table>}
        <div className="badge-tip-source">效果说明：NBA2KLab（已确认的 2K27 说明，中文翻译）</div>
    </div>;
}
