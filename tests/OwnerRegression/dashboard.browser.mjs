import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {default:puppeteer}=await import(process.env.PUPPETEER_MODULE||'puppeteer');
const root=process.cwd(), snapshots=path.join(process.env.TEMP,'AriesOwnerSnapshots');
const browser=await puppeteer.launch({headless:true}), page=await browser.newPage();
let current='Services-Edit', post;
await page.setRequestInterception(true);
page.on('request',async req=>{
 const url=new URL(req.url());
 if(url.hostname==='cdn.jsdelivr.net'&&url.pathname.includes('fullcalendar')&&url.pathname.endsWith('.js'))return req.respond({contentType:'application/javascript',body:await fs.readFile(path.join(process.env.TEMP,'aries-fullcalendar.js'))});
 if(url.hostname!=='owner.test')return req.abort();
 if(req.isNavigationRequest()){if(req.method()==='POST')post=new URLSearchParams(req.postData());return req.respond({contentType:'text/html',body:await fs.readFile(path.join(snapshots,current+'.html'),'utf8')});}
 if(url.pathname.startsWith('/Communications/'))return req.respond({contentType:'application/json',body:'{"totalUnread":0,"conversations":[]}'});
 try{return req.respond({contentType:url.pathname.endsWith('.css')?'text/css':url.pathname.endsWith('.js')?'application/javascript':'application/octet-stream',body:await fs.readFile(path.join(root,'wwwroot',url.pathname))});}catch{return req.respond({status:404,body:''});}
});
const go=async name=>{current=name;await page.goto('http://owner.test/fixture/'+name,{waitUntil:'networkidle0'});};
try {
 const names=['Staff','Owner','Admin'].flatMap(role=>['','-empty','-error'].map(state=>'Dashboard-'+role+state));
 for(const width of [1440,1024,768,390]) {
  await page.setViewport({width,height:900});
  for(const name of names) {
   await go(name);
   const issues=await page.evaluate(()=>{
    const result=[];
    if(document.documentElement.scrollWidth>innerWidth+1)result.push('document overflow');
    document.querySelectorAll('.dash-panel,.dash-metric,.dash-shortcut,.dash-row').forEach(el=>{const r=el.getBoundingClientRect();if(r.right>innerWidth+1||r.left<0)result.push('overflow '+el.className)});
    const ids=[...document.querySelectorAll('[id]')].map(el=>el.id);
    if(new Set(ids).size!==ids.length)result.push('duplicate IDs');
    document.querySelectorAll('.role-workspace a').forEach(el=>{if(!el.textContent.trim())result.push('unnamed link')});
    return result;
   });
   assert.deepEqual(issues,[],name+' '+width);
   const links=await page.$$eval('.role-workspace a',els=>els.map(el=>el.getAttribute('href')));
   if(name.includes('Staff'))assert(!links.some(h=>/^\/(Payments|Reports|UserManagement|SystemActivity)/.test(h)));
   if(name.includes('Admin'))assert(!links.some(h=>/^\/(Payments|Reports|Bookings|RescheduleRequests)/.test(h)));
   if(name.endsWith('-empty'))assert(await page.$('.dash-empty'));
   if(name.endsWith('-error')){assert(await page.$('.dash-load-errors a'));assert(await page.$('.dash-quick-grid a'));}
   if(width===390)assert.equal(await page.$eval('.dash-metric-grid',el=>getComputedStyle(el).gridTemplateColumns.split(' ').length),1);
   await page.screenshot({path:path.join(snapshots,'dashboard-'+name+'-'+width+'.png'),fullPage:true});
   console.log('PASS dashboard layout',name,width);
  }
 }
 await page.setViewport({width:1440,height:900});
 for(const role of ['Staff','Owner','Admin']){
  await go('Dashboard-'+role);
  await page.focus('.dash-quick-grid a');
  assert.equal(await page.$eval('.dash-quick-grid a',el=>getComputedStyle(el).outlineStyle),'solid','Keyboard focus is visible');
  await page.evaluate(()=>scrollTo(0,800));
  assert(Math.abs(await page.$eval('.dashboard-topbar',el=>el.getBoundingClientRect().top))<2,'Sticky header for '+role);
 }
 await go('Dashboard-Staff');
 const targets=await page.$$eval('.dash-attention a',els=>els.map(el=>el.getAttribute('href')));
 assert(targets.some(h=>h.includes('bookingStatus=Pending')));
 assert(targets.some(h=>h.includes('status=Pending')));
 await go('Dashboard-Owner');
 assert(await page.$('a[href*="Payments/Verify"]'));
 assert.equal(await page.$$eval('.dash-trend meter',els=>els.length),6);
 assert(await page.$$eval('.dash-metric',els=>els.some(el=>el.textContent.includes('Completed refunds'))));
 console.log('PASS keyboard focus, sticky headers, filtered shortcuts and accessible six-month trend');
} finally { await browser.close(); }
