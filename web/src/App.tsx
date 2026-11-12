import { listPlayers, listTeamRecords } from './api';
import { snapshotLeague } from './seasonTracker';

/** Per-game stats from season totals: snapshot the league; read-only for the game. */
const trackSeason = () => (isPreview ? Promise.resolve(0) : snapshotLeague(() => call('leagueSeason'), () => listPlayers('', null)).catch(() => 0));
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { App as AntApp, Button, Layout, Tooltip } from 'antd';
import {
    UserOutlined, TeamOutlined, SwapOutlined, IdcardOutlined, SunOutlined, MoonOutlined, TrophyOutlined, CalendarOutlined,
    HistoryOutlined, SlidersOutlined, PauseCircleOutlined, ControlOutlined,
    SafetyCertificateOutlined, PlayCircleOutlined, FolderOpenOutlined, BarChartOutlined,
    UnorderedListOutlined, ApiOutlined, LineChartOutlined,
} from '@ant-design/icons';
import { connect } from './api';
import type { SessionInfo } from './types';
import { call, isBrowserLive, isPreview } from './bridge';
import GsPanel from './components/GsPanel';
import RosterPanel from './components/RosterPanel';
import LibraryPanel from './components/LibraryPanel';
import PlayersPage from './components/PlayersPage';
import AnalyticsPage from './components/AnalyticsPage';
import ComparePage from './components/ComparePage';
import LeadersPage from './components/LeadersPage';
import SchedulePage from './components/SchedulePage';
import PageBoundary from './components/PageBoundary';
import { useThemeMode } from './themeContext';
import TeamPanel from './components/TeamPanel';
import HistoryPanel from './components/HistoryPanel';
import BuildStudio, { type StudioView } from './components/BuildStudio';
import JournalPanel from './components/JournalPanel';
import MatchPanel from './components/MatchPanel';
import { parseRoute } from './navigation';
import { WorkspaceContext } from './workspaceContext';
import { EditingSwitch, useEditing } from './editingContext';

const { Content } = Layout;

type Mode = 'players' | 'analytics' | 'versus' | 'leaders' | 'schedule' | 'gs' | 'match' | 'roster' | 'playerLibrary' | 'teams' | 'leagueHistory' | 'teamHistory' | 'journals' | StudioView;
const directMatch = new URLSearchParams(window.location.search).has('match');
const directStudio = new URLSearchParams(window.location.search).has('studio');
const studioViews: StudioView[] = ['builder', 'badgeRequirements', 'animations', 'library', 'compare'];

