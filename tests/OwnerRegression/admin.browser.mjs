import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {default:puppeteer}=await import(process.env.PUPPETEER_MODULE||'puppeteer');
const root=process.cwd(), snapshots=path.join(process.env.TEMP,'AriesOwnerSnapshots');
const browser=await puppeteer.launch({headless:true}), page=await browser.newPage();
let current='UserManagement-Index', post, ajaxPosts=0;
await page.setRequestInterception(true);
page.on('request',async req=>{
 const url=new URL(req.url());
 if(url.hostname==='cdn.jsdelivr.net'&&url.pathname.includes('fullcalendar')&&url.pathname.endsWith('.js'))return req.respond({contentType:'application/javascript',body:await fs.readFile(path.join(process.env.TEMP,'aries-fullcalendar.js'))});
 if(url.hostname!=='owner.test')return req.abort();
 if(url.pathname.includes('DateAjax') && req.method()==='POST') {ajaxPosts++; return req.respond({contentType:'application/json',body:'{"success":false,"message":"Fixture error: please try again."}'});}
 if(req.isNavigationRequest()){if(req.method()==='POST')post=new URLSearchParams(req.postData());return req.respond({contentType:'text/html',body:await fs.readFile(path.join(snapshots,current+'.html'),'utf8')});}
 if(url.pathname.startsWith('/Communications/'))return req.respond({contentType:'application/json',body:'{"totalUnread":0,"conversations":[]}'});
 try{return req.respond({contentType:url.pathname.endsWith('.css')?'text/css':url.pathname.endsWith('.js')?'application/javascript':'application/octet-stream',body:await fs.readFile(path.join(root,'wwwroot',url.pathname))});}catch{return req.respond({status:404,body:''});}
});
const go=async name=>{current=name;await page.goto('http://owner.test/fixture/'+name,{waitUntil:'networkidle0'});};
try {
 const names=process.env.ADMIN_PAGES?.split(',') || ['Dashboard-Admin','UserManagement-Index','UserManagement-Details','Calendar-Index','TrashHistory-Index','TrashHistory-Details','SystemActivity-Index','Services-Index-Admin','Communications-Index-Admin'];
 for(const width of (process.env.SKIP_LAYOUT ? [] : [1440,1024,768,390])) {
  await page.setViewport({width,height:900});
  for(const name of names) {
   await go(name);
   const issues=await page.evaluate(()=>{
    const result=[];
    if(document.documentElement.scrollWidth>innerWidth+1)result.push('document overflow');
    document.querySelectorAll('.admin-panel,.admin-table-wrap,.admin-heading,.staff-package-card,.comm-workspace').forEach(el=>{const r=el.getBoundingClientRect();if(r.right>innerWidth+1||r.left<0)result.push('overflow '+el.className)});
    document.querySelectorAll('.admin-console input:not([type=hidden]),.admin-console select').forEach(el=>{if(!el.labels?.length&&!el.getAttribute('aria-label'))result.push('unlabeled '+el.name)});
    return result;
   });
   assert.deepEqual(issues,[],name+' '+width);
   assert.equal(await page.$$eval('.sidebar-nav a[href*="Payments"],.sidebar-nav a[href*="Communications"],button[title="Settings"]',els=>els.length),0,'Admin navigation has no financial or duplicate communication shortcuts');
   await page.screenshot({path:path.join(snapshots,'admin-'+name+'-'+width+'.png'),fullPage:true});
   console.log('PASS Admin layout',name,width);
  }
 }
 await page.setViewport({width:1440,height:900});
 await go('UserManagement-Index');
 await page.evaluate(()=>scrollTo(0,700));
 assert(Math.abs(await page.$eval('.dashboard-topbar',el=>el.getBoundingClientRect().top))<2,'Header stays at viewport top');
 await page.evaluate(()=>scrollTo(0,0));
 await page.click('[data-bs-target="#createStaffModal"]');
 await page.waitForSelector('#createStaffModal.show');
 await page.waitForFunction(()=>document.querySelector('#createStaffModal').contains(document.activeElement));
 await page.keyboard.press('Escape');
 await page.waitForSelector('#createStaffModal.show',{hidden:true});
 console.log('PASS sticky header and account modal keyboard dismissal');
 await go('Calendar-Index');
 assert.equal(await page.$$eval('[name="MaxBookingsPerDay"],[name="LimitMaxBookings"],.fc-daygrid',els=>els.length),0,'No editable capacity or operational calendar');
 await page.$eval('#blockDate',el=>el.value='2027-01-20');
 await page.type('#blockReason','Maintenance');
 page.once('dialog',dialog=>dialog.dismiss());
 await page.click('form[data-admin-ajax] button');
 assert.equal(ajaxPosts,0,'Cancel sends no mutation');
 page.once('dialog',dialog=>dialog.accept());
 await page.click('form[data-admin-ajax] button');
 await page.waitForFunction(()=>document.querySelector('.admin-form-feedback').textContent.includes('Fixture error'));
 assert.equal(ajaxPosts,1);
 assert.equal(await page.$eval('#blockReason',el=>el.value),'Maintenance');
 assert.equal(await page.$eval('form[data-admin-ajax] button',el=>el.disabled),false);
 console.log('PASS calendar confirmation, recoverable error and preserved input');
 await go('Communications-Index-Admin');
 assert(await page.$('.comm-page-staff'),'Admin reuses Staff messaging layout');
 await go('SystemActivity-Index');
 assert(!(await page.content()).includes('MetadataJson'),'Raw audit JSON not rendered');
 await page.click('details summary');
 assert(await page.$eval('details',el=>el.open),'Audit details keyboard-native disclosure');
 console.log('PASS shared messaging and readable audit details');
} finally { await browser.close(); }
