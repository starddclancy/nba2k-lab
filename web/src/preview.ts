import type { FieldCell, PlayerDetail, PlayerRow, PlayerStatistics, PlayerSummary, TeamItem } from './types';
import playerSchema from '../../src/Nba2k27Editor.Core/Data/offsets_players.json';
import dropdownSchema from '../../src/Nba2k27Editor.Core/Data/dropdowns.json';
import { previewHistory } from './historyPreview';

type AuthoredField = {
    normalized_name: string;
    display_name: string;
    versions: Record<string, Record<string, unknown>>;
};
type Sections = Record<string, Record<string, AuthoredField[]>>;

const teams: TeamItem[] = [
    { key: '0', index: 0, label: 'Philadelphia 76ers', logoReference: 'logo000' },
    { key: '1', index: 1, label: 'Seattle SuperSonics', logoReference: 'logo024okc_1996' },
    { key: '2', index: 2, label: 'Los Angeles Lakers', logoReference: 'logo015' },
];
const players: PlayerRow[] = [
    { key: '1', index: 1, name: 'Sample Guard', team: teams[0].label, category: 'team', teamIndices: [0], photoId: 9820,
        summary: { position: 'PG', secondaryPosition: 'SG', playerType: 'INSIDE', offensiveType: 'ATHLETIC', birthYear: 1988, heightCm: 193.04, weightKg: 90.7, threePoint: 85, age: 19, overall: 80 } },
    { key: '2', index: 2, name: 'Sample Forward', team: teams[1].label, category: 'team', teamIndices: [1],
        summary: { position: 'SF', secondaryPosition: 'PF', playerType: 'PLAYMAKING', offensiveType: 'ALL_AROUND', birthYear: 1988, heightCm: 208.28, weightKg: 108.9, threePoint: 88, age: 19, overall: 84 } },
    { key: '3', index: 3, name: 'Sample Center', team: '', category: 'freeAgent', teamIndices: [],
        summary: { position: 'C', secondaryPosition: null, playerType: 'OUTSIDE', offensiveType: 'OUTSIDE', birthYear: 1985, heightCm: 215.9, weightKg: 117.9, threePoint: 40, age: 22, overall: 85 } },
    { key: '4', index: 4, name: 'Sample Prospect', team: '', category: 'prospect', teamIndices: [], draftYear: 2007,
        summary: { position: 'SG', secondaryPosition: 'SF', playerType: 'DEFENSIVE', offensiveType: 'ATHLETIC', birthYear: 1989, heightCm: 198.12, weightKg: 95.3, threePoint: 82, age: 18, overall: 78 } },
    { key: '5', index: 5, name: 'Sample Wing', team: teams[0].label, category: 'team', teamIndices: [0],
        summary: { position: 'SF', secondaryPosition: 'SG', playerType: 'REBOUNDING', offensiveType: 'INSIDE', birthYear: 1990, heightCm: 203.2, weightKg: 99.8, threePoint: 88, age: 17, overall: 81 } },
    { key: '6', index: 6, name: 'Sample Unknown', team: teams[0].label, category: 'team', teamIndices: [0] },
    { key: '7', index: 7, name: 'Sample Rookie', team: '', category: 'prospect', teamIndices: [], draftYear: 2007,
        summary: { position: 'C', secondaryPosition: 'PF', playerType: 'OFFENSIVE', offensiveType: 'OUTSIDE', birthYear: 1988, heightCm: 213.36, weightKg: 113.4, threePoint: 36, age: 19, overall: 80 } },
];

const prospects = players.filter((player) => player.category === 'prospect');
const rookies: PlayerRow[] = [
    { ...players[0], key: '4', index: 4, name: 'Sample Draft Guard', team: '', category: 'prospect',
        teamIndices: [], draftYear: undefined },
    { ...players[1], key: '7', index: 7, name: 'Sample Draft Forward', team: '', category: 'prospect',
        teamIndices: [], draftYear: undefined },
];
prospects[1] = { ...prospects[1], draftYear: 2008 };

