import assert from 'node:assert/strict';
import test from 'node:test';
import { badgePresentation } from '../src/badgePresentation.ts';
import type { FieldCell } from '../src/types.ts';

const level: FieldCell = {
    key: 'Badges/Inside Scoring/AERIALWIZARD', label: 'Aerial Wizard', section: 'Badges',
    group: 'Inside Scoring', kind: 'choice', value: 'UNEQUIPPED', display: 'UNEQUIPPED',
    writable: true, options: ['UNEQUIPPED', 'BRONZE', 'SILVER', 'GOLD', 'HALL_OF_FAME', 'LEGEND'],
};

test('level badge presentation follows every authored grade including legend', () => {
    const expected = ['unequipped', 'bronze', 'silver', 'gold', 'hof', 'legend'];
    for (const [index, value] of level.options.entries()) {
        assert.deepEqual(badgePresentation(level, value), { mode: 'level', state: expected[index] });
        assert.deepEqual(badgePresentation(level, index), { mode: 'level', state: expected[index] });
    }
    assert.equal(badgePresentation(level, 'FUTURE_LEVEL')?.state, 'unknown');
    assert.equal(badgePresentation(level, null)?.state, 'unknown');
});

test('binary badge equipment follows No and Yes without changing authored values', () => {
    const binary = { ...level, label: 'Alpha Dog', group: 'Personality', options: ['No', 'Yes'] };
    assert.deepEqual(badgePresentation(binary, 'No'), { mode: 'equipment', state: 'unequipped' });
    assert.deepEqual(badgePresentation(binary, 'Yes'), { mode: 'equipment', state: 'equipped' });
    assert.equal(badgePresentation(binary, 'BRONZE')?.state, 'unknown');
    assert.deepEqual(binary.options, ['No', 'Yes']);
    const boolean = { ...binary, kind: 'bool' as const, options: [] };
    assert.equal(badgePresentation(boolean, true)?.state, 'equipped');
    assert.equal(badgePresentation(boolean, false)?.state, 'unequipped');
});

test('boost flags, work ethic and unrelated fields retain their own semantics', () => {
    assert.equal(badgePresentation({ ...level, label: 'IsAerialWizardBadgeBoostable', options: ['No', 'Yes'] }), null);
    assert.equal(badgePresentation({ ...level, label: 'Work Ethic', options: ['LOW', 'MEDIUM', 'HIGH'] }), null);
    assert.equal(badgePresentation({ ...level, section: 'Vitals' }), null);
});
