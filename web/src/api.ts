import { rememberBrands } from './teamLogos';
import { call, isPreview } from './bridge';
import { displayReadCache } from './readCache';
import type { SchedulePreview } from './seasonGames';
import type { MatchEditPlan, MatchEditRequest, MatchSnapshot, MatchStatus } from './match';
import type {
    AvatarResult,
    BatchPreview,
    ChangeResult,
    GsPayload,
    MoveResult,
    DraftPickRow,
    LibraryApplyResult,
    LibraryEntry,
    LibraryKind,
    Placement,
    PlayerDetail,
    PlayerImportPreview,
    PlayerStatistics,
    PlayerRow,
    PresetList,
    PresetSaved,
    PresetValues,
    SessionInfo,
    TeamDetail,
    TeamItem,
    TeamRow,
} from './types';

/** Typed wrappers around the host commands so components never spell strings twice. */

export const connect = () => call<SessionInfo>('connect');

export const matchStatus = () => isPreview
    ? Promise.resolve<MatchStatus>({
        readsEnabled: true, writesEnabled: false, automaticDefault: false, automaticEnabled: false,
        reason: null, writeReason: '示例比赛仅供界面预览，不会连接或修改游戏',
    })
    : call<MatchStatus>('matchStatus');

let matchPending: Promise<MatchSnapshot> | null = null;

export function matchRead(): Promise<MatchSnapshot> {
    if (isPreview) {
        return import('./matchPreview').then((preview) => preview.readPreviewMatch());
    }
    // Share only an in-flight operation, including across page unmounts; never cache a sample.
    matchPending ??= call<MatchSnapshot>('matchRead').finally(() => { matchPending = null; });
    return matchPending;
}

export const matchPrepare = (readToken: string, edits: MatchEditRequest[]) =>
    isPreview
        ? import('./matchPreview').then((preview) => preview.preparePreviewMatch(readToken, edits))
        : call<MatchEditPlan>('matchPrepare', { readToken, edits });

export type MatchWriteResult = { writes: number; patches: number; undoDepth: number; transaction: string };

export const matchApply = (readToken: string, previewToken: string) =>
    call<MatchWriteResult>('matchApply', { readToken, previewToken });

export const matchUndo = () => call<MatchWriteResult>('matchUndo');

export const schedulePrepare = (gameId: number, date: string) =>
    call<SchedulePreview>('schedulePrepare', { gameId, date });
export const scheduleApply = (token: string) =>
    call<{ undoDepth: number; writes: number }>('scheduleApply', { token });
export const scheduleUndo = () => call<{ undoDepth: number; writes: number }>('scheduleUndo');

export const listPlayers = (query: string, team: string | null, scope: 'league' | 'all' = 'league') =>
    call<PlayerRow[]>('listPlayers', { query, team, scope });

const playerReadKey = (index: number, source: string) => `player:${source}:${index}`;
const playerStatsReadKey = (index: number, source: string) => `statistics:${source}:${index}`;
const teamReadKey = (index: number) => `team:${index}`;

export const peekPlayer = (index: number, source = 'roster') =>
    displayReadCache.peek<PlayerDetail>(playerReadKey(index, source));

export const readPlayerDisplay = (index: number, source = 'roster', options: { force?: boolean } = {}) =>
    displayReadCache.read(playerReadKey(index, source),
        () => call<PlayerDetail>('readPlayer', { index, source }), options);

/** Begin editing with a new host snapshot, superseding any earlier display request. */
export function freshPlayer(index: number, source = 'roster') {
    displayReadCache.invalidate(playerReadKey(index, source));
    return readPlayerDisplay(index, source, { force: true });
}

/** Existing editing callers always retain fresh-read semantics. */
export const readPlayer = freshPlayer;

export const peekPlayerStats = (index: number, source = 'roster') =>
    displayReadCache.peek<PlayerStatistics>(playerStatsReadKey(index, source));

