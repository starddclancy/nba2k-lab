import assert from 'node:assert/strict';
import test from 'node:test';
import { entityHref, parseRoute } from '../src/navigation.ts';

test('links preserve real record ids, source identity and team index zero', () => {
    assert.deepEqual(parseRoute(entityHref('players', 7, 'draft')), { page: 'players', index: 7, source: 'draft' });
    assert.deepEqual(parseRoute('#/players/roster/7'), { page: 'players', index: 7, source: 'roster' });
    assert.deepEqual(parseRoute('#/teams/0'), { page: 'teams', index: 0 });
    assert.deepEqual(parseRoute('#/teams/92'), { page: 'teams', index: 92 });
});

test('invalid routes cannot silently choose a different record', () => {
    for (const hash of ['#/teams/-1', '#/teams/1.5', '#/players/other/2', '#/teams/1junk', '#/missing'])
        assert.equal(parseRoute(hash), null);
});
