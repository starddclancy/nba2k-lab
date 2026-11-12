import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import {
    attributes, compareBuilds, createBuild, deleteBuild, duplicateBuild, evaluateAnimation,
    evaluateBadge, evaluateGoals, evaluateRequirement, exportLibrary, importLibrary,
    libraryStorageKey, loadLibrary, mergeLibraries, parseBuild, parseLibrary,
    parseRequirementCatalog, persistLibrary, saveBuild, tiers,
} from '../src/builds.ts';
import type { AnimationRequirement, PlanLibrary, Requirement } from '../src/builds.ts';

const now = '2026-11-07T10:00:00.000Z';
const later = '2026-11-07T10:01:00.000Z';
const plan = () => createBuild('one', now);
const empty = (): PlanLibrary => ({ schemaVersion: 1, game: 'nba2k27', plans: [] });
const catalog = parseRequirementCatalog(JSON.parse(
    readFileSync(new URL('../src/data/build-requirements.json', import.meta.url), 'utf8'),
));
const rule: Requirement = {
    logic: 'all',
    terms: [{ attribute: 'drivingDunk', minimum: 87 }, { attribute: 'vertical', minimum: 75 }],
};
const animation: AnimationRequirement = {
    id: 'test', name: 'test', group: 'finishing', category: 'test',
    minHeight: 77, maxHeight: 81, rule, scope: 'package',
};

test('AND rules require every attribute and report each missing amount', () => {
    const result = evaluateRequirement(rule, { drivingDunk: 90, vertical: 74 }, 78);
    assert.equal(result.status, 'blocked');
    assert.deepEqual(result.terms.map((item) => item.missing), [0, 1]);
    assert.equal(evaluateRequirement(rule, { drivingDunk: 87, vertical: 75 }, 78).status, 'eligible');
});

test('OR rules accept either attribute without demanding both', () => {
    const any: Requirement = { ...rule, logic: 'any' };
    assert.equal(evaluateRequirement(any, { drivingDunk: 87, vertical: 25 }, 78).status, 'eligible');
    assert.equal(evaluateRequirement(any, { drivingDunk: 86, vertical: 74 }, 78).status, 'blocked');
});

test('height constraints belong to each OR branch and have inclusive boundaries', () => {
    const any: Requirement = {
        logic: 'any',
        terms: [
            { attribute: 'drivingDunk', minimum: 87, minHeight: 69, maxHeight: 76 },
            { attribute: 'standingDunk', minimum: 90, minHeight: 77, maxHeight: 88 },
        ],
    };
    assert.equal(evaluateRequirement(any, { drivingDunk: 87, standingDunk: 89 }, 76).status, 'eligible');
    assert.equal(evaluateRequirement(any, { drivingDunk: 99, standingDunk: 89 }, 77).status, 'blocked');
    assert.equal(evaluateRequirement(any, { standingDunk: 90 }, 88).status, 'eligible');
});

test('unknown/malformed attributes and heights never falsely unlock a requirement', () => {
    for (const invalid of [undefined, Number.NaN, 100, 87.5]) {
        assert.equal(evaluateRequirement(rule, { drivingDunk: invalid, vertical: 75 }, 78).status, 'unknown');
    }
    for (const height of [Number.NaN, 68, 89, 78.5]) {
        assert.equal(evaluateRequirement(rule, { drivingDunk: 99, vertical: 99 }, height).status, 'unknown');
    }
    assert.equal(evaluateRequirement(rule, { drivingDunk: 86 }, 78).status, 'blocked');
});

test('unpublished connector gives unknown only when AND and OR would disagree', () => {
    const unknown: Requirement = { ...rule, logic: 'unverified' };
    assert.equal(evaluateRequirement(unknown, { drivingDunk: 90, vertical: 74 }, 78).status, 'unknown');
    assert.equal(evaluateRequirement(unknown, { drivingDunk: 90, vertical: 75 }, 78).status, 'eligible');
    assert.equal(evaluateRequirement(unknown, { drivingDunk: 25, vertical: 25 }, 78).status, 'blocked');
});

