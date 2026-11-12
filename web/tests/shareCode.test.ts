import assert from 'node:assert/strict';
import test from 'node:test';
import { attributes, createBuild } from '../src/builds.ts';
import { decodeShareCode, encodeShareCode } from '../src/shareCode.ts';

const sample = () => ({
    ...createBuild('a', '2026-11-08T00:00:00.000Z'),
    name: '控卫 · 三分 99', notes: '备注 | 含分隔符 & 符号', position: 'PG' as const,
    heightInches: 75, weightLbs: 190, wingspanInches: 80,
    ratings: Object.fromEntries(attributes.map(({ id }, index) => [id, 25 + index * 3])) as never,
    badgeGoals: [{ id: 'Deadeye', tier: 'gold' as const }, { id: 'Dimer', tier: 'hof' as const }],
    tags: ['私人'], favorite: true, animationGoals: ['x'],
});

test('share code round-trips the plan content into a new plan', () => {
    const plan = sample();
    const code = encodeShareCode(plan);
    assert.match(code, /^2K27B1-[A-Za-z0-9_-]+-[0-9a-z]{3}$/);
    const copy = decodeShareCode(code, 'new-id', '2026-11-09T00:00:00.000Z');
    assert.equal(copy.id, 'new-id');
    for (const key of ['name', 'notes', 'position', 'heightInches', 'weightLbs', 'wingspanInches', 'ratings', 'badgeGoals'] as const) {
        assert.deepEqual(copy[key], plan[key], key);
    }
    assert.deepEqual([copy.tags, copy.favorite, copy.animationGoals], [[], false, []], 'private fields are not shared');
    assert.equal(copy.createdAt, '2026-11-09T00:00:00.000Z');
    assert.equal(decodeShareCode(`  ${code}\n`, 'x').name, plan.name, 'surrounding whitespace is ignored');
});

test('damaged, foreign or out-of-range codes are rejected', () => {
    const code = encodeShareCode(sample());
    const [prefix, payload, sum] = code.split('-');
    const flipped = payload.slice(0, 10) + (payload[10] === 'A' ? 'B' : 'A') + payload.slice(11);
    assert.throws(() => decodeShareCode(`${prefix}-${flipped}-${sum}`), /校验失败/);
    assert.throws(() => decodeShareCode('hello'), /不是有效/);
    assert.throws(() => decodeShareCode(code.replace('2K27B1', '2K27B9')), /版本 9/);
    const bad = { ...sample(), ratings: { ...sample().ratings, threePoint: 120 } };
    assert.throws(() => encodeShareCode(bad as never));
});
