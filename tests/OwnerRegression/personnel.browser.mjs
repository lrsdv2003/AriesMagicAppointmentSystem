// Actual synthetic SQL-backed Razor output. All traffic intercepted; no live service access.
import fs from 'node:fs/promises';import path from 'node:path';import assert from 'node:assert/strict';
import puppeteer from '../../.tmp-owner-build/puppeteer-with-runtime.mjs';
const owner=await fs.readFile('.tmp-owner-build/personnel-owner.synthetic.html','utf8');
const admin=await fs.readFile('.tmp-owner-build/personnel-admin.synthetic.html','utf8');
const enrollment=await fs.readFile('.tmp-owner-build/personnel-enrollment.synthetic.html','utf8');
const output='.tmp-owner-build/personnel-visual';await fs.mkdir(output,{recursive:true});
const browser=await puppeteer.launch({headless:true,executablePath:process.env.PUPPETEER_EXECUTABLE_PATH||'C:/Program Files/BraveSoftware/Brave-Browser/Application/brave.exe'});
try {
 const page=await browser.newPage();const errors=[],requests=[],results=[];page.on('pageerror',e=>errors.push(e.message));
 await page.setRequestInterception(true);page.on('request',async req=>{
  requests.push(req.url());const url=new URL(req.url());if(url.hostname!=='personnel.test')return req.abort();
  if(req.isNavigationRequest())return req.respond({contentType:'text/html',body:url.pathname.startsWith('/PersonnelEnrollment')?enrollment:url.pathname.endsWith('/AdminFixture')?admin:owner});
  if(req.method()!=='GET')return req.respond({status:403,body:''});
  try {const file=path.resolve('wwwroot','.'+url.pathname);assert(file.startsWith(path.resolve('wwwroot')+path.sep));return req.respond({contentType:url.pathname.endsWith('.css')?'text/css':url.pathname.endsWith('.js')?'application/javascript':url.pathname.endsWith('.woff2')?'font/woff2':'application/octet-stream',body:await fs.readFile(file)});}catch{return req.respond({status:404,body:''});}
 });
 for(const width of [1440,768,390,320])for(const kind of ['owner','admin','enrollment']) {
  await page.setViewport({width,height:900});
  await page.goto('http://personnel.test/'+(kind==='owner'?'Personnel':kind==='admin'?'Personnel/AdminFixture':'PersonnelEnrollment/Accept#synthetic-user:'+ 'A'.repeat(64)),{waitUntil:'networkidle0'});
  if(kind!=='enrollment'){
   assert.equal(await page.$$eval('select[name="Role"] option',options=>options.some(o=>o.value==='Owner')),false);
   assert.equal(await page.$('input[type="password"]'),null);
   if(kind==='admin')assert.equal(await page.$('select[name="Role"] option[value="Admin"]'),null);
   assert(await page.$$eval('form[method="post"]',forms=>forms.every(f=>f.querySelector('input[name="__RequestVerificationToken"]'))));
  }else{
   assert.equal(await page.evaluate(()=>location.hash),'');assert.equal(await page.$eval('#enrollmentToken',e=>e.value),'A'.repeat(64));
   assert.equal(await page.$('input[name="Role"]'),null);
   await page.focus('#Password');await page.keyboard.type('Synthetic-password-for-browser');await page.keyboard.press('Tab');
   assert.equal(await page.$eval('#ConfirmPassword',e=>e===document.activeElement),true);
  }
  await page.addScriptTag({path:'.tmp-owner-build/browser-tools/node_modules/axe-core/axe.min.js'});
  const axe=await page.evaluate(()=>axe.run('#mainContent'));
  if(axe.violations.length)await fs.writeFile(`${output}/axe-${kind}-${width}.json`,JSON.stringify(axe.violations,null,2));
  assert.equal(axe.violations.length,0,kind+' '+JSON.stringify(axe.violations.map(x=>x.id)));
  assert.equal(errors.length,0,errors.join('\n'));
  assert(!requests.some(x=>{const u=new URL(x);return (u.pathname+u.search).includes('A'.repeat(64));}),'Enrollment secret must not enter an HTTP request path or query');
  await page.screenshot({path:`${output}/${kind}-${width}.png`,fullPage:true});results.push({kind,width,AxeViolations:0,JavaScriptErrors:0});
 }
 await fs.writeFile(`${output}/results.json`,JSON.stringify(results,null,2));console.log(JSON.stringify(results));
}finally{await browser.close();}