test('empty badge tiers are unavailable, never treated as free unlocks', () => {
    for (const logic of ['all', 'any', 'unverified'] as const) {
        assert.equal(evaluateRequirement({ logic, terms: [] }, plan().ratings, 78).status, 'unavailable');
    }
});

test('animation checks whole-package height even when all attributes pass', () => {
    assert.equal(evaluateAnimation(animation, { drivingDunk: 99, vertical: 99 }, 76).status, 'blocked');
    assert.equal(evaluateAnimation(animation, { drivingDunk: 99, vertical: 99 }, 77).status, 'eligible');
    assert.equal(evaluateAnimation(animation, { drivingDunk: 99, vertical: 99 }, 81).status, 'eligible');
    assert.equal(evaluateAnimation(animation, { drivingDunk: 99, vertical: 99 }, 82).status, 'blocked');
});

test('animation with no attribute requirements still checks height', () => {
    const item = { ...animation, rule: { logic: 'all' as const, terms: [] } };
    assert.equal(evaluateAnimation(item, {}, 78).status, 'eligible');
    assert.equal(evaluateAnimation(item, {}, 76).status, 'blocked');
    assert.equal(evaluateAnimation(item, {}, Number.NaN).status, 'unknown');
});

test('published Posterizer AND rule and Deadeye OR rule work at exact breakpoints', () => {
    const posterizer = catalog.badges.find((item) => item.name === 'Posterizer')!;
    const deadeye = catalog.badges.find((item) => item.name === 'Deadeye')!;
    assert.equal(posterizer.tiers.silver.logic, 'all');
    assert.equal(deadeye.tiers.gold.logic, 'any');
    assert.equal(evaluateBadge(posterizer, { drivingDunk: 87, vertical: 74 }, 78).current, 'bronze');
    assert.equal(evaluateBadge(posterizer, { drivingDunk: 87, vertical: 75 }, 78).current, 'silver');
    assert.equal(evaluateBadge(deadeye, { midRange: 25, threePoint: 92 }, 78).current, 'gold');
});

test('Unpluckable null HoF branch is absent while the Ball Handle branch survives', () => {
    const badge = catalog.badges.find((item) => item.name === 'Unpluckable')!;
    assert.equal(badge.tiers.hof.logic, 'any');
    assert.deepEqual(badge.tiers.hof.terms.map((item) => item.attribute), ['ballHandle']);
    assert.equal(evaluateBadge(badge, { postControl: 99, ballHandle: 96 }, 78).current, 'gold');
    assert.equal(evaluateBadge(badge, { postControl: 25, ballHandle: 97 }, 78).current, 'hof');
    assert.equal(evaluateBadge(badge, { postControl: 99, ballHandle: 97 }, 78).next, null);
});

test('catalog contains public requirements and no invented fifth badge tier', () => {
    assert.ok(catalog.badges.length >= 50);
    assert.ok(catalog.animations.length >= 2500);
    for (const badge of catalog.badges) {
        assert.deepEqual(Object.keys(badge.tiers), [...tiers]);
    }
    assert.ok(catalog.animations.filter((item) => item.group === 'shooting')
        .every((item) => item.rule.logic === 'unverified'));
    assert.ok(catalog.animations.filter((item) => item.group === 'jumpers')
        .every((item) => item.scope === 'baseOnly'));
});

test('goals preserve unknown IDs as missing instead of substituting another requirement', () => {
    const item = plan();
    item.badgeGoals = [{ id: 'missing-badge', tier: 'gold' }];
    item.animationGoals = ['missing-animation'];
    const result = evaluateGoals(item, catalog);
    assert.equal(result.badges[0].result, null);
    assert.equal(result.animations[0].result, null);
});

