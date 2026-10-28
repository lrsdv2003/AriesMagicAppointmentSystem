// Loopback-only About visual and interaction QA. No production traffic or user data.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const base=process.env.ARIES_PUBLIC_BASE||'http://127.0.0.1:5295';
assert(['http://127.0.0.1:5295','http://127.0.0.1:5310'].includes(base),'Only approved loopback fixtures are accepted');
import puppeteer from '../../.tmp-owner-build/audit-tools/node_modules/puppeteer/lib/puppeteer/puppeteer.js';
const output=path.resolve('.tmp-owner-build/client-ui-review',process.argv[2]||'phase-three');await fs.mkdir(output,{recursive:true});
const browser=await puppeteer.launch({headless:true,executablePath:process.env.ARIES_VISUAL_BROWSER||'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
const results=[],checks=[],failures=[],accessibility=[];
const check=(value,label)=>{assert(value,label);checks.push(label);};
try{
 const page=await browser.newPage();await page.setRequestInterception(true);
 page.on('request',r=>r.url().startsWith((base+'/'))||/^(data:|blob:|about:)/.test(r.url())?r.continue():r.abort());
 page.on('pageerror',e=>failures.push(e.message));page.on('response',r=>{if(r.status()>=400)failures.push(`${r.status()} ${new URL(r.url()).pathname}`);});
 const go=()=>page.goto((base+'/Home/About'),{waitUntil:'networkidle0'});
 const scan=async(name)=>{await page.addScriptTag({path:path.resolve('.tmp-owner-build/audit-tools/node_modules/axe-core/axe.min.js')});const r=await page.evaluate(async()=>{const r=await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21aa','wcag22aa']}});return {passes:r.passes.length,violations:r.violations.map(x=>({id:x.id,targets:x.nodes.map(n=>n.target)})),incomplete:r.incomplete.map(x=>({id:x.id,targets:x.nodes.map(n=>n.target)}))};});accessibility.push({name,...r});};
 for(const width of [320,375,390,430,768,1024,1440]){
  await page.setViewport({width,height:width<768?932:1000,deviceScaleFactor:1});await go();await page.evaluate(()=>document.fonts.ready);
  for(let y=0;y<await page.evaluate(()=>document.documentElement.scrollHeight);y+=650){await page.evaluate(y=>scrollTo(0,y),y);await new Promise(r=>setTimeout(r,80));}
  await page.evaluate(()=>scrollTo({top:0,behavior:'instant'}));
  const data=await page.evaluate(()=>({width:innerWidth,height:document.documentElement.scrollHeight,scrollWidth:document.documentElement.scrollWidth,
   h1:document.querySelectorAll('h1').length,headingSize:getComputedStyle(document.querySelector('h1')).fontSize,
   activeNav:[...document.querySelectorAll('.client-nav-links [aria-current=page]')].map(x=>x.textContent),
   galleries:document.querySelectorAll('.wa-gallery-item').length,testimonials:document.querySelectorAll('.wa-feedback-card').length,
   booking:[...document.querySelectorAll('main a[href*="Bookings/"]')].map(x=>x.getAttribute('href')),
   images:[...document.querySelectorAll('main picture img')].map(x=>({src:x.currentSrc,loading:x.loading,priority:x.fetchPriority,width:x.getBoundingClientRect().width,height:x.getBoundingClientRect().height,naturalWidth:x.naturalWidth,naturalHeight:x.naturalHeight,fit:getComputedStyle(x).objectFit})),
   overflow:[...document.querySelectorAll('body *')].filter(x=>{const r=x.getBoundingClientRect(),rail=x.closest('.wa-gallery-grid');if(rail&&rail!==x){const b=rail.getBoundingClientRect();if(b.left>=0&&b.right<=innerWidth&&getComputedStyle(rail).overflowX==='auto')return false;}return r.width&&getComputedStyle(x).position!=='fixed'&&(r.left< -1||r.right>innerWidth+1);}).slice(0,20).map(x=>x.tagName+'.'+x.className),
   sections:[...document.querySelectorAll('main>section')].map(x=>({name:x.className,height:Math.round(x.getBoundingClientRect().height)})),
   fonts:{body:getComputedStyle(document.body).fontFamily,heading:getComputedStyle(document.querySelector('h1')).fontFamily},
   portraitCreditBelowImage:document.querySelector('.wa-intro figcaption').getBoundingClientRect().top>=document.querySelector('.wa-intro img').getBoundingClientRect().bottom,
   resources:performance.getEntriesByType('resource').filter(x=>x.initiatorType==='img').map(x=>({name:new URL(x.name).pathname,bytes:x.transferSize}))
  }));results.push(data);
  check(data.scrollWidth===width&&!data.overflow.length,`No About overflow at ${width}px`);
  check(data.h1===1&&parseFloat(data.headingSize)<=48,`One restrained H1 at ${width}px`);
  check(data.fonts.body.includes('DM Sans')&&data.fonts.heading.includes('Libre Caslon'),`Approved fonts at ${width}px`);
  check(data.activeNav.length===1&&data.activeNav[0]==='About',`About navigation current at ${width}px`);
  check(data.images.length===6&&data.images.every(x=>x.naturalWidth>0),`All six authentic photographs loaded at ${width}px`);
  check(data.images.every(x=>x.src.endsWith('.webp')&&x.fit==='contain'),`Responsive full-composition images at ${width}px`);
  check(data.images[0].loading==='eager'&&data.images.slice(1).every(x=>x.loading==='lazy'),`Priority and lazy-loading at ${width}px`);
  check(data.portraitCreditBelowImage,`Portrait watermark is not covered by caption at ${width}px`);
  check(data.galleries===3,`Curated three-image gallery at ${width}px`);
  check(data.testimonials===0,`No fabricated empty testimonials at ${width}px`);
  check(data.booking.length>0&&data.booking.every(x=>x==='/Bookings/CreateStepOne'),`Existing booking route at ${width}px`);
  await page.screenshot({path:path.join(output,`about-${width}-full.png`),fullPage:true});await page.screenshot({path:path.join(output,`about-${width}-viewport.png`)});
  if(width===320||width===1440)await scan(`About ${width}px`);
 }
 await page.setViewport({width:320,height:932});await go();
 await page.keyboard.press('Tab');check(await page.$eval('.magic-skip-link',x=>document.activeElement===x),'First Tab reaches skip link');await page.keyboard.press('Enter');check(await page.evaluate(()=>document.activeElement.id==='mainContent'),'Skip link focuses main');
 await page.click('.navbar-toggler');await page.waitForFunction(()=>document.querySelector('.navbar-toggler').getAttribute('aria-expanded')==='true');await page.screenshot({path:path.join(output,'mobile-navigation.png')});await scan('Expanded About menu');
 check(await page.$eval('#clientNavbar',x=>x.getBoundingClientRect().right<=innerWidth),'Expanded About menu fits');await page.click('.navbar-toggler');await page.waitForFunction(()=>document.querySelector('.navbar-toggler').getAttribute('aria-expanded')==='false');
 await page.focus('.wa-faq summary');await page.keyboard.press('Enter');check(await page.$eval('.wa-faq details',x=>x.open),'FAQ keyboard expansion');await page.keyboard.press('Enter');check(await page.$eval('.wa-faq details',x=>!x.open),'FAQ keyboard collapse');check(await page.$$eval('.wa-faq summary',n=>n.every(x=>x.getBoundingClientRect().height>=48)),'FAQ touch targets');
 await page.focus('.wa-gallery-grid');await page.keyboard.press('ArrowRight');await page.waitForFunction(()=>document.querySelector('.wa-gallery-grid').scrollLeft>0);checks.push('Gallery rail scrolls by keyboard');
 await page.focus('.wa-gallery-item:last-child');await page.waitForFunction(()=>{const r=document.querySelector('.wa-gallery-item:last-child').getBoundingClientRect();return r.left>=0&&r.right<=innerWidth;});checks.push('Last photograph revealed by focus');
 await page.keyboard.press('Enter');await page.waitForFunction(()=>document.querySelector('#clientGallery').open&&document.querySelector('#clientGallery>img').naturalWidth>0);check(await page.evaluate(()=>document.activeElement.hasAttribute('data-gallery-close')),'Gallery initial focus');await scan('About gallery dialog');await page.screenshot({path:path.join(output,'mobile-gallery-dialog.png')});await page.keyboard.press('Escape');check(await page.evaluate(()=>!document.querySelector('#clientGallery').open&&document.activeElement.classList.contains('wa-gallery-item')),'Escape restores photo focus');
 await page.click('.wa-gallery-item');await page.waitForFunction(()=>document.querySelector('#clientGallery').open);await page.mouse.click(4,4);check(await page.$eval('#clientGallery',x=>!x.open),'Backdrop dismisses gallery');
 await page.emulateMediaFeatures([{name:'prefers-reduced-motion',value:'reduce'}]);check(await page.$eval('.wa-button',x=>getComputedStyle(x).transitionDuration==='0s'),'Reduced motion honored');await page.emulateMediaFeatures([]);
 const fallback=await browser.newPage();await fallback.setRequestInterception(true);fallback.on('request',r=>r.url().includes('/about/artist-')||!r.url().startsWith((base+'/'))&&!/^(data:|blob:|about:)/.test(r.url())?r.abort():r.continue());await fallback.goto((base+'/Home/About'),{waitUntil:'networkidle0'});
 check(await fallback.$eval('.wa-intro .wa-photo',x=>x.classList.contains('cx-media-unavailable')&&!x.querySelector('.cx-media-fallback').hidden),'Missing portrait has readable fallback');await fallback.$eval('.wa-gallery-item',x=>x.dataset.gallerySrc='/images/absent-about-fixture.jpeg');await fallback.click('.wa-gallery-item');await fallback.waitForFunction(()=>!document.querySelector('[data-gallery-status]').hidden);check(await fallback.$eval('#clientGallery>img',x=>x.hidden),'Failed gallery image has status');await fallback.keyboard.press('Escape');check(await fallback.$eval('#clientGallery',x=>!x.open),'Failed gallery remains dismissible');await fallback.close();
 await fs.writeFile(path.join(output,'metrics.json'),JSON.stringify({results,checks,accessibility,failures},null,2));
 console.log(JSON.stringify({output,checks:checks.length,viewports:results.map(x=>({width:x.width,height:x.height,overflow:x.overflow})),accessibility,failures}));assert(!failures.length,'About normal resource/console errors');assert(accessibility.every(x=>!x.violations.length),'About accessibility violations');
}finally{await browser.close();}
