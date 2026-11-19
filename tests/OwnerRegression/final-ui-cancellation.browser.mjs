import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';

const {default: puppeteer} = await import(process.env.PUPPETEER_MODULE || 'puppeteer');
const evidence = '.tmp-owner-build/final-ui-browser';
const fixture = JSON.parse(await fs.readFile(path.join(evidence, 'business-host.json'), 'utf8'));
const origin = new URL(fixture.url).origin;
assert.equal(new URL(origin).hostname, '127.0.0.1');
const browser = await puppeteer.launch({headless: true, executablePath: process.env.PUPPETEER_EXECUTABLE_PATH});
const bookingId = fixture.bookings.cancel;
const modal = '#ownerCancelBooking-' + bookingId;
const reason = 'The performer cannot attend this synthetic event.';
let checks = 0, passed = false;
const check = (value, description) => { assert.ok(value, description); checks++; console.log('PASS', description); };

async function visit(page, route) {
  const response = await page.goto(origin + route, {waitUntil: 'domcontentloaded'});
  check(response.status() === 200 && new URL(page.url()).pathname !== '/Account/Login', route + ' is usable by its signed-in role');
}
async function signIn(role) {
  const context = await browser.createBrowserContext();
  const page = await context.newPage();
  await page.setViewport({width: 1280, height: 900});
  await page.setRequestInterception(true);
  page.on('request', request => {
    const url = new URL(request.url());
    if (url.origin === origin || url.protocol === 'data:' || url.protocol === 'blob:') request.continue();
    else request.abort();
  });
  await page.goto(origin + '/Account/Login', {waitUntil: 'domcontentloaded'});
  await page.type('[name=Email]', fixture.credentials[role].email);
  await page.type('[name=Password]', fixture.credentials[role].password);
  await Promise.all([page.waitForNavigation({waitUntil: 'domcontentloaded'}), page.$eval('form[action*="/Account/Login"]', form => form.requestSubmit())]);
  check(new URL(page.url()).pathname !== '/Account/Login', role + ' has an actual Identity session');
  return page;
}
async function noCopy(page, pattern, description) {
  check(!pattern.test(await page.$eval('main', element => element.innerText)), description);
}
async function layout(page, role, width) {
  await page.setViewport({width, height: 850});
  const dimensions = await page.evaluate(() => ({width: innerWidth, content: document.documentElement.scrollWidth}));
  check(dimensions.content <= dimensions.width + 1, role + ' has no horizontal overflow at ' + width + 'px');
}
async function brandHeader(page, role, expectedPage) {
  await page.waitForFunction(() => document.querySelector('header .workspace-brand img')?.naturalWidth > 0);
  const state = await page.$eval('header .workspace-brand-title', title => {
    const header = title.closest('header');
    const home = title.closest('a');
    const lockup = home.getBoundingClientRect();
    const logo = home.querySelector('img').getBoundingClientRect();
    const range = document.createRange();
    range.selectNodeContents(title);
    const brand = range.getBoundingClientRect();
    const copy = title.parentElement.getBoundingClientRect();
    const studioTitle = home.querySelector('.workspace-brand-subtitle');
    const studio = studioTitle.getBoundingClientRect();
    const path = home.nextElementSibling;
    const breadcrumb = path.getBoundingClientRect();
    const bounds = header.getBoundingClientRect();
    const controls = header.querySelector('.topbar-right,.hub-topbar-actions').getBoundingClientRect();
    return {
      title: title.textContent.trim(),
      besideLogo: logo.width > 0 && brand.left >= logo.right
        && Math.abs(logo.top + logo.bottom - copy.top - copy.bottom) < 8,
      fits: brand.width > 0 && brand.height > 0 && logo.left >= bounds.left
        && studio.left >= logo.right && studio.top >= brand.bottom - 1 && studio.height > 0
        && Math.max(copy.right, breadcrumb.right) <= controls.left + 1
        && Math.max(copy.bottom, breadcrumb.bottom) <= bounds.bottom + 1
        && (innerWidth < 992 ? lockup.bottom <= breadcrumb.top + 1
          : breadcrumb.left >= lockup.right + 24 && breadcrumb.height < 20
            && Math.abs(lockup.top + lockup.bottom - breadcrumb.top - breadcrumb.bottom) < 8),
      count: header.querySelectorAll('.workspace-brand').length,
      home: home.classList.contains('workspace-brand') && new URL(home.href).pathname === '/',
      subtitle: studioTitle.textContent.trim(),
      subtitleUppercase: getComputedStyle(studioTitle).textTransform === 'uppercase',
      crumb: path.textContent.replace(/\s+/g, ' ').trim(),
      page: path.querySelector('[aria-current="page"]').textContent.trim()
    };
  });
  const header = await page.$('header:has(.workspace-brand)');
  const clip = await header.boundingBox();
  await page.screenshot({
    path: path.join(evidence, `brand-${role.toLowerCase().replaceAll(' ', '-')}.png`),
    clip: {...clip, width: Math.min(667, clip.width)}
  });
  const workspaceRole = role.split(' ')[0];
  check(state.title === 'Aries d’ Magic Artist' && state.besideLogo && state.fits && state.count === 1 && state.home
    && state.subtitle === 'The Wonder Studio' && state.subtitleUppercase
    && state.page === expectedPage && state.crumb === `${workspaceRole} workspace / ${expectedPage}`,
    role + ' shows the reference branding and workspace breadcrumb ' + JSON.stringify(state));
}