function fields(summary?: PlayerSummary): PlayerDetail['sections'] {
    const result = new Map<string, FieldCell[]>();
    const facts: Record<string, number | string | null | undefined> = {
        'Vitals/Vitals/POSITION': summary?.position,
        'Vitals/Vitals/SECONDARYPOSITION': summary?.secondaryPosition,
        'Vitals/Vitals/BIRTHYEAR': summary?.birthYear,
        'Vitals/Body/HEIGHT': summary?.heightCm,
        'Vitals/Body/WEIGHT': summary?.weightKg,
        'Attributes/Offense/3POINT': summary?.threePoint,
    };
    const allowed = ['Vitals', 'Attributes', 'Tendencies', 'Badges', 'Signature', 'Contract', 'Gear', 'Appearance'];
    for (const [section, groups] of Object.entries(playerSchema.Players as unknown as Sections)) {
        if (!allowed.includes(section)) {
            continue;
        }
        for (const [group, entries] of Object.entries(groups)) {
            for (const entry of entries) {
                const versions = Object.entries(entry.versions);
                const version = versions.find(([key]) => key.split(',').includes('2K27'))
                    ?? versions.find(([key]) => key.split(',').includes('2K26'));
                if (!version || ['uint64', 'pointer', 'address'].includes(String(version[1].type))) {
                    continue;
                }
                const optionEntries = (dropdownSchema.Players as unknown as Sections)[section]?.[group] ?? [];
                const optionField = optionEntries.find((field) => field.normalized_name === entry.normalized_name);
                const optionVersions = Object.entries(optionField?.versions ?? {});
                const optionVersion = optionVersions.find(([key]) => key.split(',').includes('2K27'))
                    ?? optionVersions.find(([key]) => key.split(',').includes('2K26'));
                const options = (optionVersion?.[1].dropdown ?? optionVersion?.[1].values ?? []) as string[];
                const name = section === 'Vitals' ? group : section;
                const value = facts[`${section}/${group}/${entry.normalized_name}`]
                    ?? options[0] ?? (entry.normalized_name === 'HEIGHT' ? 198 : 75);
                const bucket = result.get(name) ?? [];
                bucket.push({
                    key: `${section}/${group}/${entry.normalized_name}`, label: entry.display_name,
                    section: name, group, value, display: String(value), writable: true,
                    kind: options.length > 0 ? 'choice' : 'number', options,
                });
                result.set(name, bucket);
            }
        }
    }
    return [...result].map(([name, fields]) => ({ name, fields }));
}

