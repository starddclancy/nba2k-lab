import index from './data/avatar-index.json' with { type: 'json' };
import webIndex from './data/web-avatars.json' with { type: 'json' };

/** ESPN headshots matched by exact name + birth date (scripts/fetch-web-avatars.py). */
const webPhotos = webIndex.players as Record<string, string>;

/** Photo ids with an extracted game image: chr_g portraits first, chr_r head crops fill gaps. */
const portraits = new Set<number>(index.portrait);
const fullBody = new Set<number>(index.fullBody);

export type AvatarKind = 'portrait' | 'fullBody';

export function avatarKind(photoId?: number | null): AvatarKind | null {
    if (!photoId || photoId <= 0) {
        return null;
    }
    return portraits.has(photoId) ? 'portrait' : fullBody.has(photoId) ? 'fullBody' : null;
}

export function avatarUrl(photoId?: number | null): string | null {
    const kind = avatarKind(photoId);
    return kind === 'portrait' ? `/player-avatars/chr_g${photoId}.png`
        : kind === 'fullBody' ? `/player-avatars/chr_r${photoId}.png` : null;
}

type BorrowRow = {
    key: string;
    index: number;
    name: string;
    photoId?: number | null;
    category?: string;
    summary?: { birthYear: number | null; birthMonth?: number | null; birthDay?: number | null };
};

/** photoId 0 with url set means a web headshot rather than another roster record. */
export type BorrowedPhoto = { photoId: number; fromIndex: number; fromName: string; url?: string };

export function webPhotoUrl(key: string | null): string | null {
    const id = key ? webPhotos[key] : undefined;
    return id ? `/player-avatars/web/espn${id}.webp` : null;
}

/** Accent-, case- and punctuation-insensitive name key ("Luka Dončić" == "luka doncic"). */
export function nameKey(name: string): string {
    return name.normalize('NFKD').replace(/[\u0300-\u036f]/g, '').toLowerCase().replace(/[^a-z0-9]/g, '');
}

function identityKey(row: BorrowRow): string | null {
    const s = row.summary;
    const name = nameKey(row.name);
    if (!name || !s?.birthYear || !s.birthMonth || !s.birthDay) {
        return null;
    }
    return `${name}|${s.birthYear}-${s.birthMonth}-${s.birthDay}`;
}

/**
 * For rows without a game image, find another record of the same person: same normalized name
 * and the same full birth date. A team record is preferred, then the lowest roster index, so
 * the choice is stable across refreshes. Rows lacking any birth-date part never borrow.
 */
export function borrowPhotos(rows: BorrowRow[]): Map<string, BorrowedPhoto> {
    const donors = new Map<string, BorrowRow>();
    for (const row of rows) {
        const key = identityKey(row);
        if (!key || !avatarKind(row.photoId)) {
            continue;
        }
        const current = donors.get(key);
        const rank = (candidate: BorrowRow) => [candidate.category === 'team' ? 0 : 1, candidate.index];
        const [rowTeam, rowIndex] = rank(row);
        const better = !current || rowTeam < rank(current)[0]
            || (rowTeam === rank(current)[0] && rowIndex < rank(current)[1]);
        if (better) {
            donors.set(key, row);
        }
    }
    const borrowed = new Map<string, BorrowedPhoto>();
    for (const row of rows) {
        if (avatarKind(row.photoId)) {
            continue;
        }
        const key = identityKey(row);
        const donor = key ? donors.get(key) : undefined;
        if (donor && donor.index !== row.index) {
            borrowed.set(row.key, { photoId: donor.photoId as number, fromIndex: donor.index, fromName: donor.name });
        } else {
            const url = webPhotoUrl(key);
            if (url) {
                borrowed.set(row.key, { photoId: 0, fromIndex: -1, fromName: 'ESPN', url });
            }
        }
    }
    return borrowed;
}
