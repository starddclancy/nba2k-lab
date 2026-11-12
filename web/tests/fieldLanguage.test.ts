import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { displayValue, fieldDescription, fieldGroupLabel, fieldLabel, fieldSectionDescription,
    confirmedOptions, isConfirmedField, isConfirmedOption, isRoutineTeamField, optionLabel, sectionLabel } from '../src/labels.ts';
import type { FieldCell } from '../src/types.ts';

const field = (key: string, label: string, value: FieldCell['value'] = null): FieldCell => ({
    key, label, section: key.split('/')[0], group: key.split('/')[1],
    value, display: String(value ?? ''), kind: 'number', writable: true, options: [],
});

test('state names remain geography while the team state remains a management choice', () => {
    assert.equal(fieldLabel(field('Vitals/Vitals/STATE', 'State')), '球队经营方向');
    assert.equal(fieldLabel(field('Vitals/Vitals/LOCALIZEDSTATENAME', 'LocalizedStateName')), '州名文本引用');
    assert.equal(fieldLabel(field('Team City/Identity/UNLOCALIZEDSTATENAME', 'UnlocalizedStateName')), '州名（原文）');
    assert.equal(fieldLabel(field('Team City/Identity/LOCALIZEDSHORTTEAMNAME', 'LocalizedShortTeamName')), '球队简称文本引用');
});

test('school parameters cannot be mistaken for the current NBA team rating or location', () => {
    assert.equal(fieldLabel(field('College/Prospect Generation/TEAMSTRENGTH', 'TeamStrength')), '学校球队实力参数');
    assert.equal(fieldLabel(field('College/Geography/REGIONID', 'RegionId')), '学校所属地区');
    assert.equal(fieldLabel(field('Team City/Links/REGIONID', 'RegionId')), '城市所属地区编号');
    const schoolRegion = { ...field('College/Geography/REGIONID', 'RegionId', 'NORTH_AMERICA'), kind: 'choice' as const };
    assert.equal(displayValue(schoolRegion), '北美');
    assert.equal(optionLabel('WESTERN_EUROPE', schoolRegion), '西欧');
    assert.equal(optionLabel('HISPANIC', schoolRegion), '西班牙语地区（HISPANIC）');
});

test('undecoded team history dates remain codes rather than fabricated calendar dates', () => {
    const cell = field('Vitals/Team/TEAMHISTORY0DATE', 'TeamHistory0Date', 123456);
    assert.equal(fieldLabel(cell), '效力记录 1 · 日期编码');
    assert.equal(displayValue(cell), '123456');
    assert.equal(fieldLabel(field('Vitals/Team/TEAMHISTORY2TEAMID', 'TeamHistory2TeamId')), '效力记录 3 · 球队编号');
    assert.equal(cell.value, 123456);
});

test('raw team configuration pages name resources and slots rather than the playable roster view', () => {
    assert.equal(sectionLabel('Jerseys'), '球衣资源');
    assert.equal(sectionLabel('Team Players'), '球员名单槽位');
    assert.equal(sectionLabel('Team Lineups'), '战术阵容配置');
    assert.equal(fieldLabel(field('Team Players/Team Players/PLAYER12', 'Player 12')), '常规名单第 12 位');
    assert.equal(fieldLabel(field('Team Players/Team Players/2WAYPLAYER2', 'Two Way Player 2')), '双向合同第 2 位');
    assert.equal(fieldLabel(field('Team Lineups/Starters/STARTERSPLAYER1', 'Starters Player 1')), '首发阵容第 1 位');
    assert.equal(fieldLabel(field('Jerseys/Jerseys/JERSEY3', 'Jersey #3')), '球衣资源 3');
    assert.equal(fieldLabel(field('Jerseys/Jerseys/UNIFORM0', 'Uniform[0]')), '球衣资源 1 · 内部关联');
});

