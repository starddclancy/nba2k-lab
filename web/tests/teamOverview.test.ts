import assert from 'node:assert/strict';
import test from 'node:test';
import { teamOverview } from '../src/teamOverview.ts';
import type { DraftPickRow } from '../src/types.ts';

const pick = (patch: Partial<DraftPickRow>): DraftPickRow => ({
    index: 0, ownerIndex: 1, owner: 'A', originalIndex: 1, original: 'A', year: 2009, round: 1, order: 5,
    used: false, player: null, hasTerms: false, ...patch,
});

test('team overview counts owned, acquired and traded-away picks', () => {
    const picks = [
        pick({ index: 1, year: 2010 }),
        pick({ index: 2, round: 2 }),
        pick({ index: 3, originalIndex: 7, original: 'B' }),
        pick({ index: 4, ownerIndex: 7, owner: 'B' }),
        pick({ index: 5, used: true, player: 'X' }),
    ];
    const view = teamOverview(1, { index: 1, label: 'A', used: 13, capacity: 15, open: [14, 15] },
        { 'Business/Budget/SALARY': '49138250', 'Business/Budget/PLAYERPAYROLL': 70283000 }, picks);
    assert.equal(view.picksOwned, 3);
    assert.equal(view.firstRounders, 2);
    assert.equal(view.acquired, 1);
    assert.equal(view.traded, 1);
    assert.equal(view.payroll, 49_138_250);
    assert.equal(view.budget, 70_283_000);
    assert.equal(view.rosterUsed, 13);
    assert.deepEqual(view.nextPicks.map((item) => item.index), [3, 2, 1]);
    assert.equal(teamOverview(1, undefined, {}, []).payroll, null);
});

test('team colors decode from game RGBA bytes', async () => {
    const { teamColor, accentOf } = await import('../src/teamOverview.ts');
    // Values read from the live roster (Philadelphia, Chicago) on 2026-11-08.
    assert.equal(teamColor('00 3d a6 ff'), '#003da6');
    assert.equal(teamColor('CE 11 41 FF'), '#ce1141');
    assert.equal(teamColor(''), null);
    assert.equal(teamColor('zz'), null);
    assert.equal(accentOf('#003da6', '#ed174c'), '#003da6');
    assert.equal(accentOf('#1b2023', '#ce1141'), '#ce1141', 'Bulls black falls back to red');
});
