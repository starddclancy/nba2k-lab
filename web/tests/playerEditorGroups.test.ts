import assert from 'node:assert/strict';
import test from 'node:test';
import { groupPlayerEditorFields } from '../src/playerEditorGroups.ts';
import type { FieldCell } from '../src/types.ts';

const field = (key: string, value = 73): FieldCell => ({
    key, label: key.split('/').at(-1)!, section: key.split('/')[0],
    group: key.split('/')[1], value, raw: value, display: String(value),
    kind: 'number', writable: true, options: [], min: 25, max: 99,
});

test('rebounding combines exact offense and defense fields while keeping input order', () => {
    const defensive = field('Attributes/Defense/DEFENSEREBOUND');
    const offensive = field('Attributes/Offense/OFFENSIVEREBOUND');
    const groups = groupPlayerEditorFields('Attributes', [defensive, offensive]);
    assert.equal(groups.length, 1);
    assert.equal(groups[0].label, '篮板');
    assert.equal(groups[0].color, 'var(--rebounding)');
    assert.deepEqual(groups[0].fields, [defensive, offensive]);
});

test('speed with ball belongs to playmaking and six groups follow builder order', () => {
    const fields = [
        field('Attributes/Athleticism/SPEED'),
        field('Attributes/Defense/DEFENSEREBOUND'),
        field('Attributes/Defense/STEAL'),
        field('Attributes/Athleticism/SPEEDWITHBALL'),
        field('Attributes/Offense/3POINT'),
        field('Attributes/Offense/CLOSESHOT'),
        field('Attributes/Offense/PASSACCURACY'),
    ];
    const groups = groupPlayerEditorFields('Attributes', fields);
    assert.deepEqual(groups.map(({ label, color }) => [label, color]), [
        ['终结', 'var(--finishing)'],
        ['投射', 'var(--shooting)'],
        ['组织', 'var(--playmaking)'],
        ['防守', 'var(--defense)'],
        ['篮板', 'var(--rebounding)'],
        ['身体', 'var(--physical)'],
    ]);
    assert.deepEqual(groups[2].fields, [fields[3], fields[6]]);
});

test('every standard attribute maps exactly once without changing field objects or metadata', () => {
    const keys = [
        'Attributes/Offense/CLOSESHOT', 'Attributes/Offense/DRIVINGLAYUP',
        'Attributes/Offense/DRIVINGDUNK', 'Attributes/Offense/STANDINGDUNK',
        'Attributes/Offense/POSTCONTROL', 'Attributes/Offense/MIDRANGE',
        'Attributes/Offense/3POINT', 'Attributes/Offense/FREETHROW',
        'Attributes/Offense/PASSACCURACY', 'Attributes/Offense/BALLCONTROL',
        'Attributes/Athleticism/SPEEDWITHBALL', 'Attributes/Defense/INTERIORDEFENSE',
        'Attributes/Defense/PERIMETERDEFENSE', 'Attributes/Defense/STEAL',
        'Attributes/Defense/BLOCK', 'Attributes/Offense/OFFENSIVEREBOUND',
        'Attributes/Defense/DEFENSEREBOUND', 'Attributes/Athleticism/SPEED',
        'Attributes/Athleticism/AGILITY', 'Attributes/Athleticism/STRENGTH',
        'Attributes/Athleticism/VERTICAL', 'Attributes/Offense/DRAWFOUL',
        'Attributes/Mental/HELPDEFENSE',
    ];
    const fields = keys.map((key, index) => field(key, 25 + index));
    fields[0].writable = false;
    fields[0].error = 'read-only fixture';
    fields[0].options.push('UNCHANGED');
    const before = structuredClone(fields);
    for (const cell of fields) {
        Object.freeze(cell.options);
        Object.freeze(cell);
    }
    Object.freeze(fields);
    const groups = groupPlayerEditorFields('Attributes', fields);
    const result = groups.flatMap((group) => group.fields);
    assert.equal(result.length, fields.length);
    assert.equal(new Set(result).size, fields.length);
    for (const cell of fields) {
        assert.equal(result.find((item) => item.key === cell.key), cell);
    }
    assert.deepEqual(fields, before);
    assert.deepEqual(groups.map((group) => group.fields.length), [5, 3, 3, 4, 2, 4, 1, 1]);
    assert.equal(groups[6].fields[0], fields[21]);
    assert.equal(groups[7].fields[0], fields[22]);
});

