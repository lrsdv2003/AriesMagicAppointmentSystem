// Midnight Concierge targeted checks. Only the explicit GUID LocalDB loopback fixture is accepted.
import fs from 'node:fs/promises';import path from 'node:path';import assert from 'node:assert/strict';
const {default:puppeteer}=await import(process.env.PUPPETEER_MODULE);
const s=JSON.parse(await fs.readFile(process.env.ARIES_HUB_FIXTURE||'.tmp-owner-build/midnight-client-fixture.json','utf8'));
assert(['http://127.0.0.1:5299','http://127.0.0.1:5310'].includes(s.BaseUrl));assert(/^AriesBookingVisual_[0-9a-f]{32}$/.test(s.Database));
const out=path.resolve('.tmp-owner-build/client-ui-review',process.argv[2]||'midnight-final');await fs.mkdir(out,{recursive:true});
const b=await puppeteer.launch({headless:true,executablePath:process.env.PUPPETEER_EXECUTABLE_PATH});let checks=0;const scans=[],renders=[],errors=[];
const check=(v,n)=>{assert(v,n);checks++;};
try{
 const p=await b.newPage();p.on('pageerror',e=>errors.push(e.message));await p.setRequestInterception(true);p.on('request',r=>new URL(r.url()).hostname==='127.0.0.1'||/^(data:|blob:)/.test(r.url())?r.continue():r.abort());
 const go=async url=>{const r=await p.goto(s.BaseUrl+url,{waitUntil:'networkidle0'});check(r.status()===200,'Loads '+url.split('?')[0]);};
 const field=async(id,value)=>p.$eval('#'+id,(x,v)=>{x.value=v;x.dispatchEvent(new Event('input',{bubbles:true}));x.dispatchEvent(new Event('change',{bubbles:true}));},value);
 const shot=async(name,width)=>{await p.evaluate(()=>document.fonts.ready);check(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Reflow '+name);await p.screenshot({path:path.join(out,`${name}-${width}-full.png`),fullPage:true});renders.push({name,width,height:await p.evaluate(()=>document.documentElement.scrollHeight)});};
 const axe=async label=>{await p.addScriptTag({path:process.env.AXE_CORE_PATH});const violations=await p.evaluate(async()=>(await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa']}})).violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})));scans.push({label,violations});check(!violations.length,'Accessibility '+label);};
 await go('/Account/Login');await field('Email',s.Email);await field('Password',s.Password);await Promise.all([p.waitForNavigation({waitUntil:'networkidle0'}),p.click('form[action="/Account/Login"] button[type=submit]')]);
 for(const width of [320,375,390,430,768,1024,1440]){
  await p.setViewport({width,height:width<768?932:1000,isMobile:width<768,hasTouch:width<768});await go('/Bookings/Dashboard');
  check(await p.$eval('body',x=>getComputedStyle(x).backgroundColor==='rgb(3, 22, 43)'),'Midnight page token '+width);
  check(await p.$eval('.hub-navigation',x=>x.querySelectorAll('.hub-nav-group').length===3&&x.querySelectorAll('a').length===9&&new Set([...x.querySelectorAll('a')].map(a=>a.pathname)).size===9),'Unique grouped routes '+width);
  if(width<992){
   await p.focus('#hubMenu');await p.keyboard.press('Enter');check(await p.$eval('#hubDrawer',x=>x.open&&x.contains(document.activeElement)),'Drawer modal focus '+width);
   check(await p.$eval('body',x=>x.style.overflow==='hidden'),'Drawer locks background scroll '+width);
   const y=await p.evaluate(()=>scrollY);await p.mouse.move(width-2,500);await p.mouse.wheel({deltaY:300});check(await p.evaluate(v=>scrollY===v,y),'Background stays fixed '+width);
   await shot('drawer',width);if(width===390)await axe('Drawer 390');
   await p.keyboard.press('Escape');await p.waitForFunction(()=>!document.getElementById('hubDrawer').open&&document.activeElement===document.getElementById('hubMenu')&&document.body.style.overflow!=='hidden');check(await p.$eval('#hubMenu',x=>x===document.activeElement&&x.getAttribute('aria-expanded')==='false')&&await p.$eval('body',x=>x.style.overflow!== 'hidden'),'Drawer Escape/focus/scroll restore '+width);
   if(width<768){const box=await (await p.$('#hubMenu')).boundingBox();await p.touchscreen.tap(box.x+box.width/2,box.y+box.height/2);check(await p.$eval('#hubDrawer',x=>x.open),'Touch opens navigation '+width);await p.touchscreen.tap(width-2,500);await p.waitForFunction(()=>!document.getElementById('hubDrawer').open&&document.getElementById('hubMenu').getAttribute('aria-expanded')==='false'&&document.body.style.overflow!=='hidden');check(await p.$eval('#hubMenu',x=>x.getAttribute('aria-expanded')==='false'),'Touch backdrop dismissal '+width);}else{await p.click('#hubMenu');await p.mouse.click(width-2,500);check(await p.$eval('#hubDrawer',x=>!x.open),'Backdrop dismissal '+width);}
  }else{check(await p.$eval('.hub-sidebar',x=>x.getBoundingClientRect().width===256),'Stable rail width '+width);await shot('sidebar',width);}
 }
 for(const width of [390,1440]){
  await p.setViewport({width,height:1000});for(const [name,url]of [['refund-request','/Payments/RequestRefund?bookingId='+s.Hub.Cases.verification],['refund-history','/Payments/MyRefundRequests'],['reschedule-history','/RescheduleRequests/MyRequests']]){await go(url);check(await p.$$eval('.hub-sidebar .hub-navigation [aria-current=page]',xs=>xs.length===1),'Single active navigation '+name);await shot(name,width);await axe(name+' '+width);}
 }
 await p.setViewport({width:390,height:932});await go('/Bookings/CreateStepOne');await field('EventDate',s.AvailableDate);await field('PartyVenue','Synthetic event venue');await p.click('#findVenue');await p.waitForSelector('#venueResults button');await p.click('#venueResults button');await p.click('#checkLocation');await p.waitForSelector('[data-step="1"]:not([hidden])');await p.click('[name=ServiceId]');
 const packageData=await p.$eval('#wizardPackages',x=>JSON.parse(x.textContent));const cards=await p.$$eval('.cn-package',xs=>xs.map(x=>({id:Number(x.dataset.packageOption),text:x.textContent,hasImage:!!x.querySelector('img')})));
 for(const card of cards){const data=packageData.find(x=>x.Id===card.id);check(data&&data.Inclusions.every(x=>card.text.includes(x.Name))&&!card.hasImage,'Real inclusions without unverified image assignment');}
 check(await p.$eval('[data-step="1"] .cn-performance-photo img',x=>x.naturalWidth>0&&Math.abs(x.getBoundingClientRect().width/x.getBoundingClientRect().height-2048/1365)<.02),'Natural performance-photo aspect ratio');
 await p.click('[data-step="1"] [data-next]');await p.waitForSelector('[data-step="2"]:not([hidden])');await field('EventType','Company Events');await field('Motif','Synthetic agreement check');await p.click('[data-step="2"] [data-next]');await p.waitForSelector('[data-step="3"]:not([hidden])');
 check(await p.$$eval('.cn-terms-clauses h3',xs=>xs.length===6&&xs.every(x=>x.getClientRects().length>0)),'All six terms headings always visible');
 const rejected=await p.$eval('#clientBookingWizard',async form=>{const data=new FormData(form);data.set('AcceptTerms','false');const response=await fetch(form.action,{method:'POST',body:data});const text=await response.text();return response.status===200&&text.includes('You must agree to the Terms and Agreement before submitting your booking.');});check(rejected,'Real server rejects non-agreement');
 await p.focus('#openBookingTerms');await p.keyboard.press('Enter');check(await p.$eval('#fullBookingTerms',x=>x.open&&x.contains(document.activeElement)),'Full-text dialog focus');await axe('Full terms dialog');await shot('terms-dialog',390);await p.keyboard.press('Escape');
 await p.emulateMediaFeatures([{name:'prefers-reduced-motion',value:'reduce'}]);check(await p.$eval('.cn-button',x=>getComputedStyle(x).transitionDuration==='0s'),'Focused-flow reduced motion');
 check(!errors.length,'No script errors');await fs.writeFile(path.join(out,'checks.json'),JSON.stringify({checks,scans,renders,errors,Notes:'Synthetic data only. Full terms POST is deliberately false and rejected. No package photos without verified inclusion mapping.'},null,2));console.log(JSON.stringify({checks,scans:scans.length,renders:renders.length,violations:scans.flatMap(x=>x.violations).length,errors}));
}catch(e){await fs.writeFile(path.join(out,'failure.json'),JSON.stringify({message:e.message,checks,scans,errors},null,2));throw e;}finally{await b.close();}
