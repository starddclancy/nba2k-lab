import assert from 'node:assert/strict';
import test from 'node:test';
import { preparePreviewMatch, readPreviewMatch } from '../src/matchPreview.ts';

test('sample match has consistent team/player/period totals and returns isolated data', () => {
    const snapshot = readPreviewMatch();
    for (const team of snapshot.teams) {
        for (const field of Object.keys(team.stats.total)) {
            assert.equal(team.stats.total[field], team.players.reduce((sum, player) => sum + player.stats.total[field], 0));
            assert.equal(team.stats.total[field], team.stats.periods.reduce((sum, row) => sum + row[field], 0));
        }
    }
    snapshot.teams[0].players[0].stats.periods[1].assists = 999;
    const plan = preparePreviewMatch(snapshot.readToken, [{
        side: 0, playerId: 1, slot: 2, changes: { assists: 3 },
    }]);
    assert.equal(plan.changes.find((change) => change.playerId === 1 && change.slot === 2)?.before, 2);
    assert.equal(snapshot.pid, 0);
    assert.equal(snapshot.editable, false);
    assert.equal(plan.writes, 0);
});

test('sample preview propagates scoring without mutating the captured match', () => {
    const snapshot = readPreviewMatch();
    const edits = [{
        side: 0, playerId: 1, slot: 2, changes: { twoPointMade: 3, twoPointAttempted: 5 },
    }];
    const plan = preparePreviewMatch(snapshot.readToken, edits);
    const score = plan.changes.find((change) => change.playerId === null && change.slot === 0 && change.field === 'points')!;
    assert.equal(score.after - score.before, 2);
    assert.deepEqual(preparePreviewMatch(snapshot.readToken, edits).changes, plan.changes);
    assert.throws(() => preparePreviewMatch(snapshot.readToken, [{
        side: 0, playerId: 1, slot: 2, changes: { twoPointMade: 50 },
    }]));
    readPreviewMatch();
    assert.throws(() => preparePreviewMatch(snapshot.readToken, edits));
});
