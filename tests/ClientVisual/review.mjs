// Browser review of a loopback-only, synthetic public homepage. No production requests.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const base=process.env.ARIES_PUBLIC_BASE||'http://127.0.0.1:5295';
assert(['http://127.0.0.1:5295','http://127.0.0.1:5310'].includes(base),'Only approved loopback fixtures are accepted');
const {default:puppeteer}=await import('../../.tmp-owner-build/audit-tools/node_modules/puppeteer/lib/puppeteer/puppeteer.js');
const label=process.argv[2]||'after';
const baseline=label==='before'||label==='refinement-before';
assert(/^[a-z-]+$/.test(label));
const output=path.resolve('.tmp-owner-build/client-ui-review',label);
await fs.mkdir(output,{recursive:true});
let executablePath=process.env.ARIES_VISUAL_BROWSER||await puppeteer.executablePath();
try {await fs.access(executablePath);} catch {executablePath='C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';}
const browser=await puppeteer.launch({headless:true,executablePath});
const results=[];
const interactions=[];
const check=(value,name)=>{assert(value,name);interactions.push(name);};
try {
 const page=await browser.newPage();
 await page.setRequestInterception(true);
 page.on('request',request=>{const url=request.url();
  if(url.startsWith((base+'/'))||/^(data:|blob:|about:)/.test(url))request.continue();else request.abort();
 });
 const failures=[];
 page.on('pageerror',error=>failures.push(error.message));
 page.on('response',response=>{if(response.status()>=400)failures.push(`${response.status()} ${new URL(response.url()).pathname}`);});
 for(const width of label==='before'?[1440,430]:[320,375,390,430,768,1024,1440]){
  await page.setViewport({width,height:width<768?932:1000,deviceScaleFactor:1});
  await page.goto((base+'/'),{waitUntil:'networkidle0'});
  await page.evaluate(()=>document.fonts.ready);
  for(let y=0;y<await page.evaluate(()=>document.documentElement.scrollHeight);y+=650){
   await page.evaluate(y=>window.scrollTo(0,y),y);
   await new Promise(resolve=>setTimeout(resolve,90));
  }
  await page.evaluate(()=>window.scrollTo(0,0));
  await new Promise(resolve=>setTimeout(resolve,150));
  const metrics=await page.evaluate(()=>({
   width:innerWidth,scrollWidth:document.documentElement.scrollWidth,height:document.documentElement.scrollHeight,
   sections:[...document.querySelectorAll('main>section,.ws-occasions,.client-footer')].map(x=>({name:x.id||x.className,height:Math.round(x.getBoundingClientRect().height)})),
   cards:[...document.querySelectorAll('.ws-package')].map(x=>({height:Math.round(x.getBoundingClientRect().height),width:Math.round(x.getBoundingClientRect().width),minHeight:getComputedStyle(x).minHeight,descriptionSize:getComputedStyle(x.querySelector('.ws-package-description')).fontSize})),
   h1:document.querySelectorAll('h1').length,
   headings:[...document.querySelectorAll('h1,h2')].map(x=>({text:x.textContent.trim(),size:getComputedStyle(x).fontSize})),
   brokenImages:[...document.images].filter(x=>x.getAttribute('src')&&x.getBoundingClientRect().width>0&&(!x.complete||!x.naturalWidth)).map(x=>x.getAttribute('src')),
   packageCount:document.querySelectorAll('.ws-package').length,
   links:[...document.querySelectorAll('a')].map(x=>({text:x.textContent.trim(),href:x.getAttribute('href')})),
   overflow:[...document.querySelectorAll('body *')].filter(x=>{
    const r=x.getBoundingClientRect(),rail=x.closest('.ws-gallery-grid');
    if(rail&&rail!==x){const bounds=rail.getBoundingClientRect();if(bounds.left>=0&&bounds.right<=innerWidth&&getComputedStyle(rail).overflowX==='auto')return false;}
    return r.width&&getComputedStyle(x).position!=='fixed'&&(r.left< -1||r.right>innerWidth+1);
   }).slice(0,15).map(x=>x.tagName+'.'+x.className)
  }));
  results.push(metrics);
  if(!baseline){
   check(metrics.scrollWidth<=width,`No horizontal page overflow at ${width}px`);
   check(metrics.overflow.length===0,`No off-screen content at ${width}px`);
   check(metrics.brokenImages.length===0,`Visible images loaded at ${width}px`);
   check(metrics.h1===1,`One page heading at ${width}px`);
   check(metrics.packageCount===3,`All source-seeded packages render at ${width}px`);
   check(metrics.links.filter(x=>/Let’s plan|Plan your event|Select package/.test(x.text)).every(x=>x.href?.startsWith('/Bookings/CreateStepOne')),`Existing booking routes retained at ${width}px`);
   check(new Set(metrics.cards.map(x=>x.height)).size===1,`Equal package card heights at ${width}px`);
   check(metrics.cards.every(x=>parseFloat(x.descriptionSize)>=15),`Readable package body text at ${width}px`);
  }
  await page.screenshot({path:path.join(output,`viewport-${width}.png`)});
  await page.screenshot({path:path.join(output,`full-${width}.png`),fullPage:true});
  if(width===1440||width===430){
   const name=width===1440?'desktop':'mobile';
   await page.screenshot({path:path.join(output,`${name}-full.png`),fullPage:true});
   await page.screenshot({path:path.join(output,`${name}-viewport.png`)});
  }
 }
 let accessibility;
 if(!baseline){
  await page.setViewport({width:430,height:932,deviceScaleFactor:1});
  await page.goto((base+'/'),{waitUntil:'networkidle0'});
  await page.keyboard.press('Tab');
  check(await page.evaluate(()=>document.activeElement?.classList.contains('magic-skip-link')),'Skip link is the first keyboard target');
  await page.keyboard.press('Enter');
  check(await page.evaluate(()=>document.activeElement?.id==='mainContent'),'Skip link moves focus to main content');
  await page.click('.navbar-toggler');
  await page.waitForFunction(()=>document.querySelector('.navbar-toggler').getAttribute('aria-expanded')==='true');
  check(await page.evaluate(()=>document.querySelector('#clientNavbar').getBoundingClientRect().width<=innerWidth),'Expanded mobile navigation fits the viewport');
  await page.click('.client-nav-links a[href="/Home/About"]');
  await page.waitForFunction(()=>location.pathname==='/Home/About');
  await page.waitForFunction(()=>document.querySelector('.navbar-toggler').getAttribute('aria-expanded')==='false');
  interactions.push('Mobile anchor navigation closes the menu');
  await page.goto((base+'/'),{waitUntil:'networkidle0'});
  await page.focus('.ws-faq summary');await page.keyboard.press('Enter');
  check(await page.$eval('.ws-faq details',x=>x.open),'FAQ expands from keyboard');
  await page.keyboard.press('Enter');
  check(await page.$eval('.ws-faq details',x=>!x.open),'FAQ collapses from keyboard');
  check(await page.$$eval('.ws-faq summary',nodes=>nodes.every(x=>x.getBoundingClientRect().height>=48)),'FAQ controls retain generous touch targets');
  check(await page.$eval('.cx-testimonials',()=>false).catch(()=>true),'No empty testimonial placeholder appears without verified feedback');
  await page.focus('.ws-gallery-grid');await page.keyboard.press('ArrowRight');
  await page.waitForFunction(()=>document.querySelector('.ws-gallery-grid').scrollLeft>0);
  interactions.push('Mobile photograph rail scrolls with the keyboard');
  await page.focus('.ws-gallery-item:last-child');
  await page.waitForFunction(()=>{const r=document.querySelector('.ws-gallery-item:last-child').getBoundingClientRect();return r.left>=0&&r.right<=innerWidth;});
  check(await page.$eval('.ws-gallery-item:last-child',x=>{const r=x.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth;}),'Last mobile photograph is reachable and fully visible by keyboard');
  await page.focus('.ws-gallery-item');
  await page.click('.ws-gallery-item');
  await page.waitForFunction(()=>document.querySelector('#clientGallery').open&&document.querySelector('#clientGallery img').naturalWidth>0);
  check(await page.evaluate(()=>document.activeElement?.hasAttribute('data-gallery-close')),'Gallery opens with focus on its close control');
  await page.keyboard.press('Escape');
  check(await page.evaluate(()=>!document.querySelector('#clientGallery').open&&document.activeElement?.classList.contains('ws-gallery-item')),'Escape closes the gallery and restores opener focus');
  await page.click('.ws-gallery-item');await page.mouse.click(4,4);
  check(await page.$eval('#clientGallery',x=>!x.open),'Gallery backdrop closes the viewer');
  await page.emulateMediaFeatures([{name:'prefers-reduced-motion',value:'reduce'}]);
  await page.hover('.ws-gallery-item');
  check(await page.$eval('.ws-gallery-item img',x=>getComputedStyle(x).transitionDuration==='0s'&&getComputedStyle(x).transform==='none'),'Reduced motion disables image transitions and hover zoom');
  await page.emulateMediaFeatures([]);
  await page.setViewport({width:1440,height:1000,deviceScaleFactor:1});
  await page.goto((base+'/'),{waitUntil:'networkidle0'});
  await page.addScriptTag({path:path.resolve('.tmp-owner-build/audit-tools/node_modules/axe-core/axe.min.js')});
  const scan=()=>page.evaluate(async()=>{
   const result=await window.axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21aa','wcag22aa']}});
   return {passes:result.passes.length,incomplete:result.incomplete.map(x=>({id:x.id,targets:x.nodes.map(n=>n.target)})),violations:result.violations.map(x=>({id:x.id,impact:x.impact,targets:x.nodes.map(n=>n.target)}))};
  });
  accessibility={desktop:await scan()};
  await page.setViewport({width:320,height:932,deviceScaleFactor:1});
  accessibility.mobile=await scan();
  await page.click('.ws-gallery-item');
  await page.waitForFunction(()=>document.querySelector('#clientGallery').open);
  accessibility.dialog=await scan();
  await page.keyboard.press('Escape');
  // Failure fixtures use another page; expected missing-image requests do not pollute normal-page evidence.
  const fallback=await browser.newPage();
  await fallback.setRequestInterception(true);
  fallback.on('request',request=>{
   const url=request.url();
   if(url.includes('hero-performance.jpeg')||!url.startsWith((base+'/'))&&!/^(data:|blob:|about:)/.test(url))request.abort();else request.continue();
  });
  await fallback.goto((base+'/'),{waitUntil:'networkidle0'});
  check(await fallback.$eval('.ws-hero-media',x=>x.classList.contains('cx-media-unavailable')&&!x.querySelector('.cx-media-fallback').hidden),'Missing hero image retains a readable fallback and page content');
  await fallback.$eval('.ws-gallery-item',x=>x.dataset.gallerySrc='/images/client/wonder-studio/absent-fixture.jpg');
  await fallback.click('.ws-gallery-item');
  await fallback.waitForFunction(()=>!document.querySelector('[data-gallery-status]').hidden);
  check(await fallback.$eval('#clientGallery img',x=>x.hidden),'Failed enlarged image is replaced by a safe status message');
  await fallback.keyboard.press('Escape');
  check(await fallback.$eval('#clientGallery',x=>!x.open),'Failed-image gallery remains dismissible');
  await fallback.close();
 }
 await fs.writeFile(path.join(output,'metrics.json'),JSON.stringify({results,interactions,accessibility,failures:[...new Set(failures)]},null,2));
 console.log(JSON.stringify({output,checks:interactions.length,viewports:results.map(({width,scrollWidth,brokenImages})=>({width,scrollWidth,brokenImages})),accessibility,failures:[...new Set(failures)]}));
 if(!baseline){assert(failures.length===0,'Normal page has no console/resource failures');assert(Object.values(accessibility).every(x=>x.violations.length===0),'Automated accessibility violations need review');}
} finally {await browser.close();}
