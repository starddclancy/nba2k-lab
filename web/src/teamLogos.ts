import catalog from './team-logo-catalog.json' with { type: 'json' };
import gameCatalog from './game-logo-catalog.json' with { type: 'json' };

export type TeamBrand = {
    label: string;
    logoReference?: string | null;
    logoReferences?: string[];
    historicYearRaw?: number | null;
};

const aliases: Record<string, string> = {
    'LA Clippers': 'los-angeles-clippers',
    'Seattle SuperSonics': 'seattle-supersonics',
};

const codes: Record<string, string[]> = {
    phi: ['philadelphia-76ers'],
    mil: ['milwaukee-bucks'],
    chi: ['chicago-bulls'],
    cle: ['cleveland-cavaliers'],
    bos: ['boston-celtics'],
    lac: ['los-angeles-clippers'],
    mem: ['memphis-grizzlies', 'vancouver-grizzlies'],
    atl: ['atlanta-hawks'],
    mia: ['miami-heat'],
    no: ['new-orleans-hornets', 'new-orleans-oklahoma-city-hornets', 'new-orleans-pelicans'],
    uta: ['utah-jazz', 'new-orleans-jazz'],
    sac: ['sacramento-kings'],
    ny: ['new-york-knicks'],
    lal: ['los-angeles-lakers', 'minneapolis-lakers'],
    orl: ['orlando-magic'],
    dal: ['dallas-mavericks'],
    bkn: ['new-jersey-nets', 'brooklyn-nets', 'new-york-nets'],
    den: ['denver-nuggets'],
    ind: ['indiana-pacers'],
    det: ['detroit-pistons'],
    tor: ['toronto-raptors'],
    hou: ['houston-rockets'],
    sa: ['san-antonio-spurs'],
    phx: ['phoenix-suns'],
    okc: ['seattle-supersonics', 'oklahoma-city-thunder'],
    min: ['minnesota-timberwolves'],
    por: ['portland-trail-blazers'],
    gs: ['golden-state-warriors'],
    was: ['washington-wizards', 'washington-bullets'],
    cha: ['charlotte-bobcats', 'charlotte-hornets'],
};

const resourceAliases: Record<string, string> = {
    'logos/logo007_secondary.iff': 'logos/logo007.iff',
    'logos/logo030_secondary.iff': 'logos/logo030.iff',
    'logo_nba_eastern_conference': 'logos/logo007.iff',
    'logo_nba_western_conference': 'logos/logo030.iff',
};
const gameAssets = new Map(gameCatalog.assets.map((asset) => [asset.resource, asset]));
const datedAssetsByTeam = new Map<string, { asset: (typeof gameCatalog.assets)[number]; endYear: number }[]>();
for (const asset of gameCatalog.assets) {
    const match = asset.resource.match(/^logos\/logo\d+([a-z]+)_(\d{4})\.iff$/);
    const slugs = match && codes[match[1]];
    if (!match || !slugs) {
        continue;
    }
    const start = Number(match[2]) - 1;
    const owners = catalog.filter((logo) => slugs.includes(logo.team) && start >= logo.from
        && (logo.until === null || start < logo.until));
    for (const slug of new Set(owners.length ? owners.map((logo) => logo.team) : [slugs.at(-1)!])) {
        datedAssetsByTeam.set(slug, [...(datedAssetsByTeam.get(slug) ?? []), {
            asset, endYear: Number(match[2]),
        }]);
    }
}
const specialTeamAssets: Record<string, string> = {
    rookie: 'logos/logo504.iff',
    rookies: 'logos/logo504.iff',
    sophomores: 'logos/logo505.iff',
    sophmores: 'logos/logo505.iff',
    'ballerz ballerz': 'logos/logo619.iff',
    'ringers ringers': 'logos/logo618.iff',
    elites: 'logos/logo604.iff',
    stars: 'logos/logo605.iff',
    usa: 'logos/logo504.iff',
    world: 'logos/logo505.iff',
    home: 'logos/logo_nba_current.iff',
    away: 'logos/logo_nba_current.iff',
};

function normalizeReference(value: string) {
    const reference = value.trim().replaceAll('\\', '/').toLowerCase();
    if (resourceAliases[reference]) {
        return resourceAliases[reference];
    }
    const path = reference.includes('/') ? reference : `logos/${reference}`;
    return path.endsWith('.iff') ? path : `${path}.iff`;
}

export function historicSeasonEnd(raw?: number | null) {
    if (!Number.isInteger(raw) || raw === undefined || raw === null || raw <= 0 || raw > 200) {
        return null;
    }
    // The live game stores 65 for 1964-65 and 101 for 2000-01.
    return 1900 + raw;
}

function datedGameLogo(slug: string, season: number, allowPrior = false) {
    const candidates = datedAssetsByTeam.get(slug) ?? [];
    const requestedEnd = season + 1;
    return candidates.find(({ endYear }) => endYear === requestedEnd)?.asset
        ?? (allowPrior ? candidates.filter(({ endYear }) => endYear <= requestedEnd)
            .sort((a, b) => b.endYear - a.endYear)[0]?.asset : null) ?? null;
}

/** Each live record selects its own artwork; a season filename is authoritative over the team name. */
export function resolveTeamLogo(team: TeamBrand) {
    const references = [...new Set([team.logoReference, ...(team.logoReferences ?? [])]
        .filter((value): value is string => !!value).map(normalizeReference))];
    for (const reference of references) {
        const asset = gameAssets.get(reference);
        if (asset) {
            return { ...asset, from: null, until: null, source: asset.resource };
        }
    }
    if (references.length === 0) {
        const special = specialTeamAssets[team.label.toLowerCase()];
        const asset = special ? gameAssets.get(special) : undefined;
        if (asset) {
            return { ...asset, from: null, until: null, source: asset.resource };
        }
    }
    const slug = aliases[team.label] ?? team.label.toLowerCase().replaceAll(' ', '-');
    const dated = references.map((reference) => reference.match(/^logos\/logo\d+([a-z]+)_(\d{4})\.iff$/))
        .find((match) => match && codes[match[1]]?.includes(slug));
    const end = dated ? Number(dated[2]) : historicSeasonEnd(team.historicYearRaw);
    if (end === null) {
        return null;
    }
    const season = end - 1;
    const exactGameLogo = datedGameLogo(slug, season);
    if (exactGameLogo) {
        return { ...exactGameLogo, from: null, until: null, source: exactGameLogo.resource };
    }
    const timelineLogo = catalog.find((logo) => logo.team === slug && season >= logo.from
        && (logo.until === null || season < logo.until));
    if (timelineLogo) {
        return timelineLogo;
    }
    const hasTimeline = catalog.some((logo) => logo.team === slug);
    const historicalGameLogo = hasTimeline ? null : datedGameLogo(slug, season, true);
    return historicalGameLogo
        ? { ...historicalGameLogo, from: null, until: null, source: historicalGameLogo.resource }
        : null;
}

/**
 * Team brands seen in the current roster, by label. Filled whenever the team list is read, so a
 * place that only knows a team's name (game logs, leaderboards, history rows) still shows its logo.
 */
const brands = new Map<string, TeamBrand>();
export function rememberBrands(teams: TeamBrand[]) {
    for (const team of teams) {
        if (team.label && (team.logoReference || team.logoReferences?.length || team.historicYearRaw)) brands.set(team.label, team);
    }
}
export const knownBrand = (label: string): TeamBrand | undefined => brands.get(label);