export default function Shell() {
    const { message } = AntApp.useApp();
    const { editing, setEditing, requestLeave } = useEditing();
    const [session, setSession] = useState<SessionInfo | null>(null);
    // Preview-only deep link (?preview&page=roster) so screenshots can open a module directly.
    const previewPage = isPreview ? new URLSearchParams(window.location.search).get('page') as Mode | null : null;
    const [route, setRoute] = useState(() => parseRoute(window.location.hash));
    const [mode, setMode] = useState<Mode>((route?.page as Mode | undefined) ?? previewPage ?? (directStudio ? 'builder' : directMatch ? 'match' : 'players'));
    const acceptedHash = useRef(window.location.hash);
    useEffect(() => {
        let revision = 0;
        const changed = async () => {
            const current = ++revision;
            const hash = window.location.hash;
            if (hash === acceptedHash.current) return;
            const accepted = await requestLeave();
            if (current !== revision) return;
            if (!accepted) {
                window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}${acceptedHash.current}`);
                return;
            }
            acceptedHash.current = hash;
            const next = parseRoute(hash);
            setRoute(next);
            if (next) setMode(next.page as Mode);
        };
        window.addEventListener('hashchange', changed);
        return () => { ++revision; window.removeEventListener('hashchange', changed); };
    }, [requestLeave]);
    const planning = studioViews.includes(mode as StudioView);
    const [busy, setBusy] = useState(false);
    const [attachError, setAttachError] = useState('');
    useLayoutEffect(() => {
        setEditing(false);
    }, [mode, route?.index, route?.source, session, setEditing]);

    const handleConnect = useCallback(async () => {
        if (!await requestLeave()) return;
        setBusy(true);
        try {
            // Reconnecting remounts the pages so they re-read from the new session.
            setEditing(false);
            setSession(null);
            const info = await connect();
            // Team logos by name everywhere (game logs, leaderboards): read the brands once.
            await listTeamRecords().catch(() => undefined);
            setSession(info);
            void trackSeason();
            setAttachError('');
            message.success(`已连接游戏，共 ${info.teams} 支球队`);
        } catch (error) {
            setAttachError((error as Error).message);
            message.error((error as Error).message);
        } finally {
            setBusy(false);
        }
    }, [message, setEditing, requestLeave]);

    useEffect(() => {
        if (directMatch || directStudio || !isPreview) {
            return;
        }
        // Sample data can load immediately. Real process access requires the connection action.
        let alive = true;
        connect()
            .then(async (info) => { await listTeamRecords().catch(() => undefined); if (alive) setSession(info); })
            .catch((error: Error) => { if (alive) setAttachError(error.message); });
        return () => {
            alive = false;
        };
    }, []);

    // While connected, snapshot season totals every two minutes so each finished game is recorded.
    useEffect(() => {
        if (!session || isPreview) return;
        const timer = window.setInterval(() => { void trackSeason(); }, 120_000);
        return () => window.clearInterval(timer);
    }, [session]);

    const { mode: themeMode, toggle: toggleTheme } = useThemeMode();
    const navGroups = [
        { label: '名单', items: [
            { key: 'players', label: '球员', icon: <UserOutlined /> },
            { key: 'teams', label: '球队', icon: <TeamOutlined /> },
            { key: 'roster', label: '交易', icon: <SwapOutlined /> },
            { key: 'playerLibrary', label: '球员库与面部', icon: <IdcardOutlined /> },
        ] },
        { label: '分析', items: [
            { key: 'leaders', label: '排行榜', icon: <TrophyOutlined /> },
            { key: 'analytics', label: '数据分析', icon: <LineChartOutlined /> },
            { key: 'versus', label: '对比', icon: <BarChartOutlined /> },
            { key: 'schedule', label: '赛程', icon: <CalendarOutlined /> },
        ] },
        { label: '历史', items: [
            { key: 'leagueHistory', label: 'NBA 历史', icon: <TrophyOutlined /> },
            { key: 'teamHistory', label: '球队历史', icon: <HistoryOutlined /> },
        ] },
        { label: '比赛', items: [
            { key: 'gs', label: '比赛设置', icon: <SlidersOutlined /> },
            { key: 'match', label: '场内数据', icon: <PauseCircleOutlined /> },
        ] },
        { label: '建模', items: [
            { key: 'builder', label: '建模器', icon: <ControlOutlined /> },
            { key: 'badgeRequirements', label: '徽章门槛', icon: <SafetyCertificateOutlined /> },
            { key: 'animations', label: '动作包', icon: <PlayCircleOutlined /> },
            { key: 'library', label: '方案库', icon: <FolderOpenOutlined /> },
            { key: 'compare', label: '方案对比', icon: <BarChartOutlined /> },
        ] },
    ];
    const activeGroup = navGroups.find((group) => group.items.some((item) => item.key === mode)) ?? null;
    const modeLabel = navGroups.flatMap((group) => group.items).find((item) => item.key === mode)?.label ?? '操作记录';
    const navigate = (key: string) => { window.location.hash = `/${key}`; };

    return (
        <WorkspaceContext.Provider value={{ route, seasonEndYear: session?.seasonEndYear ?? null }}>
        <Layout className="shell">
            <header className="shell-top">
                <div className="shell-brand"><span className="brand-mark" aria-hidden>2K27</span><span>Roster Lab</span></div>
                <nav className="shell-groups" aria-label="模块分组">
                    {navGroups.map((group) => <button key={group.label} type="button"
                        className={activeGroup === group ? 'active' : ''}
                        onClick={() => navigate(group.items[0].key)}>{group.label}</button>)}
                </nav>
                <span className="spacer" />
                {isPreview && <span className="preview-indicator">示例数据</span>}
                <Tooltip title={themeMode === 'dark' ? '切换到浅色' : '切换到深色'}>
                    <Button type="text" className="theme-switch" aria-label="切换深浅色主题"
                        icon={themeMode === 'dark' ? <SunOutlined /> : <MoonOutlined />} onClick={toggleTheme} />
                </Tooltip>
            </header>
            <nav className="shell-tabs" aria-label="工作台导航">
                {(activeGroup?.items ?? []).map((item) => <button key={item.key} type="button"
                    aria-current={mode === item.key ? 'page' : undefined}
                    className={mode === item.key ? 'active' : ''}
                    onClick={() => navigate(item.key)}>
                    {item.icon}<span>{item.label}</span>
                    {item.key === 'match' && <small>调试</small>}
                </button>)}
                {!activeGroup && <button type="button" className="active">
                    <UnorderedListOutlined /><span>操作记录</span></button>}
                <span className="spacer" />
                {!['analytics', 'versus', 'leaders', 'journals', 'badgeRequirements', 'animations', 'library', 'compare'].includes(mode)
                    && <EditingSwitch />}
            </nav>
            <main className="shell-main">
                <div className="studio-host" hidden={!planning}>
                    <PageBoundary page={planning ? mode : 'builder'}><BuildStudio view={planning ? mode as StudioView : 'builder'} /></PageBoundary>
                </div>            <Content className="app-content" style={{ display: planning ? 'none' : undefined }}>
                <PageBoundary page={mode}>
                {mode === 'match' ? <MatchPanel />
                : !session && mode !== 'journals' ? <div className="empty-hint">
                    {isBrowserLive ? '场内开发连接仅用于场内数据，请选择场内数据' : '连接游戏后读取名单，或使用侧栏中的建模与方案工具'}
                </div> : mode === 'leaders' ? (
                    <LeadersPage />
                ) : mode === 'versus' ? (
                    <ComparePage />
                ) : mode === 'analytics' ? (
                    <AnalyticsPage />
                ) : mode === 'schedule' ? (
                    <SchedulePage />
                ) : mode === 'gs' ? (
                    <GsPanel />
                ) : mode === 'roster' ? (
                    <RosterPanel />
                ) : mode === 'playerLibrary' ? (
                    <LibraryPanel />
                ) : mode === 'teams' ? (
                    <TeamPanel />
                ) : mode === 'leagueHistory' ? (
                    <HistoryPanel key="league" scope="league" editable={editing} />
                ) : mode === 'teamHistory' ? (
                    <HistoryPanel key="team" scope="team" editable={editing} />
                ) : mode === 'journals' ? (
                    <JournalPanel />
                ) : (
                    <PlayersPage connected={!!session} />
                )}
                </PageBoundary>
            </Content>
            </main>
            <footer className="shell-status">
                <span className={`status-dot${session ? ' on' : attachError ? ' error' : ''}`} />
                <span>{session
                    ? `${isPreview ? '示例名单' : `已连接 NBA2K27.exe · PID ${session.pid}`} · ${session.roster} 名球员 · ${session.teams} 支球队`
                    : attachError ? `未连接：${attachError}` : '未连接游戏'}</span>
                {mode !== 'match' && <Button size="small" type="link" icon={<ApiOutlined />} onClick={handleConnect} loading={busy}
                    disabled={isBrowserLive}>{session ? '重新连接' : '连接游戏'}</Button>}
                <span className="spacer" />
                <span className="status-mode">{activeGroup ? `${activeGroup.label} / ${modeLabel}` : modeLabel}</span>
                <Button size="small" type="link" icon={<UnorderedListOutlined />} onClick={() => navigate('journals')}>操作记录</Button>
            </footer>
        </Layout>
        </WorkspaceContext.Provider>
    );
}