try {
  const client = await signIn('Client');
  const staff = await signIn('Staff');
  const owner = await signIn('Owner');
  await visit(client, '/Bookings/MyBookings');
  await noCopy(client, /All your events in one place|Manage event schedules, track verified payments/, 'My Bookings introductory copy is gone');
  check(await client.$('main a[href*="CreateStepOne"]'), 'My Bookings keeps its plan-an-event link');
  check(await client.$$eval('.hub-sidebar nav a', links => links.length === 4), 'Client sidebar keeps all four module links');
  check(!(await client.$('.hub-sidebar .brand-copy,.hub-sidebar .hub-nav-caption,.hub-rail-footer,.hub-topbar-heading small')), 'Client shell has no redundant captions');
  check(await client.$$eval('.hub-topbar .workspace-brand img', images => images.length === 1)
    && !(await client.$('.hub-sidebar img,#hubDrawer img,#hubFallback img')), 'Client logo appears only in the header');
  await brandHeader(client, 'Client desktop', 'My bookings');
  await client.setViewport({width: 768, height: 850});
  await brandHeader(client, 'Client tablet', 'My bookings');
  await client.setViewport({width: 320, height: 850});
  await brandHeader(client, 'Client 320px', 'My bookings');
  await layout(client, 'Client', 360);
  await client.click('#hubMenu');
  await client.waitForSelector('#hubDrawer[open]');
  check(await client.$$eval('#hubDrawer nav a', links => links.length === 4), 'Client mobile navigation still opens with all four links');
  await client.keyboard.press('Escape');
  await client.waitForSelector('#hubDrawer[open]', {hidden: true});
  await layout(client, 'Client', 1280);
  for (const [route, removed] of [
    ['/Bookings/Details/' + fixture.bookings.decline, /Your booking\.|Staff will review your request\. Payment opens after approval\./],
    ['/Payments/MyUploads', /Payment submission\n|Upload a payment\.|Track payment status in My Bookings|Payments and receipts remain available/],
    ['/RescheduleRequests/MyRequests', /Change your event schedule|Request rescheduling\.|Track its review in My Bookings|Your existing event schedule remains unchanged/],
    ['/Payments/MyRefundRequests', /OWNER-REVIEWED REFUNDS|Submit a request only for an eligible payment|Existing refund requests and Owner decisions remain available/],
    ['/Notifications', /Booking & account updates/],
    ['/Profile', /My Profile|Manage your personal information and account security/]
  ]) {
    await visit(client, route);
    await noCopy(client, removed, route + ' omits redundant introductory copy');
  }
  check(await client.$('form[method=post] #profilePhoto'), 'Client profile keeps its labelled photo control');

  for (const [page, role, dashboard] of [[staff, 'Staff', '/Dashboard/Staff'], [owner, 'Owner', '/Dashboard/Owner']]) {
    await visit(page, dashboard);
    check(!(await page.$('.workspace-sidebar-footer,.workspace-nav-group>p,.staff-round2-header-identity small')), role + ' shell removes duplicate group and branding captions');
    check(await page.$$eval('.sidebar-nav a', links => links.length >= 6), role + ' retains its workspace routes');
    await brandHeader(page, role + ' desktop', role + ' Dashboard');
    await page.setViewport({width: 768, height: 850});
    await brandHeader(page, role + ' tablet', role + ' Dashboard');
    await page.setViewport({width: 320, height: 850});
    await brandHeader(page, role + ' 320px', role + ' Dashboard');
    await layout(page, role, 360);
    await visit(page, dashboard);
    const menuState = await page.$eval('.ui-menu-toggle', button => {
      const target = document.querySelector(button.dataset.bsTarget);
      const bounds = button.getBoundingClientRect();
      const hit = document.elementFromPoint(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
      return {bootstrapped: typeof window.bootstrap?.Offcanvas === 'function', hit: button.contains(hit),
        position: getComputedStyle(target).position};
    });
    check(menuState.bootstrapped && menuState.hit && menuState.position === 'fixed',
      role + ' mobile menu is reachable and initialized ' + JSON.stringify(menuState));
    await page.locator('.ui-menu-toggle').click();
    await page.waitForSelector('#internalSidebar.show', {timeout: 6000});
    await page.waitForSelector('.ui-menu-toggle[aria-expanded="true"]', {timeout: 6000});
    await page.locator('#internalSidebar [aria-label="Close navigation"]').click();
    await page.waitForSelector('#internalSidebar.show', {hidden: true, timeout: 6000});
    await layout(page, role, 1280);
    await visit(page, '/InternalProfile');
    await noCopy(page, /My Profile|Manage your personal information/, role + ' profile has no duplicated introduction');
    await visit(page, '/Notifications');
    check(await page.$('section[aria-label="Notifications"]') && !(await page.$('main .workspace-page-heading,main .aries-notifications-heading h1')), role + ' notifications retain their list with one page title');
    await noCopy(page, /Staff operations|Financial & account updates/, role + ' notifications omit the introductory caption');
  }
  await visit(owner, '/Services');
  check(await owner.$('.staff-package-grid') && !(await owner.$('main .workspace-page-heading')), 'Owner Packages keeps cards without a duplicate page heading');
  await visit(owner, '/Calendar');
  await brandHeader(owner, 'Owner calendar', 'Event Calendar');

  await visit(staff, '/Bookings/Details/' + bookingId);
  await visit(client, '/Bookings/Details/' + bookingId);
  check(!(await staff.$(modal)) && !(await client.$(modal)), 'Staff and Client lack the Owner cancellation dialog');
  await visit(owner, '/Dashboard/Owner');
  const trigger = '[data-bs-target="' + modal + '"]';
  const confirm = modal + ' button[type=submit]';
  const input = modal + ' textarea';
  await layout(owner, 'Owner cancellation', 360);
  await owner.click(trigger);
  await owner.waitForSelector(modal + '.show');
  await owner.waitForFunction(selector => document.activeElement === document.querySelector(selector), {}, input);
  const dialogText = await owner.$eval(modal, element => element.innerText);
  check(/Booking ID[\s\S]*Client[\s\S]*Package[\s\S]*Philippine time[\s\S]*Confirmed/.test(dialogText), 'Modal displays booking, client, package, local event time and status');
  check(await owner.$eval(input, element => element.required && element.minLength === 8 && element.maxLength === 1000 && !!element.labels.length), 'Cancellation reason is required, limited and labelled');
  await owner.screenshot({path: path.join(evidence, 'owner-cancellation-mobile.png')});
  await owner.click(confirm);
  check(await owner.$eval(modal, element => element.classList.contains('show')), 'Blank cancellation reason prevents submission');
  await owner.click(modal + ' .modal-footer [data-bs-dismiss="modal"]');
  await owner.waitForSelector(modal + '.show', {hidden: true});
  check(await owner.$(trigger), 'Keep Booking dismisses the dialog without changing the reservation');
  await layout(owner, 'Owner cancellation', 1280);
  await owner.click(trigger);
  await owner.waitForSelector(modal + '.show');
  await owner.screenshot({path: path.join(evidence, 'owner-cancellation-desktop.png')});
  await owner.type(input, reason);
  await Promise.all([owner.waitForNavigation({waitUntil: 'domcontentloaded'}), owner.click(confirm)]);
  check(!(await owner.$(trigger)) && (await owner.$eval('main', element => element.innerText)).includes('No refund was approved or issued'), 'Cancellation removes the upcoming event and explains the separate financial review');
  await visit(client, '/Notifications');
  check((await client.$eval('main', element => element.innerText)).includes('Any refund is handled separately'), 'Affected Client sees the cancellation and refund distinction');
  passed = true;
} finally {
  await browser.close();
  await fs.writeFile(fixture.doneFile, JSON.stringify({passed, checks}));
}
console.log('FINAL_UI_BROWSER_PASS', checks);
