import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {default:puppeteer}=await import(process.env.PUPPETEER_MODULE||'puppeteer');
const root=process.cwd(), snapshots=path.join(process.env.TEMP,'AriesOwnerSnapshots');
const browser=await puppeteer.launch({headless:true,executablePath:process.env.PUPPETEER_EXECUTABLE_PATH});
const page=await browser.newPage();
let current='Home-Index';
await page.setRequestInterception(true);
page.on('request',async req=>{
 const url=new URL(req.url());
 if(url.hostname!=='owner.test')return req.abort();
 if(req.isNavigationRequest())return req.respond({contentType:'text/html',body:await fs.readFile(path.join(snapshots,current+'.html'),'utf8')});
 if(url.pathname==='/Payments/PaymentProof')return req.respond({contentType:'image/png',body:await fs.readFile(path.join(root,'tests/ReleaseIntegration/Fixtures/payment.png'))});
 if(url.pathname==='/FeedbackMedia/Private'||url.pathname==='/FeedbackMedia/Public')return req.respond({contentType:'image/jpeg',body:await fs.readFile(path.join(root,'wwwroot/images/magic-performance-1.jpeg'))});
 if(url.pathname==='/Communications/MessagesSince')return req.respond({contentType:'application/json',body:'[]'});
 if(url.pathname.startsWith('/Communications/'))return req.respond({contentType:'application/json',body:'{"totalUnread":0,"conversations":[]}'});
 try { return req.respond({contentType:url.pathname.endsWith('.css')?'text/css':url.pathname.endsWith('.js')?'application/javascript':url.pathname.endsWith('.svg')?'image/svg+xml':url.pathname.endsWith('.jpeg')?'image/jpeg':'application/octet-stream',body:await fs.readFile(path.join(root,'wwwroot',url.pathname))}); }
 catch { return req.respond({status:404,body:''}); }
});
const go=async name=>{current=name;await page.goto('http://owner.test/fixture/'+name,{waitUntil:'load'});await page.evaluate(async()=>{await document.fonts.ready;const images=[...document.querySelectorAll('main img[src]')];images.forEach(i=>i.loading='eager');await Promise.all(images.map(i=>i.decode().catch(()=>{})));});};
let checks=0;
const accessibility=[];
try {
 const pages=(process.env.CLIENT_PAGES||'Home-Index,Home-Index-Guest,Home-Packages,Home-Package,Home-Privacy,Account-Login,Account-Register,Account-ForgotPassword,Account-ResetPassword,Account-VerifyEmail,Account-EmailVerified,Account-AccessDenied,Bookings-ClientDashboard,Bookings-ClientDashboard-empty,Bookings-MyBookings,Bookings-MyBookings-empty,Bookings-ClientDetails,Bookings-Create,Payments-Upload,Payments-MyUploads,Payments-RequestRefund,Payments-MyRefundRequests,RescheduleRequests-Create,RescheduleRequests-MyRequests,Profile-Index,Notifications-Index-Client,Communications-Index-Client,Communications-Index-Client-empty,Communications-Index-Client-inbox').split(',');
 for(const width of [1440,768,390]) {
  await page.setViewport({width,height:960});
  for(const name of pages) {
   await go(name);
   const result=await page.evaluate(()=>({
    overflow:document.documentElement.scrollWidth>innerWidth+1,
    title:document.querySelector('h1')?.textContent.trim(),
    scoped:!!document.querySelector('body.client-body link[href*="client-experience"]')||!!document.querySelector('link[href*="client-experience"]'),
    broken:[...document.querySelectorAll('main img[src]')].filter(i=>i.complete&&!i.naturalWidth).map(i=>i.getAttribute('src'))
   }));
   if(result.overflow) console.log(await page.evaluate(()=>[...document.querySelectorAll('body *')].filter(el=>{const r=el.getBoundingClientRect();return r.width>0&&(r.right>innerWidth+1||r.left< -1);}).slice(0,16).map(el=>({tag:el.tagName,cls:el.className,left:el.getBoundingClientRect().left,width:el.getBoundingClientRect().width}))));
   await page.screenshot({path:path.join(snapshots,'client-'+name+'-'+width+'.png'),fullPage:true});
   assert(!result.overflow,name+' '+width+' document overflow');
   assert(result.title,name+' needs a page title');
   assert(result.scoped,name+' missing client styles');
   assert.deepEqual(result.broken,[],name+' broken local images');
   if(process.env.AXE_CORE_PATH&&width===390) {
    await page.addScriptTag({path:process.env.AXE_CORE_PATH});
    const violations=await page.evaluate(async()=> (await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa']}})).violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})));
    if(violations.length)accessibility.push({page:name,violations});
   }
   if(name==='Home-Index-Guest')await page.screenshot({path:path.join(snapshots,'client-home-hero-'+width+'.png'),fullPage:false});
   checks++; console.log('PASS client layout',name,width);
  }
 }
 await page.setViewport({width:390,height:844});await go('Home-Index');
 await page.click('.navbar-toggler');
 await page.waitForSelector('#clientNavbar.show');
 assert(await page.$eval('#clientNavbar',el=>el.getBoundingClientRect().width<=innerWidth));
 await page.click('.navbar-toggler');
 await page.waitForFunction(()=>!document.querySelector('#clientNavbar').classList.contains('show'));
 const gallery=await page.$('[data-gallery-src]');await gallery.focus();await page.keyboard.press('Enter');
 assert(await page.$eval('#clientGallery',el=>el.open));
 await page.keyboard.press('Escape');
 assert(await page.$eval('#clientGallery',el=>!el.open));
 assert(await page.$eval('[data-gallery-src]',el=>el===document.activeElement));
 checks+=5;
 await go('Profile-Index');await page.click('[data-bs-target="#editModal"]');await page.waitForSelector('#editModal.show');
 assert(await page.$eval('#editModal',el=>el.contains(document.activeElement)));
 await page.keyboard.press('Escape');
 await page.emulateMediaFeatures([{name:'prefers-reduced-motion',value:'reduce'}]);
 await go('Home-Index');
 assert.equal(await page.$eval('.cx-package-art img',el=>getComputedStyle(el).transitionDuration),'0s');
 checks+=2;
 if(process.env.AXE_CORE_PATH) {await fs.writeFile(path.join(snapshots,'client-accessibility.json'),JSON.stringify(accessibility,null,2));assert.deepEqual(accessibility,[],'Client WCAG A/AA violations; see client-accessibility.json');}
 console.log('SUCCESS:',checks,'client viewport and interaction checks');
} finally {await browser.close();}

