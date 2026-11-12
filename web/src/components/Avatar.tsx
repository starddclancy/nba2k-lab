import { useEffect, useState } from 'react';
import { avatarGet } from '../api';
import { avatarUrl, type BorrowedPhoto } from '../avatars';

/** Standard NBA club colours, keyed by the team names the game uses. */
const TEAM_COLORS: Record<string, string> = {
    'Atlanta Hawks': '#E03A3E',
    'Boston Celtics': '#007A33',
    'Brooklyn Nets': '#1A1A1A',
    'Charlotte Hornets': '#1D1160',
    'Chicago Bulls': '#CE1141',
    'Cleveland Cavaliers': '#860038',
    'Dallas Mavericks': '#00538C',
    'Denver Nuggets': '#0E2240',
    'Detroit Pistons': '#C8102E',
    'Golden State Warriors': '#1D428A',
    'Houston Rockets': '#CE1141',
    'Indiana Pacers': '#002D62',
    'LA Clippers': '#C8102E',
    'Los Angeles Clippers': '#C8102E',
    'Los Angeles Lakers': '#552583',
    'Memphis Grizzlies': '#5D76A9',
    'Miami Heat': '#98002E',
    'Milwaukee Bucks': '#00471B',
    'Minnesota Timberwolves': '#0C2340',
    'New Orleans Pelicans': '#0C2340',
    'New York Knicks': '#006BB6',
    'Oklahoma City Thunder': '#007AC1',
    'Orlando Magic': '#0077C0',
    'Philadelphia 76ers': '#006BB6',
    'Phoenix Suns': '#1D1160',
    'Portland Trail Blazers': '#E03A3E',
    'Sacramento Kings': '#5A2D81',
    'San Antonio Spurs:': '#C4CED4',
    'San Antonio Spurs': '#C4CED4',
    'Toronto Raptors': '#CE1141',
    'Utah Jazz': '#002B5C',
    'Washington Wizards': '#002B5C',
};

/** Stable per-team colour: known clubs use their brand colour, others a hash hue. */
export function teamColor(team: string): string {
    if (!team) {
        return '#8b949e';
    }
    const known = TEAM_COLORS[team];
    if (known) {
        return known;
    }
    let hash = 0;
    for (let index = 0; index < team.length; index += 1) {
        hash = (hash * 31 + team.charCodeAt(index)) % 360;
    }
    return `hsl(${hash}, 52%, 42%)`;
}

/** Two Latin initials, or the first character for CJK names. */
export function initials(name: string): string {
    const trimmed = (name || '').trim();
    if (!trimmed) {
        return '?';
    }
    const words = trimmed.split(/\s+/);
    if (words.length >= 2) {
        return (words[0][0] + words[words.length - 1][0]).toUpperCase();
    }
    return trimmed.slice(0, 2).toUpperCase();
}

type Props = {
    name: string;
    team: string;
    photoId?: number | null;
    size?: number;
    /** Look for a cached local image; kept off in the big table to avoid thousands of calls. */
    loadImage?: boolean;
    /** Bumping this re-reads the cache after the image was replaced. */
    version?: number;
    onClick?: () => void;
    title?: string;
    /** Photo of another record of the same person, used only when this record has none. */
    borrowed?: BorrowedPhoto | null;
};

/** Show the transparent game portrait, preferring a user-supplied local photo when enabled. */
export default function Avatar({ name, photoId, size = 48, loadImage = false, version = 0, onClick, title, borrowed }: Props) {
    const [image, setImage] = useState<string | null>(null);
    const ownImage = avatarUrl(photoId);
    const gameImage = ownImage ?? avatarUrl(borrowed?.photoId) ?? borrowed?.url ?? null;
    const label = !ownImage && gameImage && borrowed
        ? borrowed.url ? `${title ?? name}（照片来自 ESPN，按姓名和生日匹配）`
            : `${title ?? name}（借用 ${borrowed.fromName} #${borrowed.fromIndex} 的照片）` : title ?? name;

    useEffect(() => {
        if (!loadImage) {
            setImage(gameImage);
            return;
        }
        let alive = true;
        avatarGet(name)
            .then((result) => {
                if (alive) {
                    setImage(result.found ? result.dataUrl ?? null : gameImage);
                }
            })
            .catch(() => {
                if (alive) {
                    setImage(gameImage);
                }
            });
        return () => {
            alive = false;
        };
    }, [gameImage, loadImage, name, version]);

    const style: React.CSSProperties = {
        width: size,
        height: size,
        borderRadius: '50%',
        flex: '0 0 auto',
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        fontSize: Math.max(10, Math.round(size * 0.38)),
        fontWeight: 600,
        color: '#687786',
        background: 'transparent',
        overflow: 'hidden',
        cursor: onClick ? 'pointer' : 'default',
        objectFit: 'cover',
    };

    if (image) {
        return (
            <img className="avatar" src={image} alt={name} title={label} style={style}
                onClick={onClick} onError={() => setImage(image !== gameImage ? gameImage : null)} />
        );
    }
    return (
        <span className="avatar" style={style} onClick={onClick} title={label}>
            {initials(name)}
        </span>
    );
}
