import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {default:puppeteer}=await import(process.env.PUPPETEER_MODULE||'puppeteer');
const root=process.cwd(),snapshots=path.join(process.env.TEMP,'AriesOwnerSnapshots');
const browser=await puppeteer.launch({headless:true}),page=await browser.newPage();
let current, lastNavigation;
await page.setRequestInterception(true);
page.on('request',async req=>{
 const url=new URL(req.url());
 if(url.hostname==='cdn.jsdelivr.net'&&url.pathname.includes('fullcalendar')&&url.pathname.endsWith('.js'))return req.respond({contentType:'application/javascript',body:await fs.readFile(path.join(process.env.TEMP,'aries-fullcalendar.js'))});
 if(url.hostname!=='owner.test')return req.abort();
 if(req.isNavigationRequest()){lastNavigation=url;return req.respond({contentType:'text/html',body:await fs.readFile(path.join(snapshots,current+'.html'),'utf8')});}
 if(url.pathname.startsWith('/Communications/'))return req.respond({contentType:'application/json',body:'{"totalUnread":0,"conversations":[]}'}); 
 try{return req.respond({contentType:url.pathname.endsWith('.css')?'text/css':url.pathname.endsWith('.js')?'application/javascript':'application/octet-stream',body:await fs.readFile(path.join(root,'wwwroot',url.pathname))});}catch{return req.respond({status:404,body:''});}
});
const go=async name=>{current=name;await page.goto('http://owner.test/fixture/'+name,{waitUntil:'networkidle0'});};
const pages=['Dashboard-Staff','Dashboard-Admin','Dashboard-Owner','Bookings-StaffIndex','Bookings-StaffDetails',
 'RescheduleRequests-Index','RescheduleRequests-Details','Calendar-StaffIndex','Calendar-Index','Services-Index','Services-Index-Staff','Services-Index-Admin',
 'Services-Edit','Services-Create','Services-Details','Services-Details-Staff','Services-Details-Admin','Payments-PendingVerification','Payments-Verify','Payments-RefundRequests','Payments-RefundReview','Reports-Index',
 'UserManagement-Index','UserManagement-Details','SystemActivity-Index','TrashHistory-Index','TrashHistory-Details',
 'Communications-Index','Communications-Index-Admin','Notifications-Index',
 'InternalProfile-Index','InternalProfile-Index-Staff','InternalProfile-Index-Admin',
 'History-Index','History-Index-Staff','History-Index-empty','History-Index-Staff-empty','History-Details','History-Details-Staff'];
const selected=process.env.UI_PAGES?process.env.UI_PAGES.split(','):pages;
try{
 for(const width of (process.env.SKIP_LAYOUT?[]:[1440,1024,768,390])){
  await page.setViewport({width,height:900});
  for(const name of selected){
   await go(name);
   const issues=await page.evaluate(()=>{
    const result=[],main=document.querySelector('#mainContent');
    if(getComputedStyle(document.querySelector('.dashboard-sidebar')).backgroundColor!=='rgb(21, 33, 54)')result.push('sidebar contrast');
    if(innerWidth>=992&&getComputedStyle(document.querySelector('.ui-menu-toggle')).display!=='none')result.push('desktop menu toggle');
    if(document.documentElement.scrollWidth>innerWidth+1)result.push('document overflow');
    if(main.scrollWidth>main.clientWidth+2)result.push('content overflow '+main.scrollWidth+'/'+main.clientWidth);
    if(!getComputedStyle(document.body).fontFamily.includes('Segoe UI'))result.push('wrong primary font');
    document.querySelectorAll('button,input,select,h1,h2').forEach(e=>{if(!getComputedStyle(e).fontFamily.includes('Segoe UI'))result.push('inconsistent font '+e.tagName);});
    const heading=document.querySelector('.dashboard-title'),actions=document.querySelector('.topbar-right');
    if(heading.getBoundingClientRect().right>actions.getBoundingClientRect().left+1)result.push('header title overlaps actions');
    const ids=[...document.querySelectorAll('[id]')].map(x=>x.id);
    if(new Set(ids).size!==ids.length)result.push('duplicate IDs');
    document.querySelectorAll('h1,h2,h3').forEach(e=>{if(Number(getComputedStyle(e).fontWeight)>600)result.push('heavy heading '+e.textContent);});
    const header=document.querySelector('.dashboard-topbar');
    const before=header.getBoundingClientRect().top;main.scrollTop=500;
    if(Math.abs(header.getBoundingClientRect().top-before)>1)result.push('header moved with content');
    main.scrollTop=0;
    return result;
   });
   assert.deepEqual(issues,[],name+' '+width);
   if(name.startsWith('History-')){
    const text=await page.$eval('#mainContent',e=>e.innerText);
    if(name.includes('Staff'))assert(!/Payment Summary|Payment review|Verified payments|Remaining booking balance|Refund|Private financial|Payment Verified|PHP/.test(text),'Staff financial separation');
    if(name.includes('Details'))assert.equal(await page.$$eval('.ui-page form',els=>els.length),0,'History detail is read-only');
    if(name.includes('empty'))assert(text.includes('No records match the selected filters.'));
   }
   if(['Dashboard-Owner','History-Index-Staff','History-Details','Payments-Verify','UserManagement-Index','InternalProfile-Index'].includes(name)&&[1440,390].includes(width))
      await page.screenshot({path:path.join(snapshots,'polish-'+name+'-'+width+'.png'),fullPage:false});
   console.log('PASS internal UI',name,width);
  }
 }
 await page.setViewport({width:390,height:900});await go('Dashboard-Staff');
 assert(!(await page.$eval('#internalSidebar',e=>e.classList.contains('show'))));
 await page.click('.ui-menu-toggle');
 await page.waitForFunction(()=>document.querySelector('#internalSidebar').classList.contains('show'));
 assert(await page.$('.offcanvas-backdrop'));
 await page.keyboard.press('Escape');
 await page.waitForFunction(()=>!document.querySelector('#internalSidebar').classList.contains('show'));
 await page.waitForFunction(()=>document.activeElement?.classList.contains('ui-menu-toggle'));
 await page.setViewport({width:1440,height:900});await go('History-Index');
 await page.click('#historyFilterToggle');
 await page.select('#historyStatus','Completed');await page.select('#historyDate','Year');await page.select('#historyPayment','Verified');
 await page.$eval('#historyYear',e=>e.value='2026');
 await Promise.all([page.waitForNavigation(),page.click('.dropdown-menu.show button[type=submit]')]);
 assert.equal(lastNavigation.searchParams.get('BookingStatus'),'Completed');
 assert.equal(lastNavigation.searchParams.get('Year'),'2026');
 assert.equal(lastNavigation.searchParams.get('PaymentStatus'),'Verified');
 await go('History-Index-Staff');assert.equal(await page.$('#historyPayment'),null);
 await page.click('#accountMenu');assert(await page.$('.dropdown-menu.show a[href$="#security"]'));
 console.log('PASS mobile navigation focus, fixed header, History filters and shared account menu');
}finally{await browser.close();}
