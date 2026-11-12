import { attributes, parseBuild, positions, tiers } from './builds.ts';
import type { BuildPlan } from './builds.ts';

/**
 * Compact text code for sending one build to someone else. Version 1 layout (all integers):
 * position index, height, weight, wingspan, 21 ratings in `attributes` order, then badge goals
 * as id:tier pairs. Name and notes are carried as URI-encoded text. The code never carries the
 * plan id, timestamps, favorite flag or tags: decoding always creates a new plan.
 */
const PREFIX = '2K27B1';

function checksum(text: string): string {
    let hash = 2166136261;
    for (let index = 0; index < text.length; index += 1) {
        hash ^= text.charCodeAt(index);
        hash = Math.imul(hash, 16777619) >>> 0;
    }
    return (hash % 46656).toString(36).padStart(3, '0');
}

const toBase64Url = (text: string) => btoa(unescape(encodeURIComponent(text)))
    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
const fromBase64Url = (code: string) => decodeURIComponent(escape(atob(
    code.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - code.length % 4) % 4))));

export function encodeShareCode(plan: BuildPlan): string {
    const checked = parseBuild(plan);
    const numbers = [positions.indexOf(checked.position), checked.heightInches, checked.weightLbs,
        checked.wingspanInches, ...attributes.map(({ id }) => checked.ratings[id])];
    const badges = checked.badgeGoals.map((goal) => `${goal.id}:${tiers.indexOf(goal.tier)}`).join(',');
    const body = [numbers.join('.'), badges, encodeURIComponent(checked.name), encodeURIComponent(checked.notes)].join('|');
    const payload = toBase64Url(body);
    return `${PREFIX}-${payload}-${checksum(payload)}`;
}

export function decodeShareCode(code: string, id: string = crypto.randomUUID(), now = new Date().toISOString()): BuildPlan {
    const trimmed = code.trim();
    if (trimmed.length > 20000) {
        throw new Error('分享码过长');
    }
    const match = /^2K27B(\d+)-([A-Za-z0-9_-]+)-([0-9a-z]{3})$/.exec(trimmed);
    if (!match) {
        throw new Error('不是有效的 2K27 建模分享码');
    }
    if (match[1] !== '1') {
        throw new Error(`不支持的分享码版本 ${match[1]}`);
    }
    if (checksum(match[2]) !== match[3]) {
        throw new Error('分享码不完整或被改动过（校验失败）');
    }
    let fields: string[];
    try {
        fields = fromBase64Url(match[2]).split('|');
    } catch {
        throw new Error('分享码内容无法解码');
    }
    if (fields.length !== 4) {
        throw new Error('分享码字段数量不对');
    }
    const numbers = fields[0].split('.').map((value) => (/^\d+$/.test(value) ? Number(value) : NaN));
    if (numbers.length !== 4 + attributes.length || numbers.some((value) => !Number.isInteger(value))) {
        throw new Error('分享码属性数据不完整');
    }
    const position = positions[numbers[0]];
    if (!position) {
        throw new Error('分享码位置无效');
    }
    const badgeGoals = fields[1] ? fields[1].split(',').map((pair) => {
        const [badgeId, tier] = pair.split(':');
        const name = tiers[Number(tier)];
        if (!badgeId || !name || !/^\d$/.test(tier ?? '')) {
            throw new Error('分享码徽章目标无效');
        }
        return { id: badgeId, tier: name };
    }) : [];
    // parseBuild applies the same validation as file import (ranges, duplicates, lengths).
    return parseBuild({
        id, name: decodeURIComponent(fields[2]) || '分享方案', position,
        heightInches: numbers[1], weightLbs: numbers[2], wingspanInches: numbers[3],
        ratings: Object.fromEntries(attributes.map(({ id: key }, index) => [key, numbers[4 + index]])),
        badgeGoals, animationGoals: [], tags: [], favorite: false,
        notes: decodeURIComponent(fields[3]), createdAt: now, updatedAt: now,
    });
}
