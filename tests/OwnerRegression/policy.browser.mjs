// Synthetic Razor fixtures only; never contacts a live application.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {default:puppeteer}=await import(process.env.PUPPETEER_MODULE||'puppeteer');
const root=process.cwd(), fixtures=path.join(process.env.TEMP,'AriesOwnerSnapshots');
const output=path.join(root,'.tmp-owner-build','policy-visual');
await fs.mkdir(output,{recursive:true});
const browser=await puppeteer.launch({headless:true,executablePath:process.env.PUPPETEER_EXECUTABLE_PATH});
const page=await browser.newPage();
const pages=['Bookings-Create','Bookings-ClientDetails','Bookings-Cancel','Bookings-StaffDetails','Payments-Upload','Payments-MyUploads','Payments-RequestRefund','Payments-MyRefundRequests','Payments-Verify','Payments-ProofHistory','Payments-RefundReview','Feedback-Create','Feedback-Details','Testimonials-Review'];
let current;const results=[],errors=[];
page.on('pageerror',e=>errors.push({page:current,message:e.message}));
await page.setRequestInterception(true);
page.on('request',async req=>{
 const url=new URL(req.url());
 if(url.hostname!=='policy.test')return req.abort();
 if(req.isNavigationRequest())return req.respond({contentType:'text/html',body:await fs.readFile(path.join(fixtures,current+'.html'),'utf8')});
 if(url.pathname.startsWith('/Communications/'))return req.respond({contentType:'application/json',body:'{"count":0,"conversations":[]}'});
 if(url.pathname.startsWith('/FeedbackMedia/')||url.pathname.startsWith('/Payments/PaymentProof'))return req.respond({contentType:'image/jpeg',body:await fs.readFile(path.join(root,'wwwroot/images/magic-performance-1.jpeg'))});
 try {return req.respond({contentType:url.pathname.endsWith('.css')?'text/css':url.pathname.endsWith('.js')?'application/javascript':url.pathname.endsWith('.woff2')?'font/woff2':'application/octet-stream',body:await fs.readFile(path.join(root,'wwwroot',url.pathname))});}
 catch{return req.respond({status:404,body:''});}
});
try {
 for(const width of [1440,390,320])for(const name of pages){
  current=name;await page.setViewport({width,height:960});
  await page.goto('http://policy.test/fixture/'+name,{waitUntil:'load'});
  await page.evaluate(async()=>{await document.fonts.ready;});
  await page.screenshot({path:path.join(output,name+'-'+width+'.png'),fullPage:true});
  const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1);
  await page.addScriptTag({path:process.env.AXE_CORE_PATH});
  const violations=await page.evaluate(async()=>(await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa']}})).violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)})));
  results.push({page:name,width,overflow,violations});
  let trigger;
  if(name==='Payments-Upload')trigger='#continuePayment';
  if(name==='Bookings-Cancel')trigger='#openCancellationConfirmation';
  if(name==='Feedback-Create'){
   for(const rating of ['OverallRating','PerformanceRating','BookingRating'])await page.click(`[name="${rating}"][value="5"]`);
   await page.type('#Experience','Synthetic experience used for consent-dialog review.');trigger='#openFeedbackConsent';
  }
  if(name==='Bookings-StaffDetails'){
   await page.type('#staffCancelReason','A documented venue conflict was reviewed.');trigger='#reviewStaffCancellation';
  }
  if(trigger){
   await page.click(trigger);
   await page.screenshot({path:path.join(output,name+'-review-'+width+'.png'),fullPage:true});
   await page.screenshot({path:path.join(output,name+'-review-viewport-'+width+'.png'),fullPage:false});
   const modalViolations=await page.evaluate(async()=>(await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa']}})).violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)})));
   results.push({page:name+'-review',width,overflow:await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1),violations:modalViolations});
  }
 }
 await fs.writeFile(path.join(output,'results.json'),JSON.stringify({results,errors},null,2));
 assert.deepEqual(errors,[],'JavaScript errors; see policy-visual/results.json');
 assert.deepEqual(results.filter(r=>r.overflow||r.violations.length),[],'Layout/accessibility findings; see policy-visual/results.json');
 console.log('SUCCESS:',results.length,'rendered workflow screenshots and axe scans');
}finally{await browser.close();}