test('unmatched attributes keep original groups and do not infer a category from names', () => {
    const fields = [
        field('Attributes/Offense/SPEEDWITHBALL'),
        field('Attributes/Mental/HELPDEFENSE'),
        field('Attributes/Offense/DRAWFOUL'),
    ];
    const groups = groupPlayerEditorFields('Attributes', fields);
    assert.deepEqual(groups.map(({ fields: items }) => items), [[fields[0], fields[2]], [fields[1]]]);
    assert.ok(groups.every(({ color }) => color === 'var(--primary)'));
    assert.deepEqual(groupPlayerEditorFields('Attributes', []), []);
});

test('other sections preserve raw groups even when their translated labels are identical', () => {
    const fields = [
        field('Signature/Unknown One/FIRST'),
        field('Signature/Unknown Two/SECOND'),
        field('Signature/Unknown One/THIRD'),
    ];
    const groups = groupPlayerEditorFields('Signature', fields);
    assert.equal(groups.length, 2);
    assert.equal(groups[0].label, groups[1].label);
    assert.notEqual(groups[0].key, groups[1].key);
    assert.deepEqual(groups.map(({ fields: items }) => items), [[fields[0], fields[2]], [fields[1]]]);
    const personality = groupPlayerEditorFields('Badges', [field('Badges/Personality/TEST')]);
    assert.equal(personality[0].label, '性格徽章');
});

test('search finds whole builder categories and keeps a specific field search narrow', () => {
    const fields = [
        field('Attributes/Offense/CLOSESHOT'),
        field('Attributes/Offense/DRIVINGLAYUP'),
        field('Attributes/Offense/DRIVINGDUNK'),
        field('Attributes/Offense/STANDINGDUNK'),
        field('Attributes/Offense/POSTCONTROL'),
        field('Attributes/Offense/MIDRANGE'),
        { ...field('Attributes/Offense/3POINT'), label: '3pt Shot' },
        field('Attributes/Offense/FREETHROW'),
        field('Attributes/Athleticism/SPEED'),
    ];
    const shooting = groupPlayerEditorFields('Attributes', fields, '投射');
    assert.deepEqual(shooting.map(({ label }) => label), ['投射']);
    assert.deepEqual(shooting[0].fields, [fields[5], fields[6], fields[7]]);
    const finishing = groupPlayerEditorFields('Attributes', fields, '终结');
    assert.deepEqual(finishing.map(({ label }) => label), ['终结']);
    assert.deepEqual(finishing[0].fields, fields.slice(0, 5));
    const threePoint = groupPlayerEditorFields('Attributes', fields, '三分');
    assert.equal(threePoint.length, 1);
    assert.equal(threePoint[0].fields.length, 1);
    assert.equal(threePoint[0].fields[0], fields[6]);
    assert.deepEqual(groupPlayerEditorFields('Attributes', fields, '不存在的搜索词'), []);
});

test('search preserves legacy labels, normalized English keys, references and all metadata', () => {
    const fields = [
        field('Attributes/Offense/3POINT', 81),
        field('Attributes/Offense/CLOSESHOT', 72),
        field('Attributes/Unknown/UNVERIFIED', 43),
    ];
    fields[2].label = 'OriginalFixture';
    fields[2].kind = 'choice';
    fields[2].options = ['UNKNOWN_OPTION'];
    fields[2].writable = false;
    fields[2].error = 'fixture metadata';
    const before = structuredClone(fields);
    for (const cell of fields) {
        Object.freeze(cell.options);
        Object.freeze(cell);
    }
    Object.freeze(fields);
    const byKey = groupPlayerEditorFields('Attributes', fields, '  aTTributes/Offense/3pOint  ');
    assert.equal(byKey.length, 1);
    assert.equal(byKey[0].fields.length, 1);
    assert.equal(byKey[0].fields[0], fields[0]);
    const offense = groupPlayerEditorFields('Attributes', fields, '进攻');
    assert.deepEqual(offense.flatMap(({ fields: items }) => items), [fields[1], fields[0]]);
    const original = groupPlayerEditorFields('Attributes', fields, 'originalfixture');
    assert.equal(original.length, 1);
    assert.equal(original[0].fields[0], fields[2]);
    assert.deepEqual(
        groupPlayerEditorFields('Attributes', fields, '   '),
        groupPlayerEditorFields('Attributes', fields),
    );
    assert.deepEqual(fields, before);
});
