import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {default: puppeteer} = await import(process.env.PUPPETEER_MODULE || '../../.tmp-owner-build/puppeteer-with-runtime.mjs');

const baseline = false;
const output = '.tmp-owner-build/owner-round3/after';
await fs.mkdir(output, {recursive: true});
const browser = await puppeteer.launch({headless: true, executablePath: process.env.PUPPETEER_EXECUTABLE_PATH || 'C:/Program Files/BraveSoftware/Brave-Browser/Application/brave.exe'});
const pages = ['Dashboard-Owner', 'Payments-PendingVerification', 'Payments-Verify', 'Payments-RefundRequests', 'Payments-RefundReview', 'Reports-Index', 'Personnel-Index', 'InternalProfile-Index', 'History-Index', 'History-Details', 'TrashHistory-Index', 'TrashHistory-Details', 'Communications-Index', 'Notifications-Index', 'Calendar-StaffIndex-Owner', 'Calendar-Settings', 'Bookings-StaffDetails-Owner'];
pages.push('Payments-PendingVerification-paged', 'Payments-PendingVerification-empty', 'Payments-RefundRequests-paged', 'Payments-RefundRequests-empty',
  'Services-Index', 'Services-Index-Staff', 'Bookings-StaffIndex', 'Bookings-MyBookings', 'Bookings-MyBookings-empty',
  'RescheduleRequests-Index', 'Calendar-StaffIndex', 'SystemActivity-Index');
