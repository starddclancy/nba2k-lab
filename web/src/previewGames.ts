import { isPreview } from './bridge';
import type { GameLog } from './gameLog';

/** Preview-only sample game log so the per-game charts can be inspected without the game. */
export function previewGameLog(): GameLog | null {
    if (!isPreview) return null;
    const teams = ['Seattle SuperSonics', 'Philadelphia 76ers', 'Boston Celtics', 'Chicago Bulls', 'Miami Heat'];
    const names = ['Sample Forward', 'Sample Wing', 'Sample Guard', 'Sample Center'];
    const games = Array.from({ length: 12 }, (_, game) => {
        const scores = [96 + ((game * 7) % 19), 90 + ((game * 11) % 23)];
        return {
            id: `preview:${game}`, pid: 1, startedUtc: `2026-11-${String(game + 1).padStart(2, '0')}T01:00:00Z`, updatedUtc: '', period: 4, clock: 0,
            teams: [teams[0], teams[1 + (game % 4)]].map((label, side) => ({ label, points: scores[side],
                players: names.map((name, order) => ({ name: side ? `${name} ${label.split(' ').pop()}` : name, id: order + 1,
                    points: 8 + ((game * (order + 3) + order * 5) % 24), rebounds: 2 + ((game + order * 3) % 9), assists: 1 + ((game * 2 + order) % 8),
                    steals: (game + order) % 3, blocks: (game * order) % 3, turnovers: (game + 2 * order) % 4,
                    fgMade: 3 + ((game + order) % 8), fgAttempted: 10 + ((game + order) % 9), threeMade: (game + order) % 4, threeAttempted: 3 + (order % 4),
                    ftMade: (game + order) % 6, ftAttempted: 1 + ((game + order) % 6) + (order % 2), offensiveRebounds: (game + order) % 3, defensiveRebounds: 2 + ((game + order * 3) % 7) })) })),
        };
    });
    return { format: 'nba2k27-game-log/1', games };
}