test('celebration options retain distinct authored identifiers without positional guesses', () => {
    const cell = { ...field('Signature/Misc/EMOTEDRB', 'EmoteDRB'), kind: 'choice' as const,
        options: ['EMDRB_NONE', 'EMDRB_WAVE', 'EMDRB_67', 'EMDRB_NEW_UNKNOWN'] };
    assert.equal(optionLabel('EMDRB_NONE', cell), '无');
    assert.equal(optionLabel('EMDRB_WAVE', cell), '挥手');
    assert.equal(optionLabel('EMDRB_67', cell), '');
    assert.equal(optionLabel('EMDRB_NEW_UNKNOWN', cell), '');
    assert.equal(cell.options[2], 'EMDRB_67');
    assert.equal(optionLabel('UNKNOWN_STYLE', { key: 'Signature/Misc/FREEFALLSTYLE', label: 'FreefallStyle' }),
        '');
});

test('personality groups follow their actual contents rather than sharing an ambiguous heading', () => {
    const gum = field('Vitals/Personality/GUMCHEWER', 'GumChewer');
    const badge = field('Badges/Personality/WORKETHIC', 'Work Ethic');
    assert.equal(fieldGroupLabel(gum), '场上习惯');
    assert.equal(sectionLabel('Personality'), '场上习惯');
    assert.equal(fieldGroupLabel(badge), '性格徽章');
    assert.match(fieldDescription(gum) ?? '', /性格徽章/);
    assert.equal(fieldGroupLabel(field('Attributes/Defense/BLOCK', 'Block')), '防守');
});

test('routine team fields hide schema links and bytes without using a numeric threshold', () => {
    for (const key of ['Vitals/Vitals/CITY', 'Vitals/Vitals/GLEAGUETEAM',
        'Vitals/Vitals/COREPLAYER15', 'Vitals/Vitals/DEADCAPDATA', 'Vitals/Vitals/ADVANCEDROTATION',
        'Vitals/Vitals/AUDIOSIGNATURECRC32B', 'Vitals/Color/TEAMCOLOR1', 'Jerseys/Jerseys/UNIFORM0',
        'Team Retired Jerseys/Links/TEAM']) {
        assert.equal(isRoutineTeamField(field(key, key.split('/').at(-1)!)), false, key);
    }
    assert.equal(isRoutineTeamField({ ...field('Vitals/Vitals/ABB', 'ABB'), display: 'System.Byte[]' }), false);
    assert.equal(isRoutineTeamField(field('Vitals/Vitals/PLAYERPAYROLL', 'Player Payroll', 123456789)), true);
    assert.equal(isRoutineTeamField(field('Vitals/Vitals/CUMULATIVEATTENDANCE', 'CumulativeAttendance', 193162)), true);
    assert.equal(isRoutineTeamField(field('Team Players/Team Players/PLAYER1', 'Player 1', 2347241726040)), true);
    assert.equal(isRoutineTeamField(field('Team Lineups/Starters/STARTERSPLAYER1', 'Starters Player 1', 2347241726040)), true);
});

test('ordinary team forms exclude undecoded year bases and unsigned streak encodings', () => {
    for (const [name, value] of [
        ['CLASSICTEAMYEAR', 3], ['HISTORICYEAR', 3], ['HISTORICSEASONS', '0203'],
        ['AWAYWINLOSSSTREAK', 254], ['HOMEWINLOSSSTREAK', 255],
        ['PREVIOUSWINLOSSSTREAK', 254], ['WINLOSSSTREAK', 255],
    ] as const) {
        const cell = field(`Vitals/Vitals/${name}`, name, value);
        assert.equal(isRoutineTeamField(cell), false, name);
        assert.equal(cell.value, value);
    }
    assert.equal(displayValue(field('SEASON', 'Season', 217)), '2008-2009 赛季');
    assert.equal(isConfirmedField(field('SEASON', 'Season', 217)), true);
});

