import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

test('build studio keeps sourced action names but excludes unpublished connectors from planning', async (t) => {
    const previousWindow = globalThis.window;
    globalThis.window = { location: { search: '?preview', hash: '' } } as Window & typeof globalThis;
    const server = await createServer({
        configFile: false, server: { middlewareMode: true, hmr: { port: 0 } },
        appType: 'custom', esbuild: { jsx: 'automatic' },
    });
    let restoreCatalog: (() => void) | undefined;
    try {
        const studio = await server.ssrLoadModule('/src/components/BuildStudio.tsx');
        const { buildCatalog } = await server.ssrLoadModule('/src/buildCatalog.ts');
        const { createBuild } = await server.ssrLoadModule('/src/builds.ts');
        const original = [...buildCatalog.animations];
        const unpublished = original.find((item) => item.rule.logic === 'unverified');
        const confirmed = original.find((item) => item.rule.logic !== 'unverified');
        assert.ok(unpublished && confirmed);
        buildCatalog.animations.splice(0, original.length, unpublished, confirmed,
            ...original.filter((item) => item !== unpublished && item !== confirmed));
        restoreCatalog = () => buildCatalog.animations.splice(0, original.length, ...original);

        await t.test('an action with an unpublished connector stays browsable without eligibility or a target checkbox', () => {
            const html = renderToStaticMarkup(createElement(studio.default, { view: 'animations' }));
            const row = html.split(`data-row-key="${unpublished.id}"`)[1]?.split('</tr>')[0];
            assert.ok(row, 'The sourced action must remain in the browse list');
            assert.ok(row.includes(unpublished.name));
            assert.doesNotMatch(row, /设为目标|可用|未达门槛|待核实|连接关系/);
            assert.match(row, /仅供浏览/);
            const confirmedRow = html.split(`data-row-key="${confirmed.id}"`)[1]?.split('</tr>')[0];
            assert.match(confirmedRow ?? '', /设为目标/);
        });

        await t.test('cached and imported targets cannot restore unpublished or missing actions', () => {
            const plan = { ...createBuild(), animationGoals: [confirmed.id, unpublished.id, 'missing-action'] };
            const before = structuredClone(plan);
            const filtered = studio.withAvailableAnimationGoals(plan);
            assert.deepEqual(filtered.animationGoals, [confirmed.id]);
            assert.deepEqual(plan, before, 'Filtering a loaded plan must not mutate the original library');
        });
    } finally {
        restoreCatalog?.();
        await server.close();
        globalThis.window = previousWindow;
    }
});
