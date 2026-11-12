import type { FieldCell, TeamItem } from './types';
import type { HistoryDetail, HistoryItem, HistoryKind } from './history';

export function previewHistory(command: string, payload: Record<string, unknown>, teams: TeamItem[]) {
    function catalog(kind: HistoryKind, teamIndex: number): HistoryItem[] {
        const club = teams.find((t) => t.index === teamIndex)?.label ?? '';
        if (kind === 'league') {
            return [
                { key: 'league:0', kind, index: 0, teamIndex: null, year: 2001, category: '常规赛 MVP',
                    label: 'Sample Forward', team: teams[0].label, summary: 'Sample Forward' },
                { key: 'league:1', kind, index: 1, teamIndex: null, year: 2001, category: '得分王',
                    label: 'Sample Guard', team: teams[0].label, summary: '31.4' },
                { key: 'league:2', kind, index: 2, teamIndex: null, year: 2000, category: '历届总冠军 / 总决赛 MVP',
                    label: 'Sample Center', team: 'Los Angeles Lakers', summary: '4 : 1' },
                { key: 'league:3', kind, index: 3, teamIndex: null, year: 1995, category: '得分王',
                    label: 'Sample Guard', team: 'Chicago Bulls', summary: '30.4' },
            ];
        }
        if (kind === 'records' || kind === 'teamRecords') {
            const base = kind === 'records' ? 0 : 3100 + teamIndex * 510;
            const rows = kind === 'records' ? [
                [0, '单场纪录（常规赛）', 100], [50, '单场纪录（季后赛）', 63],
                [100, '单赛季纪录', 4029], [350, '生涯累计纪录', 40283], [2850, '分类待核对', 0],
            ] : [
                [0, '单场纪录（常规赛）', 70], [50, '单场纪录（季后赛）', 55],
                [100, '单赛季纪录', 2649], [300, '生涯累计纪录', 21586], [110, '分类待核对', 0],
            ];
            return rows.map(([offset, group, value]) => {
                const index = base + Number(offset);
                const unknown = group === '分类待核对';
                return { key: kind === 'records' ? `records:${index}` : `teamRecords:${teamIndex}:${index}`,
                    kind, index, teamIndex: kind === 'records' ? null : teamIndex, year: unknown ? null : 2001,
                    category: unknown ? String(group) : `${group} · 得分`,
                    label: unknown ? '未命名记录' : 'Sample Guard', team: club || teams[0].label,
                    summary: String(value), recordGroup: String(group), metric: unknown ? '原始记录' : '得分',
                    rank: unknown ? null : 1,
                    notice: unknown ? kind === 'records' ? '此记录未提供分类映射，保留原始编号。'
                        : '此记录的指标顺序尚无直接资料确认，保留原始编号。' : null };
            });
        }
        if (kind === 'jersey') {
            return [[6, 'Bill', 'Russell', 1972], [33, 'Larry', 'Bird', 1993], [65535, 'Johnny', 'Most', 1990]].map(([number, first, last, year], index) => ({
                key: `jersey:${teamIndex}:${index}`, kind, index, teamIndex, year: year as number, category: '退役球衣',
                label: `${first} ${last}`, team: club, summary: number === 65535 ? 'MIKE' : `#${number}` }));
        }
        return [{ key: `${kind}:${teamIndex}:0`, kind, index: 0, teamIndex, year: kind === 'franchise' ? 1950 : kind === 'season' ? 2007 : null,
            category: kind === 'totals' ? '累计战绩与荣誉' : kind === 'season' ? '赛季统计' : '球队沿革',
            label: kind === 'season' ? '2006-2007 赛季' : kind === 'franchise' ? 'Syracuse Nationals' : club,
            team: club, summary: kind === 'totals' ? '球队累计' : kind === 'season' ? '43 胜 39 负 · 7461 分' : '1950–1963' }];
    }
    if (command === 'historyList') {
        return catalog(payload.kind as HistoryKind, Number(payload.teamIndex ?? 0));
    }
    if (command !== 'historyRead') {
        throw new Error('界面预览使用示例数据，不会连接或修改游戏');
    }
    const key = String(payload.key);
    const kind = key.split(':')[0] as HistoryKind;
    const item = catalog(kind, kind === 'league' || kind === 'records' ? 0 : Number(key.split(':')[1])).find((i) => i.key === key)!;
    const fields: FieldCell[] = [];
    function field(key: string, label: string, value: number | string, writable = true, options: string[] = []) {
        fields.push({ key, label, value, raw: value, writable, display: String(value), options,
            section: item.category, group: item.category, kind: options.length ? 'choice' : typeof value === 'string' ? 'text' : 'number',
            min: 0, max: 65535 });
    }
    if (kind === 'league') {
        field('TYPE', '分类码', item.category === '常规赛 MVP' ? 9 : item.category === '得分王' ? 3 : 1, false);
        field('SEASON', '赛季', item.year! - 1791, false);
        const club = teams.find((team) => team.label === item.team) ?? teams[0];
        const split = club.label.lastIndexOf(' ');
        field('TEAM', item.index === 2 ? '冠军球队' : '所属球队', `${club.index} · ${club.label}`, true,
            teams.map((t) => `${t.index} · ${t.label}`));
        field('TEAMCITY', '球队城市', club.label.slice(0, split), false);
        field('TEAMNAME', '球队名称', club.label.slice(split + 1), false);
        field('FIRSTNAME', item.index === 2 ? '总决赛 MVP 名' : '获奖者名', 'Sample');
        field('LASTNAME', item.index === 2 ? '总决赛 MVP 姓' : '获奖者姓', item.label.split(' ')[1]);
        if (item.category === '得分王') { field('DATA', '场均数值', Number(item.summary)); }
        if (item.index === 2) {
            field('SCORE1', '冠军系列赛胜场', 4);
            field('SCORE2', '亚军系列赛胜场', 1);
        }
    } else if (kind === 'records' || kind === 'teamRecords') {
        field('TEAM', '所属球队', `${teams[0].index} · ${teams[0].label}`, true,
            teams.map((team) => `${team.index} · ${team.label}`));
        field('SIGNATUREID', '球员签名 ID', 1234, false);
        field('FIRSTNAME', '纪录保持者名', 'Sample');
        field('LASTNAME', '纪录保持者姓', 'Guard');
        field('DATA', '纪录数值', Number(item.summary));
        fields.at(-1)!.max = 3.4028234663852886e38;
        field('DAY', '日编码', 2, false);
        field('MONTH', '月编码', 3, false);
        field('YEAR', '年份', item.year ?? 0, false);
    } else if (kind === 'totals') {
        for (const [key, label, value] of [
            ['CHAMPIONSHIPS', '总冠军次数', 3], ['CONFERENCECHAMPIONSHIPS', '分区冠军次数', 9],
            ['DIVISIONTITLES', '赛区冠军次数', 5], ['PLAYOFFAPPEARANCES', '季后赛次数', 47],
            ['HISTORICWIN', '历史胜场', 3121], ['HISTORICLOSSES', '历史负场', 2836],
            ['FIRSTPICKSCOUNT', '状元签次数', 3], ['LOTTERYPICKSCOUNT', '乐透签次数', 18],
        ]) { field(String(key), String(label), Number(value)); }
    } else if (kind === 'season') {
        field('COMPACTYEAR', '赛季', 107, false);
        const labels = ['胜场', '负场', '上场分钟', '总得分', '总失分', '投篮命中', '投篮出手', '三分命中', '三分出手',
            '罚球命中', '罚球出手', '进攻篮板', '防守篮板', '抢断', '失误', '盖帽', '助攻', '犯规',
            '利用失误得分', '二次进攻得分', '内线得分', '对手投篮命中', '对手投篮出手', '对手三分命中',
            '对手三分出手', '制造失误', '替补得分', '快攻得分', '回合数'];
        labels.forEach((label, i) => field(`STAT${i}`, label, [43, 39, 3946, 7461, 7205, 2804, 6436, 214, 715][i] ?? 200 + i * 21));
    } else if (kind === 'jersey') {
        const [first, last] = item.label.split(' ');
        field('JERSEYNUMBER', '球衣号码（65535 = 荣誉/非号码）', item.summary.startsWith('#') ? Number(item.summary.slice(1)) : 65535);
        field('JERSEYSTRING', '号码文字（非号码荣誉）', item.summary.startsWith('#') ? '' : item.summary);
        field('UNLOCALIZEDFIRSTNAME', '名', first);
        field('UNLOCALIZEDLASTNAME', '姓', last);
        field('RETIREDYEAR', '退役年份', item.year ?? 0);
        field('START1', '第 1 段开始年份', (item.year ?? 1970) - 13);
        field('END1', '第 1 段结束年份', (item.year ?? 1970) - 3);
    } else {
        field('SIGNATURE', '球队关联签名', 2757610395, false);
        field('CITYNAME', '历史城市', 'Syracuse');
        field('TEAMNAME', '历史队名', 'Nationals');
        field('STARTYEAR', '开始年份', 1950);
        field('ENDYEAR', '结束年份', 1963);
        field('FILENAME', '历史图标资源', 'logos/logo000phi_1950.iff', false);
    }
    return { key, label: item.label, readToken: 'preview-history', address: 'preview',
        fields, sharedTeams: kind === 'season' ? ['#41 Philadelphia 76ers（示例共享记录）'] : [],
        undoDepth: 0, undoKey: null } satisfies HistoryDetail;
}
