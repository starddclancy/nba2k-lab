import assert from 'node:assert/strict';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createServer } from 'vite';

let chromium;
try {
    ({ chromium } = await import(process.env.DISPLAY_READ_PLAYWRIGHT
        ? pathToFileURL(process.env.DISPLAY_READ_PLAYWRIGHT).href : 'playwright'));
} catch { /* Browser tests use the existing opt-in Playwright runtime. */ }

test('library application and undo retain their original edit session with a simulated host', { skip: !chromium }, async (t) => {
    const server = await createServer({
        configFile: false,
        cacheDir: 'node_modules/.vite-library-apply-tests',
        root: fileURLToPath(new URL('../', import.meta.url)),
        server: { host: '127.0.0.1', port: 0, hmr: false },
        optimizeDeps: { noDiscovery: true, include: [
            'react', 'react/jsx-runtime', 'react/jsx-dev-runtime', 'react-dom/client', 'react-dom',
            'antd', '@ant-design/icons',
        ] },
        plugins: [{
            name: 'library-apply-test-page',
            configureServer(vite) {
                vite.middlewares.use('/library-apply-test', async (_request, response) => {
                    response.setHeader('Content-Type', 'text/html');
                    response.end(await vite.transformIndexHtml('/library-apply-test',
                        '<div id="root"></div><script type="module" src="/tests/fixtures/libraryApplyHarness.tsx"></script>'));
                });
            },
        }],
    });
    await server.listen();
    const address = server.httpServer!.address();
    assert.ok(address && typeof address !== 'string');
    const browser = await chromium.launch({
        channel: process.env.DISPLAY_READ_BROWSER ?? (process.platform === 'win32' ? 'msedge' : undefined),
        headless: true,
    }).catch(async (error) => { await server.close(); throw error; });
    async function open() {
        const page = await browser.newPage();
        page.setDefaultTimeout(15_000);
        page.on('pageerror', (error) => console.error(`Library test page: ${error.message}`));
        await page.goto(`http://127.0.0.1:${address.port}/library-apply-test`);
        await page.waitForFunction(() => !!window.libraryApplyHarness);
        await page.getByRole('cell', { name: 'Test Entry', exact: true }).waitFor();
        assert.equal(await page.locator('button').filter({ hasText: /^应\s*用$/ }).isDisabled(), true);
        await page.evaluate(() => window.libraryApplyHarness.configure({ editing: true }));
        return page;
    }
    async function prepare(page, index = 2) {
        await page.locator('button').filter({ hasText: /^应\s*用$/ }).click();
        await page.getByRole('dialog').getByRole('combobox').click();
        await page.locator('.ant-select-dropdown:visible .ant-select-item-option')
            .filter({ hasText: `Player ${index}` }).click();
        await page.getByRole('dialog').getByText('得分后卫', { exact: true }).waitFor();
        await page.getByRole('dialog').locator('.ant-modal-footer button.ant-btn-primary:not(.ant-btn-loading):not([disabled])').waitFor();
    }
    async function applyNow(page) {
        await prepare(page);
        await page.getByRole('dialog').locator('.ant-modal-footer button.ant-btn-primary').click();
        await page.getByRole('button', { name: '撤销对 Player 2 的应用', exact: true }).waitFor();
    }
    async function settle(page) {
        await page.waitForFunction(() => window.libraryApplyHarness.snapshot().pending.length === 0);
        await page.evaluate(() => new Promise<void>((resolve) =>
            requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
    }
    const writes = (page) => page.evaluate(() => window.libraryApplyHarness.snapshot().calls
        .filter((call) => call.command === 'undoPlayer'
            || call.command === 'libraryApply' && call.payload.preview === false));
    const undoWrites = async (page) => (await writes(page)).filter((call) => call.command === 'undoPlayer');
    try {
        for (const stage of ['readPlayer', 'libraryPreview'] as const) {
            await t.test(`off then on permanently cancels an application pending ${stage}`, async () => {
                const page = await open();
                await prepare(page);
                await page.evaluate((value) => window.libraryApplyHarness.hold(value), stage);
                await page.getByRole('dialog').locator('.ant-modal-footer button.ant-btn-primary').click();
                await page.waitForFunction(() => window.libraryApplyHarness.snapshot().pending.length === 1);
                await page.evaluate(() => {
                    window.libraryApplyHarness.configure({ editing: false });
                    window.libraryApplyHarness.configure({ editing: true });
                    window.libraryApplyHarness.hold(null);
                    window.libraryApplyHarness.resolve();
                });
                await settle(page);
                assert.deepEqual(await writes(page), []);
                await page.getByRole('dialog').waitFor({ state: 'hidden' });
                assert.equal(await page.getByRole('dialog').count(), 0);
                await page.close();
            });
            await t.test(`unmount cancels an application pending ${stage}`, async () => {
                const page = await open();
                await prepare(page);
                await page.evaluate((value) => window.libraryApplyHarness.hold(value), stage);
                await page.getByRole('dialog').locator('.ant-modal-footer button.ant-btn-primary').click();
                await page.waitForFunction(() => window.libraryApplyHarness.snapshot().pending.length === 1);
                await page.evaluate(() => {
                    window.libraryApplyHarness.configure({ shown: false });
                    window.libraryApplyHarness.hold(null);
                    window.libraryApplyHarness.resolve();
                });
                await settle(page);
                assert.deepEqual(await writes(page), []);
                await page.close();
            });
        }
        await t.test('off then on revokes an idle preview before another confirmation', async () => {
            const page = await open();
            await prepare(page);
            await page.evaluate(() => {
                window.libraryApplyHarness.configure({ editing: false });
                window.libraryApplyHarness.configure({ editing: true });
            });
            await page.getByRole('dialog').waitFor({ state: 'hidden' });
            assert.equal(await page.getByRole('dialog').count(), 0);
            assert.deepEqual(await writes(page), []);
            await page.close();
        });
        await t.test('a late initial preview cannot return after off then on', async () => {
            const page = await open();
            await page.evaluate(() => window.libraryApplyHarness.hold('libraryPreview'));
            await page.locator('button').filter({ hasText: /^应\s*用$/ }).click();
            await page.getByRole('dialog').getByRole('combobox').click();
            await page.locator('.ant-select-dropdown:visible .ant-select-item-option')
                .filter({ hasText: 'Player 2' }).click();
            await page.waitForFunction(() => window.libraryApplyHarness.snapshot().pending.length === 1);
            await page.evaluate(() => {
                window.libraryApplyHarness.configure({ editing: false });
                window.libraryApplyHarness.configure({ editing: true });
                window.libraryApplyHarness.hold(null);
                window.libraryApplyHarness.resolve();
            });
            await settle(page);
            await page.getByRole('dialog').waitFor({ state: 'hidden' });
            assert.deepEqual(await writes(page), []);
            await page.close();
        });
        for (const selection of ['target', 'scope'] as const) {
            await t.test(`changing ${selection} revokes confirmation until its own preview completes`, async () => {
                const page = await open();
                await prepare(page);
                await page.evaluate(() => window.libraryApplyHarness.hold('libraryPreview'));
                if (selection === 'target') {
                    await page.getByRole('dialog').locator('.ant-select-selector').click();
                    await page.locator('.ant-select-dropdown:visible .ant-select-item-option')
                        .filter({ hasText: 'Player 3' }).click();
                } else {
                    await page.getByRole('dialog').getByText('只换面部', { exact: true }).click();
                }
                await page.waitForFunction(() => window.libraryApplyHarness.snapshot().pending.length === 1);
                const pending = await page.evaluate(() => window.libraryApplyHarness.snapshot().pending[0]);
                assert.equal(pending.payload.index, selection === 'target' ? 3 : 2);
                assert.equal(pending.payload.scope, selection === 'scope' ? 'face' : 'player');
                assert.equal(await page.getByRole('dialog').locator('.ant-modal-footer button.ant-btn-primary').isDisabled(), true);
                assert.deepEqual(await writes(page), []);
                await page.evaluate(() => {
                    window.libraryApplyHarness.hold(null);
                    window.libraryApplyHarness.resolve();
                });
                await page.getByRole('dialog')
                    .locator('.ant-modal-footer button.ant-btn-primary:not(.ant-btn-loading):not([disabled])').click();
                await page.waitForFunction(() => window.libraryApplyHarness.snapshot().calls
                    .some((call) => call.command === 'libraryApply' && call.payload.preview === false));
                const sent = (await writes(page))[0].payload;
                assert.equal(sent.index, pending.payload.index);
                assert.equal(sent.scope, pending.payload.scope);
                await page.close();
            });
        }
        await t.test('same-session application submits the freshly read token', async () => {
            const page = await open();
            await applyNow(page);
            const sent = await writes(page);
            assert.equal(sent.length, 1);
            assert.deepEqual(sent[0].payload, { id: 'library-entry', index: 2, scope: 'player',
                preview: false, readToken: 'read:roster:2:1' });
            await page.close();
        });
        for (const context of ['failure', 'off-on', 'unmount', 'success'] as const) {
            await t.test(`undo requires a successful same-session fresh read: ${context}`, async () => {
                const page = await open();
                await applyNow(page);
                const readsBefore = await page.evaluate(() => window.libraryApplyHarness.snapshot().calls
                    .filter((call) => call.command === 'readPlayer').length);
                await page.evaluate((fail) => {
                    window.libraryApplyHarness.hold('readPlayer');
                    window.libraryApplyHarness.failRead(fail);
                }, context === 'failure');
                await page.getByRole('button', { name: '撤销对 Player 2 的应用', exact: true }).click();
                await page.waitForFunction((before) => {
                    const calls = window.libraryApplyHarness.snapshot().calls;
                    return calls.some((call) => call.command === 'undoPlayer')
                        || calls.filter((call) => call.command === 'readPlayer').length > before;
                }, readsBefore);
                assert.deepEqual(await undoWrites(page), [], 'Undo must wait for its own fresh read');
                await page.evaluate((change) => {
                    if (change === 'off-on') {
                        window.libraryApplyHarness.configure({ editing: false });
                        window.libraryApplyHarness.configure({ editing: true });
                    } else if (change === 'unmount') {
                        window.libraryApplyHarness.configure({ shown: false });
                    }
                    window.libraryApplyHarness.hold(null);
                    window.libraryApplyHarness.resolve();
                }, context);
                await settle(page);
                if (context === 'success') {
                    assert.deepEqual((await undoWrites(page)).map((call) => call.payload), [{ index: 2 }]);
                } else {
                    assert.deepEqual(await undoWrites(page), []);
                }
                await page.close();
            });
        }
    } finally {
        await browser.close();
        await server.close();
    }
});
