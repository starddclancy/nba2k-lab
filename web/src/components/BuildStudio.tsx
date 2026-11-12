import { useEffect, useMemo, useRef, useState } from 'react';
import {
    Alert, App, Button, Checkbox, Empty, Input, InputNumber, Modal, Popconfirm, Segmented,
    Select, Slider, Table, Tabs, Tag, Tooltip,
} from 'antd';
import {
    PlusOutlined, SaveOutlined, CopyOutlined, DeleteOutlined, ImportOutlined, ExportOutlined,
    StarOutlined, StarFilled, ReloadOutlined, SwapOutlined, ShareAltOutlined, LinkOutlined,
    FolderOpenOutlined, UserSwitchOutlined,
} from '@ant-design/icons';
import {
    attributes, compareBuilds, createBuild, deleteBuild, duplicateBuild, evaluateAnimation,
    evaluateBadge, exportLibrary, importLibrary, loadLibrary, mergeLibraries, persistLibrary,
    positions, saveBuild, tiers, extraAttributes, defaultExtraRating, defaultExtraRatings,
} from '../builds';
import type {
    AnimationRequirement, BadgeRequirement, BuildPlan, Eligibility, PlanLibrary, Requirement, Tier,
} from '../builds';
import { buildCatalog as catalog, badgeName, categoryNames, tierNames } from '../buildCatalog';
import BadgeIdentity from './BadgeIdentity';
import { badgeTierGroup, bodyIssues, bodySource, heightsFor, legalBody, templateFor } from '../buildBody';
import { decodeShareCode, encodeShareCode } from '../shareCode';
import { planChanges, planOverall, planValues, ratingFields, bodyFields } from '../buildApply';
import { archetypeOf, categoryScores } from '../archetype';
import { ArchetypeLabel, CategoryBars, OverallRing } from './PlayerProfile';
import type { PlanChange } from '../buildApply';
import { applyPlayer, listPlayers, readPlayer } from '../api';
import type { PlayerDetail, PlayerRow } from '../types';
import { useEditing } from '../editingContext';

export type StudioView = 'builder' | 'badgeRequirements' | 'animations' | 'library' | 'compare';
const labels: Record<StudioView, string> = {
    builder: '建模器', badgeRequirements: '徽章门槛', animations: '动作包',
    library: '方案库', compare: '方案对比',
};
const groupColors: Record<string, string> = {
    终结: 'var(--finishing)', 投射: 'var(--shooting)', 组织: 'var(--playmaking)',
    防守: 'var(--defense)', 篮板: 'var(--rebounding)', 身体: 'var(--physical)',
};
const attrNames = new Map(attributes.map((item) => [item.id, item.label]));
const animationGroups = {
    finishing: '终结', dribbling: '运球', shooting: '投篮',
    motionStyles: '移动风格', jumpers: '跳投基础',
};
const statusNames = { eligible: '可用', blocked: '未达门槛', unknown: '属性资料不完整', unavailable: '未提供门槛' };
const blankLibrary: PlanLibrary = { schemaVersion: 1, game: 'nba2k27', plans: [] };
const planningCatalog = {
    ...catalog,
    animations: catalog.animations.filter((animation) => animation.rule.logic !== 'unverified'),
};
const animationTargetIds = new Set(planningCatalog.animations.map((animation) => animation.id));

/** Keep loaded, imported and exported targets within the published planning rules. */
export function withAvailableAnimationGoals(plan: BuildPlan): BuildPlan {
    return { ...plan, animationGoals: plan.animationGoals.filter((id) => animationTargetIds.has(id)) };
}

function firstPlan(): BuildPlan {
    return {
        ...createBuild(), name: '双向侧翼',
        ratings: {
            closeShot: 75, drivingLayup: 80, drivingDunk: 87, standingDunk: 45, postControl: 60,
            midRange: 85, threePoint: 92, freeThrow: 75, passAccuracy: 75, ballHandle: 85,
            speedWithBall: 80, interiorDefense: 60, perimeterDefense: 85, steal: 75, block: 60,
            offensiveRebound: 40, defensiveRebound: 65, speed: 85, agility: 80, strength: 75, vertical: 75,
        },
    };
}

function ruleText(rule: Requirement): string {
    if (rule.logic === 'unverified') return '仅供浏览';
    if (!rule.terms.length) {
        return '未提供';
    }
    const joiner = rule.logic === 'any' ? ' 或 ' : ' 且 ';
    return rule.terms.map((term) => `${attrNames.get(term.attribute)} ${term.minimum}${
        (term.minHeight ?? 69) > 69 || (term.maxHeight ?? 88) < 88
            ? `（${term.minHeight ?? 69}–${term.maxHeight ?? 88} in）` : ''}`).join(joiner);
}

function gapText(result: Eligibility): string {
    const gaps = result.terms.filter((term) => term.status !== 'eligible').map((term) =>
        `${attrNames.get(term.attribute)}${term.heightMatches === false ? '：身高不符'
            : term.missing === null ? '：缺少属性数值' : term.missing > 0 ? ` +${term.missing}` : '：属性资料不完整'}`);
    return gaps.join(result.logic === 'any' ? ' 或 ' : ' / ') || statusNames[result.status];
}

