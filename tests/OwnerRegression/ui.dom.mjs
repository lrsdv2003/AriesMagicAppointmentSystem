// DOM tests only: no browser, external requests, or production data.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
const {JSDOM}=await import(process.env.JSDOM_MODULE || 'jsdom');
const snapshots=path.join(process.env.TEMP,'AriesOwnerSnapshots');
const root=process.cwd();
let checks=0;
const check=(value,message)=>{ assert(value,message); checks++; };
const tick=()=>new Promise(resolve=>setTimeout(resolve,0));
function fixture(name){
 const dom=new JSDOM(fs.readFileSync(path.join(snapshots,name+'.html'),'utf8'),{url:'http://fixture.test/',runScripts:'outside-only'});
 dom.window.HTMLElement.prototype.scrollTo=function(){};
 return dom;
}
function run(dom,script){dom.window.eval(fs.readFileSync(path.join(root,'wwwroot/js',script),'utf8'));}
for(const name of fs.readdirSync(snapshots).filter(f=>f.endsWith('.html'))){
 const dom=fixture(name.slice(0,-5)),d=dom.window.document;
 const ids=[...d.querySelectorAll('[id]')].map(e=>e.id);
 check(new Set(ids).size===ids.length,name+': duplicate IDs');
 const missing=[...d.querySelectorAll('input:not([type=hidden]),select,textarea')].filter(e=>!e.labels?.length&&!e.getAttribute('aria-label')&&!e.getAttribute('aria-labelledby')&&!['submit','button'].includes(e.type));
 check(!missing.length,name+': unnamed fields '+missing.map(e=>e.id||e.name));
 check([...d.querySelectorAll('.modal')].every(e=>e.getAttribute('aria-label')||d.getElementById(e.getAttribute('aria-labelledby'))),name+': dialog lacks a title');
 check(!/â‚|â€|Â·|â†/.test(d.body.textContent),name+': corrupted display characters');
 check([...d.querySelectorAll('form[method=post]')].every(f=>f.querySelector('[name=__RequestVerificationToken]')),name+': missing antiforgery token');
 if(name==='Communications-Index-Admin.html') check(![...d.querySelectorAll('.comm-chat-header a')].some(a=>a.textContent.includes('View Booking')),'Admin sees a booking action outside its role');
 run(dom,'app-ui.js');
 check([...d.querySelectorAll('.modal')].every(m=>m.parentElement===d.body),name+': clipped dialog');
 dom.window.close();
}
console.log('PASS rendered markup, labels, dialog names, encoding and antiforgery');
for(const name of ['Services-Create','Services-Edit']){
 const dom=fixture(name),w=dom.window,d=w.document;
 run(dom,'package-editor.js');
 const add=d.getElementById('addInclusion'),container=d.getElementById('inclusions-container');
 add.click();add.click();add.click();
 container.children[1].querySelector('[data-remove-inclusion]').click();
 [...container.children].forEach((row,i)=>{
  check(row.querySelector('input[name$=".Name"]').name===`Inclusions[${i}].Name`,name+': noncontiguous model binding');
  check([...row.querySelectorAll('label')].every(l=>d.getElementById(l.htmlFor)),name+': broken inclusion label');
 });
 while(container.children.length)container.firstElementChild.querySelector('[data-remove-inclusion]').click();
 const event=new w.Event('submit',{bubbles:true,cancelable:true});
 d.getElementById('packageEditorForm').dispatchEvent(event);
 check(event.defaultPrevented,name+': empty inclusions accepted');
 dom.window.close();
}
console.log('PASS package add/remove binding and empty validation');
{
 const dom=fixture('Communications-Index-Client'),w=dom.window,d=w.document;
 let posts=0,resolveSend;
 w.fetch=async (url,options)=>{
  if(options?.method==='POST'){ posts++;return new Promise(resolve=>{resolveSend=resolve;}); }
  return {ok:true,json:async()=>[]};
 };
 run(dom,'communication-center.js');
 const form=d.getElementById('commComposer'),input=d.getElementById('commMessageInput');
 input.value='First draft';
 form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 check(posts===1,'Chat submitted the same draft twice');
 input.value='Next draft typed during send';
 resolveSend({ok:true,redirected:false});await tick();await tick();
 check(input.value==='Next draft typed during send','Chat erased the next draft');
 check(!form.hasAttribute('aria-busy'),'Chat stayed busy after send');
 w.fetch=async()=>({ok:false});
 form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await tick();
 check(input.value==='Next draft typed during send','Failed send erased draft');
 check(d.getElementById('commComposerStatus').textContent.includes('try again'),'Failed send has no feedback');
 dom.window.close();
}
console.log('PASS chat duplicate-send prevention, draft preservation and failure feedback');
{
 const dom=fixture('Bookings-Create'),w=dom.window,d=w.document;
 const pack=JSON.parse(d.getElementById('wizardPackages').textContent)[0];
 w.fetch=async url=>({ok:true,headers:{get:()=> 'application/json'},json:async()=>String(url).includes('SearchVenue')?[{displayName:'Fixture venue'}]:String(url).includes('CheckVenueServiceability')?{success:true,isServiceable:true,distanceKm:12,travelFee:500}:{available:true,availableIds:[pack.Id],message:'Available'}});
 run(dom,'client-booking-wizard.js');
 d.getElementById('PartyVenue').value='Fixture';d.getElementById('findVenue').click();await tick();
 d.querySelector('#venueResults button').click();
 d.querySelector('[data-step="0"] [data-next]').click();await tick();await tick();
 check(!d.querySelector('[data-step="1"]').hidden,'Wizard did not advance after valid location');
 const radio=d.querySelector('[name=ServiceId]');radio.checked=true;radio.dispatchEvent(new w.Event('change'));
 const removals=[...d.querySelectorAll('[name=RemovedInclusionIds]:not(:disabled)')];
 removals.slice(0,3).forEach(input=>{input.checked=true;input.dispatchEvent(new w.Event('change'));});
 check(d.querySelectorAll('[name=RemovedInclusionIds]:checked').length===2,'Wizard accepted more than two removals');
 d.querySelector('[data-step="1"] [data-next]').click();await tick();await tick();
 d.getElementById('EventType').value='Company Events';d.getElementById('EventType').dispatchEvent(new w.Event('change'));
 d.getElementById('Motif').value='Coral';
 d.querySelector('[data-step="2"] [data-next]').click();
 check(!d.querySelector('[data-step="3"]').hidden,'Wizard did not reach review');
 check(d.querySelectorAll('.cr-mobile-summary').length===1,'Missing mobile review price');
 const ids=[...d.querySelectorAll('[id]')].map(e=>e.id);check(new Set(ids).size===ids.length,'Review duplicated IDs');
 const event=new w.Event('submit',{bubbles:true,cancelable:true});d.getElementById('clientBookingWizard').dispatchEvent(event);
 check(event.defaultPrevented,'Wizard accepted unchecked terms');
 d.getElementById('PartyVenue').value='Changed venue';d.getElementById('PartyVenue').dispatchEvent(new w.Event('input'));
 const invalid=new w.Event('submit',{bubbles:true,cancelable:true});d.getElementById('clientBookingWizard').dispatchEvent(invalid);
 check(invalid.defaultPrevented&&!d.querySelector('[data-step="0"]').hidden,'Wizard accepted changed location without rechecking');
 dom.window.close();
}
console.log('PASS booking wizard progression, discount limit, review, terms and location invalidation');
console.log(`SUCCESS: ${checks} UI DOM checks passed.`);
