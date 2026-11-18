// Current Razor output from the disposable SQL integration fixture; no live requests.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import puppeteer from '../../.tmp-owner-build/puppeteer-with-runtime.mjs';
const html=await fs.readFile('.tmp-owner-build/feedback-uploads-disabled.synthetic.html','utf8');
const browser=await puppeteer.launch({headless:true,executablePath:process.env.PUPPETEER_EXECUTABLE_PATH||'C:/Program Files/BraveSoftware/Brave-Browser/Application/brave.exe'});
const output='.tmp-owner-build/feedback-disabled-visual';await fs.mkdir(output,{recursive:true});
try {
 const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.setRequestInterception(true);
 page.on('request',async req=>{
  const url=new URL(req.url());if(url.hostname!=='feedback.test')return req.abort();
  if(req.isNavigationRequest())return req.respond({contentType:'text/html',body:html});
  if(req.method()!=='GET')return req.respond({status:403,body:''});
  try {const file=path.resolve('wwwroot','.'+url.pathname);assert(file.startsWith(path.resolve('wwwroot')+path.sep));await req.respond({contentType:url.pathname.endsWith('.css')?'text/css':url.pathname.endsWith('.js')?'application/javascript':'application/octet-stream',body:await fs.readFile(file)});}catch{await req.respond({status:404,body:''});}
 });
 const results=[];
 for(const width of [1440,390,320]) {
  await page.setViewport({width,height:900});await page.goto('http://feedback.test/Feedback/Create',{waitUntil:'networkidle0'});
  assert.equal(await page.$('#clientFeedbackForm input[name="Files"]'),null);
  assert((await page.$eval('#clientFeedbackForm',e=>e.innerText)).includes('temporarily unavailable'));
  await page.evaluate(()=>{for(const name of ['OverallRating','PerformanceRating','BookingRating'])document.querySelector(`[name="${name}"][value="5"]`).click();});
  await page.click('#openFeedbackConsent');assert(await page.$eval('#feedbackConsentDialog',e=>e.open));
  await page.click('[name="SharingPreference"][value="Private"]');await page.click('#feedbackConsentDialog [data-close-feedback-consent]');
  assert.equal(errors.length,0,errors.join('\n'));
  await page.addScriptTag({path:'.tmp-owner-build/browser-tools/node_modules/axe-core/axe.min.js'});
  const axe=await page.evaluate(()=>axe.run('#clientFeedbackForm'));
  if(axe.violations.length)await fs.writeFile(`${output}/axe-diagnostic.json`,JSON.stringify(axe.violations,null,2));
  assert.equal(axe.violations.length,0,JSON.stringify(axe.violations.map(x=>x.id)));
  await page.screenshot({path:`${output}/${width}.png`,fullPage:true});results.push({width,PickerAbsent:true,ConsentDialogOperable:true,JavaScriptErrors:errors.length,AxeFormViolations:axe.violations.length});
 }
 await fs.writeFile(`${output}/results.json`,JSON.stringify(results,null,2));console.log(JSON.stringify(results));
}finally{await browser.close();}
