import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {default: puppeteer} = await import(process.env.PUPPETEER_MODULE || 'puppeteer');
const root = process.cwd();
const snapshots = path.join(process.env.TEMP, 'AriesOwnerSnapshots');
const browser = await puppeteer.launch({headless: true});
const page = await browser.newPage();
let current, submitted;
await page.setRequestInterception(true);
page.on('request', async req => {
    const url = new URL(req.url());
    if (url.hostname !== 'owner.test') return req.abort();
    const route = url.pathname;
    if (req.isNavigationRequest()) {
        submitted = url;
        return req.respond({status: 200, contentType: 'text/html', body: await fs.readFile(path.join(snapshots, current + '.html'), 'utf8')});
    }
    if (route.startsWith('/Communications/')) return req.respond({status:200,contentType:'application/json',body:'{"totalUnread":0,"conversations":[]}'});
    try {
        const file = path.join(root, 'wwwroot', route);
        return req.respond({status:200,contentType:route.endsWith('.css')?'text/css':route.endsWith('.js')?'application/javascript':'application/octet-stream',body:await fs.readFile(file)});
    } catch { return req.respond({status:404,body:''}); }
});
const names = ['Bookings-Index','Bookings-StaffIndex','Bookings-MyBookings','RescheduleRequests-Index','History-Index','SystemActivity-Index','TrashHistory-Index','Calendar-StaffIndex','UserManagement-Index','Communications-Index','Payments-PendingVerification','Payments-RefundRequests','Reports-Index'];
try {
    for (const width of [1440, 390]) {
        await page.setViewport({width, height: 900});
        for (current of names) {
            await page.goto('http://owner.test/fixture/' + current, {waitUntil:'networkidle0'});
            assert(await page.$('.am-filter-bar'), current + ' shared filter missing');
            const toggle = await page.$('.am-filter-bar [data-bs-toggle="dropdown"]');
            if (toggle) await toggle.click();
            const problems = await page.evaluate(() => {
                const issues = [];
                document.querySelectorAll('.am-filter-bar').forEach(bar => {
                    const rect = bar.getBoundingClientRect();
                    if (rect.left < -1 || rect.right > innerWidth + 1) issues.push('bar overflow ' + rect.right);
                    bar.querySelectorAll('input:not([type=hidden]), select, .btn, .am-filter-chips button, .dropdown-menu.show').forEach(el => {
                        const r = el.getBoundingClientRect();
                        if (!r.width || !r.height) return;
                        if (r.right > innerWidth + 1 || r.left < -1) issues.push(el.tagName + ' overflow ' + r.right);
                        if (!el.classList.contains('dropdown-menu') && r.height < 43) issues.push(el.tagName + ' too short ' + r.height);
                        if (el.matches('input,select') && !el.labels?.length && !el.getAttribute('aria-label')) issues.push(el.name + ' missing label');
                    });
                });
                return issues;
            });
            assert.deepEqual(problems, [], current + ' at ' + width);
            await page.screenshot({path:path.join(snapshots,'filters-' + current + '-' + width + '.png'),fullPage:true});
            console.log('PASS filter layout, labels and touch targets', current, width);
        }
    }
    await page.setViewport({width:1440,height:900});
    for (current of names.filter(n => !['Bookings-MyBookings','Calendar-StaffIndex'].includes(n))) {
        await page.goto('http://owner.test/fixture/' + current, {waitUntil:'networkidle0'});
        const expected = await page.$eval('form.am-filter-bar', form => {
            const search = form.querySelector('input[type=search],input[type=text]');
            if (search) search.value = 'Fixture & combined';
            form.querySelectorAll('select').forEach(select => { if (select.options.length > 1) select.selectedIndex = 1; });
            form.querySelectorAll('input[type=date]').forEach(input => input.value = '2026-09-17');
            return [...new FormData(form)].map(([k,v]) => [k,String(v)]);
        });
        await Promise.all([page.waitForNavigation({waitUntil:'networkidle0'}),page.$eval('form.am-filter-bar', form => form.requestSubmit())]);
        for (const [key,value] of expected) assert.equal(submitted.searchParams.get(key),value,current + ' preserves ' + key);
        assert(!submitted.searchParams.has('page'), 'new filters should reset pagination');
        const reset = await page.$eval('form.am-filter-bar', form => [...form.querySelectorAll('a')].find(a => a.textContent.trim() === 'Reset')?.getAttribute('href'));
        assert(reset, current + ' reset link missing');
        const params = new URL(reset,'http://owner.test').searchParams;
        assert(!params.has('search') && !params.has('q') && !params.has('Search'), 'reset must clear search');
        console.log('PASS combined GET serialization and reset',current);
    }
    current = 'Bookings-MyBookings';
    await page.goto('http://owner.test/fixture/' + current, {waitUntil:'networkidle0'});
    await page.type('#bookingSearchInput','no-such-package');
    assert(await page.$eval('#bookingEmptyFilterState',el => !el.classList.contains('d-none')));
    await page.click('[data-filter="completed"]');
    assert.equal(new URL(page.url()).searchParams.get('bookingsFilter'),'completed');
    await page.reload({waitUntil:'networkidle0'});
    assert.equal(await page.$eval('#bookingSearchInput',el=>el.value),'no-such-package');
    assert.equal(await page.$eval('[data-filter="completed"]',el=>el.getAttribute('aria-pressed')),'true');
    await page.click('[data-filter-reset]');
    assert.equal(await page.$eval('#bookingSearchInput',el=>el.value),'');
    assert.equal(await page.$eval('[data-filter="all"]',el=>el.getAttribute('aria-pressed')),'true');
    assert(!new URL(page.url()).searchParams.has('bookingsSearch'));
    assert(await page.$eval('#bookingEmptyFilterState',el=>el.classList.contains('d-none')));
    console.log('PASS client combined filters, empty state, refresh, pressed state and reset');
    current = 'Calendar-StaffIndex';
    await page.goto('http://owner.test/fixture/' + current, {waitUntil:'networkidle0'});
    await page.type('#upcomingSearch','Fixture Venue');
    await page.click('[data-package-filter="Other Package"]');
    assert(await page.$eval('#upcomingFilteredEmpty',el=>!el.classList.contains('d-none')));
    await page.reload({waitUntil:'networkidle0'});
    assert.equal(await page.$eval('#upcomingSearch',el=>el.value),'Fixture Venue');
    assert.equal(await page.$eval('[data-package-filter="Other Package"]',el=>el.getAttribute('aria-pressed')),'true');
    await page.click('[data-filter-reset]');
    assert(await page.$eval('#upcomingFilteredEmpty',el=>el.classList.contains('d-none')));
    console.log('PASS calendar combined filters, refresh, empty state and reset');
} finally { await browser.close(); }

