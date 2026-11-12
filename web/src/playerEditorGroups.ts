import { attributes } from './builds.ts';
import { ratingFields } from './buildApply.ts';
import { fieldGroupLabel, fieldLabel, sectionLabel } from './labels.ts';
import type { FieldCell } from './types.ts';

type PlayerEditorGroup = { key: string; label: string; color: string; fields: FieldCell[] };

const categories = [
    { key: 'attribute:finishing', label: '终结', color: 'var(--finishing)' },
    { key: 'attribute:shooting', label: '投射', color: 'var(--shooting)' },
    { key: 'attribute:playmaking', label: '组织', color: 'var(--playmaking)' },
    { key: 'attribute:defense', label: '防守', color: 'var(--defense)' },
    { key: 'attribute:rebounding', label: '篮板', color: 'var(--rebounding)' },
    { key: 'attribute:physical', label: '身体', color: 'var(--physical)' },
];
const standardGroups = new Map(attributes.map(({ id, group }) => [ratingFields[id], group]));

export function groupPlayerEditorFields(
    section: string,
    fields: FieldCell[],
    query = '',
): PlayerEditorGroup[] {
    const groups: PlayerEditorGroup[] = section === 'Attributes'
        ? categories.map((category) => ({ ...category, fields: [] }))
        : [];
    const standard = new Map(groups.map((group) => [group.label, group]));
    const original = new Map<string, PlayerEditorGroup>();
    for (const field of fields) {
        const label = section === 'Attributes' ? standardGroups.get(field.key) : undefined;
        let group = label ? standard.get(label) : undefined;
        if (!group) {
            group = original.get(field.group);
            if (!group) {
                group = {
                    key: `field:${field.group}`,
                    label: fieldGroupLabel(field),
                    color: 'var(--primary)',
                    fields: [],
                };
                original.set(field.group, group);
                groups.push(group);
            }
        }
        group.fields.push(field);
    }
    const needle = query.trim().toLowerCase();
    if (needle) {
        for (const group of groups) {
            group.fields = group.fields.filter((field) =>
                `${group.label} ${fieldLabel(field)} ${sectionLabel(field.group)} ${field.label} ${field.key}`
                    .toLowerCase().includes(needle));
        }
    }
    return groups.filter((group) => group.fields.length > 0);
}
