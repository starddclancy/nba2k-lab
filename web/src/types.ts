/** Shared shapes mirrored from the C# bridge responses. */
import type { TeamBrand } from './teamLogos';

export type SessionInfo = {
    pid: number;
    base: string;
    roster: number;
    teams: number;
    draft: number;
    draftStatus: string;
    fields: number;
    seasonEndYear?: number | null;
    resourcePlayers?: number;
    resourceTeams?: number;
};

export type HeadScanInfo = {
    status: 'scanned' | 'not_scanned' | 'unavailable';
    flag: number | null;
    address: string | null;
    error: string | null;
};

export type PlayerRow = {
    key: string;
    index: number;
    name: string;
    team: string;
    photoId?: number | null;
    position?: string;
    overall?: number | null;
    summary?: PlayerSummary;
    category?: 'team' | 'freeAgent' | 'prospect' | 'historic' | 'unassigned';
    teamIndices?: number[];
    prospectIndex?: number;
    draftYear?: number;
    draftRound?: number;
    draftPick?: number;
    birthYear?: number;
    headScan?: HeadScanInfo;
};

export type PlayerSummary = {
    position: string | null;
    secondaryPosition: string | null;
    /** The game's own player type and offensive type (PLAYERDATA_PLAYERTYPE), e.g. OUTSIDE. */
    playerType?: string | null;
    offensiveType?: string | null;
    birthYear: number | null;
    birthMonth?: number | null;
    birthDay?: number | null;
    heightCm: number | null;
    weightKg: number | null;
    threePoint: number | null;
    age?: number | null;
    overall?: number | null;
};

export type FieldCell = {
    key: string;
    label: string;
    section: string;
    group: string;
    value: number | string | boolean | null;
    raw?: number | string | boolean;
    display: string;
    writable: boolean;
    kind: 'number' | 'text' | 'bool' | 'choice';
    options: string[];
    min?: number;
    max?: number;
    error?: string;
};

export type PlayerStatistics = {
    index: number;
    name: string;
    readToken: string;
    direct: FieldCell[];
    seasons: { selector: string; id: number; address: string; fields: FieldCell[] }[];
    warnings: string[];
};

export type PlayerDetail = {
    index: number;
    readToken: string;
    name: string;
    photoId?: number | null;
    address: string;
    team: string;
    teamLogoReference?: string | null;
    teamBrand?: TeamBrand | null;
    sections: {
        name: string;
        fields: FieldCell[];
        state?: 'loaded' | 'absent';
        message?: string | null;
    }[];
    errorCount?: number;
    errorsBySection?: Record<string, number>;
    unavailableCount?: number;
    unavailableBySection?: Record<string, number>;
    headScan?: HeadScanInfo;
    summary?: PlayerSummary;
};

export type BatchPreview = {
    readToken: string;
    field: FieldCell;
    players: { index: number; name: string; value: number | string | boolean; display: string }[];
};

export type ChangeResult = {
    journal: string;
    patches: number;
    changes: { field: string; before: string; after: string }[];
    undoDepth: number;
};

/** One of the 70 GS sliders, with both sides already decoded to percentages. */
export type GsFieldRow = {
    id: string;
    label: string;
    group: string;
    user: number | null;
    cpu: number | null;
};

export type GsPayload = {
    notice?: string;
    fields: GsFieldRow[];
    groups: { id: string; label: string }[];
    sides: { id: string; label: string }[];
    undoDepth: number;
    writeAttempts: number;
};

export type TeamRow = {
    index: number;
    label: string;
    logoReference?: string | null;
    logoReferences?: string[];
    historicYearRaw?: number | null;
    used: number;
    capacity: number;
    open: number[];
};

export type Placement = {
    team: string;
    teamIndex: number;
    slot: number;
    salary?: number;
    yearsLeft?: number;
    teamPayroll?: number;
    extrasError?: string;
};

export type MoveResult = {
    kind: string;
    summary: string;
    patchCount: number;
    playerCount: number;
    teamCount: number;
    applied: boolean;
    journal: string;
    undo?: boolean;
    /** Linked changes such as payroll updates and draft pick transfers. */
    details?: string[];
};

export type DraftPickRow = {
    index: number;
    ownerIndex: number;
    owner: string;
    originalIndex: number | null;
    original: string | null;
    year: number | null;
    round: number;
    order: number;
    used: boolean;
    player: string | null;
    /** Protection or swap terms exist but are not decoded. */
    hasTerms: boolean;
};

export type PresetFile = {
    name: string;
    bytes: number;
    modified: string;
};

export type PresetList = {
    directory: string;
    files: PresetFile[];
};

export type PresetValues = {
    name: string;
    count: number;
    values: { key: string; value: number }[];
};

export type PresetSaved = {
    name: string;
    path: string;
    count: number;
};

/** Local portrait override: the game itself stores no photos. */
export type AvatarResult = {
    key: string;
    found: boolean;
    dataUrl?: string;
    cancelled?: boolean;
    deleted?: boolean;
};

export type TeamItem = {
    key: string;
    index: number;
    label: string;
    logoReference?: string | null;
    logoReferences?: string[];
    historicYearRaw?: number | null;
};

export type TeamField = {
    key: string;
    label: string;
    section: string;
    group: string;
    value: number | string | boolean | null;
    display: string;
    writable: boolean;
    options: string[];
    kind: 'number' | 'text' | 'bool' | 'choice';
    playerIndex?: number;
    playerName?: string;
};

export type TeamDetail = {
    index: number;
    label: string;
    logoReference?: string | null;
    logoReferences?: string[];
    historicYearRaw?: number | null;
    address: string;
    sections: { name: string; fields: TeamField[] }[];
};

export type LibraryKind = 'player' | 'face';
export type LibraryEntry = {
    id: string;
    name: string;
    kind: LibraryKind;
    created: string;
    sourcePlayer: string;
    fieldCount: number;
    notes: string;
};
export type LibraryApplyResult = {
    entry: LibraryEntry;
    index: number;
    player: string;
    scope: LibraryKind;
    changeCount: number;
    skipped: number;
    changes: {
        field: string;
        label: string;
        before: unknown;
        after: unknown;
        beforeField?: FieldCell;
        afterField?: FieldCell;
    }[];
    applied: boolean;
    patches?: number;
    journal?: string;
    undoDepth?: number;
};
export type PlayerImportPreview = { cancelled: true } | {
    cancelled: false;
    index: number;
    source: string;
    fileName: string;
    readToken: string;
    changeCount: number;
    skipped: number;
    changes: LibraryApplyResult['changes'];
    requestChanges: Record<string, number | string | boolean>;
};