test('confirmed field and choice filters suppress undecoded meanings and retain explicit team options', () => {
    for (const key of ['Vitals/Team/TEAMHISTORY0DATE', 'College/Prospect Generation/TEAMSTRENGTH',
        'Team City/Links/REGIONID']) {
        assert.equal(isConfirmedField(field(key, key.split('/').at(-1)!)), false, key);
    }
    assert.equal(isConfirmedField(field('Advanced/Other/NOMADDUNK', 'NoMadDunk', 1)), false);
    for (const name of ['AMBIENTPACKAGE1', 'AMBIENTPACKAGE2', 'AMBIENTPACKAGE3',
        'AMBIENTPACKAGE1WEIGHT', 'AMBIENTPACKAGE2WEIGHT', 'AMBIENTPACKAGE3WEIGHT']) {
        assert.equal(isConfirmedField(field(`Signature/Misc/${name}`, name, 153)), false, name);
    }
    assert.equal(isConfirmedOption('Unknown', { key: 'Vitals/General/TYPE', label: 'Type' }), false);
    assert.equal(isConfirmedOption('UNKNOWN_ANKLE_INJURY',
        { key: 'Health/Health/INJURY', label: 'Injury' }), true);
    const celebration = { ...field('Signature/Misc/EMOTEDRB', 'EmoteDRB', 'EMDRB_WAVE'),
        kind: 'choice' as const, options: ['EMDRB_WAVE', 'EMDRB_67', 'EMDRB_NEW_UNKNOWN'] };
    assert.equal(isConfirmedField(celebration), true);
    assert.deepEqual(confirmedOptions(celebration), ['EMDRB_WAVE']);
    assert.equal(isConfirmedField({ ...celebration, value: 'EMDRB_67' }), false);
    assert.equal(isConfirmedOption('0 · Philadelphia 76ers', { key: 'TEAM', label: '所属球队' }), true);
    assert.equal(optionLabel('0 · Philadelphia 76ers', { key: 'TEAM', label: '所属球队' }), '0 · Philadelphia 76ers');
    assert.equal(optionLabel('保留现有关联', { key: 'OPPONENT', label: '亚军球队' }), '保留现有关联');
    assert.equal(isConfirmedOption('0 · Philadelphia 76ers', celebration), false);
    assert.equal(isConfirmedOption('UNKNOWN_TEAM', { key: 'TEAM', label: '所属球队' }), false);
});

test('authored 2K27 celebrations retain translated definitions instead of hiding the whole lists', () => {
    const definitions = JSON.parse(readFileSync(new URL('../../src/Nba2k27Editor.Core/Data/dropdowns.json', import.meta.url), 'utf8'));
    const signatures = definitions.Players.Signature.Misc as {
        normalized_name: string; display_name: string; versions: Record<string, { values: string[] }>;
    }[];
    for (const [name, unresolved] of [
        ['EMOTEDRB', ['EMDRB_67']],
        ['EMOTETT', ['EMTT_67', 'EMTT_BEAMME', 'EMTT_HORSE']],
    ] as const) {
        const definition = signatures.find((entry) => entry.normalized_name === name)!;
        const cell = { key: `Signature/Misc/${name}`, label: definition.display_name,
            options: definition.versions['2K27'].values };
        assert.deepEqual(cell.options.filter((option) => !isConfirmedOption(option, cell)), unresolved);
        for (const option of confirmedOptions(cell)) assert.match(optionLabel(option, cell), /[\u3400-\u9fff]/u);
    }
});

