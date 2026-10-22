import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {default:puppeteer}=await import(process.env.PUPPETEER_MODULE||'puppeteer');
const root=process.cwd(),snapshots=path.join(process.env.TEMP,'AriesOwnerSnapshots');
const browser=await puppeteer.launch({headless:true,executablePath:process.env.PUPPETEER_EXECUTABLE_PATH});
const page=await browser.newPage();
let current='Feedback-Create',posts=0,release;
await page.setRequestInterception(true);
page.on('request',async req=>{
 const url=new URL(req.url());
 if(url.hostname!=='owner.test')return req.abort();
 if(req.method()==='POST'&&url.pathname==='/Feedback/Create'){
  posts++;return new Promise(resolve=>{release=async()=>{await req.respond({contentType:'text/html',body:'<!doctype html><html><body><div id="feedbackFormErrors">A fixture validation error. Your answers are retained.</div></body></html>'});resolve();};});
 }
 if(req.isNavigationRequest())return req.respond({contentType:'text/html',body:await fs.readFile(path.join(snapshots,current+'.html'),'utf8')});
 if(url.pathname==='/FeedbackMedia/Private')return req.respond({contentType:'image/jpeg',body:await fs.readFile(path.join(root,'wwwroot/images/magic-performance-1.jpeg'))});
 if(url.pathname.startsWith('/Communications/'))return req.respond({contentType:'application/json',body:'{"count":0}'});
 try{return req.respond({contentType:url.pathname.endsWith('.css')?'text/css':url.pathname.endsWith('.js')?'application/javascript':url.pathname.endsWith('.svg')?'image/svg+xml':'application/octet-stream',body:await fs.readFile(path.join(root,'wwwroot',url.pathname))});}
 catch{return req.respond({status:404,body:''});}
});
const go=async name=>{current=name;await page.goto('http://owner.test/fixture/'+name,{waitUntil:'load'});await page.evaluate(async()=>{await document.fonts.ready;const images=[...document.querySelectorAll('main img')];images.forEach(i=>i.loading='eager');await Promise.all(images.map(i=>i.decode().catch(()=>{})));});};
let checks=0;const findings=[];
try {
 for(const width of [1440,768,390]) {
  await page.setViewport({width,height:960});
  for(const name of ['Feedback-Create','Feedback-Index','Feedback-Details','Testimonials-Index','Testimonials-Review']){
   await go(name);
   if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1))console.log(await page.evaluate(()=>[...document.querySelectorAll('body *')].filter(el=>{const r=el.getBoundingClientRect();return r.width>0&&r.right>innerWidth+1;}).slice(0,12).map(el=>({tag:el.tagName,cls:el.className,width:el.getBoundingClientRect().width,left:el.getBoundingClientRect().left}))));
   await page.screenshot({path:path.join(snapshots,'feedback-'+name+'-'+width+'.png'),fullPage:true});
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),name+' '+width+' overflow');
   assert(await page.$('h1'),name+' title');
   assert(await page.$$eval('main img',els=>els.every(i=>i.naturalWidth>0)),name+' image loading');
   if(process.env.AXE_CORE_PATH&&width===390){
    await page.addScriptTag({path:process.env.AXE_CORE_PATH});
    const violations=await page.evaluate(async()=>(await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa']}})).violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})));
    if(violations.length)findings.push({page:name,violations});
   }
   await page.screenshot({path:path.join(snapshots,'feedback-'+name+'-'+width+'.png'),fullPage:true});
   console.log('PASS feedback layout',name,width);checks++;
  }
 }
 await go('Feedback-Create');
 assert(!await page.$eval('#clientFeedbackForm',el=>el.checkValidity()));checks++;
 assert(await page.$$eval('[name$="Consent"]',els=>els.every(e=>!e.checked)));checks++;
 for(const name of ['OverallRating','PerformanceRating','BookingRating'])await page.click('[name="'+name+'"][value="5"]');
 await page.type('#Experience','My fixture celebration was memorable.');
 await page.type('#Suggestions','A private suggestion retained during validation.');
 await page.$eval('[name="Files"]',el=>el.scrollIntoView({block:'center'}));
 await (await page.$('[name="Files"]')).uploadFile(path.join(root,'wwwroot/images/magic-performance-1.jpeg'));
 await page.waitForSelector('#feedbackPreviews img');
 assert.equal(await page.$$eval('#feedbackPreviews img',els=>els.length),1);checks++;
 await page.$eval('#clientFeedbackForm',el=>{el.requestSubmit();el.requestSubmit();});
 await page.waitForFunction(()=>document.getElementById('clientFeedbackForm').getAttribute('aria-busy')==='true');
 assert(await page.$eval('#clientFeedbackForm button[type="submit"]',el=>el.disabled));checks++;
 while(!release)await new Promise(resolve=>setTimeout(resolve,10));
 await release();
 await page.waitForFunction(()=>!document.getElementById('clientFeedbackForm').hasAttribute('aria-busy'));
 assert.equal(posts,1);checks++;
 assert.equal(await page.$eval('#Experience',el=>el.value),'My fixture celebration was memorable.');checks++;
 assert(await page.$eval('#feedbackFormErrors',el=>el.textContent.includes('fixture validation error')));checks++;
 assert(await page.$eval('#feedbackFormErrors',el=>document.activeElement===el));checks++;
 await go('Testimonials-Review');
 assert(await page.$eval('[name="ConfirmPublication"]',el=>el.required&&!el.checked));checks++;
 assert(await page.$eval('[name="Decision"][value="Feature"]',el=>el.disabled));checks++;
 assert(await page.$$eval('[name="PublicMediaIds"]:disabled',els=>els.length>0));checks++;
 await fs.writeFile(path.join(snapshots,'feedback-accessibility.json'),JSON.stringify(findings,null,2));
 assert.deepEqual(findings,[],'Feedback accessibility violations; see feedback-accessibility.json');
 console.log('SUCCESS:',checks,'feedback browser checks');
}finally{await browser.close();}