export const readPlayerStatsDisplay = (index: number, source = 'roster', options: { force?: boolean } = {}) =>
    displayReadCache.read(playerStatsReadKey(index, source),
        () => call<PlayerStatistics>('readPlayerStats', { index, source }), options);

export function freshPlayerStats(index: number, source = 'roster') {
    displayReadCache.invalidate(playerStatsReadKey(index, source));
    return readPlayerStatsDisplay(index, source, { force: true });
}

export const readPlayerStats = freshPlayerStats;

/** The draft class table, which the game only populates inside the draft flow. */
export const readRookies = () =>
    call<{ status: string; items: PlayerRow[] }>('rookies');

export const readProspects = (query = '') =>
    call<{ status: string; items: PlayerRow[] }>('prospects', { query });

export const applyPlayer = (
    index: number,
    changes: Record<string, number | string | boolean>,
    source = 'roster',
    readToken?: string,
) => call<ChangeResult>('applyPlayer', { index, changes, source, readToken });

export const readBatch = (indices: number[], field: string, source = 'roster') =>
    call<BatchPreview>('readBatch', { indices, field, source });

/** Same field values across several players, one journaled transaction. */
export const applyBatch = (
    indices: number[],
    changes: Record<string, number | string | boolean>,
    source = 'roster',
    readToken?: string,
) =>
    call<{ journal: string; players: number; patches: number; undoDepth: number }>('applyBatch', {
        indices,
        changes,
        source,
        readToken,
    });

export const undoSingle = (index: number) => call<ChangeResult>('undoPlayer', { index });

/** Writes a JSON backup of every writable field to the editor's exports folder. */
export const exportPlayer = (index: number, source = 'roster') =>
    call<{ path: string; name: string; count: number }>('exportPlayer', { index, source });

/** Applies a previously exported backup onto this player (opens a file dialog in the host). */
export const importPlayer = (index: number, source = 'roster', readToken?: string) =>
    call<{
        cancelled: boolean;
        applied: number;
        skipped: number;
        patches: number;
        journal: string;
        source: string;
    }>('importPlayer', { index, source, readToken });

/** Parse once without applying; confirmation submits the returned decoded request through applyPlayer. */
export const previewPlayerImport = (index: number, source = 'roster', readToken?: string) =>
    call<PlayerImportPreview>('importPlayer', { index, source, readToken, preview: true });

export type JournalRow = {
    kind: string; name: string; directory: string; bytes: number; modified: string;
    detail?: {
        state: 'supported' | 'unsupported' | 'invalid'; format: string; status?: string; mode?: string;
        time?: string; error?: string; changes?: { subject: string; field: string; before: string; after: string }[];
    };
};
export const readJournals = () =>
    call<JournalRow[]>(
        'journalList',
    );

/** Local portrait overrides, cached as 128×128 PNGs under the editor data folder. */
export const avatarGet = (key: string) => call<AvatarResult>('avatarGet', { key });
export const avatarPick = (key: string) => call<AvatarResult>('avatarPick', { key });
export const avatarClear = (key: string) => call<AvatarResult>('avatarClear', { key });

/** Read-only browsing of the Teams domain; distinct from the roster-move `readTeams`. */
export const listTeamRecords = (scope: 'league' | 'all' = 'league') =>
    call<TeamItem[]>('listTeams', { scope }).then((rows) => { rememberBrands(rows); return rows; });
export const peekTeamRecord = (index: number) => displayReadCache.peek<TeamDetail>(teamReadKey(index));

export const readTeamRecordDisplay = (index: number, options: { force?: boolean } = {}) =>
    displayReadCache.read(teamReadKey(index), () => call<TeamDetail>('readTeam', { index }), options);

export function freshTeamRecord(index: number) {
    displayReadCache.invalidate(teamReadKey(index));
    return readTeamRecordDisplay(index, { force: true });
}

