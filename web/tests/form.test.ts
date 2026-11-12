import assert from 'node:assert/strict';
import test from 'node:test';
import { chronological, heat, mean, streakText, teamRecentForm } from '../src/form.ts';
import type { TeamGame } from '../src/gameLog.ts';

test('heat compares recent and season averages', () => {
    // Live Chris Mihm last five Game Scores (2026-11-08 read): 5.4 9.4 10.3 12.7 7.6.
    const recent = mean([5.4, 9.4, 10.3, 12.7, 7.6]);
    assert.equal(recent, 9.1);
    assert.equal(heat(recent, 6).label, '火热');
    assert.equal(heat(recent, 8).label, '上升');
    assert.equal(heat(recent, 9).label, '稳定');
    assert.equal(heat(recent, 11).label, '下滑');
    assert.equal(heat(recent, 14).label, '低迷');
    assert.equal(heat(null, 5).label, '数据不足');
    assert.equal(heat(9.1, 6).delta, 3.1);
});

test('streaks and results read naturally', () => {
    assert.equal(streakText(3), '3 连胜');
    assert.equal(streakText(-1), '1 连败');
    // Live Cleveland LAST10 = 647: newest first WWWLLLLWLW.
    assert.deepEqual(chronological('WWWLLLLWLW').join(''), 'WLWLLLLWWW');
    assert.equal(mean([]), null);
});

test('team form uses the native games for recent results, home splits and streaks', () => {
    const games = [true, true, false, true, false, false, true, true, true, false, true, true]
        .map((won, index) => ({ won, home: index % 2 === 0 }) as TeamGame);
    const form = teamRecentForm(games, 7, 'Houston Rockets');
    assert.equal(form.last10, 'WWLWWWLLWL');
    assert.equal(form.wins, 6);
    assert.equal(form.losses, 4);
    assert.equal(form.streak, 2);
    assert.equal(form.longestWin, 3);
    assert.equal(form.longestLoss, 2);
    assert.equal(form.homeWins, 4);
    assert.equal(form.homeLosses, 2);
    assert.equal(form.last10Home, 'WWWLLW');
    assert.equal(teamRecentForm([], 7, 'Houston Rockets').last10, '');
});
