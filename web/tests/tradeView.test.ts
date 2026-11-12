import assert from 'node:assert/strict';
import test from 'node:test';
import { money, moneyShort, payrollAfter, salaryMatch } from '../src/tradeView.ts';

test('payroll after a swap uses the same arithmetic as the bridge', () => {
    // Values from the 2026-11-08 dry run: Atlanta sends Rasual Butler, receives Baron Davis.
    assert.equal(payrollAfter({ before: 49_138_250, out: 2_600_850, in: 13_155_600 }), 59_693_000);
    assert.equal(payrollAfter({ before: 1, out: undefined, in: 2 }), null);
});

test('money formats', () => {
    assert.equal(money(59_693_000), '$59,693,000');
    assert.equal(money(null), '—');
    assert.equal(moneyShort(19_014_187), '$19.0M');
    assert.equal(moneyShort(830_000), '$830K');
    assert.equal(salaryMatch(2_600_850, 13_155_600), 506);
    assert.equal(salaryMatch(0, 5), null);
});