export async function previewCall(command: string, payload: Record<string, unknown>): Promise<unknown> {
    if (command.startsWith('history')) {
        return previewHistory(command, payload, teams);
    }
    switch (command) {
        case 'connect':
            return { pid: 0, base: '', roster: players.length, teams: teams.length, draft: 2, fields: 0, seasonEndYear: 2008 };
        case 'listPlayers':
            return payload.scope === 'all' ? players : players.filter((row) => row.category === 'team' || row.category === 'freeAgent');
        case 'listTeams':
            return teams;
        case 'teams':
            return teams.map((team) => ({ ...team, used: 1, capacity: 15, open: [2] }));
        case 'readPlayer': {
            const source = payload.source === 'draft' ? rookies : payload.source === 'prospects' ? prospects : players;
            const player = source.find((row) => row.index === payload.index);
            if (!player) {
                throw new Error('示例名单中没有该球员');
            }
            const team = teams.find((team) => team.label === player.team);
            return { ...player, teamBrand: team ? { label: team.label, logoReference: team.logoReference } : null,
                readToken: 'isolated-preview', address: 'preview', sections: fields(player.summary) };
        }
        case 'readPlayerStats': {
            const source = payload.source === 'draft' ? rookies : payload.source === 'prospects' ? prospects : players;
            const player = source.find((row) => row.index === payload.index);
            if (!player) {
                throw new Error('示例名单中没有该球员');
            }
            // Season lines shaped like the live Allen Iverson records (2026-11-08), scaled per sample player.
            const base = statisticsFixture(player);
            const lines = [[97, 76, 1787, 312, 567], [98, 80, 1758, 296, 494], [99, 48, 1284, 236, 223], [100, 70, 1989, 267, 328],
                [101, 71, 2207, 273, 325], [102, 60, 1883, 269, 331], [103, 78, 2717, 503, 319], [104, 82, 2448, 568, 355],
                [105, 64, 1847, 437, 267], [106, 63, 1638, 303, 281], [107, 82, 2019, 329, 305], [108, 82, 1727, 320, 323]];
            const scale = 0.7 + (player.index % 4) * 0.1;
            const cell = (selector: string, name: string, value: number) => ({ key: `SeasonStats/${selector}/Season/${name}`, label: name,
                section: 'SeasonStats', group: 'Season', value, display: String(value), writable: false, kind: 'number' as const });
            return { ...base, seasons: lines.map(([year, games, points, rebounds, assists], slot) => {
                const selector = slot === lines.length - 1 ? 'CURRENTYEARSTATID' : `STATSID${lines.length - 1 - slot}`;
                return { selector, id: slot, address: '0x0', fields: [
                    cell(selector, 'COMPACTYEAR', year), cell(selector, 'GAMESPLAYED', games), cell(selector, 'MINUTES', games * 38),
                    cell(selector, 'POINTS', Math.round(points * scale)), cell(selector, 'OFFENSIVEREBOUNDS', Math.round(rebounds * 0.25)),
                    cell(selector, 'DEFENSEREBOUNDS', Math.round(rebounds * 0.75)), cell(selector, 'ASSISTS', assists),
                    cell(selector, 'STEALS', Math.round(games * 2.1)), cell(selector, 'BLOCKS', Math.round(games * 0.2)),
                    cell(selector, 'FIELDGOALSMADE', Math.round(points * 0.36)), cell(selector, 'FIELDGOALSATTEMPTED', Math.round(points * 0.86)),
                    cell(selector, '3POINTERSMADE', 40 + slot * 6), cell(selector, '3POINTERSATTEMPTED', 150 + slot * 9),
                    cell(selector, 'FREETHROWMADE', Math.round(points * 0.26)), cell(selector, 'FREETHROWATTEMPTED', Math.round(points * 0.33)),
                    cell(selector, 'OVERALL', [81, 85, 79, 94, 86, 94, 94, 95, 95, 94, 92, 90][slot]),
                    cell(selector, 'GAMESSTARTED', games), cell(selector, 'TURNOVERS', Math.round(games * 3.1)), cell(selector, 'FOULS', Math.round(games * 2)),
                    cell(selector, 'BOX+-', (slot - 5) * 40), cell(selector, 'POSSESSIONS', games * 64), cell(selector, 'TOTALPOINTSALLOWED', games * 12),
                    cell(selector, 'DEFENSIVEFIELDGOALSMADE', games * 5), cell(selector, 'DEFENSIVEFIELDGOALSATTEMPTED', games * 11),
                    cell(selector, 'DOUBLEDOUBLES', slot % 5), cell(selector, 'FORTYPOINTGAMES', slot % 3),
                    cell(selector, 'MOSTVALUABLEPLAYER', slot === 4 ? 1 : 0), cell(selector, 'ALLSTARSTARTER', slot > 2 ? 1 : 0),
                ] };
            }) };
        }
        case 'prospects':
            return { status: '示例待选秀名单', items: prospects };
        case 'rookies':
            return { status: '示例新秀名单', items: rookies };
        case 'avatarGet':
            return { found: false, key: payload.key };
        case 'placement': {
            // Spread example players over two teams so the trade view shows both sides.
            const index = Number(payload.index ?? 0);
            const team = teams[index % 2] ?? teams[0];
            return { team: team.label, teamIndex: team.index, slot: 1, salary: 2_600_850 + index * 1_350_000,
                yearsLeft: 1 + (index % 3), teamPayroll: index % 2 ? 68_996_529 : 49_138_250 };
        }
        case 'playerRatings': {
            const fields = ['Attributes/Offense/CLOSESHOT', 'Attributes/Offense/DRIVINGLAYUP', 'Attributes/Offense/DRIVINGDUNK',
                'Attributes/Offense/STANDINGDUNK', 'Attributes/Offense/POSTCONTROL', 'Attributes/Offense/MIDRANGE',
                'Attributes/Offense/3POINT', 'Attributes/Offense/FREETHROW', 'Attributes/Offense/PASSACCURACY',
                'Attributes/Offense/BALLCONTROL', 'Attributes/Athleticism/SPEEDWITHBALL', 'Attributes/Defense/INTERIORDEFENSE',
                'Attributes/Defense/PERIMETERDEFENSE', 'Attributes/Defense/STEAL', 'Attributes/Defense/BLOCK',
                'Attributes/Offense/OFFENSIVEREBOUND', 'Attributes/Defense/DEFENSEREBOUND', 'Attributes/Athleticism/SPEED',
                'Attributes/Athleticism/AGILITY', 'Attributes/Athleticism/STRENGTH', 'Attributes/Athleticism/VERTICAL',
                'Attributes/Misc/POTENTIAL', 'Contract/Salary/SALARYYEAR1', 'Contract/Contract Terms/YEARSLEFT'];
            // Deterministic sample ratings: each player leans toward a different category.
            return { fields, rows: players.map((player) => ({ index: player.index, values: fields.map((_field, column) =>
                column >= 22 ? (column === 22 ? 2_600_850 + player.index * 1_350_000 : 1 + (player.index % 3))
                    : 52 + ((player.index * 7 + column * 11) % 40)) })) };
        }
        case 'planPackage':
        case 'applyPackage':
            return { kind: 'trade', summary: '示例交易', patchCount: 96, playerCount: ((payload.firstPlayers as number[] | undefined)?.length ?? 0) + ((payload.secondPlayers as number[] | undefined)?.length ?? 0),
                teamCount: 2, applied: false, journal: '', details: ['payroll Philadelphia 76ers: 39921882 -> 50900932',
                    'payroll Seattle SuperSonics: 49859325 -> 38880275'] };        case 'planTrade':
            return { kind: 'trade', summary: 'Sample Guard ↔ Sample Forward（示例）', patchCount: 79, playerCount: 2, teamCount: 2,
                applied: false, journal: '', details: [
                    'payroll Philadelphia 76ers: 49138250 -> 59693000', 'payroll Seattle SuperSonics: 68996529 -> 58441779',
                    'pick 3 (round 1): Philadelphia 76ers -> Seattle SuperSonics'] };        case 'readTeam': {
            // Colors and payroll as read from the live roster on 2026-11-08 (76ers, SuperSonics, Lakers).
            const index = Number(payload.index ?? 0);
            const team = teams.find((row) => row.index === index) ?? teams[0];
            const colors = [['00 3d a6 ff', 'ed 17 4c ff'], ['00 65 3a ff', 'ff c2 00 ff'], ['55 25 83 ff', 'fd b9 27 ff']][index % 3];
            const field = (group: string, name: string, label: string, value: string | number) => ({
                key: `${group}/${name}`, label, section: group.split('/')[0], group: group.split('/')[1], value,
                display: String(value), writable: true, options: [], kind: typeof value === 'number' ? 'number' as const : 'text' as const });
            return { ...team, address: '0x0', sections: [
                { name: 'Vitals', fields: [field('Vitals/Color', 'TEAMCOLOR1', 'Team Color 1', colors[0]),
                    field('Vitals/Color', 'TEAMCOLOR2', 'Team Color 2', colors[1]), field('Vitals/Names', 'CITYNAME', 'City Name', team.label.split(' ')[0])] },
                { name: 'Business', fields: [field('Business/Budget', 'SALARY', 'Salary', 49_138_250 + index * 9_000_000),
                    field('Business/Budget', 'PLAYERPAYROLL', 'Player Payroll', 70_283_000)] },
            ] };
        }        case 'leagueSeason': {
            // One current-season line per sample player, so the leaderboards have something to rank.
            const fields = ['COMPACTYEAR', 'GAMESPLAYED', 'GAMESSTARTED', 'MINUTES', 'POINTS', 'FIELDGOALSMADE', 'FIELDGOALSATTEMPTED', '3POINTERSMADE', '3POINTERSATTEMPTED',
                'FREETHROWMADE', 'FREETHROWATTEMPTED', 'OFFENSIVEREBOUNDS', 'DEFENSEREBOUNDS', 'ASSISTS', 'STEALS', 'BLOCKS', 'TURNOVERS', 'FOULS', 'BOX+-', 'DOUBLEDOUBLES', 'TRIPLEDOUBLES'];
            return { fields, writes: 0, skipped: 0, seasonEndYear: 2008,
                rows: players.filter((row) => row.category === 'team' || row.category === 'freeAgent').map((row, n) => {
                const g = 60 + ((n * 7) % 23);
                const pts = g * (14 + ((n * 5) % 15));
                return { index: row.index, values: [108, g, g - (n % 9), g * (28 + (n % 9)), pts, Math.round(pts * 0.38), Math.round(pts * 0.82), g * ((n % 4) + 0.6) | 0, g * ((n % 4) * 2 + 2),
                    Math.round(pts * 0.2), Math.round(pts * 0.25), g * (1 + (n % 3)), g * (3 + (n % 6)), g * (2 + (n % 7)), g * (1 + (n % 2)), Math.round(g * ((n % 5) * 0.4)), g * 2, g * 2, (n - 2) * 30, n % 7, n % 2] };
            }) };
        }
        case 'seasonGames':
            return previewSeasonGames(teams, players);
        case 'schedulePrepare': {
            const game = previewSeasonGames(teams, players).games.find((g) => g.id === Number(payload.gameId));
            if (!game || game.played) throw new Error('只能修改未赛场次');
            const date = String(payload.date);
            if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || date === game.date) throw new Error('请选择新的比赛日期');
            return { token: 'preview-only', gameId: game.id, beforeDate: game.date,
                afterDate: date, patchBytes: 4, writes: 0, writesEnabled: false };
        }
        case 'scheduleApply':
        case 'scheduleUndo':
            throw new Error('示例模式不能修改游戏赛程');
        case 'leagueTeamSeason':
            return teams.map((team, n) => {
                const wins = 30 + ((n * 11) % 33);
                return { index: team.index, label: team.label, slot: 0, id: n, COMPACTYEAR: 108, WINS: wins, LOSSES: 82 - wins, TOTALPOINTS: 7800 + wins * 13, TOTALPOINTSALLOWED: 8300 - wins * 7,
                    FIELDGOALSMADE: 3000 + wins * 3, FIELDGOALSATTEMPTED: 6650, THREEPOINTSHOTSMADE: 350 + ((n * 37) % 300), THREEPOINTSHOTSATTEMPTED: 1100 + ((n * 53) % 600),
                    POSSESSIONS: 7600 + ((n * 41) % 600), TURNOVERS: 1100 + n * 5, POINTSINPAINT: 3200 + n * 20, FASTBREAKPOINTS: 1000 + n * 9, BENCHPOINTS: 2300 - n * 10,
                    OPPONENTFIELDGOALSMADE: 3050 - wins * 3, OPPONENTFIELDGOALSATTEMPTED: 6600, OPPONENTTHREEPOINTSHOTSMADE: 400, MINUTESPLAYED: 19780 };
            });        case 'playerForm':
            // Shaped like the live Chris Mihm read (2026-11-08): newest game first.
            return { index: payload.index, name: 'Sample', writes: 0,
                rankings: { LEAGUEOVRRATINGRANKING: 20, LEAGUEOVRRATINGRANKINGBYPOSITION: 6, LEAGUEPERSCORERANKING: 30, OFFENSERATINGRANKING: 8, DEFENSERATINGRANKING: 67 },
                games: [['2008-06-14', 23, 5.4], ['2008-06-09', 23, 9.4], ['2008-06-07', 24, 10.3], ['2008-06-05', 25, 12.7], ['2008-06-03', 20, 7.6]]
                    .map(([date, minutes, gameScore], slot) => ({ slot, date, minutes, gameScore })) };
        case 'teamForm':
            // Live Cleveland values (LAST10 647, streak 3).
            return [{ index: payload.teamIndex ?? 0, label: 'Sample', last10: 'WWWLLLLWLW', wins: 5, losses: 5, last10Home: 'WLWWLWLWWW', last10Away: 'WLLLLWLWLW',
                streak: 3, homeStreak: 2, awayStreak: 1, previousStreak: -4, longestWin: 4, longestLoss: 6, homeWins: 20, homeLosses: 21 }];        case 'teamSeasons':
            // Ten seasons of sample team lines; slot 0 is the current season.
            return Array.from({ length: 10 }, (_, slot) => {
                const wins = [48, 50, 33, 43, 35, 38, 55, 41, 39, 52][slot];
                return { slot, id: slot, COMPACTYEAR: 108 - slot, WINS: wins, LOSSES: 82 - wins,
                    TOTALPOINTS: 7790 + wins * 12, TOTALPOINTSALLOWED: 8200 - wins * 6, FIELDGOALSMADE: 3000 + wins * 4,
                    FIELDGOALSATTEMPTED: 6700, THREEPOINTSHOTSMADE: 420 + slot * 9, THREEPOINTSHOTSATTEMPTED: 1210, POSSESSIONS: 7480 + slot * 22,
                    OPPONENTFIELDGOALSMADE: 3050 - wins * 3, OPPONENTFIELDGOALSATTEMPTED: 6650, OPPONENTTHREEPOINTSHOTSMADE: 400, TURNOVERS: 1150 + slot * 8,
                    POINTSINPAINT: 3300 + wins * 6, FASTBREAKPOINTS: 1050 + slot * 15, BENCHPOINTS: 2400 - slot * 20, SECONDCHANCEPOINTS: 1100, TURNOVERSFORCED: 1200, ASSISTS: 1800, OFFENSIVEREBOUNDS: 900, DEFENSIVEREBOUNDS: 2600 };
            });        case 'draftPicks':
            return teams.slice(0, 2).flatMap((team, side) => [2009, 2010].flatMap((year, slot) => [1, 2].map((round) => ({
                index: side * 10 + slot * 2 + round, ownerIndex: team.index, owner: team.label,
                originalIndex: team.index, original: team.label, year, round, order: 5 + side * 7 + round,
                used: false, player: null, hasTerms: false,
            }))));
        case 'libraryList':
            return [
                { id: 'preview-face', name: 'Allen Iverson 面部', kind: 'face', created: '2026-11-08T05:30:00+08:00', sourcePlayer: 'Allen Iverson', fieldCount: 111, notes: '' },
                { id: 'preview-player', name: '自建锋线', kind: 'player', created: '2026-11-07T21:10:00+08:00', sourcePlayer: 'Tracy McGrady', fieldCount: 812, notes: '' },
            ];
        case 'journalList':
            return [];
        case 'presetList':
            return { directory: '', files: [] };
        case 'readBatch': {
            const source = payload.source === 'draft' ? rookies : payload.source === 'prospects' ? prospects : players;
            const field = fields().flatMap((section) => section.fields).find((field) => field.key === payload.field);
            return { readToken: 'isolated-preview', field,
                players: (payload.indices as number[]).map((index) => {
                    const player = source.find((player) => player.index === index);
                    const current = fields(player?.summary).flatMap((section) => section.fields)
                        .find((cell) => cell.key === payload.field);
                    return { index, name: player?.name, value: current?.value, display: current?.display };
                }) };
        }
        default:
            throw new Error('界面预览使用示例数据，不会连接或修改游戏');
    }
}

