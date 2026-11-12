import { theme as antTheme } from 'antd';
import type { ThemeConfig } from 'antd';

export type ThemeMode = 'dark' | 'light';
const storageKey = 'nba2k27-editor.theme';

/** One palette per mode. CSS reads the same names as custom properties (--canvas, --surface…). */
export const palettes: Record<ThemeMode, Record<string, string>> = {
    // "Midnight court": deep navy layers, cyan accent, coral for losses and warnings.
    dark: {
        canvas: '#070b14', surface: '#0c1322', raised: '#121b2e', input: '#0f1828', line: '#1a2540',
        lineStrong: '#2a3a5c', text: '#e8eefb', secondary: '#9fb0cf', muted: '#62749a',
        primary: '#2ee6d6', primaryHover: '#6ff0e4', onPrimary: '#04121a', primarySoft: 'rgba(46, 230, 214, .10)',
        info: '#6aa8ff', success: '#3ddc97', warning: '#ffc857', danger: '#ff6b6b', dangerSoft: 'rgba(255, 107, 107, .12)',
        rowActive: 'rgba(46, 230, 214, .10)', rowDirty: 'rgba(255, 200, 87, .10)', shadow: '0 16px 40px rgba(0, 0, 0, .5)',
        finishing: '#ff8a5b', shooting: '#2ee6d6', playmaking: '#6aa8ff', defense: '#3ddc97', rebounding: '#c792ff', physical: '#ff6fae',
        bronze: '#d08b5b', silver: '#b9c6dc', gold: '#ffc857', hof: '#c792ff', legend: '#ff6b6b',
        up: '#3ddc97', down: '#ff6b6b', grid: 'rgba(120, 150, 210, .09)', glow: 'rgba(46, 230, 214, .35)',
    },
    light: {
        canvas: '#eef2f8', surface: '#ffffff', raised: '#f5f8fc', input: '#ffffff', line: '#d9e1ee',
        lineStrong: '#b9c6da', text: '#0d1a33', secondary: '#3c4d6e', muted: '#7184a6',
        primary: '#0b8f86', primaryHover: '#0aa79c', onPrimary: '#ffffff', primarySoft: 'rgba(11, 143, 134, .09)',
        info: '#2563d9', success: '#138a5a', warning: '#b07800', danger: '#d63c3c', dangerSoft: 'rgba(214, 60, 60, .08)',
        rowActive: 'rgba(11, 143, 134, .09)', rowDirty: 'rgba(176, 120, 0, .08)', shadow: '0 16px 40px rgba(13, 26, 51, .10)',
        finishing: '#d9572c', shooting: '#0b8f86', playmaking: '#2563d9', defense: '#138a5a', rebounding: '#8048d6', physical: '#c93682',
        bronze: '#9b5a2f', silver: '#5b6b85', gold: '#b07800', hof: '#8048d6', legend: '#d63c3c',
        up: '#138a5a', down: '#d63c3c', grid: 'rgba(13, 26, 51, .06)', glow: 'rgba(11, 143, 134, .25)',
    },
};
/** Numbers use a monospaced face so columns line up like a quote screen. */
export const monoFont = "'Cascadia Mono', 'JetBrains Mono', Consolas, 'Microsoft YaHei UI', monospace";
const kebab = (name: string) => name.replace(/[A-Z]/g, (letter) => `-${letter.toLowerCase()}`);

export function readThemeMode(): ThemeMode {
    const forced = import.meta.env.DEV ? new URLSearchParams(window.location.search).get('theme') : null;
    if (forced === 'dark' || forced === 'light') {
        return forced;
    }
    try {
        const saved = window.localStorage.getItem(storageKey);
        if (saved === 'dark' || saved === 'light') {
            return saved;
        }
    } catch {
        // Storage may be unavailable in WebView2 private contexts; fall back to the default.
    }
    return 'dark';
}

/** Write the palette as CSS custom properties on <html> and remember the choice. */
export function applyThemeMode(mode: ThemeMode) {
    const root = document.documentElement;
    if (root.dataset.theme && root.dataset.theme !== mode) {
        // Cross-fade colors for the switch only, so normal interactions keep their own timing.
        root.classList.add('theme-fade');
        window.setTimeout(() => root.classList.remove('theme-fade'), 400);
    }
    root.dataset.theme = mode;
    root.style.colorScheme = mode;
    for (const [name, value] of Object.entries(palettes[mode])) {
        root.style.setProperty(`--${kebab(name)}`, value);
    }
    try {
        window.localStorage.setItem(storageKey, mode);
    } catch {
        // Not persisting is acceptable; the page still switches.
    }
}

export function antdTheme(mode: ThemeMode): ThemeConfig {
    const p = palettes[mode];
    return {
        algorithm: mode === 'dark' ? antTheme.darkAlgorithm : antTheme.defaultAlgorithm,
        token: {
            colorPrimary: p.primary, colorInfo: p.info, colorSuccess: p.success, colorWarning: p.warning,
            colorError: p.danger, colorBgBase: p.canvas, colorBgContainer: p.surface, colorBgElevated: p.raised,
            colorBorder: p.line, colorBorderSecondary: p.line, colorText: p.text, colorTextSecondary: p.secondary,
            colorTextTertiary: p.muted, borderRadius: 3, borderRadiusSM: 2, borderRadiusLG: 4, fontSize: 13, controlHeight: 30, controlHeightSM: 24,
            fontFamily: "'Segoe UI Variable Display', 'Segoe UI Variable Text', 'Segoe UI', 'Microsoft YaHei UI', system-ui, sans-serif",
            motionDurationMid: '0.18s', motionDurationSlow: '0.24s',
        },
        components: {
            Button: { primaryColor: p.onPrimary, primaryShadow: 'none', defaultShadow: 'none', dangerShadow: 'none' },
            Menu: { itemSelectedBg: p.primarySoft, itemSelectedColor: p.primary },
            Table: { headerBg: p.raised, rowSelectedBg: p.rowActive, rowSelectedHoverBg: p.rowActive, rowHoverBg: p.primarySoft, cellPaddingBlockSM: 4, cellPaddingInlineSM: 8, headerColor: p.muted, headerSplitColor: 'transparent', borderColor: p.line, fontSizeSM: 12 },
            Input: { colorBgContainer: p.input },
            InputNumber: { colorBgContainer: p.input },
            Select: { selectorBg: p.input },
            Tabs: { itemSelectedColor: p.text, inkBarColor: p.primary },
            Segmented: { itemSelectedBg: p.raised },
            Modal: { contentBg: p.surface, headerBg: p.surface },
        },
    };
}
