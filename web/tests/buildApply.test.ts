import assert from 'node:assert/strict';
import test from 'node:test';
import { attributes, createBuild, extraAttributes, parseBuild } from '../src/builds.ts';
import { bodyFields, planChanges, planOverall, planValues, ratingFields } from '../src/buildApply.ts';
import { overallFields } from '../src/overall.ts';

test('every builder and extra attribute maps to a distinct overall-model field', () => {
    const keys = [...attributes, ...extraAttributes].map(({ id }) => ratingFields[id]);
    assert.equal(new Set(keys).size, 37);
    for (const key of keys) {
        assert.ok(overallFields.includes(key), key);
    }
    assert.ok(overallFields.includes(bodyFields.height));
});

test('old plans without extra ratings still load, and new plans carry them', () => {
    const plan = createBuild('p', '2026-11-08T00:00:00.000Z');
    assert.equal(plan.extraRatings?.hands, 50);
    const old = { ...plan };
    delete (old as { extraRatings?: unknown }).extraRatings;
    assert.equal(parseBuild(old).extraRatings, undefined);
    assert.equal(planValues(parseBuild(old))['Attributes/Offense/HANDS'], 50);
    assert.throws(() => parseBuild({ ...plan, extraRatings: { hands: 120 } }), /hands/);
    assert.throws(() => parseBuild({ ...plan, extraRatings: { nope: 50 } }), /unknown/);
});

test('plan values use editor units and overall rises with ratings', () => {
    const plan = { ...createBuild('p', '2026-11-08T00:00:00.000Z'), position: 'PG' as const, heightInches: 75, weightLbs: 190, wingspanInches: 80 };
    const values = planValues(plan);
    assert.equal(values[bodyFields.height], 190.5);
    assert.equal(values[bodyFields.wingspan], 203.2);
    assert.equal(values[bodyFields.weight], 86.183);
    const low = planOverall(plan)!;
    const high = planOverall({ ...plan, ratings: Object.fromEntries(attributes.map(({ id }) => [id, 90])) as never })!;
    assert.ok(low >= 25 && high > low + 10, `${low} -> ${high}`);
});

test('changes list only differing fields, with body tolerance', () => {
    const plan = { ...createBuild('p', '2026-11-08T00:00:00.000Z'), position: 'PG' as const, heightInches: 74 };
    const current: Record<string, unknown> = { ...planValues(plan), [bodyFields.height]: 187.96, 'Attributes/Offense/3POINT': 97 };
    const changes = planChanges(plan, current);
    assert.deepEqual(changes.map((item) => item.key), ['Attributes/Offense/3POINT']);
    assert.equal(changes[0].after, 25);
    assert.deepEqual(planChanges(plan, {}), [], 'fields the player does not expose are skipped');
});
