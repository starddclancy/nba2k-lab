import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { translated } from '../src/labels.ts';

const catalog = JSON.parse(readFileSync(new URL('../src/data/build-requirements.json', import.meta.url), 'utf8'));
const manifest = JSON.parse(readFileSync(new URL('../src/data/badge-icons.json', import.meta.url), 'utf8'));
const normalize = (value: string) => value.toLowerCase().replace(/[^a-z0-9]/g, '').replace(/badge$/, '');

test('every current badge has a Chinese label and a real image', () => {
    assert.equal(catalog.badges.length, 53);
    for (const badge of catalog.badges) {
        const label = translated(badge.name) ?? translated(`${badge.id}Badge`);
        assert.match(label ?? '', /[\u3400-\u9fff]/u, badge.name);
        assert.ok(manifest.entries[badge.id]?.file, badge.name);
    }
    assert.deepEqual(manifest.missing, []);
});

test('packaged badge files match their recorded checksums and image signatures', () => {
    for (const entry of Object.values({ ...manifest.entries, ...manifest.categories }) as {
        file: string; sha256: string; width: number; height: number;
    }[]) {
        assert.match(entry.file, /^[a-z0-9-]+\.(png|webp)$/i);
        const bytes = readFileSync(new URL(`../public/badge-icons/${entry.file}`, import.meta.url));
        assert.equal(createHash('sha256').update(bytes).digest('hex'), entry.sha256, entry.file);
        if (entry.file.endsWith('.png')) {
            assert.deepEqual([...bytes.subarray(0, 8)], [137, 80, 78, 71, 13, 10, 26, 10]);
            assert.equal(bytes.readUInt32BE(16), entry.width);
            assert.equal(bytes.readUInt32BE(20), entry.height);
        } else {
            assert.equal(bytes.toString('ascii', 0, 4), 'RIFF');
            assert.equal(bytes.toString('ascii', 8, 12), 'WEBP');
        }
    }
});

test('personality artwork is the installed category icon, without claiming individual glyph mappings', () => {
    const entry = manifest.categories.Personality;
    assert.equal(entry.metadata, 'icon_mycareer_badge_personality.TXTR');
    assert.equal(entry.container, 'badgeicons.iff');
    assert.equal(entry.scope, 'category');
    assert.match(entry.texture_resource, /\/icon_mycareer_badge_personality\.[a-f0-9]+\.tld$/);
    assert.equal(manifest.entries.AlphaDog, undefined);
    assert.equal(manifest.entries.Enforcer, undefined);
});

test('badge mapping uses exact installed texture names or named 2K27 source records', () => {
    for (const badge of catalog.badges) {
        const entry = manifest.entries[badge.id];
        if (entry.metadata) {
            assert.equal(normalize(entry.metadata.replace(/^icon_mycareer_badge_/, '').replace(/\.TXTR$/, '')),
                normalize(badge.name), badge.name);
            assert.equal(entry.container, 'badgeicons.iff');
        } else {
            assert.equal(entry.name, badge.name);
            assert.equal(entry.gameYear, '2k27');
            assert.equal(new URL(entry.sourcePage).hostname, 'www.nba2klab.com');
            assert.equal(new URL(entry.sourceUrl).hostname, 'cdn.sanity.io');
        }
    }
});