export const readTeamRecord = freshTeamRecord;
export const applyTeamRecord = (index: number, changes: Record<string, number | string | boolean>) =>
    call<{ team: string; patches: number; journal: string; undoDepth: number; note?: string }>(
        'applyTeam',
        { index, changes },
    );
export const undoTeamRecord = () =>
    call<{ team: string; restored: number; journal: string; undoDepth: number }>('undoTeam');

/** General GS (the settings-menu array) and in-match GS share one payload shape. */
export const gsRead = () => call<GsPayload>('gsRead');
export const gsApply = (changes: Record<string, number>) => call<GsPayload>('gsApply', { changes });
export const gsUndo = () => call<GsPayload>('gsUndo');

export const inMatchRead = () => call<GsPayload>('inMatchRead');
export const inMatchApply = (changes: Record<string, number>) =>
    call<GsPayload>('inMatchApply', { changes });
export const inMatchUndo = () => call<GsPayload>('inMatchUndo');

/** Roster move and trade. Previews are read-only; only the apply calls write to the game. */
export const readTeams = () => call<TeamRow[]>('teams').then((rows) => { rememberBrands(rows); return rows; });
export const readPlacement = (index: number) => call<Placement>('placement', { index });
export const planMove = (index: number, team: number, syncPayroll = false) =>
    call<MoveResult>('planMove', { index, team, syncPayroll });
export const applyMove = (index: number, team: number, syncPayroll = false) =>
    call<MoveResult>('applyMove', { index, team, syncPayroll });
/** Optional trade linkage; omitted options preserve the original write behavior. */
export type TradeOptions = { firstPicks?: number[]; secondPicks?: number[]; syncPayroll?: boolean };
export const planTrade = (first: number, second: number, options: TradeOptions = {}) =>
    call<MoveResult>('planTrade', { first, second, ...options });
export const applyTrade = (first: number, second: number, options: TradeOptions = {}) =>
    call<MoveResult>('applyTrade', { first, second, ...options });
export const readDraftPicks = () => call<DraftPickRow[]>('draftPicks');
export const undoMove = () => call<MoveResult>('undoMove');

/** GS presets: portable JSON files stored under the editor's own presets folder. */
export const presetList = () => call<PresetList>('presetList');
export const presetLoad = (name: string) => call<PresetValues>('presetLoad', { name });
export const presetSave = (name: string, values: Record<string, number>) =>
    call<PresetSaved>('presetSave', { name, values });
export const presetDelete = (name: string) =>
    call<{ name: string; deleted: boolean }>('presetDelete', { name });

export const libraryList = () => call<LibraryEntry[]>('libraryList');
export const librarySave = (index: number, kind: LibraryKind, name: string, notes = '') =>
    call<LibraryEntry>('librarySave', { index, kind, name, notes });
/** preview lists the changes without writing; readToken is only used when applying. */
export const libraryApply = (id: string, index: number, scope: LibraryKind, preview: boolean, readToken?: string) =>
    call<LibraryApplyResult>('libraryApply', { id, index, scope, preview, readToken });
export const libraryDelete = (id: string) => call<{ id: string; deleted: boolean }>('libraryDelete', { id });
export const libraryImport = () => call<LibraryEntry & { cancelled: boolean }>('libraryImport');
/** Read-only rating/contract columns for every roster player (one bulk request). */
export const readPlayerRatings = () => call<import('./scouting').RatingTable>('playerRatings');

export type TradePackage = {
    firstTeam: number;
    secondTeam: number;
    firstPlayers: number[];
    secondPlayers: number[];
    firstPicks: number[];
    secondPicks: number[];
    syncPayroll: boolean;
};
/** Team-to-team trade with any number of players and picks per side. Planning never writes. */
export const planPackage = (trade: TradePackage) => call<MoveResult>('planPackage', trade);
export const applyPackage = (trade: TradePackage) => call<MoveResult>('applyPackage', trade);