function download(library: PlanLibrary, name: string) {
    const url = URL.createObjectURL(new Blob([exportLibrary({
        ...library, plans: library.plans.map(withAvailableAnimationGoals),
    })], { type: 'application/json' }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `${name.replace(/[<>:"/\\|?*]/g, '_')}.json`;
    anchor.click();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export default function BuildStudio({ view }: { view: StudioView }) {
    const { message } = App.useApp();
    const { editing } = useEditing();
    const editingRef = useRef(editing);
    editingRef.current = editing;
    const [plan, setPlan] = useState(firstPlan);
    const [baseline, setBaseline] = useState(() => JSON.stringify(plan));
    const [library, setLibrary] = useState<PlanLibrary | null>(null);
    const currentLibrary = useRef<PlanLibrary | null>(null);
    const [storageError, setStorageError] = useState('');
    const [staleLibrary, setStaleLibrary] = useState(false);
    const [queries, setQueries] = useState({ badges: '', animations: '', library: '' });
    const [category, setCategory] = useState<string | undefined>();
    const [animationGroup, setAnimationGroup] = useState<string | undefined>();
    const [eligibility, setEligibility] = useState<string | undefined>();
    const [onlyTargets, setOnlyTargets] = useState(false);
    const [favoriteOnly, setFavoriteOnly] = useState(false);
    const [resultTab, setResultTab] = useState('badges');
    const [leftId, setLeftId] = useState('__draft');
    const [rightId, setRightId] = useState<string>();
    const [differencesOnly, setDifferencesOnly] = useState(false);
    const [incoming, setIncoming] = useState<PlanLibrary | null>(null);
    const [pending, setPending] = useState<(() => void) | null>(null);
    const input = useRef<HTMLInputElement>(null);
    const dirty = JSON.stringify(plan) !== baseline;
    const [units, setUnits] = useState('imperial');

    function reloadLibrary() {
        try {
            const loaded = loadLibrary(window.localStorage);
            currentLibrary.current = loaded;
            setLibrary(loaded);
            setStorageError('');
            setStaleLibrary(false);
        } catch (error) {
            setStorageError(`方案库读取失败：${(error as Error).message}`);
            currentLibrary.current = null;
            setLibrary(null);
        }
    }
    useEffect(() => {
        reloadLibrary();
        const changed = (event: StorageEvent) => {
            if (event.storageArea === window.localStorage
                && (event.key === null || event.key === 'nba2k27.buildStudio.library.v1')) {
                setStaleLibrary(true);
            }
        };
        window.addEventListener('storage', changed);
        return () => window.removeEventListener('storage', changed);
    }, []);
    useEffect(() => {
        if (!dirty) {
            return;
        }
        const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
        window.addEventListener('beforeunload', warn);
        return () => window.removeEventListener('beforeunload', warn);
    }, [dirty]);

    function commit(next: PlanLibrary): boolean {
        const expected = currentLibrary.current;
        if (!expected) {
            message.error('方案库不可用，请先重新载入；当前草稿仍可导出');
            return false;
        }
        try {
            const available = { ...next, plans: next.plans.map(withAvailableAnimationGoals) };
            persistLibrary(window.localStorage, available, expected);
            currentLibrary.current = available;
            setLibrary(available);
            setStorageError('');
            setStaleLibrary(false);
            return true;
        } catch (error) {
            setStorageError(`方案库保存失败：${(error as Error).message}`);
            return false;
        }
    }
    function save(): boolean {
        if (!library) {
            message.error('请先载入方案库');
            return false;
        }
        try {
            const saved = withAvailableAnimationGoals({ ...plan, updatedAt: new Date().toISOString() });
            if (!commit(saveBuild(library, saved))) {
                return false;
            }
            setPlan(saved);
            setBaseline(JSON.stringify(saved));
            message.success('方案已保存');
            return true;
        } catch (error) {
            message.error((error as Error).message);
            return false;
        }
    }
    function replacePlan(next: BuildPlan) {
        const available = withAvailableAnimationGoals(next);
        setPlan(available);
        setBaseline(JSON.stringify(available));
    }
    function loadPlan(id: string) {
        const saved = currentLibrary.current?.plans.find((item) => item.id === id);
        if (saved) replacePlan(saved);
    }
    function acceptImport(next: PlanLibrary, imported: PlanLibrary): boolean {
        if (!commit(next)) return false;
        const saved = imported.plans.find((item) => item.id === plan.id);
        if (saved) {
            if (dirty) {
                setBaseline(JSON.stringify(withAvailableAnimationGoals(saved)));
                message.warning('库中当前方案已更新，未保存草稿已保留，请核对后保存');
            } else {
                replacePlan(saved);
            }
        }
        return true;
    }
    function guard(action: () => void) {
        if (dirty) {
            setPending(() => action);
        } else {
            action();
        }
    }
    const [applyOpen, setApplyOpen] = useState(false);
    const [applyRows, setApplyRows] = useState<PlayerRow[]>([]);
    const [applyTarget, setApplyTarget] = useState<number | null>(null);
    const [applyDetail, setApplyDetail] = useState<PlayerDetail | null>(null);
    const [applyChanges, setApplyChanges] = useState<PlanChange[]>([]);
    const [applyBusy, setApplyBusy] = useState(false);
    const applyReadId = useRef(0);
    useEffect(() => () => { ++applyReadId.current; }, []);
    const fieldLabels = useMemo(() => new Map<string, string>([
        ...[...attributes, ...extraAttributes].map(({ id, label }) => [ratingFields[id], label] as [string, string]),
        [bodyFields.position, '位置'], [bodyFields.height, '身高 (cm)'], [bodyFields.weight, '体重 (kg)'], [bodyFields.wingspan, '臂展 (cm)'],
    ]), []);
    async function openApply() {
        if (!editingRef.current || applyBusy) return;
        const request = ++applyReadId.current;
        setApplyOpen(true);
        setApplyTarget(null);
        setApplyDetail(null);
        setApplyChanges([]);
        try {
            const rows = await listPlayers('', null);
            if (request === applyReadId.current) setApplyRows(rows);
        } catch (error) {
            message.error(`读取名单失败：${(error as Error).message}`);
        }
    }
    async function pickTarget(index: number) {
        const request = ++applyReadId.current;
        setApplyTarget(index);
        setApplyDetail(null);
        setApplyChanges([]);
        setApplyBusy(true);
        try {
            const detail = await readPlayer(index);
            if (request !== applyReadId.current) return;
            const current: Record<string, unknown> = {};
            for (const section of detail.sections) {
                for (const cell of section.fields) {
                    if (cell.writable) {
                        current[cell.key] = cell.value;
                    }
                }
            }
            setApplyDetail(detail);
            setApplyChanges(planChanges(plan, current));
        } catch (error) {
            if (request === applyReadId.current) message.error((error as Error).message);
        } finally {
            if (request === applyReadId.current) setApplyBusy(false);
        }
    }
    async function confirmApply() {
        if (!editingRef.current || applyBusy || !applyDetail || applyDetail.index !== applyTarget || applyChanges.length === 0) {
            return;
        }
        setApplyBusy(true);
        try {
            const result = await applyPlayer(applyDetail.index,
                Object.fromEntries(applyChanges.map((item) => [item.key, item.after])), 'roster', applyDetail.readToken);
            message.success(`已写入 ${applyDetail.name} 的 ${result.changes.length} 项，可在球员编辑页撤销`);
            setApplyOpen(false);
        } catch (error) {
            message.error((error as Error).message);
        } finally {
            setApplyBusy(false);
        }
    }    const [shareOpen, setShareOpen] = useState(false);
    const [shareInput, setShareInput] = useState('');
    const bodyHeights = useMemo(() => heightsFor(plan.position), [plan.position]);
    const bodyTemplate = templateFor(plan.position, plan.heightInches);
    const bodyProblems = bodyIssues(plan);
    function update<K extends keyof BuildPlan>(key: K, value: BuildPlan[K]) {
        // Body fields always land on a published legal combination for the position.
        const body = key === 'position' || key === 'heightInches' || key === 'weightLbs' || key === 'wingspanInches';
        setPlan((current) => {
            const next = { ...current, [key]: value };
            return body ? legalBody(next) : key === 'animationGoals' ? withAvailableAnimationGoals(next) : next;
        });
    }
    async function importFile(file: File) {
        try {
            if (!library) {
                throw new Error('请先修复或重新载入方案库');
            }
            if (file.size > 8_000_000) {
                throw new Error('文件超过 8 MB');
            }
            const parsed = importLibrary(await file.text());
            const collisions = parsed.plans.filter((item) => library.plans.some((saved) => saved.id === item.id));
            if (collisions.length) {
                setIncoming(parsed);
            } else if (acceptImport(mergeLibraries(library, parsed), parsed)) {
                message.success(`已导入 ${parsed.plans.length} 个方案`);
            }
        } catch (error) {
            message.error(`导入失败：${(error as Error).message}`);
        }
    }
    const badges = useMemo(() => catalog.badges.map((badge) => ({
        badge, ...evaluateBadge(badge, plan.ratings, plan.heightInches),
    })), [plan.ratings, plan.heightInches]);
    const animations = useMemo(() => catalog.animations.map((animation) => ({
        animation, result: animationTargetIds.has(animation.id)
            ? evaluateAnimation(animation, plan.ratings, plan.heightInches) : null,
    })), [plan.ratings, plan.heightInches]);
    const searchMode = view === 'library' ? 'library'
        : view === 'animations' || (view === 'builder' && resultTab === 'animations') ? 'animations' : 'badges';
    const query = queries[searchMode];
    const setQuery = (value: string) => setQueries((current) => ({ ...current, [searchMode]: value }));
    const needle = query.trim().toLowerCase();
    const selectedCategories = [...new Set(catalog.badges.map((badge) => badge.category))];
    const visibleBadges = badges.filter(({ badge, current }) =>
        `${badgeName(badge.id)} ${badge.name}`.toLowerCase().includes(needle)
        && (!category || badge.category === category)
        && (!onlyTargets || plan.badgeGoals.some((goal) => goal.id === badge.id))
        && (!eligibility || (eligibility === 'eligible' ? current !== null : current === null)));
    const visibleAnimations = animations.filter(({ animation, result }) =>
        `${animation.name} ${animation.category}`.toLowerCase().includes(needle)
        && (!animationGroup || animation.group === animationGroup)
        && (!onlyTargets || animationTargetIds.has(animation.id) && plan.animationGoals.includes(animation.id))
        && (!eligibility || result?.status === eligibility));

    function goalTier(badge: BadgeRequirement, value: Tier | undefined) {
        update('badgeGoals', [
            ...plan.badgeGoals.filter((goal) => goal.id !== badge.id),
            ...(value ? [{ id: badge.id, tier: value }] : []),
        ]);
    }
    function animationGoal(animation: AnimationRequirement, checked: boolean) {
        if (checked && !animationTargetIds.has(animation.id)) return;
        update('animationGoals', checked ? [...plan.animationGoals.filter((id) => id !== animation.id), animation.id]
            : plan.animationGoals.filter((id) => id !== animation.id));
    }
    function results(mode: 'badges' | 'animations', full = false) {
        return <div className="studio-results">
            <div className="studio-filters">
                <Input.Search allowClear aria-label="搜索门槛" placeholder={mode === 'badges' ? '搜索中文徽章名' : '搜索动作包'}
                    value={query} onChange={(event) => setQuery(event.target.value)} />
                {mode === 'badges' ? <Select allowClear aria-label="徽章类别" placeholder="全部类别"
                    value={category} onChange={setCategory}
                    options={selectedCategories.map((value) => ({ value, label: categoryNames[value] ?? value }))} />
                    : <Select allowClear aria-label="动作类型" placeholder="全部类型" value={animationGroup}
                        onChange={setAnimationGroup} options={Object.entries(animationGroups).map(([value,label]) => ({ value,label }))} />}
                <Select allowClear aria-label="门槛状态" placeholder="全部状态" value={eligibility}
                    onChange={setEligibility} options={[
                        { value: 'eligible', label: '可用' }, { value: 'blocked', label: '未达门槛' },
                    ]} />
                <Checkbox checked={onlyTargets} onChange={(event) => setOnlyTargets(event.target.checked)}>仅目标</Checkbox>
            </div>
            {mode === 'badges' ? <Table rowKey="id" size="small" dataSource={visibleBadges}
                pagination={{ pageSize: full ? 20 : 8, showSizeChanger: false, showTotal: (total) => `${total} 枚徽章` }}
                scroll={{ x: full ? 960 : 460 }}
                columns={[
                    { title: '徽章', key: 'name', width: 220, render: (_, item) =>
                        <BadgeIdentity name={item.badge.id} tier={item.current} /> },
                    { title: '身高分组', key: 'tierGroup', width: 96, render: (_, item) => {
                        const group = badgeTierGroup(item.badge.name, plan.heightInches);
                        // 19 of 53 current badges are absent from the published height table.
                        return group === undefined ? <span className="muted">未公布</span>
                            : group === null ? <Tag>此身高不可用</Tag>
                                : <Tooltip title="+1 最高等级加成只能给第 1 组、第 2 组各一枚徽章"><Tag color={group === 1 ? 'gold' : 'blue'}>第 {group} 组</Tag></Tooltip>;
                    } },
                    ...(full ? tiers.map((tier) => ({
                        title: tierNames[tier], key: tier, width: 185,
                        render: (_: unknown, item: typeof badges[number]) => <span className="rule-text">{ruleText(item.badge.tiers[tier])}</span>,
                    })) : [{
                        title: '下一级门槛', key: 'next', width: 220, render: (_: unknown, item: typeof badges[number]) =>
                            item.next ? <div className="rule-text">{tierNames[item.next.tier]} · {ruleText(item.badge.tiers[item.next.tier])}
                                <div className="rule-gap">{gapText(item.next.result)}</div></div>
                                : <span className="muted">已达当前资料最高档</span>,
                    }]),
                    { title: '目标', key: 'goal', width: 106, render: (_, item) => <Select
                        aria-label={`${badgeName(item.id)}目标等级`} allowClear placeholder="未设定"
                        value={plan.badgeGoals.find((goal) => goal.id === item.id)?.tier}
                        onChange={(value) => goalTier(item.badge, value)}
                        options={tiers.map((value) => ({ value, label: tierNames[value] }))} /> },
                ]} />
                : <Table rowKey={(item) => item.animation.id} size="small" dataSource={visibleAnimations}
                    pagination={{ pageSize: full ? 20 : 8, showSizeChanger: false, showTotal: (total) => `${total} 个动作` }}
                    scroll={{ x: 560 }} columns={[
                        { title: '动作', key: 'name', width: 185, render: (_, { animation }) => <div>
                            <strong>{animation.name}</strong><div className="muted">{animation.category}
                                {animation.scope === 'baseOnly' ? ' · 仅基础跳投' : ''}</div></div> },
                        { title: '要求', key: 'rule', width: 260, render: (_, { animation, result }) =>
                            result ? <div className="rule-text">{animation.minHeight}–{animation.maxHeight} in
                                <div>{animation.rule.terms.length ? ruleText(animation.rule) : '无属性门槛'}</div>
                                <div className="rule-gap">{result.heightMatches === false ? '身高不符' : gapText(result)}</div></div>
                                : <span className="muted">未开放门槛规划</span> },
                        { title: '状态', key: 'status', width: 100, render: (_, { result }) =>
                            result ? <span className={`eligibility-${result.status}`}>{statusNames[result.status]}</span>
                                : <span className="muted">仅供浏览</span> },
                        { title: '目标', key: 'goal', width: 62, render: (_, { animation }) =>
                            animationTargetIds.has(animation.id) ? <Checkbox aria-label={`${animation.name}设为目标`}
                                checked={plan.animationGoals.includes(animation.id)}
                                onChange={(event) => animationGoal(animation, event.target.checked)} />
                                : null },
                    ]} />}
        </div>;
    }
    const compareOptions = [
        { value: '__draft', label: `${plan.name || '未命名'} · 当前草稿` },
        ...(library?.plans.map((item) => ({ value: item.id, label: item.name })) ?? []),
    ];
    const getPlan = (id?: string) => id === '__draft' ? plan : library?.plans.find((item) => item.id === id);
    const left = getPlan(leftId);
    const right = getPlan(rightId);
    const comparison = useMemo(() => left && right ? compareBuilds(left, right, planningCatalog) : null, [left, right]);

    return <div className="build-studio">
        <div className="module-toolbar studio-toolbar">
            <strong>{labels[view]}</strong>
            <span className="studio-current-name">{plan.name || '未命名方案'}</span>
            <span className="spacer" /><span className="muted">{dirty ? '未保存修改' : library?.plans.some((item) => item.id === plan.id) ? '已保存' : '未保存方案'}</span>
            <Tooltip title="新建方案"><Button aria-label="新建方案" icon={<PlusOutlined />}
                onClick={() => guard(() => replacePlan(createBuild()))} /></Tooltip>
            <Button type="primary" icon={<SaveOutlined />} onClick={save} disabled={!library || staleLibrary}>保存</Button>
            <Tooltip title="复制分享码"><Button aria-label="复制分享码" icon={<ShareAltOutlined />} onClick={async () => {
                try {
                    const code = encodeShareCode(plan);
                    await navigator.clipboard.writeText(code);
                    message.success(`已复制分享码（${code.length} 个字符），对方粘贴即可导入`);
                } catch (error) { message.error((error as Error).message); }
            }} /></Tooltip>
            <Tooltip title="粘贴分享码"><Button aria-label="粘贴分享码" icon={<LinkOutlined />}
                onClick={() => { setShareInput(''); setShareOpen(true); }} /></Tooltip>
            <Tooltip title="导出当前方案"><Button aria-label="导出当前方案" icon={<ExportOutlined />}
                onClick={() => { try { download({ ...blankLibrary, plans: [plan] }, plan.name || '方案'); }
                    catch (error) { message.error((error as Error).message); } }} /></Tooltip>
        </div>
        <Modal title="把方案应用到名单球员" open={applyOpen} width={720} okText={`写入 ${applyChanges.length} 项`}
            cancelText="取消" okButtonProps={{ disabled: !editing || !applyDetail || applyChanges.length === 0, loading: applyBusy }}
            onCancel={() => { ++applyReadId.current; setApplyBusy(false); setApplyOpen(false); }} onOk={confirmApply}>
            <Select showSearch aria-label="目标球员" placeholder="搜索并选择球员" style={{ width: '100%' }}
                optionFilterProp="label" value={applyTarget ?? undefined} loading={applyBusy}
                options={applyRows.map((row) => ({ value: row.index, label: `#${row.index} ${row.name}${row.team ? ` · ${row.team}` : ''}` }))}
                onChange={(value) => void pickTarget(value)} />
            {applyDetail && <div className="muted" style={{ margin: '8px 0' }}>
                ${applyDetail.name}：当前总评 ${applyDetail.summary?.overall ?? '未知'}，方案预估 ${planOverall(plan) ?? '—'}。写入走球员编辑同一流程，有操作记录，可撤销。</div>}
            {applyDetail && (applyChanges.length === 0
                ? <Empty description="该球员已与方案一致" />
                : <Table size="small" rowKey="key" pagination={false} scroll={{ y: 320 }} dataSource={applyChanges}
                    columns={[
                        { title: '字段', key: 'label', render: (_, item) => fieldLabels.get(item.key) ?? item.key },
                        { title: '当前', key: 'before', render: (_, item) => String(item.before ?? '—') },
                        { title: '方案', key: 'after', render: (_, item) => String(item.after) },
                    ]} />)}
        </Modal>        <Modal title="粘贴分享码" open={shareOpen} okText="作为新方案打开" cancelText="取消"
            onCancel={() => setShareOpen(false)} onOk={() => {
                try {
                    const shared = decodeShareCode(shareInput);
                    setShareOpen(false);
                    guard(() => replacePlan(shared));
                } catch (error) { message.error((error as Error).message); }
            }}>
            <Input.TextArea aria-label="分享码" autoSize={{ minRows: 3, maxRows: 8 }} value={shareInput}
                placeholder="2K27B1-..." onChange={(event) => setShareInput(event.target.value)} />
            <div className="muted">分享码只包含位置、身材、属性、徽章目标、名称和备注；打开后是一个未保存的新方案。</div>
        </Modal>        {(storageError || staleLibrary) && <Alert type="error" showIcon message={storageError || '其他窗口已更新方案库，请重新载入后保存'}
            action={<Button icon={<ReloadOutlined />} onClick={reloadLibrary}>重新载入</Button>} />}
        {view === 'builder' && <div className="builder-layout">
            <aside className="builder-body">
                {(() => {
                    const scores = categoryScores(planValues(plan));
                    return <div className="builder-summary" aria-label="方案概览">
                        <div className="builder-summary-head">
                            <OverallRing value={planOverall(plan)} label="预估"
                                title="按名单球员的属性和游戏总评拟合，按位置分别计算；约 83–90% 的球员误差在 ±1 以内。和 MyPLAYER 的总评规则不一定相同。" />
                            <div><div className="muted">{plan.position} · {Math.floor(plan.heightInches / 12)}′{plan.heightInches % 12}″ · {plan.weightLbs} 磅</div>
                                <ArchetypeLabel archetype={scores ? archetypeOf(scores) : null} /></div>
                        </div>
                        {scores && <CategoryBars scores={scores} />}
                    </div>;
                })()}
                <h2>身体参数</h2>
                <label className="body-field"><span>方案名称</span><Input aria-label="方案名称" maxLength={120}
                    value={plan.name} onChange={(event) => update('name', event.target.value)} /></label>
                <label className="body-field"><span>位置</span><Select aria-label="建模位置" value={plan.position}
                    options={positions.map((value) => ({ value, label: value }))}
                    onChange={(value) => update('position', value)} /></label>
                <Segmented size="small" aria-label="身体单位" value={units} onChange={(value) => setUnits(String(value))}
                    options={[{ value: 'imperial', label: '英制' }, { value: 'metric', label: '公制' }]} />
                {([
                    ['heightInches', '身高', bodyHeights[0] ?? 69, bodyHeights[bodyHeights.length - 1] ?? 88, 2.54, 'cm', 'in'],
                    ['weightLbs', '体重', bodyTemplate?.weightMin ?? 130, bodyTemplate?.weightMax ?? 400, .45359237, 'kg', 'lb'],
                    ['wingspanInches', '臂展', bodyTemplate?.wingspanMin ?? 60, bodyTemplate?.wingspanMax ?? 110, 2.54, 'cm', 'in'],
                ] as const).map(([key,label,min,max,factor,metric,imperial]) => {
                    const scale = units === 'metric' ? factor : 1;
                    return <label className="body-field" key={key}><span>{label} · {units === 'metric' ? metric : imperial}</span>
                        <InputNumber aria-label={`建模${label}`} value={Number((plan[key] * scale).toFixed(1))}
                            min={Number((min * scale).toFixed(1))} max={Number((max * scale).toFixed(1))}
                            precision={units === 'metric' ? 1 : 0} step={units === 'metric' ? Number(scale.toFixed(2)) : 1}
                            onChange={(value) => { if (value !== null) update(key, Math.max(min, Math.min(max, Math.round(value / scale)))); }} />
                    </label>;
                })}
                <div className="body-range">{bodyTemplate ? `可选范围：身高 ${bodyHeights[0]}–${bodyHeights[bodyHeights.length - 1]} in · 体重 ${bodyTemplate.weightMin}–${bodyTemplate.weightMax} lb · 臂展 ${bodyTemplate.wingspanMin}–${bodyTemplate.wingspanMax} in` : '没有该位置的身体模板'}
                    {bodyProblems.map((issue) => <div className="body-issue" key={issue}>{issue}</div>)}
                    <div className="muted">身体模板来源：<a href={bodySource.url} target="_blank" rel="noreferrer">NBA2KLab</a>，更新 {String(bodySource.updatedAt ?? bodySource.capturedAt).slice(0, 10)}</div></div>
                <Button block disabled={!editing || applyBusy} icon={<UserSwitchOutlined />} onClick={openApply}>应用到名单球员</Button>
            </aside>
            <div className="builder-attributes"><h2>属性</h2>
                {Object.entries(groupColors).map(([group,color]) => <section className="attribute-group" key={group}
                    style={{ '--category': color } as React.CSSProperties}><h3>{group}</h3>
                    {attributes.filter((item) => item.group === group).map(({ id,label }) => <div className="attribute-control" key={id}>
                        <label htmlFor={`build-${id}`}>{label}</label>
                        <Slider aria-label={`${label}滑块`} min={25} max={99} value={plan.ratings[id]}
                            onChange={(value) => update('ratings', { ...plan.ratings, [id]: value })} />
                        <InputNumber id={`build-${id}`} aria-label={label} min={25} max={99} precision={0}
                            value={plan.ratings[id]} onChange={(value) => {
                                if (value !== null) update('ratings', { ...plan.ratings, [id]: value });
                            }} />
                    </div>)}
                </section>)}
                <section className="attribute-group" style={{ '--category': 'var(--secondary)' } as React.CSSProperties}>
                    <h3>其他（球员编辑属性）</h3>
                    {extraAttributes.map(({ id,label }) => {
                        const value = plan.extraRatings?.[id] ?? defaultExtraRating;
                        const set = (next: number) => update('extraRatings', { ...defaultExtraRatings(), ...plan.extraRatings, [id]: next });
                        return <div className="attribute-control" key={id}>
                            <label htmlFor={`build-${id}`}>{label}</label>
                            <Slider aria-label={`${label}滑块`} min={25} max={99} value={value} onChange={set} />
                            <InputNumber id={`build-${id}`} aria-label={label} min={25} max={99} precision={0}
                                value={value} onChange={(next) => { if (next !== null) set(next); }} />
                        </div>;
                    })}
                </section>
            </div>
            <div className="builder-requirements"><Tabs activeKey={resultTab} onChange={setResultTab}
                items={[{ key: 'badges', label: '徽章门槛' }, { key: 'animations', label: '动作包' }]} />
                {results(resultTab as 'badges' | 'animations')}
            </div>
        </div>}
        {(view === 'badgeRequirements' || view === 'animations') && <div className="studio-page">
            <div className="studio-summary"><span>{plan.position} · {plan.heightInches} in</span>
                <span>{catalog.badges.length} 枚徽章 · {catalog.animations.length} 个动作</span>
                <span className="muted">资料更新 {catalog.capturedAt.slice(0,10)}</span></div>
            {results(view === 'badgeRequirements' ? 'badges' : 'animations', true)}
        </div>}
        {view === 'library' && <div className="studio-page">
            <div className="studio-filters"><Input.Search allowClear placeholder="搜索方案或标签" aria-label="搜索方案"
                value={query} onChange={(event) => setQuery(event.target.value)} />
                <Checkbox checked={favoriteOnly} onChange={(event) => setFavoriteOnly(event.target.checked)}>仅收藏</Checkbox>
                <span className="spacer" /><Button icon={<ImportOutlined />} disabled={!library || staleLibrary}
                    onClick={() => input.current?.click()}>导入</Button>
                <Button icon={<ExportOutlined />} disabled={!library}
                    onClick={() => { if (library) download(library, 'NBA2K27-方案库'); }}>导出方案库</Button>
                <Tooltip title="重新载入"><Button icon={<ReloadOutlined />} aria-label="重新载入方案库" onClick={reloadLibrary} /></Tooltip>
            </div>
            <Table rowKey="id" size="small" scroll={{ x: 850 }}
                dataSource={(library?.plans ?? []).filter((item) => `${item.name} ${item.tags.join(' ')}`.toLowerCase().includes(needle)
                    && (!favoriteOnly || item.favorite))}
                locale={{ emptyText: <Empty description={library ? '尚无保存的方案' : '方案库不可用'} /> }}
                columns={[
                    { title: '收藏', key: 'favorite', width: 54, render: (_, item) =>
                        <Button type="text" aria-label={`${item.name}${item.favorite ? '取消收藏' : '收藏'}`}
                            icon={item.favorite ? <StarFilled /> : <StarOutlined />}
                            onClick={() => {
                                const favorite = !item.favorite;
                                if (library && commit(saveBuild(library, { ...item, favorite })) && plan.id === item.id) {
                                    setPlan((current) => ({ ...current, favorite }));
                                    setBaseline((current) => JSON.stringify({ ...JSON.parse(current), favorite }));
                                }
                            }} /> },
                    { title: '方案', dataIndex: 'name', render: (name, item) => <button className="text-command"
                        onClick={() => guard(() => loadPlan(item.id))}>{name}{plan.id === item.id ? ' · 当前' : ''}</button> },
                    { title: '位置', dataIndex: 'position', width: 64 },
                    { title: '身材', key: 'body', render: (_, item) => `${item.heightInches} in / ${item.weightLbs} lb / ${item.wingspanInches} in` },
                    { title: '标签', key: 'tags', render: (_, item) => item.tags.map((tag) => <Tag key={tag}>{tag}</Tag>) },
                    { title: '更新时间', dataIndex: 'updatedAt', render: (value: string) => new Date(value).toLocaleString() },
                    { title: '操作', key: 'actions', width: 128, render: (_, item) => <div className="inline-actions">
                        <Tooltip title="载入"><Button aria-label={`载入${item.name}`} icon={<FolderOpenOutlined />}
                            onClick={() => guard(() => loadPlan(item.id))} /></Tooltip>
                        <Tooltip title="复制"><Button aria-label={`复制${item.name}`} icon={<CopyOutlined />}
                            onClick={() => { if (library) commit(saveBuild(library, duplicateBuild(item))); }} /></Tooltip>
                        <Popconfirm title={`删除「${item.name}」？`} onConfirm={() => {
                            const remove = () => {
                                const latest = currentLibrary.current;
                                if (latest && commit(deleteBuild(latest, item.id)) && plan.id === item.id) {
                                    replacePlan(createBuild());
                                }
                            };
                            if (plan.id === item.id) guard(remove); else remove();
                        }}><Button danger aria-label={`删除${item.name}`} icon={<DeleteOutlined />} /></Popconfirm>
                    </div> },
                ]} />
            <section className="plan-metadata"><h2>当前方案</h2>
                <label className="body-field"><span>标签</span><Select aria-label="方案标签" mode="tags" value={plan.tags}
                    maxCount={20} onChange={(tags) => update('tags', tags)} /></label>
                <Checkbox checked={plan.favorite} onChange={(event) => update('favorite', event.target.checked)}>收藏当前方案</Checkbox>
                <label className="body-field"><span>备注</span><Input.TextArea aria-label="方案备注" maxLength={10000}
                    value={plan.notes} onChange={(event) => update('notes', event.target.value)} /></label>
            </section>
        </div>}
        {view === 'compare' && <div className="studio-page">
            <div className="studio-filters compare-selects"><Select aria-label="左侧方案" value={leftId} onChange={setLeftId} options={compareOptions} />
                <Button icon={<SwapOutlined />} aria-label="交换方案" disabled={!rightId} onClick={() => {
                    if (rightId) { setLeftId(rightId); setRightId(leftId); }
                }} /><Select aria-label="右侧方案" placeholder="选择对比方案" value={rightId} onChange={setRightId} options={compareOptions} />
                <Checkbox checked={differencesOnly} onChange={(event) => setDifferencesOnly(event.target.checked)}>仅差异</Checkbox></div>
            {!comparison ? <Empty description="选择两个方案" /> : <>
                <div className="studio-summary"><span>身高差 {comparison.body.heightInches > 0 ? '+' : ''}{comparison.body.heightInches} in</span>
                    <span>体重差 {comparison.body.weightLbs > 0 ? '+' : ''}{comparison.body.weightLbs} lb</span>
                    <span>臂展差 {comparison.body.wingspanInches > 0 ? '+' : ''}{comparison.body.wingspanInches} in</span></div>
                <Table size="small" rowKey="id" pagination={false} dataSource={comparison.attributes.filter((item) => !differencesOnly || item.delta)}
                    columns={[{ title: '属性', dataIndex: 'label' }, { title: left?.name, dataIndex: 'before' },
                        { title: right?.name, dataIndex: 'after' }, { title: '右侧 − 左侧', dataIndex: 'delta',
                            render: (value: number) => <span className={value > 0 ? 'delta-positive' : value < 0 ? 'delta-negative' : ''}>{value > 0 ? '+' : ''}{value}</span> }]} />
                <h2>徽章变化 · {comparison.badges.length}</h2>
                <Table size="small" rowKey="id" dataSource={comparison.badges}
                    columns={[{ title: '徽章', key: 'badge', render: (_, item) => <BadgeIdentity name={item.id} /> },
                        { title: left?.name, dataIndex: 'before', render: (value: Tier | null) => value ? tierNames[value] : '未达门槛' },
                        { title: right?.name, dataIndex: 'after', render: (value: Tier | null) => value ? tierNames[value] : '未达门槛' }]} />
                <h2>动作变化 · {comparison.animations.length}</h2>
                <Table size="small" rowKey="id" dataSource={comparison.animations}
                    columns={[{ title: '动作', dataIndex: 'name' },
                        { title: left?.name, dataIndex: 'before', render: (value: keyof typeof statusNames) => statusNames[value] },
                        { title: right?.name, dataIndex: 'after', render: (value: keyof typeof statusNames) => statusNames[value] }]} />
            </>}
        </div>}
        <input type="file" accept=".json,application/json" hidden ref={input} onChange={(event) => {
            const file = event.target.files?.[0];
            if (file) void importFile(file);
            event.target.value = '';
        }} />
        <Modal title="当前方案有未保存修改" open={pending !== null} onCancel={() => setPending(null)} footer={[
            <Button key="cancel" onClick={() => setPending(null)}>返回编辑</Button>,
            <Button key="discard" onClick={() => { const action = pending; setPending(null); action?.(); }}>放弃并继续</Button>,
            <Button key="save" type="primary" onClick={() => { if (save()) { const action = pending; setPending(null); action?.(); } }}>保存并继续</Button>,
        ]}>方案：{plan.name || '未命名'}</Modal>
        <Modal title="导入存在相同编号的方案" open={incoming !== null} onCancel={() => setIncoming(null)}
            okText="替换冲突方案" onOk={() => {
                if (library && incoming && acceptImport(mergeLibraries(library, incoming, 'replace'), incoming)) {
                    setIncoming(null);
                    message.success('导入完成');
                }
            }}>
            {incoming?.plans.filter((item) => library?.plans.some((saved) => saved.id === item.id)).map((item) =>
                <p key={item.id}>{item.name}</p>)}
        </Modal>
    </div>;
}
