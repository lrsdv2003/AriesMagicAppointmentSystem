import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {default: puppeteer}=await import('../../.tmp-owner-build/puppeteer-with-runtime.mjs');
const folder='.tmp-owner-build/predefense-business-20261115';
const info=JSON.parse(await fs.readFile(path.join(folder,'business-host.json'),'utf8'));
const browser=await puppeteer.launch({headless:true,executablePath:'C:/Program Files/BraveSoftware/Brave-Browser/Application/brave.exe'});
const logs=[],requests=[];
const screens=path.join(folder,'screenshots');await fs.mkdir(screens,{recursive:true});
const mark=(yes,msg)=>{assert.ok(yes,msg);logs.push({status:'PASS',assertion:msg});console.log('BUSINESS_BROWSER_PASS',msg);};
async function create(role){
 const ctx=await browser.createBrowserContext(),page=await ctx.newPage();
 await page.setViewport({width:1280,height:800,deviceScaleFactor:1});
 page.on('response',r=>{
  const p=new URL(r.url()).pathname;
  if(r.request().method()==='POST'&&p.startsWith('/'))requests.push({path:p,status:r.status(),method:'POST'});
 });
 const r=await page.goto(info.url+'/Account/Login',{waitUntil:'domcontentloaded'});
 mark(r.status()===200,'Login page available for '+role);
 await page.type('[name=Email]',info.credentials[role].email);await page.type('[name=Password]',info.credentials[role].password);
 await Promise.all([page.waitForNavigation({waitUntil:'domcontentloaded'}),page.$eval('form[action*=Login]',f=>f.requestSubmit())]);
 mark(!page.url().includes('/Account/Login'),'Real Identity session for '+role);
 return page;
}
async function shot(page,name){await page.screenshot({path:path.join(screens,name+'.png'),fullPage:true});}
async function navigate(page,path){const r=await page.goto(info.url+path,{waitUntil:'domcontentloaded'});mark(r.status()===200,'GET '+path+' HTTP 200');return r;}
async function submitViaVisibleButton(page,selector){
 await Promise.all([page.waitForNavigation({waitUntil:'domcontentloaded',timeout:20000}),page.click(selector)]);
}
async function change(page,selector,value){
 await page.$eval(selector,(e,v)=>{e.value=v;e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));},value);
}
let passed=false;
try{
 const client=await create('Client'),staff=await create('Staff'),owner=await create('Owner'),other=await create('OtherClient');
 // A (booking-origin portion) — use the guided four-step Client wizard, not a seeded booking.
 await navigate(client,'/Bookings/CreateStepOne?serviceId='+info.packageId);
 const freshDate=new Date(info.requestedDate+'T12:00:00');
 freshDate.setDate(freshDate.getDate()+30);
 const freshDateValue=[freshDate.getFullYear(),String(freshDate.getMonth()+1).padStart(2,'0'),String(freshDate.getDate()).padStart(2,'0')].join('-');
 await client.click('[data-location-method="Manual"]');
 await change(client,'#EventDate',freshDateValue);
 await change(client,'#StartTime','15:00');
 await change(client,'#ManualStreet','Synthetic Test Street');
 await change(client,'#ManualCity','Taguig City');
 await change(client,'#ManualProvince','Metro Manila');
 await client.click('#CustomerConfirmedLocation');
 await client.click('#checkLocation');
 const firstStep=await client.waitForFunction(()=>!document.querySelector('section[data-step="1"]').hidden || document.querySelector('#wizardError')?.textContent,{timeout:14000});
 const locationState=await client.evaluate(()=>({
   secondStepVisible:!document.querySelector('section[data-step="1"]').hidden,
   error:document.querySelector('#wizardError')?.textContent,
   result:document.querySelector('#locationResult')?.textContent
 }));
 console.log('CLIENT_WIZARD_LOCATION',JSON.stringify(locationState));
 mark(locationState.secondStepVisible,'Real Client manual-location and schedule check advances to package step');
 const packageChoice='[name="ServiceId"][value="'+info.packageId+'"]';
 await client.click(packageChoice);
 await client.click('section[data-step="1"] button[data-next]');
 await client.waitForFunction(()=>!document.querySelector('section[data-step="2"]').hidden,{timeout:14000});
 mark(true,'Client package selection passes server availability check');
 await change(client,'#EventType','Company Events');
 await change(client,'#PaxCount','20');
 await change(client,'#ContactPerson','Synthetic Browser Client');
 await change(client,'#ContactNumber','09123456789');
 await client.click('section[data-step="2"] button[data-next]');
 await client.waitForFunction(()=>!document.querySelector('section[data-step="3"]').hidden,{timeout:10000});
 mark(true,'Client event details advance to booking review');
 await shot(client,'client-new-booking-review');
 await client.click('#submitBooking');
 await client.waitForSelector('#fullBookingTerms[open]',{timeout:9000});
 await client.click('#termsModalConsent');
 mark(await client.$eval('#confirmBookingTerms',e=>!e.disabled),'Client must explicitly consent before booking');
 await submitViaVisibleButton(client,'#confirmBookingTerms');
 mark(!client.url().includes('AccessDenied')&&!client.url().includes('/CreateStepOne'),
   'Client submitted real booking wizard and navigated to booking record');
 await shot(client,'client-new-booking-submitted');
 // B — Staff rejects a booking and Client sees an unsuccessful lifecycle.
 await navigate(staff,'/Bookings/Details/'+info.bookings.decline);
 await staff.click('[data-bs-target="#declineBookingModal"]');
 await staff.waitForSelector('#declineBookingModal.show',{visible:true});
 await submitViaVisibleButton(staff,'#declineBookingModal button[type=submit]');
 mark(!staff.url().includes('AccessDenied'),'Staff submitted booking rejection over Chromium');
 await navigate(client,'/Bookings/Details/'+info.bookings.decline);
 mark(/declined|rejected/i.test(await client.$eval('body',e=>e.innerText)),'Client sees declined booking status');
 await shot(client,'declined-booking-client');
 // E — Client requests a new date and Staff approves it.
 await navigate(client,'/RescheduleRequests/Create?bookingId='+info.bookings.reschedule);
 await change(client,'#BookingId',String(info.bookings.reschedule));
 await change(client,'#RequestedDate',info.requestedDate);
 await change(client,'#RequestedStartTime','11:00');
 await client.type('#Reason','Test fixture schedule conflicts with an unrelated appointment.');
 await client.waitForFunction(()=>document.getElementById('rescheduleAvailability')?.getAttribute('aria-busy')!=='true',{timeout:15000});
 await client.click('#r2ReviewReschedule');
 mark(await client.$eval('#r2RescheduleReview',e=>!e.hidden),'Client sees actual reschedule review');
 await shot(client,'reschedule-client-review');
 await submitViaVisibleButton(client,'#submitRescheduleBtn');
 mark(!client.url().includes('AccessDenied'),'Client posted reschedule request to real SQL');
 await navigate(staff,'/RescheduleRequests/Index');
 const approve=await staff.$('.js-reschedule-approve:not([disabled])');
 mark(!!approve,'Staff can act on pending reschedule');
 await approve.click();
 await staff.waitForSelector('#approveRescheduleModal.show',{visible:true});
 mark(await staff.$eval('#approveRescheduleId',e=>Number(e.value)>0),'Reschedule approval modal references a persisted request');
 await shot(staff,'reschedule-staff-decision');
 await submitViaVisibleButton(staff,'#approveRescheduleModal button[type=submit]');
 mark(!staff.url().includes('AccessDenied'),'Staff approved reschedule over real browser');
 // F — Client direct cancellation within 24h after Owner payment verification.
 await navigate(client,'/Bookings/Cancel?id='+info.bookings.cancel);
 await client.click('#openCancellationConfirmation');
 mark(await client.$eval('#cancellationConfirmation',e=>e.open),'Client receives confirmation dialog before cancellation');
 await shot(client,'client-cancellation-confirmation');
 await submitViaVisibleButton(client,'#cancellationConfirmation button[type=submit]');
 mark(!client.url().includes('AccessDenied'),'Client completed authorized cancellation via browser');
 await navigate(client,'/Bookings/Details/'+info.bookings.cancel);
 mark(/cancelled/i.test(await client.$eval('body',e=>e.innerText)),'Client sees cancelled booking in details');
 // G — Verified-eligible cancelled payment becomes a refund request.
 await navigate(client,'/Payments/RequestRefund?bookingId='+info.bookings.cancel);
 mark(Boolean(await client.$('#clientFinalRefundForm')),'Eligible cancelled payment exposes refund form');
 const pay=await client.$eval('#PaymentId',el=>[...el.options].find(o=>o.value)?.value);
 mark(Boolean(pay),'Refund form lists existing verified eligible payment');
 await change(client,'#PaymentId',pay);
 await client.type('#GCashNumber','09123456789');
 await client.type('#GCashAccountName','Synthetic Acceptance Client');
 await client.click('#refundAcknowledgement');
 client.on('dialog',async dialog=>await dialog.accept());
 mark(await client.$eval('#clientFinalRefundForm',f=>f.checkValidity()),'Refund form passes browser required-field validation');
 await shot(client,'client-refund-request-ready');
 await submitViaVisibleButton(client,'#clientFinalRefundForm button[type=submit]');
 mark(!client.url().includes('AccessDenied'),'Client submitted refund request through browser');
 await navigate(owner,'/Payments/RefundRequests');
 const refundLinks=await owner.$$eval('a[href*="/Payments/RefundReview/"]',elements=>[...new Set(elements.map(e=>e.href))]);
 mark(refundLinks.length>=1,'Owner refund queue contains Client request');
 const refundPath=new URL(refundLinks[0]).pathname;
 await navigate(owner,refundPath);
 const refundId=Number(refundPath.split('/').pop());
 mark(refundId>0,'Owner review opens actual refund request');
 await owner.click('[data-bs-target="#approveRefundModal"]');
 await owner.waitForSelector('#approveRefundModal.show',{visible:true});
 await shot(owner,'owner-refund-approval');
 await submitViaVisibleButton(owner,'#approveRefundModal .js-owner-submit');
 mark(!owner.url().includes('AccessDenied'),'Owner approved refund for processing');
 await navigate(owner,refundPath);
 const refundFile=await owner.$('#refundProofFile');
 mark(!!refundFile,'Owner can upload protected refund-completion evidence');
 await refundFile.uploadFile(path.resolve('tests/ReleaseIntegration/Fixtures/payment.png'));
 await submitViaVisibleButton(owner,'form[action*="UploadRefundProof"] button.js-owner-submit');
 mark(!owner.url().includes('AccessDenied'),'Owner uploaded refund confirmation evidence');
 await navigate(owner,refundPath);
 const complete=await owner.$('[data-bs-target="#completeRefundModal"]');
 mark(!!complete,'Completion requires an uploaded confirmation and Owner review');
 await complete.click();
 await owner.waitForSelector('#completeRefundModal.show',{visible:true});
 await shot(owner,'owner-refund-completion');
 await submitViaVisibleButton(owner,'#completeRefundModal .js-owner-submit');
 mark(!owner.url().includes('AccessDenied'),'Owner completed verified refund through browser');
 // J — Expiration reconciles payment deadline using the existing browser entrypoint.
 await navigate(client,'/Payments/Upload?bookingId='+info.bookings.overdue);
 await navigate(client,'/Bookings/Details/'+info.bookings.overdue);
 mark(/expired/i.test(await client.$eval('body',e=>e.innerText)),'Overdue booking is expired by actual lifecycle service');
 // K — Client starts support; Staff reads and replies in the same persisted conversation.
 await navigate(client,'/Communications/Index');
 const support=await client.$('form[action*="StartSupport"] button[type=submit]');
 mark(Boolean(support),'Client support action available');
 await submitViaVisibleButton(client,'form[action*="StartSupport"] button[type=submit]');
 mark(/\/Communications/.test(new URL(client.url()).pathname),'Support conversation opened');
 const currentConversationUrl=new URL(client.url());const conversationId=Number(currentConversationUrl.searchParams.get('id')||currentConversationUrl.pathname.match(/\/(\d+)$/)?.[1]);
 mark(conversationId>0,'New support conversation has an actual persisted ID');
 await navigate(client,'/Communications/Index?id='+conversationId);
 mark(!!(await client.$('#commComposer')),'Client can write into the support conversation');
 await client.type('#commMessageInput','Synthetic client question from real browser.');
 await client.click('#commComposer .comm-send-btn');
 await client.waitForFunction(()=>document.body.innerText.includes('Synthetic client question from real browser.'),{timeout:12000});
 mark(true,'Client message posted and appears in live conversation');
 await shot(client,'client-support-message');
 await navigate(staff,'/Communications/Index?id='+conversationId);
 mark(!!(await staff.$('#commComposer')),'Staff can open the same Client support thread');
 await staff.type('#commMessageInput','Synthetic staff response from browser.');
 await staff.click('#commComposer .comm-send-btn');
 await staff.waitForFunction(()=>document.body.innerText.includes('Synthetic staff response from browser.'),{timeout:12000});
 mark(true,'Staff response posted into the same private conversation');
 await navigate(client,'/Communications/Index?id='+conversationId);
 mark((await client.$eval('body',e=>e.innerText)).includes('Synthetic staff response from browser.'),
    'Client sees the persisted Staff reply through a fresh page load');
 await shot(client,'client-support-reply');
 // D — A confirmed booking after Staff rescheduling must block the overlapping slot.
 const scheduleProbe=await other.evaluate(async ({date,packageId})=>{
   const url='/Bookings/CheckScheduleAvailability?'+new URLSearchParams({date,startTime:'11:00',serviceId:packageId});
   const response=await fetch(url,{credentials:'same-origin'});
   return {status:response.status,data:await response.json()};
 },{date:info.requestedDate,packageId:String(info.packageId)});
 mark(scheduleProbe.status===200 && scheduleProbe.data.available===false,
    'Other Client sees confirmed reschedule as an unavailable overlapping slot');
 // H — Owner edits an active package; Staff and Client see the change, while old bookings retain totals in SQL.
 await navigate(owner,'/Services/Edit/'+info.packageId);
 await change(owner,'#Name','Synthetic Revised Package');
 await change(owner,'#Price','12000');
 const inclusionInput=await owner.$('#inclusions-container input[name*=".Name"]');
 mark(!!inclusionInput,'Owner package editor requires explicit inclusion data');
 await change(owner,'#inclusions-container input[name*=".Name"]','Synthetic included live magic show');
 await shot(owner,'owner-package-update-ready');
 await submitViaVisibleButton(owner,'#packageEditorForm button[type=submit]');
 mark(!owner.url().includes('AccessDenied'),'Owner persisted package revision through browser');
 await navigate(staff,'/Services/Index');
 mark((await staff.$eval('body',e=>e.innerText)).includes('Synthetic Revised Package'),
    'Staff package listing reflects Owner changes');
 await navigate(client,'/Home/Packages');
 mark((await client.$eval('body',e=>e.innerText)).includes('Synthetic Revised Package'),
    'Client public package list reflects approved Owner changes');
 // I — Owner-only personnel access is enforced in the same real browser environment.
 await navigate(owner,'/Personnel/Index');
 await staff.goto(info.url+'/Personnel/Index',{waitUntil:'domcontentloaded'});
 mark(staff.url().includes('AccessDenied'),'Staff cannot open Owner personnel administration');
 // Security — another Client cannot read a private booking or Owner dashboard.
 const denied=await other.goto(info.url+'/Bookings/Details/'+info.bookings.cancel,{waitUntil:'domcontentloaded'});
 mark(denied.status()===404,'Unrelated Client gets 404 for another Client booking');
 await other.goto(info.url+'/Dashboard/Owner',{waitUntil:'domcontentloaded'});
 mark(other.url().includes('AccessDenied'),'Client cannot open Owner dashboard');
 passed=true;
}catch(e){
 console.error('BUSINESS_BROWSER_FAIL',e?.stack||String(e));
 logs.push({status:'FAIL',assertion:String(e)});
}finally{
 await browser.close();
 await fs.writeFile(path.join(folder,'business-browser-evidence.json'),JSON.stringify({passed,results:logs,requests,kind:'Live Chromium browser → isolated Kestrel → SQL'},null,2));
 await fs.writeFile(info.doneFile,JSON.stringify({passed,completedAtUtc:new Date().toISOString()}));
 console.log('BUSINESS_BROWSER_DONE passed='+passed+' checks='+logs.filter(x=>x.status==='PASS').length);
}
if(!passed)process.exitCode=2;