test('comparison uses right minus left and shows changed unlocks without mutation', () => {
    const left = plan();
    const right = duplicateBuild(left, 'two', later);
    right.ratings.drivingDunk = 87;
    right.ratings.vertical = 75;
    const result = compareBuilds(left, right, catalog);
    assert.equal(result.attributes.find((item) => item.id === 'drivingDunk')!.delta, 62);
    assert.equal(result.badges.find((item) => item.name === 'Posterizer')!.after, 'silver');
    assert.equal(left.ratings.drivingDunk, 25);
    assert.deepEqual(compareBuilds(left, left, catalog).badges, []);
    assert.deepEqual(compareBuilds(left, left, catalog).animations, []);
});

test('default plan has all attributes and survives strict serialization', () => {
    assert.equal(attributes.length, 21);
    assert.deepEqual(parseBuild(plan()), plan());
    const library = saveBuild(empty(), plan());
    assert.deepEqual(importLibrary(exportLibrary(library)), library);
});

test('import rejects wrong schema, game and ambiguous primitive coercion', () => {
    const library = saveBuild(empty(), plan());
    assert.throws(() => parseLibrary({ ...library, game: 'nba2k26' }));
    assert.throws(() => parseLibrary({ ...library, schemaVersion: 2 }));
    assert.throws(() => parseBuild({ ...plan(), heightInches: '78' }));
    assert.throws(() => parseBuild({ ...plan(), favorite: 'false' }));
    assert.throws(() => parseBuild({ ...plan(), position: 'G' }));
    assert.throws(() => importLibrary('{broken'));
    assert.throws(() => importLibrary(' '.repeat(8_000_001)));
});

test('import requires all known attributes, whole values and valid bounds', () => {
    const item = plan();
    for (const invalid of [24, 100, 70.1, Number.NaN, null, '70']) {
        assert.throws(() => parseBuild({ ...item, ratings: { ...item.ratings, threePoint: invalid } }));
    }
    const { closeShot: _, ...missing } = item.ratings;
    assert.throws(() => parseBuild({ ...item, ratings: missing }));
    assert.throws(() => parseBuild({ ...item, ratings: { ...item.ratings, inventedRating: 90 } }));
    assert.throws(() => parseBuild({ ...item, heightInches: 68 }));
    assert.throws(() => parseBuild({ ...item, heightInches: 89 }));
    assert.throws(() => parseBuild({ ...item, weightLbs: 401 }));
});

test('import rejects duplicate identities/goals, invalid tiers and reversed timestamps', () => {
    const item = plan();
    assert.throws(() => parseLibrary({ ...empty(), plans: [item, item] }));
    assert.throws(() => parseBuild({ ...item, animationGoals: ['same', 'same'] }));
    assert.throws(() => parseBuild({ ...item, badgeGoals: [{ id: 'x', tier: 'legend' }] }));
    assert.throws(() => parseBuild({ ...item, tags: ['same', 'same'] }));
    assert.throws(() => parseBuild({ ...item, updatedAt: 'yesterday' }));
    assert.throws(() => parseBuild({ ...item, updatedAt: '2026-02-30T10:00:00.000Z' }));
    assert.throws(() => parseBuild({ ...item, createdAt: later, updatedAt: now }));
});

test('copies deeply isolate ratings, goals and tags', () => {
    const original = plan();
    original.badgeGoals = [{ id: 'Deadeye', tier: 'gold' }];
    original.animationGoals = ['one'];
    original.tags = ['射手'];
    const copy = duplicateBuild(original, 'two', later);
    copy.ratings.threePoint = 99;
    copy.badgeGoals[0].tier = 'hof';
    copy.tags[0] = 'new';
    copy.animationGoals.push('two');
    assert.equal(original.ratings.threePoint, 25);
    assert.equal(original.badgeGoals[0].tier, 'gold');
    assert.deepEqual(original.tags, ['射手']);
    assert.deepEqual(original.animationGoals, ['one']);
    assert.equal(copy.createdAt, later);
    assert.throws(() => duplicateBuild(original, original.id, later));
});

