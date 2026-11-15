import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {default: puppeteer}=await import('../../.tmp-owner-build/puppeteer-with-runtime.mjs');
const template=await fs.readFile(path.join(process.env.TEMP,'AriesOwnerSnapshots','Payments-Upload.html'),'utf8');
const matching=template.match(/<option[^>]*value="(\d+)"[^>]*>/);
const bookingId=Number(matching?.[1]||1);
const context=[{Id:bookingId,Reference:'BK-TEST-001',Total:10000,Verified:0,Balance:10000,LatestPaymentId:0,
  PaymentOptions:[{Key:'Minimum',Name:'Minimum downpayment',Amount:2000,Remaining:8000,Explanation:'Secure your booking.'},{Key:'Half',Name:'50% of booking total',Amount:5000,Remaining:5000,Explanation:'Pay half.'},{Key:'Full',Name:'Full payment',Amount:10000,Remaining:0,Explanation:'Pay in full.'}]}];
let html=template.replace(/(<script type="application\/json" id="clientBookingContext">)[\s\S]*?(<\/script>)/,'$1'+JSON.stringify(context)+'$2');
assert(html.includes('BK-TEST-001'));
const browser=await puppeteer.launch({headless:true,executablePath:'C:/Program Files/BraveSoftware/Brave-Browser/Application/brave.exe'});
try {
 const page=await browser.newPage();
 await page.setRequestInterception(true);
 page.on('request',async request=>{
  try{
   const url=new URL(request.url());
   if(url.hostname!=='owner.test')return request.abort();
   if(request.isNavigationRequest())return request.respond({contentType:'text/html',body:html});
   const fp=path.resolve('wwwroot','.'+url.pathname);
   if(!fp.startsWith(path.resolve('wwwroot')+path.sep))return request.abort();
   const kind=path.extname(fp)==='.js'?'application/javascript':path.extname(fp)==='.css'?'text/css':'application/octet-stream';
   return request.respond({contentType:kind,body:await fs.readFile(fp)});
  }catch{return request.respond({status:404,body:''});}
 });
 await page.goto('https://owner.test/Payments/Upload',{waitUntil:'networkidle2'});
 const state=await page.evaluate(()=>({
  options:document.querySelectorAll('#clientPaymentForm input[name=PaymentOption]').length,
  checked:document.querySelectorAll('#clientPaymentForm input[name=PaymentOption]:checked').length,
  hiddenVersion:document.getElementById('ExpectedLatestPaymentId')?.value,
  proofHidden:document.getElementById('paymentProof')?.hidden
 }));
 console.log('UI_INITIAL_STATE',JSON.stringify(state));
 assert.equal(state.options,3,'Test fixture must render 3 available choices');
 assert.equal(state.hiddenVersion,'0','Payment version should match synthetic fixture');
 await page.click('#continuePayment');
 const next=await page.evaluate(()=>({proofHidden:document.getElementById('paymentProof').hidden,checked:document.querySelectorAll('input[name=PaymentOption]:checked').length}));
 console.log('UI_AFTER_CONTINUE',JSON.stringify(next));
 assert.equal(state.checked,1,'Minimum must remain selected after dynamic refresh');
 assert.equal(next.proofHidden,false,'Valid default payment choice advances to proof upload');
 const input=await page.$('#ProofImage');
 await input.uploadFile('tests/ReleaseIntegration/Fixtures/payment.png');
 await page.click('#paymentAcknowledgement');
 const prepared=await page.evaluate(()=>{
   const form=document.querySelector('#clientPaymentForm');
   const payload=new FormData(form);
   const file=payload.get('ProofImage');
   return {valid:form.checkValidity(),booking:payload.get('BookingId'),option:payload.get('PaymentOption'),
    version:payload.get('ExpectedLatestPaymentId'),fileName:file?.name,fileSize:file?.size,
    ack:payload.get('AcknowledgesPaymentTerms')};
 });
 console.log('UI_READY_TO_POST',JSON.stringify(prepared));
 assert.equal(prepared.valid,true,'Actual browser must accept the safe receipt fixture');
 assert.equal(prepared.option,'Minimum');
 assert.equal(prepared.version,'0');
 assert(prepared.fileSize>0 && prepared.fileName.endsWith('.png'));
 await page.setViewport({width:390,height:844,deviceScaleFactor:1});
 const evidence=path.resolve('.tmp-owner-build/predefense-audit-20261115/payment-upload-step-browser-fixture.png');
 await fs.mkdir(path.dirname(evidence),{recursive:true});
 await page.screenshot({path:evidence,fullPage:true});
 console.log('BROWSER_FIXTURE_SCREENSHOT',evidence);
 console.log('PAYMENT_WIZARD_BROWSER_REGRESSION_PASS');
}finally{await browser.close();}
