import { useState } from 'react';
import { teamLabel } from '../labels';
import { historicSeasonEnd, knownBrand, resolveTeamLogo } from '../teamLogos';
import type { TeamBrand } from '../teamLogos';
import EntityLink from './EntityLink';

type Props = TeamBrand & { size?: number; text?: string; linkIndex?: number };

export default function TeamIdentity({ label, logoReference, logoReferences, historicYearRaw, size = 28, text, linkIndex }: Props) {
    // Only a name given: use the brand read with the team list, so the logo still shows.
    const brand = !logoReference && !logoReferences?.length && !historicYearRaw ? knownBrand(label) : undefined;
    const logo = resolveTeamLogo(brand ?? { label, logoReference, logoReferences, historicYearRaw });
    const src = logo ? `${import.meta.env.BASE_URL}team-logos/${logo.file}` : null;
    const conference = logo?.file === 'game-logo007.png' || logo?.file === 'game-logo030.png';
    const [failed, setFailed] = useState<string | null>(null);
    const seasonEnd = historicSeasonEnd(historicYearRaw);
    const title = logo && src !== failed
        ? `${teamLabel(label)} · ${seasonEnd !== null
            ? `${seasonEnd - 1}-${String(seasonEnd % 100).padStart(2, '0')} 赛季`
            : logo.from === null ? '游戏内队徽' : `${logo.from}${logo.until ? `–${logo.until}` : ' 起'}`}`
        : `${teamLabel(label)} · 队徽待核实`;
    return <EntityLink page="teams" index={linkIndex}><span className="team-identity" data-team={label} data-logo-reference={logoReference ?? ''}
        data-historic-year={historicYearRaw ?? ''} title={title}>
        {src && src !== failed
            ? <img className={`team-logo${conference ? ' team-logo-conference' : ''}`} src={src} alt={`${teamLabel(label)} 队徽`}
                width={size} height={size} onError={() => setFailed(src)} />
            : <span className="team-logo team-logo-pending" role="img" aria-label="队徽待核实"
                style={{ width: size, height: size }}>?</span>}
        <span className="team-identity-name">{text ?? teamLabel(label)}</span>
    </span></EntityLink>;
}