function statisticsFixture(player: PlayerRow): PlayerStatistics {
    const sections = playerSchema.Players as unknown as Sections;
    function cell(entry: AuthoredField, section: string, group: string, selector: string, values: Record<string, number>): FieldCell {
        const versions = Object.entries(entry.versions);
        const payload = versions.find(([key]) => key.split(',').includes('2K27'))?.[1] ?? {};
        const name = entry.normalized_name;
        const value = values[name] ?? 0;
        return {
            key: selector ? `SeasonStats/${selector}/${group}/${name}` : `${section}/${group}/${name}`,
            label: entry.display_name, section: selector ? 'SeasonStats' : section === 'Vitals' ? 'Awards' : section,
            group, value, raw: value, display: String(value), options: [],
            kind: Number(payload.bit_length) === 1 ? 'bool' : 'number',
            writable: !['COMPACTYEAR', 'JERSEYNUMBER', 'OVERALL', 'BOX+-', 'POINTSOFFFREETHROW',
                'ALLNBATEAM', 'ALLDEFENSETEAM', 'ALLROOKIETEAM', 'REFERENCECOUNT'].includes(name),
            min: 0, max: payload.bit_length ? 2 ** Number(payload.bit_length) - 1 : 4294967295,
        };
    }
    const direct = [
        ...['Career High Stats', 'Season High Stats'].flatMap((group) =>
            sections.Stats[group].map((entry) => cell(entry, 'Stats', group, '', { POINTS: 52, ASSISTS: 14 }))),
        ...sections.Vitals.Awards.map((entry) => cell(entry, 'Vitals', 'Awards', '', { PLAYEROFMONTH: 3, PLAYEROFWEEK: 12 })),
    ];
    const seasons = ['CURRENTYEARSTATID', 'STATSID1'].map((selector, index) => ({
        selector, id: 101 + index, address: 'preview',
        fields: Object.entries(sections.Stats).flatMap(([group, entries]) => entries.filter((entry) =>
            Object.entries(entry.versions).some(([key, payload]) => key.split(',').includes('2K27')
                && payload.stat_role === 'season_id_detail')
            && (group !== 'Internal / Links' || entry.normalized_name === 'REFERENCECOUNT'))
            .map((entry) => cell(entry, 'Stats', group, selector, {
                COMPACTYEAR: 107 - index, GAMESPLAYED: 40 + index * 30, GAMESSTARTED: 38,
                POINTS: 1000 + index * 800, MINUTES: 1400 + index * 1000, ASSISTS: 280,
                DEFENSEREBOUNDS: 210, OFFENSIVEREBOUNDS: 50, FIELDGOALSATTEMPTED: 800,
                FIELDGOALSMADE: 350, '3POINTERSATTEMPTED': 200, '3POINTERSMADE': 80,
                FREETHROWATTEMPTED: 260, FREETHROWMADE: 220, MOSTVALUABLEPLAYER: index,
                ALLNBATEAM: 1, REFERENCECOUNT: 1,
            }))),
    }));
    return { index: player.index, name: player.name, readToken: 'isolated-statistics-preview', direct, seasons, warnings: [] };
}

