import assert from 'node:assert/strict';
import test from 'node:test';
import model from '../src/data/overall-model.json' with { type: 'json' };
import { estimateOverall, overallFields } from '../src/overall.ts';

const base = Object.fromEntries(overallFields.map((field) => [field, field.endsWith('HEIGHT') ? 200 : 60]));

test('model covers all positions and the 37 attributes plus height', () => {
    assert.equal(overallFields.length, 38);
    assert.ok(overallFields.includes('Attributes/Offense/3POINT'));
    for (const position of ['PG', 'SG', 'SF', 'PF', 'C'] as const) {
        assert.equal(model.positions[position].weights.length, overallFields.length);
        assert.ok(model.positions[position].heldOut.within1 > 0.8, `${position} held-out accuracy`);
    }
});

test('estimate is anchored on the real overall and only moves with relevant edits', () => {
    assert.equal(estimateOverall('SG', 80, base, {}), null, 'nothing changed');
    assert.equal(estimateOverall('SG', 80, base, { 'Vitals/Vitals/FIRSTNAME': 'x' }), null, 'unrelated field');
    assert.equal(estimateOverall(null, 80, base, { 'Attributes/Offense/3POINT': 90 }), null, 'unknown position');
    assert.equal(estimateOverall('SG', null, base, { 'Attributes/Offense/3POINT': 90 }), null, 'unknown overall');
    const missing = { ...base };
    delete (missing as Record<string, unknown>)['Attributes/Offense/3POINT'];
    assert.equal(estimateOverall('SG', 80, missing, { 'Attributes/Offense/MIDRANGE': 90 }), null, 'missing input');
    const up = estimateOverall('SG', 80, base, { 'Attributes/Offense/3POINT': 99 });
    assert.ok(up !== null && up > 80, `three-point raise lifts a guard overall (${up})`);
    const all = Object.fromEntries(overallFields.filter((field) => field.startsWith('Attributes/')).map((field) => [field, 99]));
    assert.equal(estimateOverall('SG', 80, base, all), 99, 'clamped to 99');
});
