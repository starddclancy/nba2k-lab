import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import test from 'node:test';
import index from '../src/data/avatar-index.json' with { type: 'json' };
import { avatarKind, avatarUrl, borrowPhotos, nameKey } from '../src/avatars.ts';

const portrait = index.portrait[0];
const body = index.fullBody[0];
const none = 999999;

test('index lists every extracted file and a portrait wins over a full-body crop', () => {
    assert.ok(index.portrait.length > 2000 && index.fullBody.length > 500);
    for (const id of index.portrait) {
        assert.ok(existsSync(new URL(`../public/player-avatars/chr_g${id}.png`, import.meta.url)), `chr_g${id}`);
    }
    for (const id of index.fullBody) {
        assert.ok(existsSync(new URL(`../public/player-avatars/chr_r${id}.png`, import.meta.url)), `chr_r${id}`);
        assert.ok(!index.portrait.includes(id), `full-body ${id} duplicates a portrait`);
    }
    assert.equal(avatarUrl(portrait), `/player-avatars/chr_g${portrait}.png`);
    assert.equal(avatarUrl(body), `/player-avatars/chr_r${body}.png`);
    assert.equal(avatarKind(none), null);
    assert.equal(avatarUrl(0), null);
    assert.equal(avatarUrl(null), null);
});

const born = (y: number | null, m: number | null, d: number | null) =>
    ({ birthYear: y, birthMonth: m, birthDay: d });

test('borrowing needs the same name and full birth date', () => {
    const rows = [
        { key: 'a', index: 10, name: 'Luka Dončić', photoId: portrait, category: 'team', summary: born(1999, 2, 28) },
        { key: 'b', index: 900, name: 'luka doncic', photoId: 0, category: 'historic', summary: born(1999, 2, 28) },
        { key: 'c', index: 901, name: 'Luka Doncic', photoId: none, summary: born(1999, 3, 1) },
        { key: 'd', index: 902, name: 'Luka Doncic', photoId: 0, summary: born(1999, 2, null) },
        { key: 'e', index: 903, name: 'Other Player', photoId: 0, summary: born(1999, 2, 28) },
    ];
    const borrowed = borrowPhotos(rows);
    assert.deepEqual(borrowed.get('b'), { photoId: portrait, fromIndex: 10, fromName: 'Luka Dončić' });
    assert.equal(borrowed.has('a'), false, 'own image is kept');
    assert.equal(borrowed.has('c'), false, 'different birthday');
    assert.equal(borrowed.has('d'), false, 'unknown day never borrows');
    assert.equal(borrowed.has('e'), false, 'different name');
    assert.equal(nameKey('Jr. O\'Neal-Smith'), 'jronealsmith');
});

test('donor choice is stable: a team record first, then the lowest index', () => {
    const rows = [
        { key: 'h', index: 5, name: 'Kevin Durant', photoId: body, category: 'historic', summary: born(1988, 9, 29) },
        { key: 't', index: 40, name: 'Kevin Durant', photoId: portrait, category: 'team', summary: born(1988, 9, 29) },
        { key: 'x', index: 3000, name: 'Kevin Durant', photoId: 0, category: 'historic', summary: born(1988, 9, 29) },
    ];
    assert.equal(borrowPhotos(rows).get('x')?.fromIndex, 40);
    assert.equal(borrowPhotos(rows.reverse()).get('x')?.fromIndex, 40);
    const noTeam = rows.filter((row) => row.key !== 't');
    assert.equal(borrowPhotos(noTeam).get('x')?.fromIndex, 5);
});

test('web headshots fill only rows with no game or same-person photo', async () => {
    const web = (await import('../src/data/web-avatars.json', { with: { type: 'json' } })).default as { players: Record<string, string> };
    const { webPhotoUrl } = await import('../src/avatars.ts');
    const entries = Object.entries(web.players);
    for (const [key, id] of entries) {
        assert.match(key, /^[a-z0-9]+\|\d{4}-\d{1,2}-\d{1,2}$/, 'key is nameKey|Y-M-D');
        assert.ok(existsSync(new URL(`../public/player-avatars/web/espn${id}.webp`, import.meta.url)), `espn${id}`);
    }
    assert.equal(webPhotoUrl(null), null);
    assert.equal(webPhotoUrl('nobody|1900-1-1'), null);
    if (entries.length > 0) {
        const [key, id] = entries[0];
        const [, date] = key.split('|');
        const [y, m, d] = date.split('-').map(Number);
        assert.equal(webPhotoUrl(key), `/player-avatars/web/espn${id}.webp`);
        // A row whose name key matches gets the web photo; the same name with another birthday does not.
        const name = key.split('|')[0];
        const rows = [
            { key: 'a', index: 1, name, photoId: 0, summary: born(y, m, d) },
            { key: 'b', index: 2, name, photoId: 0, summary: born(y + 1, m, d) },
        ];
        const result = borrowPhotos(rows);
        assert.equal(result.get('a')?.url, `/player-avatars/web/espn${id}.webp`);
        assert.equal(result.has('b'), false);
    }
});