/** Preview season: three teams, 30 games, the first 18 played with box scores. */
function previewSeasonGames(clubs: TeamItem[], roster: PlayerRow[]) {
    const lineup = (team: number) => roster.filter((p) => p.teamIndices?.includes(team));
    const games = Array.from({ length: 30 }, (_, n) => {
        const home = n % 3, away = (n + 1 + (n % 2)) % 3 === home ? (home + 1) % 3 : (n + 1 + (n % 2)) % 3;
        const date = new Date(Date.UTC(2008, 10, 1 + n * 2 + (n % 3 === 0 ? 1 : 0))).toISOString().slice(0, 10);
        const played = n < 18;
        const lines = (team: number, seed: number) => lineup(team).map((p, k) => {
            const fga = 10 + ((seed + k * 3) % 9), fgm = 4 + ((seed + k) % 6), tpa = 2 + (k % 4), tpm = Math.min(fgm, (seed + k) % 3), fta = 2 + ((seed * k) % 6), ftm = Math.max(0, fta - (k % 2));
            return [p.index, p.name, 28 + ((seed + k) % 10), ((seed * 7 + k) % 21) - 10, 2 * fgm + tpm + ftm, fgm, fga, tpm, tpa, ftm, fta, (k + seed) % 3, 3 + ((seed + k) % 6), 2 + ((seed + 2 * k) % 7), (seed + k) % 3, (seed * k) % 2, 1 + ((seed + k) % 4), 1 + ((seed + k) % 4)];
        });
        const hl = played ? lines(home, n) : null, al = played ? lines(away, n + 5) : null;
        const sum = (rows: (number | string)[][] | null) => (rows ? rows.reduce((s, r) => s + Number(r[4]), 0) : null);
        const q = (pts: number | null) => (pts === null ? null : [0, 1, 2, 3].map((i) => Math.floor(pts / 4) + (i < pts % 4 ? 1 : 0)));
        const hp = sum(hl), ap = sum(al);
        return { id: n + 1, date, slot: 39360, played, home, away, homeLabel: clubs[home].label, awayLabel: clubs[away].label,
            homePoints: hp, awayPoints: ap, homeQuarters: q(hp), awayQuarters: q(ap), homeLines: hl, awayLines: al };
    });
    return { format: 'nba2k27-season-games/1', columns: [], games, table: 'preview', writes: 0 };
}
