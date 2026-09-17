import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {default:puppeteer}=await import(process.env.PUPPETEER_MODULE||'puppeteer');
const root=process.cwd(), snapshots=path.join(process.env.TEMP,'AriesOwnerSnapshots');
const browser=await puppeteer.launch({headless:true}), page=await browser.newPage();
let current='Services-Edit', post;
await page.setRequestInterception(true);
page.on('request',async req=>{
 const url=new URL(req.url());
 if(url.hostname==='cdn.jsdelivr.net'&&url.pathname.includes('fullcalendar')&&url.pathname.endsWith('.js'))return req.respond({contentType:'application/javascript',body:await fs.readFile(path.join(process.env.TEMP,'aries-fullcalendar.js'))});
 if(url.hostname!=='owner.test')return req.abort();
 if(req.isNavigationRequest()){if(req.method()==='POST')post=new URLSearchParams(req.postData());return req.respond({contentType:'text/html',body:await fs.readFile(path.join(snapshots,current+'.html'),'utf8')});}
 if(url.pathname.startsWith('/Communications/'))return req.respond({contentType:'application/json',body:'{"totalUnread":0,"conversations":[]}'});
 try{return req.respond({contentType:url.pathname.endsWith('.css')?'text/css':url.pathname.endsWith('.js')?'application/javascript':'application/octet-stream',body:await fs.readFile(path.join(root,'wwwroot',url.pathname))});}catch{return req.respond({status:404,body:''});}
});
const go=async name=>{current=name;await page.goto('http://owner.test/fixture/'+name,{waitUntil:'networkidle0'});};
try{
 for(const width of (process.env.SKIP_LAYOUT ? [] : [1440,1024,768,390])){
  await page.setViewport({width,height:900});
  for(const name of ['Services-Index','Services-Index-Staff','Services-Edit','Notifications-Index','Notifications-Index-Client','Calendar-StaffIndex']){
   await go(name);
   const issues=await page.evaluate(()=>{
    const problems=[];
    document.querySelectorAll('.staff-package-card,.package-editor-section,.notice-row,.staff-calendar-panel').forEach(el=>{const r=el.getBoundingClientRect();if(r.right>innerWidth+1||r.left<0)problems.push('overflow '+el.className);});
    document.querySelectorAll('.staff-day-content').forEach(el=>{const frame=el.closest('.fc-daygrid-day-frame'), date=frame.querySelector('.fc-daygrid-day-top');if(date.getBoundingClientRect().bottom>el.getBoundingClientRect().top+1)problems.push('date overlap');});
    return problems;
   });
   assert.deepEqual(issues,[],name+' '+width);
   if(name==='Calendar-StaffIndex'){assert(await page.$('.fc-daygrid-day'),'calendar must actually render');assert(await page.$('.staff-day-blocked-label'));assert(await page.$('.staff-day-summary'));}
   if(name==='Services-Index-Staff')assert.equal(await page.$$eval('.staff-package-actions a[href*="/Edit"]',els=>els.length),0);
   if(name==='Services-Index')assert(await page.$('.staff-package-actions a[href*="/Edit"]'));
   await page.screenshot({path:path.join(snapshots,'interface-'+name+'-'+width+'.png'),fullPage:true});
   console.log('PASS layout',name,width);
  }
 }
 await page.setViewport({width:1440,height:900});await go('Services-Edit');
 await page.$$eval('[data-remove-inclusion]',buttons=>buttons[1].click());
 await page.click('#addInclusion');
 const fields=await page.$$eval('#inclusions-container input[name$=".Name"]',els=>els.map(el=>el.name));
 assert.deepEqual(fields,Array.from({length:5},(_,i)=>'Inclusions['+i+'].Name'));
 await page.type('#inclusions-container .package-inclusion-row:last-child input[name$=".Name"]','New inclusion');
 await page.$eval('#Name',el=>{el.value='Edited package';el.dispatchEvent(new Event('input',{bubbles:true}));});
 assert.equal(await page.$eval('#previewPackageName',el=>el.textContent),'Edited package');
 assert.equal(await page.$$eval('#inclusions-container [id]',els=>new Set(els.map(el=>el.id)).size),await page.$$eval('#inclusions-container [id]',els=>els.length));
 await Promise.all([page.waitForNavigation({waitUntil:'networkidle0'}),page.click('#packageEditorForm button[type=submit]')]);
 assert.equal(post.get('Inclusions[1].Name'),'Inclusion 3');
 assert.equal(post.get('Inclusions[4].Name'),'New inclusion');
 assert.equal(post.get('Name'),'Edited package');
 assert.equal(post.get('Inclusions[0].IsRemovable'),'false');
 assert.equal(post.get('Inclusions[1].IsRemovable'),'true');
 await go('Services-Edit');
 await page.$$eval('[data-remove-inclusion]',buttons=>buttons.slice(1).forEach(button=>button.click()));
 await Promise.all([page.waitForNavigation({waitUntil:'networkidle0'}),page.click('#packageEditorForm button[type=submit]')]);
 assert(![...post.keys()].some(key=>/Inclusions\[[1-9]/.test(key)),'Deleted rows must not submit orphan checkbox values');
 console.log('PASS inclusion removal/addition, checkbox values, unique IDs, preview and submission');
 await go('Calendar-StaffIndex');
 await page.focus('.staff-calendar-blocked');
 await page.keyboard.press('Enter');await page.waitForSelector('#blockedDateModal.show');
 console.log('PASS blocked date keyboard preview');
 await go('Notifications-Index');
 assert.equal(await page.$$eval('.notice-category',els=>els.map(el=>el.textContent).join(',')),'PAYMENT,REFUND,BOOKING,SYSTEM');
 assert.equal(await page.$$eval('.notice-row:last-child .notice-action',els=>els.length),0);
 assert(await page.$('form[action*="MarkAllRead"] input[name="__RequestVerificationToken"]'));
 console.log('PASS notification labels, useful actions and anti-forgery form');
 await go('Notifications-Index-Client');
 assert.equal(await page.$eval('.notice-copy h2',el=>getComputedStyle(el).color),'rgb(30, 41, 59)');
 assert.equal(await page.$eval('.notice-copy p',el=>getComputedStyle(el).color),'rgb(71, 85, 105)');
 console.log('PASS Client notification contrast');
}finally{await browser.close();}
