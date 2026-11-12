import { createContext, useContext } from 'react';
import type { WorkspaceRoute } from './navigation';

export const WorkspaceContext = createContext<{ route: WorkspaceRoute | null; seasonEndYear: number | null }>({
    route: null, seasonEndYear: null,
});
export const useWorkspace = () => useContext(WorkspaceContext);