let current;
const results = [], errors = [];
try {
    const page = await browser.newPage();
    page.on('pageerror', error => errors.push({page: current, error: error.message}));
    await page.setRequestInterception(true);
    page.on('request', async request => {
        try {
            const url = new URL(request.url());
            if (url.hostname === 'cdn.jsdelivr.net' && url.pathname.includes('fullcalendar')) return request.respond({contentType: 'application/javascript', body: await fs.readFile(path.join(process.env.TEMP, 'aries-fullcalendar.js'))});
            if (url.hostname === 'cdn.jsdelivr.net' && url.pathname.includes('chart.js')) return request.respond({contentType: 'application/javascript', body: await fs.readFile('.tmp-owner-build/owner-round2/chart.umd.min.js')});
            if (url.hostname !== 'owner.test') return request.abort();
            if (request.isNavigationRequest()) return request.respond({contentType: 'text/html', body: await fs.readFile(path.join(process.env.TEMP, 'AriesOwnerSnapshots', current + '.html'))});
            if (/^\/Payments\/(PaymentProof|RefundProof)/.test(url.pathname)) return request.respond({contentType: 'image/png', body: await fs.readFile('tests/ReleaseIntegration/Fixtures/payment.png')});
            if (url.pathname.startsWith('/Communications/')) return request.respond({contentType: 'application/json', body: '{"totalUnread":0,"conversations":[]}'});
            const file = path.resolve('wwwroot', '.' + url.pathname);
            assert(file.startsWith(path.resolve('wwwroot') + path.sep));
            const extension = path.extname(file);
            const types = {'.css': 'text/css', '.js': 'application/javascript', '.woff2': 'font/woff2', '.png': 'image/png', '.jpeg': 'image/jpeg', '.svg': 'image/svg+xml'};
            return request.respond({contentType: types[extension] || 'application/octet-stream', body: await fs.readFile(file)});
        } catch { return request.respond({status: 404, body: ''}); }
    });
    const manifest = JSON.parse(await fs.readFile(path.join(process.env.TEMP, 'AriesOwnerSnapshots/current-fixtures.json')));
    for (const width of [1440, 1024, 768, 390, 320]) for (const name of pages) {
        assert(manifest.includes(name + '.html'), 'Current fixture required: ' + name);
        current = name;
        await page.setViewport({width, height: 900});
        await page.goto('http://owner.test/' + name, {waitUntil: 'networkidle0'});
        await page.evaluate(async () => { await document.fonts.ready; });
        await page.evaluate(() => {
            for (const canvas of document.querySelectorAll('canvas')) {
                const chart = window.Chart?.getChart(canvas);
                if (chart) { chart.stop(); chart.update('none'); }
            }
        });
        await page.addScriptTag({path: process.env.AXE_CORE_PATH || '.tmp-owner-build/browser-tools/node_modules/axe-core/axe.min.js'});
        const axe = await page.evaluate(() => window.axe.run(document, {runOnly: {type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa']}}));
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1);
        results.push({name, width, overflow, violations: axe.violations.map(v => ({id: v.id, targets: v.nodes.map(n => n.target)}))});
        await page.screenshot({path: `${output}/${name}-${width}.png`});
        if (!baseline) {
            assert(!overflow, `${name} overflow at ${width}`);
            assert.equal(axe.violations.length, 0, JSON.stringify(results.at(-1)));
            assert.equal(await page.$$eval('.sidebar-nav a[href*="/Calendar/Settings"]', nodes => nodes.length), 0);
            if (name === 'Calendar-StaffIndex-Owner') assert.equal(await page.$$eval('a[href="/Calendar/Settings"]', nodes => nodes.length), 1);
            if (name === 'Dashboard-Owner') {
                await page.focus('#ownerPaymentsTab'); await page.keyboard.press('ArrowRight');
                assert.equal(await page.$eval('#ownerRefundsTab', node => node.getAttribute('aria-selected')), 'true');
                assert.equal(await page.$eval('#ownerPaymentsPanel', node => node.hidden), true);
                await page.keyboard.press('Home');
                if (width < 768) {
                    await page.click('#accountMenu');
                    assert(await page.$eval('.internal-account-menu a[href="/Communications/Index"]', node => node.getBoundingClientRect().width > 0), 'Owner mobile account menu keeps Messages reachable');
                    await page.keyboard.press('Escape');
                    assert.equal(await page.$eval('#accountMenu', node => node.getAttribute('aria-expanded')), 'false');
                }
            }
            if (name === 'Personnel-Index') {
                await page.type('#ownerPersonnelSearch', 'staff@example.test');
                assert.equal(await page.$$eval('#ownerPersonnelTable tbody tr', rows => rows.filter(row => !row.hidden).length), 1);
                await page.$eval('#ownerPersonnelSearch', node => {node.value = ''; node.dispatchEvent(new Event('input'));});
                assert.deepEqual(await page.$$eval('#personRole option', nodes => nodes.map(node => node.value)), ['Staff']);
                if (width >= 1200) assert.equal(await page.$eval('.owner-account-filters', node => getComputedStyle(node).gridTemplateColumns.split(' ').length), 5, 'Personnel filters stay compact on desktop');
            }
            if (name.startsWith('Payments-PendingVerification') || name.startsWith('Payments-RefundRequests')) {
                assert.equal(await page.$$eval('.aries-five-pager', nodes => nodes.length), 1, 'Single shared five-record pager');
                assert.equal(await page.$$eval('#queuePageSize', nodes => nodes.length), 0, 'No page-size selector');
                assert.equal(await page.$$eval('.bi-chevron-double-left,.bi-chevron-double-right', nodes => nodes.length), 0, 'No double arrows');
                assert.equal(await page.$$eval('a[data-queue-refresh]', nodes => nodes.length), 1);
                if (name.endsWith('-paged')) {
                    const next = new URL(await page.$eval('a[aria-label="Next page"]', node => node.href));
                    assert.equal(next.searchParams.get('pageSize'), '5');
                    assert.equal(next.searchParams.get('q'), 'Fixture & queue');
                    const refresh = new URL(await page.$eval('[data-queue-refresh]', node => node.href));
                    assert.equal(refresh.searchParams.get('q'), 'Fixture & queue');
                }
            }
            if (name === 'Dashboard-Owner') {
                assert.equal(await page.$$eval('.owner-round3-metric', nodes => nodes.length), 3, 'Three financial totals');
                assert.equal(await page.$$eval('.owner-round3-upcoming', nodes => nodes.length), 1, 'Upcoming events sidebar');
            }
            if (name === 'Calendar-StaffIndex-Owner' || name === 'Calendar-StaffIndex') {
                assert.equal(await page.$$eval('#upcomingSearch, #staffUpcomingPageSize', nodes => nodes.length), 0,
                    'Calendar booking search and page-size selector removed');
                assert.equal(await page.$$eval('#staffUpcomingPrev,#staffUpcomingPageInfo,#staffUpcomingNext', nodes => nodes.length), 3,
                    'Calendar single-arrow paging controls present');
            }
            if (['Bookings-StaffIndex','RescheduleRequests-Index','SystemActivity-Index','History-Index',
                 'TrashHistory-Index','Reports-Index'].includes(name)) {
                assert.equal(await page.$$eval('.aries-five-pager', nodes => nodes.length), 1,
                    'Shared five-record navigation present');
            }
            if (name === 'Bookings-MyBookings' || name === 'Bookings-MyBookings-empty') {
                assert.equal(await page.$$eval('.aries-client-five-pager', nodes => nodes.length), 1);
                assert.equal(await page.$$eval('#bookingStatusFilter option', nodes => nodes.length) >= 10, true,
                    'Client-specific status filters retained');
                const count = await page.$$eval('#clientBookingList .cr-booking-item', nodes => nodes.filter(n => !n.hidden).length);
                assert(count <= 5, 'Client My Bookings has at most five displayed cards');
            }
            if (name === 'Reports-Index') {
                assert.equal(await page.$$eval('.owner-report-metrics > article', nodes => nodes.length), 4);
                assert.equal(await page.$$eval('.owner-chart-grid > article', nodes => nodes.length), 3);
                const columns = await page.$eval('.owner-chart-grid', node => getComputedStyle(node).gridTemplateColumns.split(' ').length);
                assert.equal(columns, width >= 1200 ? 3 : 1, 'Financial charts use the intended responsive columns');
                assert.equal(await page.$$eval('canvas', nodes => nodes.filter(node => window.Chart?.getChart(node)).length), 5, 'All existing charts initialize');
                await page.click('.owner-operational-details summary');
                await page.waitForFunction(() => document.querySelector('#monthlyBookingsChart').getBoundingClientRect().width > 0);
                await page.click('.owner-operational-details summary');
            }
        }
        if ([1440, 390].includes(width)) {
            const height = await page.evaluate(() => Math.max(900, document.querySelector('#mainContent').scrollHeight + 76));
            await page.setViewport({width, height});
            await page.screenshot({path: `${output}/${name}-${width}-complete.png`});
        }
        console.log('PASS', baseline ? 'baseline' : 'updated', name, width);
    }
    if (!baseline) assert.deepEqual(errors, [], 'No JavaScript exceptions');
} finally {
    await fs.writeFile(`${output}/results.json`, JSON.stringify({results, errors}, null, 2));
    await browser.close();
}
console.log('COMPLETE', results.length, 'page/viewport cases');
