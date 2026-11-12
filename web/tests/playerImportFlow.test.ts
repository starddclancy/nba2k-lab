import assert from 'node:assert/strict';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createServer } from 'vite';

let chromium;
try {
    ({ chromium } = await import(process.env.DISPLAY_READ_PLAYWRIGHT
        ? pathToFileURL(process.env.DISPLAY_READ_PLAYWRIGHT).href : 'playwright'));
} catch { /* Browser tests use the existing opt-in Playwright runtime. */ }

test('backup import confirms only the current decoded preview with a simulated host', { skip: !chromium }, async (t) => {
    const server = await createServer({
        configFile: false,
        cacheDir: 'node_modules/.vite-player-import-tests',
        root: fileURLToPath(new URL('../', import.meta.url)),
        server: { host: '127.0.0.1', port: 0, hmr: false },
        optimizeDeps: { noDiscovery: true, include: [
            'react', 'react/jsx-runtime', 'react/jsx-dev-runtime', 'react-dom/client', 'react-dom',
            'antd', '@ant-design/icons',
        ] },
        plugins: [{
            name: 'player-import-test-page',
            configureServer(vite) {
                vite.middlewares.use('/player-import-test', async (_request, response) => {
                    response.setHeader('Content-Type', 'text/html');
                    response.end(await vite.transformIndexHtml('/player-import-test',
                        '<div id="root"></div><script type="module" src="/tests/fixtures/playerImportHarness.tsx"></script>'));
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
        page.on('pageerror', (error) => console.error(`Import test page: ${error.message}`));
        await page.goto(`http://127.0.0.1:${address.port}/player-import-test`);
        await page.waitForFunction(() => !!window.playerImportHarness);
        await page.getByRole('button', { name: '导入', exact: true }).waitFor();
        assert.equal(await page.getByRole('button', { name: '导入', exact: true }).isDisabled(), true);
        await page.evaluate(() => window.playerImportHarness.configure({ editing: true }));
        await page.waitForFunction(() => {
            const button = [...document.querySelectorAll('button')].find((node) => node.textContent === '导入');
            return button && !button.disabled;
        });
        return page;
    }
    const writes = async (page) => page.evaluate(() =>
        window.playerImportHarness.snapshot().calls.filter((call) => call.command === 'applyPlayer'));
    try {
        await t.test('unknown preview is rejected and cancelling a known preview does not write', async () => {
            const page = await open();
            await page.evaluate(() => window.playerImportHarness.unknown(true));
            await page.getByRole('button', { name: '导入', exact: true }).click();
            await page.getByText('备份包含当前未开放的项目，未导入任何修改。').waitFor();
            assert.equal(await page.getByRole('dialog').count(), 0);
            assert.deepEqual(await writes(page), []);
            await page.evaluate(() => window.playerImportHarness.unknown(false));
            await page.getByRole('button', { name: '导入', exact: true }).click();
            await page.getByRole('dialog').getByText('得分后卫').waitFor();
            await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click();
            assert.deepEqual(await writes(page), []);
            await page.close();
        });
        await t.test('confirmation submits decoded values with the preview token and picks the file only once', async () => {
            const page = await open();
            await page.getByRole('button', { name: '导入', exact: true }).click();
            await page.getByRole('dialog').getByRole('button', { name: '应用 1 项', exact: true }).click();
            await page.waitForFunction(() => window.playerImportHarness.snapshot().calls.some((call) => call.command === 'applyPlayer'));
            assert.deepEqual((await writes(page))[0].payload, {
                index: 2, source: 'roster', readToken: 'import:roster:2', changes: { 'Vitals/General/POSITION': 'SG' },
            });
            const count = await page.evaluate(() => window.playerImportHarness.snapshot().calls
                .filter((call) => call.command === 'importPlayer' && call.payload.preview === true).length);
            assert.equal(count, 1);
            await page.close();
        });
        for (const patch of [{ editing: false }, { index: 3 }, { source: 'prospects' }, { shown: false }]) {
            await t.test(`context change cancels confirmation: ${JSON.stringify(patch)}`, async () => {
                const page = await open();
                await page.getByRole('button', { name: '导入', exact: true }).click();
                await page.getByRole('dialog').waitFor();
                await page.evaluate((value) => window.playerImportHarness.configure(value), patch);
                await page.getByRole('dialog').waitFor({ state: 'hidden' });
                assert.deepEqual(await writes(page), []);
                await page.close();
            });
        }
        await t.test('a late file preview cannot reopen confirmation after the target changes', async () => {
            const page = await open();
            await page.evaluate(() => window.playerImportHarness.hold(true));
            await page.getByRole('button', { name: '导入', exact: true }).click();
            await page.waitForFunction(() => window.playerImportHarness.snapshot().pending === 1);
            await page.evaluate(() => {
                window.playerImportHarness.configure({ index: 3 });
                window.playerImportHarness.resolve();
            });
            await page.waitForFunction(() => window.playerImportHarness.snapshot().pending === 0);
            assert.equal(await page.getByRole('dialog').count(), 0);
            assert.deepEqual(await writes(page), []);
            await page.close();
        });
    } finally {
        await browser.close();
        await server.close();
    }
});
