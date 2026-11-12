import assert from 'node:assert/strict';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createServer } from 'vite';

// Use an installed Playwright package or the desktop's bundled package without adding app dependencies.
let chromium;
try {
    ({ chromium } = await import(process.env.DISPLAY_READ_PLAYWRIGHT
        ? pathToFileURL(process.env.DISPLAY_READ_PLAYWRIGHT).href : 'playwright'));
} catch { /* Browser tests are opt-in when Playwright is not installed. */ }

test('useDisplayRead runs against real React with controlled loaders and clock', { skip: !chromium }, async (t) => {
    const server = await createServer({
        configFile: false,
        cacheDir: 'node_modules/.vite-display-read-tests',
        root: fileURLToPath(new URL('../', import.meta.url)),
        server: { host: '127.0.0.1', port: 0, hmr: false },
        optimizeDeps: { noDiscovery: true,
            include: ['react', 'react/jsx-runtime', 'react/jsx-dev-runtime', 'react-dom/client', 'react-dom'] },
        plugins: [{
            name: 'display-read-test-page',
            configureServer(vite) {
                vite.middlewares.use('/display-read-test', async (_request, response) => {
                    response.setHeader('Content-Type', 'text/html');
                    response.end(await vite.transformIndexHtml('/display-read-test',
                        '<div id="root"></div><script type="module" src="/tests/fixtures/displayReadHarness.tsx"></script>'));
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
    }).catch(async (error) => {
        await server.close();
        throw error;
    });
    async function open(query = '') {
        const page = await browser.newPage();
        page.setDefaultTimeout(8_000);
        page.on('pageerror', (error) => console.error(error.message));
        await page.goto(`http://127.0.0.1:${address.port}/display-read-test${query}`);
        await page.waitForFunction(() => !!window.displayReadHarness);
        return page;
    }
    const snapshot = (page) => page.evaluate(() => window.displayReadHarness.snapshot());
    const waitPending = (page, count) => page.waitForFunction((expected) =>
        window.displayReadHarness.snapshot().pending.length === expected, count);
    try {
        await t.test('cached first view respects TTL and unchanged background reads retain data identity', async () => {
            const page = await open();
            const initial = await snapshot(page);
            assert.equal(initial.data.readToken, 'cached-A');
            assert.equal(initial.editReady, false);
            assert.deepEqual(initial.requestLog, []);
            await page.evaluate(() => window.displayReadHarness.tick());
            await waitPending(page, 1);
            await page.evaluate(() => window.displayReadHarness.resolve({ name: 'Alpha', readToken: 'background-A' }));
            await waitPending(page, 0);
            await page.waitForFunction(() => !window.displayReadHarness.snapshot().refreshing);
            assert.equal((await snapshot(page)).reference, initial.reference);
            await page.close();
        });

        await t.test('editing revokes readiness on its first frame and only a strict new response enables writes', async () => {
            const page = await open();
            const initial = await snapshot(page);
            await page.evaluate(() => window.displayReadHarness.configure({ editing: true }));
            const first = await snapshot(page);
            assert.equal(first.frames.find((frame) => frame.editing).editReady, false);
            assert.equal(first.editReady, false);
            await waitPending(page, 1);
            await page.evaluate(() => window.displayReadHarness.resolve({ name: 'Alpha', readToken: 'editing-A' }));
            await page.waitForFunction(() => window.displayReadHarness.snapshot().editReady);
            const ready = await snapshot(page);
            assert.equal(ready.data.readToken, 'editing-A');
            assert.notEqual(ready.reference, initial.reference);
            await page.evaluate(() => window.displayReadHarness.tick());
            assert.equal((await snapshot(page)).requestLog.length, 1);
            await page.evaluate(() => {
                window.displayReadHarness.configure({ enabled: false });
                window.displayReadHarness.visible(false);
                window.displayReadHarness.tick();
            });
            assert.equal((await snapshot(page)).editReady, false);
            await page.evaluate(() => {
                window.displayReadHarness.configure({ enabled: true });
                window.displayReadHarness.visible(true);
            });
            assert.equal((await snapshot(page)).editReady, true);
            assert.equal((await snapshot(page)).requestLog.length, 1);
            assert.equal((await snapshot(page)).data.readToken, 'editing-A');
            await page.evaluate(() => window.displayReadHarness.configure({ editing: false }));
            assert.equal((await snapshot(page)).editReady, false);
            await page.close();
        });

        await t.test('editing identity changes require a new strict response for the selected record', async () => {
            const page = await open();
            await page.evaluate(() => window.displayReadHarness.configure({ editing: true }));
            await waitPending(page, 1);
            await page.evaluate(() => window.displayReadHarness.configure({ identity: 'B' }));
            await waitPending(page, 2);
            assert.equal((await snapshot(page)).data.readToken, 'cached-B');
            assert.equal((await snapshot(page)).editReady, false);
            await page.evaluate(() => window.displayReadHarness.resolve({ name: 'Late Alpha', readToken: 'edit-A' }));
            await waitPending(page, 1);
            assert.equal((await snapshot(page)).editReady, false);
            assert.equal((await snapshot(page)).data.name, 'Beta');
            await page.evaluate(() => window.displayReadHarness.resolve({ name: 'Beta', readToken: 'edit-B' }));
            await page.waitForFunction(() => window.displayReadHarness.snapshot().editReady);
            assert.equal((await snapshot(page)).data.readToken, 'edit-B');
            await page.close();
        });

        await t.test('disabled initial and manual reads are suppressed and hidden or dirty panels do not poll', async () => {
            const page = await open('?disabled=1');
            await page.evaluate(() => {
                window.displayReadHarness.reload();
                window.displayReadHarness.tick();
            });
            assert.deepEqual((await snapshot(page)).requestLog, []);
            await page.evaluate(() => window.displayReadHarness.configure({ enabled: true }));
            await page.evaluate(() => {
                window.displayReadHarness.visible(false);
                window.displayReadHarness.tick();
            });
            assert.deepEqual((await snapshot(page)).requestLog, []);
            await page.evaluate(() => window.displayReadHarness.configure({ dirty: true }));
            await page.evaluate(() => {
                window.displayReadHarness.visible(true);
                window.displayReadHarness.tick();
            });
            assert.deepEqual((await snapshot(page)).requestLog, []);
            await page.close();
        });

        await t.test('late identity responses are dropped and a failed current refresh preserves cached values', async () => {
            const page = await open();
            await page.evaluate(() => window.displayReadHarness.tick());
            await waitPending(page, 1);
            await page.evaluate(() => window.displayReadHarness.configure({ identity: 'B' }));
            assert.equal((await snapshot(page)).data.name, 'Beta');
            await page.evaluate(() => window.displayReadHarness.resolve({ name: 'Late Alpha', readToken: 'late-A' }));
            await waitPending(page, 0);
            assert.equal((await snapshot(page)).data.name, 'Beta');
            await page.evaluate(() => window.displayReadHarness.tick());
            await waitPending(page, 1);
            await page.evaluate(() => window.displayReadHarness.fail());
            await page.waitForFunction(() => window.displayReadHarness.snapshot().error === 'read unavailable');
            assert.equal((await snapshot(page)).data.name, 'Beta');
            await page.close();
        });
    } finally {
        await browser.close();
        await server.close();
    }
});
