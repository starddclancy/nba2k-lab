import assert from 'node:assert/strict';
import test from 'node:test';
import body from '../src/data/build-body.json' with { type: 'json' };
import { badgeTierGroup, bodyIssues, heightsFor, legalBody, templateFor } from '../src/buildBody.ts';

test('body data keeps its public source and covers every position', () => {
    assert.match(body.sources[0].url, /^https:\/\/www\.nba2klab\.com\/myplayer-builder$/);
    assert.match(body.sources[0].sha256, /^[0-9a-f]{64}$/);
    for (const position of ['PG', 'SG', 'SF', 'PF', 'C']) {
        const heights = heightsFor(position);
        assert.ok(heights.length >= 7, position);
        assert.deepEqual(heights, heights.map((_, index) => heights[0] + index), `${position} heights are contiguous`);
    }
    assert.deepEqual(templateFor('C', 79), { position: 'C', height: 79, weightMin: 215, weightMax: 270, wingspanMin: 79, wingspanMax: 85 });
});

test('legalBody clamps height, then weight and wingspan for that height', () => {
    const plan = { id: 'x', position: 'PG', heightInches: 90, weightLbs: 400, wingspanInches: 60 };
    const fixed = legalBody(plan);
    assert.equal(fixed.heightInches, 79);
    assert.equal(fixed.weightLbs, templateFor('PG', 79)!.weightMax);
    assert.equal(fixed.wingspanInches, templateFor('PG', 79)!.wingspanMin);
    assert.equal(fixed.id, 'x', 'other plan fields survive');
    const legal = { position: 'C', heightInches: 84, weightLbs: 250, wingspanInches: 88 };
    assert.equal(legalBody(legal), legal, 'legal body is returned unchanged');
    assert.deepEqual(bodyIssues(legal), []);
    assert.deepEqual(bodyIssues({ position: 'C', heightInches: 84, weightLbs: 200, wingspanInches: 95 }),
        ['体重应在 215–279 磅之间', '臂展应在 84–90 英寸之间']);
    assert.match(bodyIssues({ position: 'PG', heightInches: 85, weightLbs: 200, wingspanInches: 85 })[0], /69–79/);
});

test('badge tier groups follow height, blank cells mean unavailable', () => {
    assert.equal(badgeTierGroup('Aerial Wizard', 69), 2);
    assert.equal(badgeTierGroup('Ankle Assassin', 69), 1);
    assert.equal(badgeTierGroup('Ankle Assassin', 82), 2);
    assert.equal(badgeTierGroup('Ankle Assassin', 83), null);
    assert.equal(badgeTierGroup('Not A Badge', 75), undefined);
});