test('save updates only the same identity and delete preserves the others', () => {
    const first = plan();
    const second = duplicateBuild(first, 'two', later);
    const library = saveBuild(saveBuild(empty(), first), second);
    const updated = saveBuild(library, { ...first, name: '改名' });
    assert.equal(updated.plans.length, 2);
    assert.equal(updated.plans.find((item) => item.id === 'one')!.name, '改名');
    assert.equal(library.plans.find((item) => item.id === 'one')!.name, '新建方案');
    assert.deepEqual(deleteBuild(updated, 'one').plans, [second]);
});

test('merge rejects collisions by default and replaces only under an explicit policy', () => {
    const current = saveBuild(empty(), plan());
    const incoming = saveBuild(empty(), { ...plan(), name: '传入方案' });
    assert.throws(() => mergeLibraries(current, incoming));
    assert.throws(() => mergeLibraries(current, incoming, 'overwrite' as 'replace'));
    assert.equal(current.plans[0].name, '新建方案');
    assert.equal(mergeLibraries(current, incoming, 'replace').plans[0].name, '传入方案');
    assert.equal(mergeLibraries(current, saveBuild(empty(), duplicateBuild(plan(), 'two', later))).plans.length, 2);
});

function memoryStorage() {
    const values = new Map<string, string>();
    return {
        values,
        getItem: (key: string) => values.get(key) ?? null,
        setItem: (key: string, value: string) => { values.set(key, value); },
    };
}

test('storage round trip succeeds and a corrupt library is never overwritten during load', () => {
    const storage = memoryStorage();
    assert.deepEqual(loadLibrary(storage), empty());
    persistLibrary(storage, saveBuild(empty(), plan()));
    assert.equal(loadLibrary(storage).plans[0].id, 'one');
    storage.setItem(libraryStorageKey, 'broken');
    assert.throws(() => loadLibrary(storage));
    assert.equal(storage.getItem(libraryStorageKey), 'broken');
});

test('storage quota/access failures propagate without reporting success', () => {
    const storage = memoryStorage();
    assert.throws(() => loadLibrary({ ...storage, getItem: () => { throw new Error('denied'); } }), /denied/);
    assert.throws(() => persistLibrary({
        ...storage, setItem: () => { throw new Error('quota'); },
    }, saveBuild(empty(), plan())), /quota/);
    assert.equal(storage.getItem(libraryStorageKey), null);
});

test('stale view save detects another view update before replacing its plans', () => {
    const storage = memoryStorage();
    const initial = empty();
    const other = saveBuild(initial, plan());
    persistLibrary(storage, other);
    assert.throws(() => persistLibrary(storage, initial, initial), /another view/);
    assert.deepEqual(loadLibrary(storage), other);
    persistLibrary(storage, saveBuild(other, duplicateBuild(plan(), 'two', later)), other);
    assert.equal(loadLibrary(storage).plans.length, 2);
});

test('catalog validation rejects bad attributes, duplicate identities and inverted bounds', () => {
    assert.throws(() => parseRequirementCatalog({
        ...catalog, animations: [catalog.animations[0], catalog.animations[0]],
    }));
    assert.throws(() => parseRequirementCatalog({
        ...catalog, animations: [{ ...catalog.animations[0], minHeight: 88, maxHeight: 69 }],
    }));
    assert.throws(() => parseRequirementCatalog({
        ...catalog, animations: [{
            ...catalog.animations[0], rule: { logic: 'all', terms: [{ attribute: 'overall', minimum: 90 }] },
        }],
    }));
    assert.throws(() => parseRequirementCatalog({
        ...catalog, sources: [{ ...catalog.sources[0], sha256: 'not-a-hash' }],
    }));
});
