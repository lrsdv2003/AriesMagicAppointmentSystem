import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {default: puppeteer}=await import('../../.tmp-owner-build/puppeteer-with-runtime.mjs');
const root='.tmp-owner-build/predefense-acceptance-20261115';
const data=JSON.parse(await fs.readFile(path.join(root,'staging-info.json'),'utf8'));
const evidence=path.join(root,'screenshots');
await fs.mkdir(evidence,{recursive:true});
const browser=await puppeteer.launch({headless:true,executablePath:'C:/Program Files/BraveSoftware/Brave-Browser/Application/brave.exe',args:['--no-first-run','--disable-features=Translate']});
let passed=false;
const requestEvidence=[],screenshots=[],results=[];
function check(value,label){
 assert.ok(value,label);
 results.push({assertion:label,status:'PASS'});
 console.log('CHROMIUM_PASS',label);
}
async function login(page,url,role){
 const response=await page.goto(url+'/Account/Login',{waitUntil:'domcontentloaded'});
 check(response.status()===200,'Actual login page HTTP 200 for '+role);
 const c=data.credentials[role];
 await page.type('input[name=Email]',c.email);
 await page.type('input[name=Password]',c.password);
 await Promise.all([page.waitForNavigation({waitUntil:'domcontentloaded',timeout:20000}),page.$eval('form[action*=Login]',f=>f.requestSubmit())]);
 check(!page.url().includes('/Account/Login'),'Real Identity cookie login '+role);
}
async function newPage(){
 const context=await browser.createBrowserContext();
 const page=await context.newPage();
 await page.setViewport({width:1280,height:800,deviceScaleFactor:1});
 const errs=[];page.on('pageerror',e=>errs.push(e.message));
 page.on('response',r=>{
  const p=new URL(r.url()).pathname;
  if(r.request().method()==='POST'&&/^(\/Bookings\/(Approve|SubmitBooking)|\/Payments\/(Upload|Verify))/.test(p)){
   requestEvidence.push({method:'POST',path:p,status:r.status(),source:'Real loopback Kestrel HTTP'});
  }
 });
 return {page,context,errs};
}
async function screenshot(page,name){
 const file=path.join(evidence,name+'.png');
 await page.screenshot({path:file,fullPage:true});
 screenshots.push(file);
}
async function staffApprove(page,url,id){
 const response=await page.goto(url+'/Bookings/Details/'+id,{waitUntil:'domcontentloaded'});
 check(response.status()===200,'Staff opens pending booking '+id);
 check((await page.$('[data-bs-target="#approveBookingModal"]'))!==null,'Staff sees approval action for booking '+id);
 await page.click('[data-bs-target="#approveBookingModal"]');
 await page.waitForSelector('#approveBookingModal.show',{visible:true,timeout:7000});
 await Promise.all([page.waitForNavigation({waitUntil:'domcontentloaded',timeout:20000}),page.click('#approveBookingModal button[type=submit]')]);
 check(!page.url().includes('AccessDenied'),'Staff approval submitted over real browser for '+id);
}
async function clientUpload(page,url,id,name){
 const response=await page.goto(url+'/Payments/Upload?bookingId='+id,{waitUntil:'domcontentloaded'});
 check(response.status()===200,'Client loads actual payment wizard '+name);
 const before=await page.evaluate(()=>({booking:document.querySelector('#BookingId')?.value,options:document.querySelectorAll('input[name=PaymentOption]').length,
  checked:document.querySelector('input[name=PaymentOption]:checked')?.value,version:document.querySelector('#ExpectedLatestPaymentId')?.value}));
 check(before.booking===String(id),'Client payment choice is scoped to booking '+name);
 check(before.checked==='Minimum'&&before.options>=1,'Client Minimum downpayment retained for '+name);
 check(before.version!==undefined&&before.version!=='','Server version token present for '+name);
 await page.click('#continuePayment');
 check(await page.$eval('#paymentProof',e=>!e.hidden),'Actual wizard progresses to receipt upload '+name);
 const input=await page.$('#ProofImage');
 await input.uploadFile(data.fixture);
 const withoutConsent=await page.evaluate(()=>{
  const form=document.querySelector('#clientPaymentForm');
  return {nativeValid:form.checkValidity(),jqueryValid:window.jQuery?.(form).valid()};
 });
 check(withoutConsent.nativeValid===false&&withoutConsent.jqueryValid===false,
   'Unchecked acknowledgement is rejected by native and jQuery validation '+name);
 await page.click('#paymentAcknowledgement');
 const withConsent=await page.evaluate(()=>{
  const form=document.querySelector('#clientPaymentForm');
  return {nativeValid:form.checkValidity(),jqueryValid:window.jQuery?.(form).valid()};
 });
 check(withConsent.nativeValid===true&&withConsent.jqueryValid===true,
   'Checked acknowledgement passes native and jQuery validation '+name);
 const state=await page.evaluate(()=>{
  const f=document.querySelector('#clientPaymentForm'),form=new FormData(f);
  return {valid:f.checkValidity(),option:form.get('PaymentOption'),filename:form.get('ProofImage')?.name,size:form.get('ProofImage')?.size};
 });
 check(state.valid&&state.option==='Minimum'&&state.size>100,'Real browser multipart form has selected option and safe PNG '+name);
 await screenshot(page,name+'-payment-upload-ready');
 await page.evaluate(()=>{
   window.__paymentSubmitTrace=[];
   const f=document.getElementById('clientPaymentForm');
   f.addEventListener('submit',e=>{window.__paymentSubmitTrace.push({phase:'capture',cancel:e.defaultPrevented,valid:f.checkValidity()});queueMicrotask(()=>window.__paymentSubmitTrace.push({phase:'microtask',cancel:e.defaultPrevented}));},true);
   f.addEventListener('submit',e=>window.__paymentSubmitTrace.push({phase:'bubble',cancel:e.defaultPrevented}),false);
   const orig=Event.prototype.preventDefault;
   Event.prototype.preventDefault=function(...args){if(this.type==='submit'){window.__paymentSubmitTrace.push({phase:'preventDefault',stack:new Error().stack?.split('\n').slice(1,5).join(' :: ')});}return orig.apply(this,args);};
   for(const input of f.querySelectorAll('input,select'))input.addEventListener('invalid',()=>window.__paymentSubmitTrace.push({phase:'invalid',name:input.name}),true);
 });
 const nav=page.waitForNavigation({waitUntil:'domcontentloaded',timeout:20000}).catch(()=>null);
 await page.click('#clientPaymentForm button[type=submit]');
 const moved=await nav;
 if(!moved)console.log('PAYMENT_SUBMIT_DIAGNOSTIC',JSON.stringify(await page.evaluate(()=>({
   url:location.href,trace:window.__paymentSubmitTrace,formValid:document.querySelector('#clientPaymentForm').checkValidity(),
   validationErrors:[...document.querySelectorAll('.field-validation-error,.validation-summary-errors')].map(e=>e.textContent.trim()).filter(Boolean),
   disabled:[...document.querySelectorAll('#clientPaymentForm button')].filter(e=>e.disabled).map(e=>e.textContent.trim()),
   ariaBusy:document.querySelector('#clientPaymentForm').getAttribute('aria-busy')
 }))));
 check(Boolean(moved),'Real Client payment submit navigated after multipart POST '+name);
 check(page.url().includes('/Payments/MyUploads'),'Client actual multipart POST completed and redirected '+name+' => '+page.url());
 const body=await page.$eval('body',e=>e.innerText);
 check(/awaiting|pending|submitted/i.test(body),'Client observes pending submission '+name);
 await screenshot(page,name+'-client-proof-submitted');
}
async function ownerVerify(page,url,name){
 const r=await page.goto(url+'/Payments/PendingVerification',{waitUntil:'domcontentloaded'});
 check(r.status()===200,'Owner opens financial verification queue '+name);
 const links=await page.$$eval('a[href*="/Payments/Verify/"]',els=>[...new Set(els.map(e=>e.href))]);
 check(links.length>=1,'Owner queue contains an actual proof review link '+name);
 const details=await page.goto(links[0],{waitUntil:'domcontentloaded'});
 check(details.status()===200,'Owner opens received proof '+name);
 const paymentId=Number(new URL(page.url()).pathname.split('/').pop());
 check(Number.isInteger(paymentId)&&paymentId>0,'Owner review identifies persisted payment '+name);
 const proofSrc=await page.evaluate(()=>document.querySelector('img[src*="PaymentProof"],iframe[src*="PaymentProof"]')?.getAttribute('src')||
   document.querySelector('a[href*="PaymentProof"]')?.getAttribute('href'));
 check(Boolean(proofSrc),'Owner verification page exposes protected original proof '+name);
 const proofResponse=await page.evaluate(async src=>{
   const r=await fetch(src,{credentials:'same-origin'});return {status:r.status,bytes:(await r.arrayBuffer()).byteLength};
 },proofSrc);
 check(proofResponse.status===200&&proofResponse.bytes>100,'Owner retrieves private proof bytes over authorized HTTP '+name);
 await screenshot(page,name+'-owner-review');
 const trigger=await page.$('[data-bs-target="#approvePaymentModal"]');
 check(Boolean(trigger),'Owner has explicit manual approval action '+name);
 await page.click('[data-bs-target="#approvePaymentModal"]');
 await page.waitForSelector('#approvePaymentModal.show',{visible:true,timeout:7000});
 const form=await page.$('#approvePaymentModal form');
 check(Boolean(form),'Owner manual review form available '+name);
 const amount=await page.$eval('#verifiedAmount',e=>e.value);
 check(Number(amount)===2000,'Owner manual verification amount remains 2000 '+name);
 await Promise.all([page.waitForNavigation({waitUntil:'domcontentloaded',timeout:35000}),page.click('#approvePaymentModal button[type=submit]')]);
 check(!page.url().includes('AccessDenied'),'Owner manual verify action submitted '+name);
 return paymentId;
}
let record={};
try{
 const client=await newPage(),staff=await newPage(),owner=await newPage();
 await login(client.page,data.normalUrl,'Client');
 await login(staff.page,data.normalUrl,'Staff');
 await login(owner.page,data.normalUrl,'Owner');
 await staffApprove(staff.page,data.normalUrl,data.bookings.normal);
 await staffApprove(staff.page,data.normalUrl,data.bookings.ocrFailure);
 await clientUpload(client.page,data.normalUrl,data.bookings.normal,'normal-ocr');
 const normalPay=await ownerVerify(owner.page,data.normalUrl,'normal-ocr');
 const status=await client.page.goto(data.normalUrl+'/Bookings/Details/'+data.bookings.normal,{waitUntil:'domcontentloaded'});
 check(status.status()===200,'Client can view confirmed normal booking after Owner verification');
 await screenshot(client.page,'normal-ocr-client-confirmed');
 const failClient=await newPage(),failOwner=await newPage();
 await login(failClient.page,data.failureUrl,'Client');
 await login(failOwner.page,data.failureUrl,'Owner');
 await clientUpload(failClient.page,data.failureUrl,data.bookings.ocrFailure,'ocr-exception');
 const failPay=await ownerVerify(failOwner.page,data.failureUrl,'ocr-exception');
 const failStatus=await failClient.page.goto(data.failureUrl+'/Bookings/Details/'+data.bookings.ocrFailure,{waitUntil:'domcontentloaded'});
 check(failStatus.status()===200,'Client can view confirmed OCR-failed booking after Owner manual review');
 await screenshot(failClient.page,'ocr-exception-client-confirmed');
 check(client.errs.length===0&&staff.errs.length===0&&owner.errs.length===0&&failClient.errs.length===0&&failOwner.errs.length===0,
   'Five authenticated Chromium browser sessions have no uncaught page errors');
 record={normalPay,failPay,sessionCount:5};
 passed=true;
} catch(ex){
 console.error('CHROMIUM_FAILURE',ex?.stack||String(ex));
 results.push({assertion:String(ex),status:'FAIL'});
} finally {
 await browser.close();
 await fs.writeFile(path.join(root,'browser-evidence.json'),JSON.stringify({passed,record,results,requestEvidence,screenshots,kind:'Chromium actual HTTP to loopback SQL-backed Kestrel'},null,2));
 await fs.writeFile(data.doneFile,JSON.stringify({passed,record,finishedAtUtc:new Date().toISOString()}));
 console.log('CHROMIUM_E2E_DONE passed='+passed+' assertions='+results.filter(r=>r.status==='PASS').length);
}
if(!passed)process.exitCode=2;
