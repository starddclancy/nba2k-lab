import { createContext, useContext } from 'react';
import type { ThemeMode } from './theme';

export const ThemeContext = createContext<{ mode: ThemeMode; toggle: () => void }>({ mode: 'dark', toggle: () => undefined });
export const useThemeMode = () => useContext(ThemeContext);
