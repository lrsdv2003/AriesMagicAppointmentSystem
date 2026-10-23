import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {default:puppeteer}=await import(process.env.PUPPETEER_MODULE||'puppeteer');
const root=process.cwd(),snapshots=path.join(process.env.TEMP,'AriesOwnerSnapshots');
const browser=await puppeteer.launch({headless:true,executablePath:process.env.PUPPETEER_EXECUTABLE_PATH});
const page=await browser.newPage();let current='Home-Index-Guest';
const errors=[],findings=[],requests=[];
page.on('pageerror',error=>errors.push({page:current,error:error.message}));
await page.setRequestInterception(true);
page.on('request',async req=>{
 const url=new URL(req.url());requests.push(url.href);
 if(url.hostname!=='owner.test')return req.abort();
 if(req.isNavigationRequest())return req.respond({contentType:'text/html',body:await fs.readFile(path.join(snapshots,current+'.html'),'utf8')});
 if(url.pathname==='/Payments/PaymentProof')return req.respond({contentType:'image/png',body:await fs.readFile(path.join(root,'tests/ReleaseIntegration/Fixtures/payment.png'))});
 if(url.pathname.startsWith('/FeedbackMedia/'))return req.respond({contentType:'image/jpeg',body:await fs.readFile(path.join(root,'wwwroot/images/magic-performance-1.jpeg'))});
 if(url.pathname.startsWith('/Communications/'))return req.respond({contentType:'application/json',body:'{"count":0,"totalUnread":0,"conversations":[]}'});
 try{return req.respond({contentType:url.pathname.endsWith('.css')?'text/css':url.pathname.endsWith('.js')?'application/javascript':url.pathname.endsWith('.woff2')?'font/woff2':url.pathname.endsWith('.svg')?'image/svg+xml':url.pathname.endsWith('.jpeg')?'image/jpeg':'application/octet-stream',body:await fs.readFile(path.join(root,'wwwroot',url.pathname))});}
 catch{return req.respond({status:404,body:''});}
});
const go=async name=>{current=name;await page.goto('http://owner.test/fixture/'+name,{waitUntil:'load'});await page.evaluate(async()=>{await document.fonts.ready;for(const image of document.querySelectorAll('main img[src]')){image.loading='eager';await image.decode().catch(()=>{});}});};
const pages=['Home-Index-Guest','Home-Index','Home-Index-Guest-published','Home-Packages','Home-Package','Home-Privacy','Account-Login','Account-Register','Account-ForgotPassword','Account-ResetPassword','Account-VerifyEmail','Account-EmailVerified','Account-AccessDenied','Bookings-ClientDashboard','Bookings-ClientDashboard-empty','Bookings-MyBookings','Bookings-MyBookings-empty','Bookings-ClientDetails','Bookings-Create','Payments-Upload','Payments-MyUploads','Payments-RequestRefund','Payments-MyRefundRequests','RescheduleRequests-Create','RescheduleRequests-MyRequests','Profile-Index','Notifications-Index-Client','Communications-Index-Client','Communications-Index-Client-empty','Communications-Index-Client-inbox','Feedback-Create','Feedback-Index','Feedback-Details'].filter(name=>!process.env.WONDER_PAGES||process.env.WONDER_PAGES.split(',').includes(name));
let checks=0;
try{
 for(const width of [320,390,768,1280,1920]){
  await page.setViewport({width,height:960});
  for(const name of pages){
   await go(name);
   const result=await page.evaluate(()=>({overflow:document.documentElement.scrollWidth>innerWidth+1,title:!!document.querySelector('h1'),brand:getComputedStyle(document.body).getPropertyValue('--cx-brand').trim(),font:getComputedStyle(document.body).fontFamily,broken:[...document.querySelectorAll('main img[src]')].filter(i=>!i.naturalWidth).map(i=>i.src)}));
   assert(!result.overflow,name+' '+width+' horizontal overflow');assert(result.title,name+' missing h1');assert.equal(result.brand,'#194F49');assert(result.font.includes('DM Sans'),name+' body font');assert.deepEqual(result.broken,[],name+' broken media');
   if(['Home-Index-Guest','Home-Packages','Bookings-ClientDashboard','Bookings-Create','Communications-Index-Client','Home-Index-Guest-published'].includes(name))await page.screenshot({path:path.join(snapshots,'wonder-'+name+'-'+width+'.png'),fullPage:true});
   if(name==='Home-Index-Guest')await page.screenshot({path:path.join(snapshots,'wonder-hero-'+width+'.png'),fullPage:false});
   if(process.env.AXE_CORE_PATH&&(width===320||width===1280)){await page.addScriptTag({path:process.env.AXE_CORE_PATH});const violations=await page.evaluate(async()=>(await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa']}})).violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})));if(violations.length)findings.push({page:name,width,violations});}
   checks++;console.log('PASS Wonder Studio layout',name,width);
  }
 }
 await go('Home-Index-Guest');
 assert(await page.evaluate(()=>document.fonts.check('600 32px "Plus Jakarta Sans"')&&document.fonts.check('400 16px "DM Sans"')));checks++;
 assert(!requests.some(url=>url.includes('fonts.googleapis.com')||url.includes('fonts.gstatic.com')),'Fonts must be local');checks++;
 assert.equal(await page.$$eval('.cx-media img[fetchpriority="high"]',els=>els.length),1);checks++;
 const before=await page.$eval('.cx-hero h1',el=>getComputedStyle(el).fontFamily);assert(before.includes('Plus Jakarta Sans'));checks++;
 for(const name of ['Dashboard-Staff','Dashboard-Admin','Dashboard-Owner']){await go(name);assert(await page.evaluate(()=>!document.body.classList.contains('client-body')&&!getComputedStyle(document.body).fontFamily.includes('DM Sans')&&!document.querySelector('link[href*="client-experience.css"]')));checks++;}
 await fs.writeFile(path.join(snapshots,'wonder-accessibility.json'),JSON.stringify(findings,null,2));
 await fs.writeFile(path.join(snapshots,'wonder-js-errors.json'),JSON.stringify(errors,null,2));
 assert.deepEqual(findings,[],'Wonder Studio accessibility violations; see wonder-accessibility.json');assert.deepEqual(errors,[],'Client JavaScript errors');
 console.log('SUCCESS:',checks,'Wonder Studio checks across five viewports and isolated internal roles');
}finally{await browser.close();}
