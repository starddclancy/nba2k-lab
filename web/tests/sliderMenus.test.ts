import assert from 'node:assert/strict';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createServer } from 'vite';
import { mkdir } from 'node:fs/promises';

let chromium;
try {
    ({ chromium } = await import(process.env.DISPLAY_READ_PLAYWRIGHT
        ? pathToFileURL(process.env.DISPLAY_READ_PLAYWRIGHT).href : 'playwright'));
} catch { /* Uses the installed optional browser test runtime. */ }

test('player and GS slider menus keep drafts, ranges and edit gates', { skip: !chromium }, async (t) => {
    const server = await createServer({
        configFile: false,
        cacheDir: 'node_modules/.vite-slider-menus-tests',
        root: fileURLToPath(new URL('../', import.meta.url)),
        server: { host: '127.0.0.1', port: 0, hmr: false },
        optimizeDeps: { noDiscovery: true, include: [
            'react', 'react/jsx-runtime', 'react/jsx-dev-runtime', 'react-dom/client', 'react-dom',
            'antd', '@ant-design/icons',
        ] },
        plugins: [{
            name: 'slider-menus-test-page',
            configureServer(vite) {
                vite.middlewares.use('/slider-menus-test', async (_request, response) => {
                    response.setHeader('Content-Type', 'text/html');
                    response.end(await vite.transformIndexHtml('/slider-menus-test',
                        '<div id="root"></div><script type="module" src="/tests/fixtures/sliderMenusHarness.tsx"></script>'));
                });
            },
        }],
    });
    await server.listen();
    const address = server.httpServer!.address();
    assert.ok(address && typeof address !== 'string');
    const browser = await chromium.launch({ channel: process.platform === 'win32' ? 'msedge' : undefined, headless: true })
        .catch(async (error) => { await server.close(); throw error; });
    async function open(view = 'player') {
        const page = await browser.newPage({ viewport: { width: 1440, height: 940 } });
        page.on('pageerror', (error) => console.error(error.message));
        page.setDefaultTimeout(15_000);
        await page.goto(`http://127.0.0.1:${address.port}/slider-menus-test`);
        await page.waitForFunction(() => !!window.sliderMenusHarness);
        if (view === 'gs') {
            await page.evaluate(() => window.sliderMenusHarness.configure({ view: 'gs' }));
            await page.getByText('三分命中率', { exact: true }).waitFor();
        } else {
            await page.getByRole('tab', { name: /^能力值/ }).click();
        }
        return page;
    }
    const calls = (page, command) => page.evaluate((value) =>
        window.sliderMenusHarness.snapshot().calls.filter((call) => call.command === value), command);
    try {
        await t.test('player sliders obey the schema range, stay local, and synchronize the number input', async () => {
            const page = await open();
            assert.equal(await page.getByRole('slider').count(), 0);
            await page.evaluate(() => window.sliderMenusHarness.configure({ editing: true }));
            const row = page.locator('[data-row-key="Attributes/Offense/3POINT"]');
            const slider = row.getByRole('slider');
            await slider.waitFor();
            assert.equal(await slider.getAttribute('aria-valuemin'), '25');
            assert.equal(await slider.getAttribute('aria-valuemax'), '110');
            await slider.focus();
            await slider.press('End');
            assert.equal(await row.getByRole('spinbutton').inputValue(), '110');
            await row.getByRole('spinbutton').fill('92');
            await row.getByRole('spinbutton').blur();
            assert.equal(await slider.getAttribute('aria-valuenow'), '92');
            assert.equal(await page.locator('[data-row-key="Attributes/Offense/FREETHROW"]').getByRole('slider').count(), 0);
            assert.deepEqual(await calls(page, 'applyPlayer'), []);
            await page.getByRole('tab', { name: /^球员倾向/ }).click();
            await page.getByRole('tab', { name: /^能力值/ }).click();
            assert.equal(await row.getByRole('spinbutton').inputValue(), '92');
            await page.evaluate(() => window.sliderMenusHarness.configure({ editing: false }));
            await row.getByRole('slider').waitFor({ state: 'hidden' });
            assert.equal(await row.locator('.field-control').textContent(), '85');
            await page.close();
        });
        await t.test('GS groups search and independently stage user and CPU values before applying', async () => {
            const page = await open('gs');
            assert.equal(await page.getByRole('slider').count(), 0);
            await page.evaluate(() => window.sliderMenusHarness.configure({ editing: true }));
            const row = page.locator('[data-gs-field="three_point"]');
            const user = row.getByRole('slider', { name: '三分命中率 用户滑块', exact: true });
            const cpu = row.getByRole('slider', { name: '三分命中率 电脑滑块', exact: true });
            await user.waitFor();
            await user.focus();
            await user.press('Home');
            await cpu.focus();
            await cpu.press('End');
            assert.equal(await row.getByRole('spinbutton', { name: '三分命中率 用户', exact: true }).inputValue(), '0');
            assert.equal(await row.getByRole('spinbutton', { name: '三分命中率 电脑', exact: true }).inputValue(), '100');
            const absent = page.locator('[data-gs-field="block"]');
            assert.equal(await absent.getByRole('slider', { name: '盖帽 用户滑块', exact: true }).count(), 0);
            await page.getByRole('button', { name: /^防守.*1/ }).click();
            assert.equal(await page.locator('[data-gs-field]').count(), 1);
            await page.getByRole('button', { name: /^全部.*4/ }).click();
            await page.getByPlaceholder('搜索 GS 项目').fill('三分');
            assert.equal(await page.locator('[data-gs-field]').count(), 1);
            await page.getByPlaceholder('搜索 GS 项目').fill('');
            assert.equal(await page.locator('[data-gs-field]').count(), 4);
            assert.equal(await user.getAttribute('aria-valuenow'), '0');
            assert.equal(await cpu.getAttribute('aria-valuenow'), '100');
            assert.deepEqual(await calls(page, 'gsApply'), []);
            await page.getByRole('button', { name: '应用 GS 改动', exact: true }).click();
            await page.waitForFunction(() => window.sliderMenusHarness.snapshot().calls.some((call) => call.command === 'gsApply'));
            assert.deepEqual((await calls(page, 'gsApply'))[0].payload, { changes: {
                'user.three_point': 0, 'cpu.three_point': 100,
            } });
            await page.close();
        });
        await t.test('changing GS source waits for its own read and routes only to the current source', async () => {
            const page = await open('gs');
            await page.evaluate(() => window.sliderMenusHarness.configure({ editing: true }));
            await page.getByRole('slider').first().waitFor();
            await page.evaluate(() => window.sliderMenusHarness.hold(true));
            await page.getByText('当前比赛设置', { exact: true }).click();
            await page.waitForFunction(() => window.sliderMenusHarness.snapshot().pending === 1);
            assert.equal(await page.getByRole('button', { name: '应用 GS 改动', exact: true }).isDisabled(), true);
            assert.equal(await page.locator('[role=slider]:not([aria-disabled=true])').count(), 0);
            await page.evaluate(() => {
                window.sliderMenusHarness.hold(false);
                window.sliderMenusHarness.resolve();
            });
            const user = page.getByRole('slider', { name: '三分命中率 用户滑块', exact: true });
            await user.waitFor();
            assert.equal(await user.getAttribute('aria-valuenow'), '55');
            await user.focus();
            await user.press('ArrowRight');
            await page.getByRole('button', { name: '应用 GS 改动', exact: true }).click();
            await page.waitForFunction(() => window.sliderMenusHarness.snapshot().calls.some((call) => call.command === 'inMatchApply'));
            assert.deepEqual((await calls(page, 'inMatchApply'))[0].payload, { changes: { 'user.three_point': 56 } });
            assert.deepEqual(await calls(page, 'gsApply'), []);
            await page.close();
        });
        await t.test('unloaded in-match GS explains its state and cannot create edits', async () => {
            const page = await open('gs');
            await page.evaluate(() => {
                window.sliderMenusHarness.inMatchUnavailable();
                window.sliderMenusHarness.configure({ editing: true });
            });
            await page.getByText('当前比赛设置', { exact: true }).click();
            await page.getByText('当前比赛设置尚未加载，请进入比赛后重新读取。', { exact: true }).waitFor();
            assert.equal(await page.getByRole('slider').count(), 0);
            assert.equal(await page.getByRole('button', { name: '应用 GS 改动', exact: true }).isDisabled(), true);
            assert.equal(await page.getByRole('button', { name: '保存当前值', exact: true }).isDisabled(), true);
            assert.deepEqual(await calls(page, 'inMatchApply'), []);
            await page.close();
        });
        await t.test('slider layouts fit desktop and compact views with usable drag tracks', async () => {
            for (const view of ['player', 'gs']) {
                const page = await open(view);
                await page.evaluate(() => window.sliderMenusHarness.configure({ editing: true }));
                await page.getByRole('slider').first().waitFor();
                for (const width of [1440, 900, 640]) {
                    await page.setViewportSize({ width, height: 940 });
                    const layout = await page.evaluate(() => {
                        const sliders = [...document.querySelectorAll('.numeric-slider')];
                        return {
                            overflow: document.documentElement.scrollWidth > innerWidth + 1,
                            controls: sliders.map((element) => {
                                const track = element.querySelector('.ant-slider').getBoundingClientRect();
                                const number = element.querySelector('.ant-input-number').getBoundingClientRect();
                                return { width: track.width, separated: track.right <= number.left + 1 };
                            }),
                        };
                    });
                    assert.equal(layout.overflow, false, `${view} fits ${width}`);
                    assert.ok(layout.controls.length > 0);
                    assert.ok(layout.controls.every((item) => item.width >= 45 && item.separated), `${view} usable tracks ${width}`);
                }
                await page.close();
            }
        });
        await t.test('the player header applies ordinary sections with their own fresh player token', async () => {
            const page = await open();
            await page.evaluate(() => window.sliderMenusHarness.configure({ editing: true }));
            for (const sample of [
                { tab: '基本资料', key: 'Vitals/General/JERSEYNUMBER', next: '8' },
                { tab: '身高与体型', key: 'Vitals/Body/HEIGHT', next: '205' },
                { tab: '合同', key: 'Contract/Salary/SALARYYEAR1', next: '2000000' },
                { tab: '球员倾向', key: 'Tendencies/Shooting/SHOT3PT', next: '65' },
            ]) {
                await page.getByRole('tab', { name: new RegExp(`^${sample.tab}`) }).click();
                const row = page.locator(`[data-row-key="${sample.key}"]`);
                await row.getByRole('spinbutton').fill(sample.next);
                await row.getByRole('spinbutton').blur();
                const button = page.locator('.fe-bar').getByRole('button', { name: /^应用/ });
                assert.equal(await button.count(), 1, `${sample.tab}: ${await page.locator('.fe-bar').innerText()}`);
                assert.equal(await button.isEnabled(), true, sample.tab);
                const before = (await calls(page, 'applyPlayer')).length;
                await button.click();
                await page.waitForFunction((count) => window.sliderMenusHarness.snapshot().calls
                    .filter((call) => call.command === 'applyPlayer').length === count + 1, before);
                const request = (await calls(page, 'applyPlayer')).at(-1).payload;
                assert.deepEqual(request.changes, { [sample.key]: Number(sample.next) });
                assert.match(request.readToken, /^slider-test:/);
                await page.waitForFunction(() => !document.querySelector('.fe-bar .ant-btn-loading'));
            }
            await page.close();
        });
        await t.test('the player header applies statistics, career highs and awards with their statistics token', async () => {
            const page = await open();
            await page.getByRole('tab', { name: '数据与荣誉', exact: true }).click();
            await page.locator('[data-row-key="SeasonStats/CURRENTYEARSTATID/Scoring & Impact/POINTS"]').waitFor();
            await page.evaluate(() => window.sliderMenusHarness.configure({ editing: true }));
            const topApply = page.locator('.fe-bar').getByRole('button', { name: /^应用/ });
            for (const sample of [
                { tab: '赛季累计与场均', key: 'SeasonStats/CURRENTYEARSTATID/Scoring & Impact/POINTS', next: '1200' },
                { tab: '单场最高', key: 'Stats/Career High Stats/POINTS', next: '60' },
                { tab: '奖项与累计荣誉', key: 'Vitals/Awards/PLAYEROFMONTH', next: '4' },
            ]) {
                await page.locator('.statistics-editor').getByRole('tab', { name: sample.tab, exact: true }).click();
                const row = page.locator(`[data-row-key="${sample.key}"]`);
                await row.getByRole('spinbutton').fill(sample.next);
                await row.getByRole('spinbutton').blur();
                assert.equal(await topApply.isEnabled(), true, sample.tab);
                const before = (await calls(page, 'applyPlayer')).length;
                await topApply.click();
                await page.waitForFunction((count) => window.sliderMenusHarness.snapshot().calls
                    .filter((call) => call.command === 'applyPlayer').length === count + 1, before);
                const request = (await calls(page, 'applyPlayer')).at(-1).payload;
                assert.deepEqual(request.changes, { [sample.key]: Number(sample.next) });
                assert.match(request.readToken, /^statistics-test:/);
                await page.waitForFunction(() => !document.querySelector('.fe-bar .ant-btn-loading'));
                assert.equal(await topApply.isDisabled(), true);
            }
            assert.equal(await page.locator('.statistics-toolbar').getByRole('button', { name: /^应用/ }).count(), 0);
            await page.evaluate(() => window.sliderMenusHarness.configure({ editing: false }));
            assert.equal(await topApply.isDisabled(), true);
            await page.close();
        });
        await t.test('statistics header actions revoke drafts on edit changes and wait for a fresh statistics read', async () => {
            const page = await open();
            await page.getByRole('tab', { name: '数据与荣誉', exact: true }).click();
            const row = page.locator('[data-row-key="SeasonStats/CURRENTYEARSTATID/Scoring & Impact/POINTS"]');
            await row.waitFor();
            await page.evaluate(() => {
                window.sliderMenusHarness.hold(true);
                window.sliderMenusHarness.configure({ editing: true });
            });
            await page.waitForFunction(() => window.sliderMenusHarness.snapshot().pending === 1);
            const topApply = page.locator('.fe-bar').getByRole('button', { name: '应用统计修改', exact: true });
            assert.equal(await topApply.isDisabled(), true);
            assert.equal(await row.getByRole('spinbutton').isDisabled(), true);
            await page.evaluate(() => window.sliderMenusHarness.resolve());
            await row.getByRole('spinbutton').fill('1200');
            await row.getByRole('spinbutton').blur();
            assert.equal(await topApply.isEnabled(), true);
            assert.match(await topApply.innerText(), /\(1\)/);
            await page.evaluate(() => window.sliderMenusHarness.configure({ editing: false }));
            assert.equal(await topApply.isDisabled(), true);
            assert.doesNotMatch(await topApply.innerText(), /\(1\)/);
            await page.evaluate(() => window.sliderMenusHarness.configure({ editing: true }));
            await page.waitForFunction(() => window.sliderMenusHarness.snapshot().pending === 1);
            assert.equal(await topApply.isDisabled(), true);
            await page.evaluate(() => window.sliderMenusHarness.resolve());
            await row.getByRole('spinbutton').fill('1300');
            await row.getByRole('spinbutton').blur();
            assert.equal(await topApply.isEnabled(), true);
            await page.getByRole('tab', { name: /^基本资料/ }).click();
            assert.equal(await page.locator('.fe-bar').getByRole('button', { name: '应用统计修改', exact: true }).count(), 0);
            assert.deepEqual(await calls(page, 'applyPlayer'), []);
            await page.close();
        });
        await t.test('statistics drafts survive category round trips with their original read token', async () => {
            const page = await open();
            await page.evaluate(() => window.sliderMenusHarness.configure({ editing: true }));
            await page.getByRole('tab', { name: '数据与荣誉', exact: true }).click();
            const row = page.locator('[data-row-key="SeasonStats/CURRENTYEARSTATID/Scoring & Impact/POINTS"]');
            await row.getByRole('spinbutton').fill('1200');
            await row.getByRole('spinbutton').blur();
            const reads = (await calls(page, 'readPlayerStats')).length;
            const playerReads = (await calls(page, 'readPlayer')).length;
            await page.getByRole('tab', { name: /^基本资料/ }).click();
            await page.getByPlaceholder('搜索球员资料').fill('no matching field');
            await page.getByPlaceholder('搜索球员资料').fill('');
            await page.getByRole('tab', { name: /^数据与荣誉/ }).click();
            assert.equal(await row.getByRole('spinbutton').inputValue(), '1200');
            assert.equal((await calls(page, 'readPlayerStats')).length, reads);
            await page.getByRole('button', { name: '应用统计修改', exact: true }).click();
            await page.waitForFunction(() => window.sliderMenusHarness.snapshot().calls.some((call) => call.command === 'applyPlayer'));
            const request = (await calls(page, 'applyPlayer'))[0].payload;
            assert.equal(request.readToken, `statistics-test:${playerReads + reads}`);
            assert.deepEqual(request.changes, { 'SeasonStats/CURRENTYEARSTATID/Scoring & Impact/POINTS': 1200 });
            await page.close();
        });
        await t.test('pending changes show before and after, reset individually, and ignore restored baseline values', async () => {
            const page = await open();
            await page.evaluate(() => window.sliderMenusHarness.configure({ editing: true }));
            const row = page.locator('[data-row-key="Attributes/Offense/3POINT"]');
            await row.getByRole('spinbutton').fill('92');
            await row.getByRole('spinbutton').blur();
            const pending = page.getByRole('region', { name: '球员待应用修改', exact: true });
            await pending.waitFor();
            assert.match(await pending.innerText(), /85/);
            assert.match(await pending.innerText(), /92/);
            await pending.getByRole('button', { name: /^还原 / }).click();
            assert.equal(await row.getByRole('spinbutton').inputValue(), '85');
            await pending.waitFor({ state: 'hidden' });
            await row.getByRole('spinbutton').fill('92');
            await row.getByRole('spinbutton').blur();
            await row.getByRole('spinbutton').fill('85');
            await row.getByRole('spinbutton').blur();
            await pending.waitFor({ state: 'hidden' });
            assert.equal(await page.getByRole('button', { name: '应用球员修改', exact: true }).isDisabled(), true);
            assert.deepEqual(await calls(page, 'applyPlayer'), []);
            await page.close();
        });
        await t.test('refresh and close protect local drafts; cancelling keeps values and sends no writes', async () => {
            const page = await open();
            await page.evaluate(() => window.sliderMenusHarness.configure({ editing: true }));
            const row = page.locator('[data-row-key="Attributes/Offense/3POINT"]');
            await row.getByRole('spinbutton').fill('92');
            await row.getByRole('spinbutton').blur();
            const before = (await calls(page, 'readPlayer')).length;
            await page.getByRole('button', { name: '刷新球员资料', exact: true }).click();
            await page.getByRole('dialog').getByRole('button', { name: '保留草稿', exact: true }).click();
            assert.equal((await calls(page, 'readPlayer')).length, before);
            assert.equal(await row.getByRole('spinbutton').inputValue(), '92');
            await page.getByRole('button', { name: '关闭', exact: true }).click();
            await page.getByRole('dialog').getByRole('button', { name: '保留草稿', exact: true }).click();
            assert.equal(await page.evaluate(() => window.sliderMenusHarness.snapshot().closes), 0);
            await page.getByRole('button', { name: '刷新球员资料', exact: true }).click();
            await page.getByRole('dialog').getByRole('button', { name: '放弃草稿', exact: true }).click();
            await page.getByRole('region', { name: '球员待应用修改', exact: true }).waitFor({ state: 'hidden' });
            assert.equal(await row.getByRole('spinbutton').inputValue(), '85');
            assert.deepEqual(await calls(page, 'applyPlayer'), []);
            await page.close();
        });
        await t.test('journals display readable changes and explicit unsupported or invalid states', async () => {
            const page = await open();
            await page.evaluate(() => window.sliderMenusHarness.configure({ view: 'journals' }));
            const row = page.getByRole('row').filter({ hasText: 'edit.json' });
            await row.getByRole('button').click();
            const detail = page.getByRole('region', { name: '事务详情', exact: true });
            await detail.waitFor();
            assert.match(await detail.innerText(), /Slider Sample/);
            assert.match(await detail.innerText(), /球衣号码/);
            assert.match(await detail.innerText(), /7/);
            assert.match(await detail.innerText(), /8/);
            await page.getByRole('row').filter({ hasText: 'preset.json' }).getByRole('button').click();
            await page.getByText('此文件格式暂不提供事务详情', { exact: true }).waitFor();
            await page.getByRole('row').filter({ hasText: 'broken.json' }).getByRole('button').click();
            await page.getByText('Invalid JSON', { exact: true }).waitFor();
            assert.deepEqual(await calls(page, 'applyPlayer'), []);
            await page.close();
        });
        await t.test('draft previews and transaction details fit desktop and narrow windows', async () => {
            const page = await open();
            await page.evaluate(() => window.sliderMenusHarness.configure({ editing: true }));
            const row = page.locator('[data-row-key="Attributes/Offense/3POINT"]');
            await row.getByRole('spinbutton').fill('92');
            await row.getByRole('spinbutton').blur();
            const directory = process.env.EDITOR_EVIDENCE_DIR;
            if (directory) await mkdir(directory, { recursive: true });
            for (const width of [1440, 640]) {
                await page.setViewportSize({ width, height: 940 });
                assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
                assert.equal(await page.getByRole('region', { name: '球员待应用修改' }).isVisible(), true);
                if (directory) await page.screenshot({ path: `${directory}/pending-${width}.png` });
            }
            await page.evaluate(() => window.sliderMenusHarness.configure({ view: 'journals' }));
            await page.getByRole('row').filter({ hasText: 'edit.json' }).getByRole('button').click();
            for (const width of [1440, 640]) {
                await page.setViewportSize({ width, height: 940 });
                assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
                if (directory) await page.screenshot({ path: `${directory}/journal-${width}.png` });
            }
            await page.close();
        });
        await t.test('drawer dismissal, entity navigation and module navigation protect both draft types', async () => {
            const page = await open();
            const decide = async (name: string) => {
                await page.getByRole('button', { name, exact: true }).click();
                await page.locator('.ant-modal-confirm').waitFor({ state: 'hidden' });
            };
            await page.evaluate(() => window.sliderMenusHarness.configure({ view: 'shell' }));
            assert.match(await page.locator('body').innerText(), /连接游戏/);
            await page.locator('.shell-status button').filter({ hasText: '连接游戏' }).click();
            await page.locator('.players-table').getByText('Slider Sample 2', { exact: true }).dblclick();
            const drawer = page.locator('.player-drawer');
            await drawer.getByRole('switch', { name: '编辑开关' }).click();
            const jersey = drawer.locator('[data-row-key="Vitals/General/JERSEYNUMBER"]');
            await jersey.getByRole('spinbutton').fill('8');
            await jersey.getByRole('spinbutton').blur();
            await drawer.getByRole('tab', { name: '数据与荣誉', exact: true }).click();
            const points = drawer.locator('[data-row-key="SeasonStats/CURRENTYEARSTATID/Scoring & Impact/POINTS"]');
            await points.getByRole('spinbutton').fill('1200');
            await points.getByRole('spinbutton').blur();
            await points.getByRole('spinbutton').focus();
            await page.keyboard.press('Escape');
            await decide('保留草稿');
            assert.equal(await points.getByRole('spinbutton').inputValue(), '1200');
            await page.locator('.ant-drawer-mask').click({ position: { x: 20, y: 100 } });
            await decide('保留草稿');
            await page.evaluate(() => { window.location.hash = '/players/roster/3'; });
            await decide('保留草稿');
            await page.waitForFunction(() => window.location.hash === '#/players/roster/2');
            assert.equal(await points.getByRole('spinbutton').inputValue(), '1200');
            await page.evaluate(() => { window.location.hash = '/journals'; });
            await decide('保留草稿');
            await page.waitForFunction(() => window.location.hash === '#/players/roster/2');
            await drawer.getByRole('tab', { name: /^基本资料/ }).click();
            assert.equal(await jersey.getByRole('spinbutton').inputValue(), '8');
            await drawer.getByRole('button', { name: '关闭', exact: true }).click();
            await decide('放弃草稿');
            await drawer.waitFor({ state: 'hidden' });
            assert.equal(await page.getByRole('dialog').count(), 0);
            await page.locator('.players-table').getByText('Slider Sample 2', { exact: true }).dblclick();
            await drawer.getByRole('switch', { name: '编辑开关' }).click();
            await jersey.getByRole('spinbutton').fill('9');
            await jersey.getByRole('spinbutton').blur();
            await page.evaluate(() => { window.location.hash = '/journals'; });
            await decide('放弃草稿');
            await page.getByText('操作记录', { exact: true }).first().waitFor();
            await drawer.waitFor({ state: 'hidden' });
            assert.deepEqual(await calls(page, 'applyPlayer'), []);
            await page.close();
        });
    } finally {
        await browser.close();
        await server.close();
    }
});
