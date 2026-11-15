import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import puppeteer from '../../.tmp-owner-build/puppeteer-with-runtime.mjs';
const baseline=process.argv.includes('--baseline');
const out=`tmp/pdfs/owner-round1/${baseline?'before':'after'}`;
await fs.mkdir(out,{recursive:true});
const browser=await puppeteer.launch({headless:true,executablePath:'C:/Program Files/BraveSoftware/Brave-Browser/Application/brave.exe'});
const pages=['Dashboard-Owner','Payments-PendingVerification','Payments-Verify','Payments-RefundRequests','Payments-RefundReview','Reports-Index','Personnel-Index','InternalProfile-Index','History-Index','Communications-Index','Notifications-Index','Calendar-StaffIndex-Owner','Bookings-StaffDetails-Owner'];
let current;
const results=[];
try{
const page=await browser.newPage();
await page.setRequestInterception(true);
page.on('request',async req=>{const u=new URL(req.url());if(u.hostname==='cdn.jsdelivr.net'&&u.pathname.includes('fullcalendar')&&u.pathname.endsWith('.js'))return req.respond({contentType:'application/javascript',body:await fs.readFile(path.join(process.env.TEMP,'aries-fullcalendar.js'))});if(u.hostname!=='owner.test')return req.abort();
if(req.isNavigationRequest())return req.respond({contentType:'text/html',body:await fs.readFile(path.join(process.env.TEMP,'AriesOwnerSnapshots',current+'.html'),'utf8')});
if(u.pathname.startsWith('/Communications/'))return req.respond({contentType:'application/json',body:'{"totalUnread":0,"conversations":[]}'});
try{const file=path.resolve('wwwroot','.'+u.pathname);assert(file.startsWith(path.resolve('wwwroot')+path.sep));return req.respond({contentType:u.pathname.endsWith('.css')?'text/css':u.pathname.endsWith('.js')?'application/javascript':'application/octet-stream',body:await fs.readFile(file)});}catch{return req.respond({status:404,body:''});}});
for(const width of [1440,768,390,320])for(const name of pages){current=name;await page.setViewport({width,height:900});await page.goto('http://owner.test/'+name,{waitUntil:'networkidle0'});
await page.screenshot({path:`${out}/${name}-${width}.png`,fullPage:true});
const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1);
await page.addScriptTag({path:'.tmp-owner-build/browser-tools/node_modules/axe-core/axe.min.js'});
const axe=await page.evaluate(()=>axe.run('#mainContent'));
results.push({name,width,overflow,violations:axe.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}))});
if(!baseline){assert(!overflow,`${name} overflow at ${width}`);assert.equal(axe.violations.length,0,`${name} axe at ${width}: ${JSON.stringify(results.at(-1).violations)}`);}
if(!baseline&&name==='Dashboard-Owner'){
await page.focus('#ownerPaymentsTab');await page.keyboard.press('ArrowRight');assert.equal(await page.$eval('#ownerRefundsTab',e=>e.getAttribute('aria-selected')),'true');assert.equal(await page.$eval('#ownerPaymentsPanel',e=>e.hidden),true);await page.keyboard.press('Home');assert.equal(await page.$eval('#ownerPaymentsTab',e=>e.getAttribute('aria-selected')),'true');}
if(!baseline&&name==='Personnel-Index'){await page.type('#ownerPersonnelSearch','staff@example.test');assert.equal(await page.$$eval('#ownerPersonnelTable tbody tr',r=>r.filter(e=>!e.hidden).length),1);await page.$eval('#ownerPersonnelSearch',e=>{e.value='';e.dispatchEvent(new Event('input'))});}
if(!baseline && [1440,390].includes(width)) {
const captureStyle=await page.addStyleTag({content:'html,body,.dashboard-shell,.dashboard-main{height:auto!important;max-height:none!important;overflow:visible!important} #mainContent{height:auto!important;max-height:none!important;overflow:visible!important}'});
await page.screenshot({path:`${out}/full-${name}-${width}.png`,fullPage:true});await captureStyle.evaluate(e=>e.remove());
}
console.log('PASS rendered',name,width);}
}finally{await fs.writeFile(`${out}/results.json`,JSON.stringify(results,null,2));await browser.close();}