test('defined player names and animation types remain available without accepting arbitrary identifiers', () => {
    const signature = { key: 'Signature/Shooting/SHOTJUMPER', label: 'Shot Jumper' };
    assert.equal(optionLabel('BAM_ADEBAYO', signature), 'BAM ADEBAYO');
    assert.equal(optionLabel('MICHAEL_JORDAN', signature), 'MICHAEL JORDAN');
    assert.equal(optionLabel('LARRY_BIRD', signature), 'LARRY BIRD');
    assert.equal(isConfirmedOption('SOME_NEW_PERSON', signature), false);
    assert.equal(optionLabel('BASIC_2', signature), '基础 2');
    assert.equal(optionLabel('NORMAL_GUARD', signature), '普通后卫');
    const layup = { key: 'Signature/Finishing/LAYUPPACKAGE', label: 'Layup Package' };
    assert.equal(optionLabel('LUP_DEFAULT_SMALL', layup), '默认小个子');
    assert.equal(optionLabel('LUP_B_ADEBAYO', layup), 'B. ADEBAYO');
    assert.equal(optionLabel('LUP_M_JORDAN', layup), 'Michael Jordan');
    assert.equal(isConfirmedOption('LUP_X_UNKNOWN', layup), false);
    assert.equal(optionLabel('BLACK_HIGH', { key: 'Signature/Misc/VOICETYPE', label: 'VoiceType' }), '黑人声线 · 高音');
    const opponent = { ...field('LASTYEARPLAYOFFSOPPONENT', '上季季后赛最后对手', '0 · Philadelphia 76ers'),
        kind: 'choice' as const, options: ['0 · Philadelphia 76ers'] };
    assert.equal(isConfirmedField(opponent), true);
    assert.equal(isConfirmedField(field('Vitals/Vitals/LASTYEARPLAYOFFSOPPONENT', 'LAST_YEAR_PLAYOFFS_OPPONENT', 123456789)), false);
});

test('moved shooting definitions retain their explicit actor and numbered WNBA names', () => {
    const freeThrow = { key: 'Signature/Jump Shooting II/FREETHROW', label: 'Free Throw' };
    for (const name of ['CAITLIN_CLARK', 'CAMERON_REDDISH', 'CANDACE_PARKER', 'J_COLE',
        'MAITE_CAZORLA', 'NICO_MANNION', 'RONDAE_HOLLIS_JEFFERSON']) {
        assert.equal(isConfirmedOption(name, freeThrow), true, name);
    }
    assert.equal(optionLabel('FREE_THROW_WNBA_1', freeThrow), 'WNBA 罚球 1');
    assert.equal(optionLabel('FREE_THROW_WNBA_2', freeThrow), 'WNBA 罚球 2');
    assert.equal(optionLabel('GOOFY_2K27_V01', freeThrow), '');
    assert.equal(optionLabel('SOME_NEW_PERSON', freeThrow), '');
});

test('action names reuse exact authored face, portrait and announcer names', () => {
    const definitions = JSON.parse(readFileSync(new URL('../../src/Nba2k27Editor.Core/Data/dropdowns.json', import.meta.url), 'utf8'));
    const normalize = (value: string) => value.replace(/[^a-z0-9]/gi, '').toLowerCase();
    const names = new Set<string>();
    type Definition = { normalized_name: string; display_name: string;
        versions: Record<string, { values?: string[]; dropdown?: string[] }> };
    for (const definition of definitions.Players.Vitals.ID as Definition[]) {
        if (!['FACEID', 'PORTRAITID', 'AUDIOSIGNATUREID'].includes(definition.normalized_name)) continue;
        for (const version of Object.values(definition.versions)) {
            for (const option of version.values ?? version.dropdown ?? []) {
                names.add(normalize(option.replace(/^\[\d+\]\s*/, '')));
            }
        }
    }
    let checked = 0;
    for (const [group, fields] of Object.entries(definitions.Players.Signature)) {
        for (const definition of fields as Definition[]) {
            const version = Object.entries(definition.versions).find(([key]) => key.split(',').includes('2K27'))?.[1];
            for (const option of version?.values ?? version?.dropdown ?? []) {
                if (!names.has(normalize(option))) continue;
                const cell = { key: `Signature/${group}/${definition.normalized_name}`, label: definition.display_name };
                assert.equal(isConfirmedOption(option, cell), true, `${cell.key}: ${option}`);
                checked++;
            }
        }
    }
    assert.ok(checked > 1000, 'Check real authored name entries across the action lists');
});

