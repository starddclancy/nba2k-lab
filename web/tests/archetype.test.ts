import assert from 'node:assert/strict';
import test from 'node:test';
import { archetypeOf, categoryScores } from '../src/archetype.ts';
import type { CategoryScores } from '../src/archetype.ts';

const base: CategoryScores = { finishing: 60, shooting: 60, playmaking: 60, defense: 60, rebounding: 60, physical: 60 };

test('the category that stands out most names the player and the next sets the prefix', () => {
    assert.equal(archetypeOf({ ...base, shooting: 95, defense: 70 }).name, '攻防俱佳空间威胁');
    assert.equal(archetypeOf({ ...base, defense: 88, shooting: 82 }).name, '3D 锁防者');
    assert.equal(archetypeOf({ ...base, finishing: 90, physical: 80 }).name, '运动型内线终结者');
    assert.equal(archetypeOf({ ...base, finishing: 90, rebounding: 85 }).name, '禁区野兽');
});

test('close high scores make an all-rounder; ranking is relative to the league', () => {
    assert.equal(archetypeOf({ finishing: 80, shooting: 82, playmaking: 79, defense: 78, rebounding: 77, physical: 80 }).name, '全能核心');
    assert.equal(archetypeOf({ ...base }).primary, 'defense', 'an even 60 stands out most where it is furthest above the league norm');
    assert.equal(archetypeOf({ ...base }).balanced, false, 'an even but low profile is not an all-rounder');
});

test('category scores are weighted averages of roster fields', () => {
    const values: Record<string, unknown> = {
        'Attributes/Offense/CLOSESHOT': 80, 'Attributes/Offense/DRIVINGLAYUP': 80, 'Attributes/Offense/DRIVINGDUNK': 80,
        'Attributes/Offense/STANDINGDUNK': 80, 'Attributes/Offense/POSTCONTROL': 80,
        'Attributes/Offense/MIDRANGE': 70, 'Attributes/Offense/3POINT': 90, 'Attributes/Offense/FREETHROW': 50,
        'Attributes/Offense/PASSACCURACY': 60, 'Attributes/Offense/BALLCONTROL': 60, 'Attributes/Athleticism/SPEEDWITHBALL': 60,
        'Attributes/Defense/INTERIORDEFENSE': 50, 'Attributes/Defense/PERIMETERDEFENSE': 50, 'Attributes/Defense/STEAL': 50,
        'Attributes/Defense/BLOCK': 50, 'Attributes/Offense/OFFENSIVEREBOUND': 40, 'Attributes/Defense/DEFENSEREBOUND': 40,
        'Attributes/Athleticism/SPEED': 70, 'Attributes/Athleticism/AGILITY': 70, 'Attributes/Athleticism/STRENGTH': 70,
        'Attributes/Athleticism/VERTICAL': 70,
    };
    const scores = categoryScores(values)!;
    assert.equal(scores.finishing, 80);
    assert.equal(scores.shooting, 77, '(70 + 1.5 * 90 + 0.5 * 50) / 3');
    assert.equal(categoryScores({}), null);
});
