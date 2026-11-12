import type { ReactNode } from 'react';
import { entityHref } from '../navigation';
import type { EntitySource } from '../navigation';

export default function EntityLink({ page, index, source = 'roster', children }: {
    page: 'players' | 'teams'; index: number | null | undefined; source?: EntitySource; children: ReactNode;
}) {
    return index === null || index === undefined || index < 0 ? <>{children}</>
        : <a className="entity-link" href={entityHref(page, index, source)}
            onClick={(event) => event.stopPropagation()} onDoubleClick={(event) => event.stopPropagation()}>{children}</a>;
}
