import assert from 'node:assert/strict';
import test from 'node:test';
import { salaryRatio, sideSummary, toggle } from '../src/tradeMachine.ts';

const player = (index: number, salary: number, overall: number) => ({ index, name: `P${index}`, overall, salary, teamIndex: 0 });

test('side summary matches the live 2-for-1 dry run payroll', () => {
    // 2026-11-08 package plan: Atlanta sends Butler + House, receives Baron Davis.
    const atlanta = sideSummary([player(1, 2_600_850, 72), player(2, 1_800_000, 70)], [player(3, 15_379_900, 86)],
        { payroll: 39_921_882, used: 14, capacity: 15 });
    assert.equal(atlanta.payrollAfter, 50_900_932);
    assert.equal(atlanta.rosterAfter, 13);
    assert.equal(atlanta.bestIncoming, 86);
    assert.equal(atlanta.overRoster, false);
    const full = sideSummary([], [player(4, 1, 60), player(5, 1, 60)], { payroll: null, used: 14, capacity: 15 });
    assert.equal(full.overRoster, true);
    assert.equal(full.payrollAfter, null);
});

test('salary ratio and toggle helpers', () => {
    const side = sideSummary([player(1, 10, 70)], [player(2, 12, 70)], {});
    assert.equal(salaryRatio(side), 120);
    assert.equal(salaryRatio(sideSummary([], [], {})), null);
    assert.equal(salaryRatio(sideSummary([], [player(2, 5, 70)], {})), Infinity);
    assert.deepEqual(toggle([1, 2], 2), [1]);
    assert.deepEqual(toggle([1], 3), [1, 3]);
});
