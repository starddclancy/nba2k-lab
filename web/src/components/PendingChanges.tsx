import { Button, Tooltip } from 'antd';
import { UndoOutlined } from '@ant-design/icons';
import type { FieldCell } from '../types';
import { displayValue, fieldLabel } from '../labels';

type Draft = Record<string, number | string | boolean>;

/** Keep restored values out of the write request as well as the dirty count. */
export function stageField(draft: Draft, cell: FieldCell, next: Draft[string]): Draft {
    const result = { ...draft };
    if (next === cell.value) delete result[cell.key];
    else result[cell.key] = next;
    return result;
}

export default function PendingChanges({ fields, draft, label, disabled, onReset, onResetAll, onJump }: {
    fields: FieldCell[];
    draft: Draft;
    label: string;
    disabled: boolean;
    onReset: (key: string) => void;
    onResetAll: () => void;
    onJump?: (cell: FieldCell) => void;
}) {
    const pending = fields.filter((cell) => cell.key in draft);
    if (pending.length === 0) return null;
    return <section className="pending-changes" aria-label={label}>
        <div className="pending-heading"><strong>{pending.length} 项未应用</strong>
            <Button size="small" type="text" disabled={disabled} onClick={onResetAll}>全部还原</Button></div>
        <div className="pending-list">{pending.map((cell) => <div className="pending-row" key={cell.key}>
            {onJump ? <Button type="link" size="small" className="pending-field" title={cell.key}
                onClick={() => onJump(cell)}>{fieldLabel(cell)}</Button>
                : <span className="pending-field" title={cell.key}>{fieldLabel(cell)}</span>}
            <span className="pending-value">{displayValue(cell)} → {displayValue(cell, draft[cell.key])}</span>
            <Tooltip title="还原此项"><Button size="small" type="text" icon={<UndoOutlined />}
                aria-label={`还原 ${fieldLabel(cell)}`} disabled={disabled} onClick={() => onReset(cell.key)} /></Tooltip>
        </div>)}</div>
    </section>;
}
