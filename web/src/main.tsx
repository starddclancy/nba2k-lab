import React, { useEffect, useState } from 'react';
import ReactDOM from 'react-dom/client';
import { ConfigProvider, App as AntApp } from 'antd';
import zhCN from 'antd/locale/zh_CN';
import Shell from './App';
import { antdTheme, applyThemeMode, readThemeMode } from './theme';
import type { ThemeMode } from './theme';
import { ThemeContext } from './themeContext';
import { EditingProvider } from './editingContext';
import './index.css';
import './workspace.css';
import './polish.css';
import './analytics.css';

// Apply before the first paint so the page never flashes the other palette.
applyThemeMode(readThemeMode());

function Root() {
    const [mode, setMode] = useState<ThemeMode>(readThemeMode);
    useEffect(() => applyThemeMode(mode), [mode]);
    return (
        <ThemeContext.Provider value={{ mode, toggle: () => setMode(mode === 'dark' ? 'light' : 'dark') }}>
            <ConfigProvider locale={zhCN} theme={antdTheme(mode)}>
                <AntApp>
                    <EditingProvider><Shell /></EditingProvider>
                </AntApp>
            </ConfigProvider>
        </ThemeContext.Provider>
    );
}

ReactDOM.createRoot(document.getElementById('root') as HTMLElement).render(
    <React.StrictMode>
        <Root />
    </React.StrictMode>,
);
