// Real public Razor pages and loopback-only synthetic SQL catalog. No live requests.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const base=process.env.ARIES_PUBLIC_BASE||'http://127.0.0.1:5295';
assert(['http://127.0.0.1:5295','http://127.0.0.1:5310'].includes(base),'Only approved loopback fixtures are accepted');
import puppeteer from '../../.tmp-owner-build/audit-tools/node_modules/puppeteer/lib/puppeteer/puppeteer.js';
const label=process.argv[2]||'phase-two',baseline=label==='phase-two-before';
const catalog=baseline?null:JSON.parse(await fs.readFile(path.resolve(process.env.ARIES_PUBLIC_CATALOG||'.tmp-owner-build/client-visual-catalog.json'),'utf8'));
assert(/^[a-z-]+$/.test(label));
const output=path.resolve('.tmp-owner-build/client-ui-review',label);
await fs.mkdir(output,{recursive:true});
const browser=await puppeteer.launch({headless:true,executablePath:process.env.ARIES_VISUAL_BROWSER||'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
const results=[],checks=[],failures=[],accessibility=[];
const check=(value,name)=>{assert(value,name);checks.push(name);};
try {
 const page=await browser.newPage();
 await page.setRequestInterception(true);
 page.on('request',r=>r.url().startsWith((base+'/'))||/^(data:|blob:|about:)/.test(r.url())?r.continue():r.abort());
 page.on('pageerror',error=>failures.push(error.message));
 page.on('response',r=>{if(r.status()>=400)failures.push(`${r.status()} ${new URL(r.url()).pathname}`);});
 for(const width of baseline?[430,1440]:[320,375,390,430,768,1024,1440]){
  await page.setViewport({width,height:width<768?932:1000,deviceScaleFactor:1});
  const routes=baseline?[['experiences','/Home/Packages'],['detail','/Home/Package/1'],]:[
   ['experiences','/Home/Packages'],...catalog.Packages.map((p,i)=>[i===0?'detail':`detail-${p.Id}`,`/Home/Package/${p.Id}`])
  ];
  for(const [name,url] of routes){
   await page.goto(base+url,{waitUntil:'networkidle0'});
   await page.evaluate(()=>document.fonts.ready);
   for(let y=0;y<await page.evaluate(()=>document.documentElement.scrollHeight);y+=700){await page.evaluate(y=>scrollTo(0,y),y);await new Promise(r=>setTimeout(r,60));}
   await page.evaluate(()=>scrollTo({top:0,behavior:'instant'}));
   const data=await page.evaluate(()=>({width:innerWidth,height:document.documentElement.scrollHeight,scrollWidth:document.documentElement.scrollWidth,
    h1:document.querySelectorAll('h1').length,bodyFont:getComputedStyle(document.body).fontFamily,
    cards:[...document.querySelectorAll('.we-card')].map(x=>({height:Math.round(x.getBoundingClientRect().height),id:Number(x.querySelector('h2').id.replace('package-','')),name:x.querySelector('h2').textContent,price:x.querySelector('.we-price strong').textContent.trim(),duration:x.querySelector('.we-duration').textContent.trim(),description:x.querySelector('.we-description')?.textContent||null,inclusions:[...x.querySelectorAll('.we-inclusions li')].map(i=>i.textContent),descriptionSize:x.querySelector('.we-description')?getComputedStyle(x.querySelector('.we-description')).fontSize:'15px'})),
    detail:document.querySelector('.we-detail-heading')?{name:document.querySelector('h1').textContent,price:document.querySelector('.we-price strong').textContent.trim(),duration:document.querySelector('.we-detail-heading>p').textContent,inclusions:[...document.querySelectorAll('.we-detail-inclusions li')].map(i=>i.textContent),description:document.querySelector('.we-description').textContent,booking:document.querySelector('.we-detail-book').getAttribute('href')}:null,
    photo:document.querySelector('.we-editorial-photo img')?{naturalWidth:document.querySelector('.we-editorial-photo img').naturalWidth,naturalHeight:document.querySelector('.we-editorial-photo img').naturalHeight,fit:getComputedStyle(document.querySelector('.we-editorial-photo img')).objectFit,position:getComputedStyle(document.querySelector('.we-editorial-photo img')).objectPosition,renderWidth:document.querySelector('.we-editorial-photo img').getBoundingClientRect().width,renderHeight:document.querySelector('.we-editorial-photo img').getBoundingClientRect().height}:null,
    broken:[...document.images].filter(x=>x.getBoundingClientRect().width>0&&(!x.complete||!x.naturalWidth)).map(x=>x.getAttribute('src')),
    overflow:[...document.querySelectorAll('body *')].filter(x=>{const r=x.getBoundingClientRect();return r.width&&getComputedStyle(x).position!=='fixed'&&(r.left< -1||r.right>innerWidth+1);}).slice(0,15).map(x=>x.tagName+'.'+x.className),
    bookingLinks:[...document.querySelectorAll('main a[href*="CreateStepOne"]')].map(x=>x.getAttribute('href')),
    activeNav:[...document.querySelectorAll('.client-nav-links [aria-current="page"]')].map(x=>x.textContent.trim())
   }));
   results.push({name,...data});
   if(!baseline){
    check(data.scrollWidth===width&&data.overflow.length===0,`${name}: no overflow at ${width}px`);
    check(data.h1===1,`${name}: single H1 at ${width}px`);
    check(!data.broken.length,`${name}: images loaded at ${width}px`);
    check(data.bodyFont.includes('DM Sans'),`${name}: DM Sans at ${width}px`);
    check(data.activeNav.length===1&&data.activeNav[0]==='Experiences',`${name}: current navigation at ${width}px`);
    check(data.bookingLinks.length>0&&data.bookingLinks.every(x=>x.startsWith('/Bookings/CreateStepOne')),`${name}: booking routes at ${width}px`);
    if(name==='experiences'){
     check(data.cards.length===catalog.Packages.length,`SQL fixture catalog renders at ${width}px`);
     check(new Set(data.cards.map(x=>x.height)).size===1,`Equal card heights at ${width}px`);
     check(data.cards.every(x=>parseFloat(x.descriptionSize)>=15),`Readable descriptions at ${width}px`);
     for(const expected of catalog.Packages){
      const actual=data.cards.find(x=>x.id===expected.Id);
      const price='₱'+expected.Price.toLocaleString('en-US',{minimumFractionDigits:expected.Price%1?2:0,maximumFractionDigits:2});
      check(actual?.name===expected.Name&&actual.price===price&&actual.description===expected.Description&&actual.duration.startsWith(`${expected.DurationInHours} `),`Listing matches SQL fixture values for ID ${expected.Id} at ${width}px`);
      check(JSON.stringify(actual.inclusions)===JSON.stringify(expected.Inclusions.map(i=>i.Name)),`Listing matches every SQL fixture inclusion for ID ${expected.Id} at ${width}px`);
     }
     check(data.photo.fit==='contain'&&Math.abs(data.photo.renderWidth/data.photo.renderHeight-data.photo.naturalWidth/data.photo.naturalHeight)<.01,`Full performance-photo composition retained at ${width}px`);
    }else{
     const expected=catalog.Packages.find(p=>url.endsWith('/'+p.Id));
     const price='₱'+expected.Price.toLocaleString('en-US',{minimumFractionDigits:expected.Price%1?2:0,maximumFractionDigits:2});
     check(data.detail.name===expected.Name&&data.detail.price===price&&data.detail.description===expected.Description&&data.detail.duration.startsWith(`${expected.DurationInHours} `),`Detail matches SQL fixture values for ID ${expected.Id} at ${width}px`);
     check(JSON.stringify(data.detail.inclusions)===JSON.stringify(expected.Inclusions.map(i=>i.Name)),`Detail matches every SQL fixture inclusion for ID ${expected.Id} at ${width}px`);
     check(new URL(data.detail.booking,base).searchParams.get('serviceId')===String(expected.Id),`Detail retains package ID ${expected.Id} at ${width}px`);
    }
   }
   await page.screenshot({path:path.join(output,`${name}-${width}-full.png`),fullPage:true});
   await page.screenshot({path:path.join(output,`${name}-${width}-viewport.png`)});
   if(!baseline&&(width===320||width===1440)){
    await page.addScriptTag({path:path.resolve('.tmp-owner-build/audit-tools/node_modules/axe-core/axe.min.js')});
    const scan=await page.evaluate(async()=>{const r=await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21aa','wcag22aa']}});return {passes:r.passes.length,violations:r.violations.map(x=>({id:x.id,targets:x.nodes.map(n=>n.target)})),incomplete:r.incomplete.map(x=>({id:x.id,targets:x.nodes.map(n=>n.target)}))};});
    accessibility.push({name,width,...scan});
   }
  }
 }
 if(!baseline){
  await page.setViewport({width:320,height:932});
  await page.goto((base+'/Home/Packages'),{waitUntil:'networkidle0'});
  await page.keyboard.press('Tab');check(await page.$eval('.magic-skip-link',x=>x===document.activeElement),'First Tab reaches skip link');
  await page.keyboard.press('Enter');check(await page.evaluate(()=>document.activeElement.id==='mainContent'),'Skip link moves focus to content');
  await page.click('.navbar-toggler');await page.waitForFunction(()=>document.querySelector('.navbar-toggler').getAttribute('aria-expanded')==='true');
  await page.screenshot({path:path.join(output,'mobile-navigation.png')});
  await page.addScriptTag({path:path.resolve('.tmp-owner-build/audit-tools/node_modules/axe-core/axe.min.js')});
  const menuScan=await page.evaluate(async()=>{const r=await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21aa','wcag22aa']}});return {passes:r.passes.length,violations:r.violations.map(x=>({id:x.id,targets:x.nodes.map(n=>n.target)})),incomplete:r.incomplete.map(x=>({id:x.id,targets:x.nodes.map(n=>n.target)}))};});
  accessibility.push({name:'mobile-navigation',width:320,...menuScan});
  check(await page.$eval('#clientNavbar',x=>x.getBoundingClientRect().right<=innerWidth),'Mobile menu fits');
  await page.click('.navbar-toggler');await page.waitForFunction(()=>document.querySelector('.navbar-toggler').getAttribute('aria-expanded')==='false');
  const hrefs=await page.$$eval('.we-card-book',nodes=>nodes.map(x=>x.getAttribute('href')));
  check(new Set(hrefs).size===catalog.Packages.length&&hrefs.every(x=>/[?&]serviceId=\d+/.test(x)),'Each card selects a distinct real package ID');
  await page.focus('.we-card-details');await page.keyboard.press('Enter');await page.waitForFunction(()=>location.pathname.startsWith('/Home/Package/'));
  check(await page.$eval('h1',(x,name)=>x.textContent.trim()===name,catalog.Packages[0].Name),'Keyboard detail navigation opens the selected package');
  check(await page.$eval('.we-detail-book',(x,id)=>new URL(x.href).searchParams.get('serviceId')===String(id),catalog.Packages[0].Id),'Detail booking retains package ID');
  await page.focus('.we-faq summary');await page.keyboard.press('Enter');check(await page.$eval('.we-faq details',x=>x.open),'Keyboard FAQ expands');
  await page.keyboard.press('Enter');check(await page.$eval('.we-faq details',x=>!x.open),'Keyboard FAQ collapses');
  check(await page.$$eval('.we-faq summary',nodes=>nodes.every(x=>x.getBoundingClientRect().height>=48)),'FAQ targets are at least 48px');
  await page.emulateMediaFeatures([{name:'prefers-reduced-motion',value:'reduce'}]);
  check(await page.$eval('.we-button',x=>getComputedStyle(x).transitionDuration==='0s'),'Reduced motion honored');
  await page.emulateMediaFeatures([]);
 }
 await fs.writeFile(path.join(output,'metrics.json'),JSON.stringify({results,checks,accessibility,failures},null,2));
 console.log(JSON.stringify({output,checks:checks.length,viewports:results.map(x=>({name:x.name,width:x.width,height:x.height,overflow:x.overflow})),accessibility,failures}));
 if(!baseline){assert(!failures.length,'No normal page console/resource failures');assert(accessibility.every(x=>!x.violations.length),'Accessibility violations require review');}
}finally{await browser.close();}
