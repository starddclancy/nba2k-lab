import assert from 'node:assert/strict';
import test from 'node:test';
import { categoryAverages } from '../src/scouting.ts';

const s = (v: number) => ({ finishing: v, shooting: v + 1, playmaking: v + 2, defense: v + 3, rebounding: v + 4, physical: v + 5 });

test('league and per-position six-category averages', () => {
    const averages = categoryAverages([
        { scores: s(60), position: 'PG' }, { scores: s(70), position: 'PG' }, { scores: s(80), position: 'C' },
        { scores: null, position: 'SF' }, { scores: s(50), position: null },
    ]);
    assert.equal(averages.byPosition.PG!.finishing, 65);
    assert.equal(averages.byPosition.C!.physical, 85);
    assert.equal(averages.byPosition.SF, null, 'unrated players do not count');
    assert.equal(averages.counts.PG, 2);
    assert.equal(averages.counts.league, 4);
    assert.equal(averages.league!.finishing, 65, '(60+70+80+50)/4');
    assert.equal(categoryAverages([]).league, null);
});
