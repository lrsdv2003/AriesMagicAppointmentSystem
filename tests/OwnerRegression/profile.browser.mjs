import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {default:puppeteer}=await import(process.env.PUPPETEER_MODULE||'puppeteer');
const root=process.cwd(),snapshots=path.join(process.env.TEMP,'AriesOwnerSnapshots');
const browser=await puppeteer.launch({headless:true}),page=await browser.newPage();
let current='InternalProfile-Index',lastRequest;
await page.setRequestInterception(true);
page.on('request',async req=>{
 const url=new URL(req.url());
 if(url.hostname!=='owner.test')return req.abort();
 if(req.isNavigationRequest()){lastRequest={url,method:req.method(),body:req.postData()};return req.respond({contentType:'text/html',body:await fs.readFile(path.join(snapshots,current+'.html'),'utf8')});}
 if(url.pathname.startsWith('/Communications/'))return req.respond({contentType:'application/json',body:'{"totalUnread":0,"conversations":[]}'}); 
 try{return req.respond({contentType:url.pathname.endsWith('.css')?'text/css':url.pathname.endsWith('.js')?'application/javascript':'application/octet-stream',body:await fs.readFile(path.join(root,'wwwroot',url.pathname))});}catch{return req.respond({status:404,body:''});}
});
const go=async name=>{current=name;await page.goto('http://owner.test/fixture/'+name,{waitUntil:'networkidle0'});};
try{
 for(const width of (process.env.SKIP_LAYOUT ? [] : [1440,1024,768,390])){
  await page.setViewport({width,height:900});
  for(const name of ['InternalProfile-Index','InternalProfile-Index-Staff','InternalProfile-Index-Admin','UserManagement-Index']){
   await go(name);
   const issues=await page.evaluate(()=>{
    const result=[];
    if(document.documentElement.scrollWidth>innerWidth+1)result.push('document overflow');
    const ids=[...document.querySelectorAll('[id]')].map(x=>x.id);
    if(new Set(ids).size!==ids.length)result.push('duplicate IDs');
    document.querySelectorAll('.account-page input:not([type=hidden]),.am-filter-bar input,.am-filter-bar select').forEach(input=>{
     if(!input.labels.length&&!input.getAttribute('aria-label'))result.push('missing label '+input.id);
     const rect=input.getBoundingClientRect();if(rect.right>innerWidth+1||rect.left<0)result.push('input overflow '+input.id);
    });
    document.querySelectorAll('.account-page form').forEach(form=>{if(!form.querySelector('[name=__RequestVerificationToken]'))result.push('missing CSRF');});
    return result;
   });
   assert.deepEqual(issues,[],name+' '+width);
   if(name.startsWith('Internal')){
    assert.equal(await page.$$eval('.account-page input[name]',els=>els.filter(e=>/^(Id|Role|Email|IsActive|EmailConfirmed)$/.test(e.name)).length),0);
    if(width===390)assert.equal(await page.$eval('.account-grid',e=>getComputedStyle(e).gridTemplateColumns.split(' ').length),1);
   }else if(width===390){
    const rects=await page.$$eval('.am-filter-field',els=>els.map(e=>e.getBoundingClientRect().width));
    assert(rects.every(w=>w>250),'Filters fill mobile width');
   }
   await page.screenshot({path:path.join(snapshots,'profile-'+name+'-'+width+'.png'),fullPage:true});
   console.log('PASS profile/filter responsive',name,width);
  }
 }
 await page.setViewport({width:1440,height:900});
 await go('InternalProfile-Index');
 await page.click('#accountMenu');
 assert(await page.$('.dropdown-menu.show a[href="/InternalProfile/Index"]'));
 assert(await page.$('.dropdown-menu.show a[href$="#security"]'));
 await page.keyboard.press('Escape');
 await page.focus('#Personal_FullName');
 assert.equal(await page.$eval('#Personal_FullName',e=>getComputedStyle(e).outlineStyle),'solid');
 for(const id of ['Password_CurrentPassword','Password_NewPassword','Password_ConfirmPassword']){
  await page.click('[data-toggle-password="'+id+'"]');
  assert.equal(await page.$eval('#'+id,e=>e.type),'text');
  await page.click('[data-toggle-password="'+id+'"]');
  assert.equal(await page.$eval('#'+id,e=>e.type),'password');
 }
 await page.type('#Password_CurrentPassword','somepassword');
 await page.click('[data-toggle-password="Password_CurrentPassword"]');
 await page.click('#security button[type=reset]');
 assert.equal(await page.$eval('#Password_CurrentPassword',e=>e.value),'');
 assert.equal(await page.$eval('#Password_CurrentPassword',e=>e.type),'password');
 const photo=path.join(snapshots,'fixture-photo.png');
 await fs.writeFile(photo,Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=','base64'));
 await (await page.$('#profilePhoto')).uploadFile(photo);
 await page.waitForFunction(()=>!document.getElementById('photoPreviewPanel').hidden&&document.getElementById('photoPreview').naturalWidth>0);
 await page.click('#cancelPhoto');
 assert.equal(await page.$eval('#profilePhoto',e=>e.value),'');
 assert(await page.$eval('#photoPreviewPanel',e=>e.hidden));
 await page.$eval('#Personal_FullName',e=>{e.value='Updated Browser User';});
 await Promise.all([page.waitForNavigation(),page.click('form[action="/InternalProfile/Edit"] button[type=submit]')]);
 assert.equal(lastRequest.method,'POST');
 const body=new URLSearchParams(lastRequest.body);
 assert.equal(body.get('Personal.FullName'),'Updated Browser User');
 assert(body.has('__RequestVerificationToken'));
 await go('UserManagement-Index');
 await page.type('#userSearch','staff@example.test');
 await page.select('#userRole','Staff');await page.select('#userStatus','Active');await page.select('#userVerification','Verified');
 await Promise.all([page.waitForNavigation(),page.click('.am-filter-bar button')]);
 assert.equal(lastRequest.url.searchParams.get('search'),'staff@example.test');
 assert.equal(lastRequest.url.searchParams.get('role'),'Staff');
 assert.equal(lastRequest.url.searchParams.get('status'),'Active');
 assert.equal(lastRequest.url.searchParams.get('verification'),'Verified');
 await Promise.all([page.waitForNavigation(),page.click('.am-filter-bar a')]);
 assert.equal(lastRequest.url.pathname,'/UserManagement/Index');
 assert.equal(lastRequest.url.search,'');
 console.log('PASS account menu, keyboard focus, password visibility/reset, photo preview/cancel, profile submission and filter apply/clear');
}finally{await browser.close();}