test('authored 2K27 shooter names remain available when a web portrait is missing', () => {
    const definitions = JSON.parse(readFileSync(new URL('../../src/Nba2k27Editor.Core/Data/dropdowns.json', import.meta.url), 'utf8'));
    const fields = definitions.Players.Signature['Jump Shooting'] as {
        normalized_name: string; display_name: string; versions: Record<string, { values: string[] }>;
    }[];
    for (const name of ['SHOTJUMPER', 'UPPERRELEASE1', 'UPPERRELEASE2']) {
        const definition = fields.find((entry) => entry.normalized_name === name)!;
        const cell = { key: `Signature/Jump Shooting/${name}`, label: definition.display_name };
        assert.deepEqual(definition.versions['2K27'].values.filter((option) => !isConfirmedOption(option, cell)), ['SAM_P']);
    }
    for (let slot = 1; slot <= 15; slot++) {
        assert.equal(optionLabel('JAVALE_MCGEE', { key: `Signature/Dunking/DUNKPACKAGE${slot}`, label: 'Dunk Package' }), 'JAVALE MCGEE');
    }
    const action = { key: 'Signature/Misc/MOTIONSTYLE', label: 'Motion Style' };
    for (const name of [
        'TERRY_ROZIER_III', 'CAMERON_THOMAS', 'LAUREN_BETTS', 'BOBBY_PORTIS', 'ROB_DILLINGHAM',
        'BREANNA_STEWART', 'AJA_WILSON', 'ANTHONY_LAMB', 'THEO_PINSON', 'TREVELIN_QUEEN',
        'ADMIRAL_SCHOFIELD', 'COLE_SWIDER', 'TERRY_TAYLOR', 'JT_THOR', 'JUAN_TOSCANO',
        'LONNIE_WALKER_IV', 'ROBERT_WILLIAMS', 'JUSTISE_WINSLOW',
    ]) assert.equal(optionLabel(name, action), name.replace(/_/g, ' '));
});

test('intro definitions preserve authored actor names and only describe explicit action words', () => {
    const intro = { key: 'Signature/Misc/SIGPLAYERINTRO1', label: 'Signature Player Intro' };
    assert.equal(optionLabel('PAOLO_RIMHANG', intro), 'Paolo 挂筐');
    assert.equal(optionLabel('JBRUNSON_INTRO', intro), 'J. Brunson 开场动作');
    assert.equal(optionLabel('IRVING_LBJ_HUG', intro), 'Irving / LBJ 拥抱');
    assert.equal(optionLabel('GEN_BACKFLIP2', intro), '后空翻 2');
    assert.equal(isConfirmedOption('RWESTBROOK_HITDEMFOLK_V1', intro), false);
    assert.equal(isConfirmedOption('GEN_NEW_UNKNOWN', intro), false);
    const jump = { key: 'Signature/Misc/SIGJUMPBALLSTAND', label: 'Signature Jump Ball Stand' };
    for (let variant = 1; variant <= 20; variant++) {
        const number = String(variant).padStart(2, '0');
        assert.equal(optionLabel(`TIP_INTRO_V${number}`, jump), `跳球开场动作 · 变体 ${number}`);
    }
    assert.equal(isConfirmedOption('TIP_INTRO_V21', jump), false);
    assert.equal(isConfirmedOption('TIP_INTRO_V01', intro), false);
});

test('raw codes and shared college records expose their limits without changing values', () => {
    const history = field('Vitals/Team/TEAMHISTORY0DATE', 'TeamHistory0Date', 4294967295);
    assert.match(fieldDescription(history) ?? '', /未定义编码规则/);
    assert.match(fieldDescription(field('College/Record/WINS', 'Wins')) ?? '', /多名球员引用/);
    assert.match(fieldDescription(field('Team City/Links/REGIONID', 'RegionId')) ?? '', /未提供编号与地区名称/);
    assert.match(fieldSectionDescription('Team Lineups') ?? '', /五人阵容/);
    assert.equal(fieldDescription(field('Attributes/Defense/BLOCK', 'Block', 85)), undefined);
    assert.equal(history.value, 4294967295);
});
