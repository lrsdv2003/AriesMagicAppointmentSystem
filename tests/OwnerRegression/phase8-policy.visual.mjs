// Read-only real MVC pages through the existing isolated synthetic role fixture.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {default:puppeteer}=await import(process.env.PUPPETEER_MODULE||'puppeteer');
const state=JSON.parse(await fs.readFile(process.env.PHASE8_FIXTURE_STATE,'utf8'));
for(const value of Object.values(state.Urls)){const url=new URL(value);assert.equal(url.hostname,'127.0.0.1');assert.equal(url.protocol,'http:');}
const output=process.env.PHASE8_VISUAL_OUTPUT||'.tmp-owner-build/reconciled-release/current-visual';
await fs.mkdir(output,{recursive:true});
const browser=await puppeteer.launch({headless:true,executablePath:process.env.PUPPETEER_EXECUTABLE_PATH});
const page=await browser.newPage();let current;
const errors=[],results=[];
page.on('pageerror',error=>errors.push({page:current,message:error.message}));
const routes={Staff:[['Dashboard','/Dashboard/Staff'],['Bookings','/Bookings/Index'],['Calendar','/Calendar/Index'],['Rescheduling','/RescheduleRequests'],['Messages','/Communications'],['Details','/Bookings/Details/'+state.BookingIds[0]],['Profile','/InternalProfile']],
 Admin:[['Dashboard','/Dashboard/Admin'],['Users','/UserManagement'],['Packages','/Services'],['Calendar','/Calendar'],['Settings','/Calendar'],['Profile','/InternalProfile']],
 Owner:[['Dashboard','/Dashboard/Owner'],['Payments','/Payments/PendingVerification'],['Refunds','/Payments/RefundRequests'],['FinancialSummary','/Reports'],['Notifications','/Notifications'],['PaymentReview','/Payments/Verify/'+state.PaymentIds[0]],['Profile','/InternalProfile']],
 Client:[['Homepage','/'],['Experiences','/Home/Packages'],['About','/Home/About'],['Gallery','/Home/Gallery'],['Dashboard','/Bookings/Dashboard']]};
try{
 for(const width of [1440,1024,768,430,390,375,320])for(const [role,views] of Object.entries(routes))for(const [label,route] of views){
  current=role+'-'+label;await page.setViewport({width,height:960});
  const response=await page.goto(state.Urls[role]+route,{waitUntil:'networkidle0',timeout:30000});
  assert.equal(response.status(),200,current+' did not load');
  await page.evaluate(async()=>{await document.fonts.ready;for(const image of document.images)image.loading='eager';await Promise.all([...document.images].map(i=>Promise.race([i.decode().catch(()=>{}),new Promise(resolve=>setTimeout(resolve,10000))])));});
  const metrics=await page.evaluate(()=>{const main=document.querySelector('#mainContent');return {pageOverflow:document.documentElement.scrollWidth>innerWidth+1,paneOverflow:main?main.scrollWidth>main.clientWidth+2:false,font:getComputedStyle(document.body).fontFamily,background:getComputedStyle(document.body).backgroundColor,internal:document.body.classList.contains('midnight-workspace'),brokenImages:[...document.images].filter(i=>i.offsetWidth&&(!i.complete||i.naturalWidth===0)).map(i=>i.getAttribute('src'))};});
  if(role!=='Client')assert(metrics.internal,current+' lost workspace shell');
  await page.screenshot({path:path.join(output,current+'-'+width+'.png'),fullPage:true});
  let violations=[];
  if(process.env.PHASE8_CHECK_AXE==='1'){
   await page.addScriptTag({path:process.env.AXE_CORE_PATH});
   violations=await page.evaluate(async()=>(await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa']}})).violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)})));
  }
  results.push({page:current,width,status:response.status(),...metrics,violations});
  if(label==='Dashboard'&&role!=='Client'&&[1440,390].includes(width)){
   await page.evaluate(()=>{const main=document.querySelector('#mainContent');if(main)main.scrollTop=main.scrollHeight;});
   await page.screenshot({path:path.join(output,current+'-lower-'+width+'.png'),fullPage:true});
  }
  console.log('CAPTURE',current,width);
 }
 await fs.writeFile(path.join(output,'results.json'),JSON.stringify({results,errors,sourceData:'Isolated synthetic LocalDB; no production data'},null,2));
 assert.deepEqual(errors,[],'Browser errors; see results.json');
 assert.deepEqual(results.filter(r=>r.pageOverflow||r.paneOverflow||r.brokenImages.length||r.violations.length),[],'Visual/accessibility findings; see results.json');
 console.log('SUCCESS:',results.length,'real MVC responsive states',process.env.PHASE8_CHECK_AXE==='1'?'with axe scans':'baseline capture');
}finally{await fs.writeFile(path.join(output,'results.json'),JSON.stringify({results,errors,sourceData:'Isolated synthetic LocalDB; no production data'},null,2));await browser.close();}
