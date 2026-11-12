const validCode = (value: unknown): value is number =>
    typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 255;

/** Statistics store the ending year; NBA award history stores the starting year. */
export function compactSeason(value: unknown): string {
    return validCode(value) && value > 0 ? `${1899 + value}-${1900 + value} 赛季` : '年份未知';
}

export function historySeason(value: unknown): string {
    return validCode(value) ? `${1791 + value}-${1792 + value} 赛季` : '年份未知';
}
