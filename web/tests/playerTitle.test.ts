import assert from 'node:assert/strict';
import test from 'node:test';
import { playerTitle } from '../src/archetype.ts';

test('title comes from the game type, estimate only as fallback', () => {
    const lebron = playerTitle({ playerType: 'INSIDE', offensiveType: 'ATHLETIC' }, '冲击型锁防者');
    assert.equal(lebron?.name, '内线型');
    assert.equal(lebron?.fromGame, true);
    assert.match(lebron!.detail, /进攻风格：运动型/);
    assert.equal(playerTitle({ playerType: 'OUTSIDE', offensiveType: 'OUTSIDE' }, null)?.name, '外线型');
    const fallback = playerTitle({ playerType: null }, '组织核心');
    assert.equal(fallback?.fromGame, false);
    assert.equal(playerTitle(null, null), null);
    assert.equal(playerTitle({ playerType: 'BOGUS' }, 'x')?.name, 'x', 'unknown enum text falls back');
});
