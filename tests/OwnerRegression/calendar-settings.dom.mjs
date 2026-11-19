import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { JSDOM } from 'jsdom';
const page=path.join(process.env.TEMP,'AriesOwnerSnapshots','Calendar-Settings.html');
const source=path.join(process.cwd(),'wwwroot','js','calendar-settings.js');
const dom=new JSDOM(fs.readFileSync(page,'utf8'),{
  url:'http://fixture.test/Calendar/Settings',runScripts:'outside-only'
});
const w=dom.window,d=w.document;
const mockRow=d.createElement('button');
mockRow.type='button';
mockRow.dataset.limitEdit='';
mockRow.dataset.limitDate='2026-11-26';
d.querySelector('.calendar-settings-page').appendChild(mockRow);
let fetchGets=0, fetchPosts=0;
let shown=0;
w.bootstrap={Modal:{getOrCreateInstance:()=>({
  show:()=>{shown++;d.getElementById('calendarLimitModal').classList.add('show');d.getElementById('calendarLimitModal').dispatchEvent(new w.Event('shown.bs.modal'));},
  hide:()=>{d.getElementById('calendarLimitModal').classList.remove('show');d.getElementById('calendarLimitModal').dispatchEvent(new w.Event('hidden.bs.modal'));}
})}};
w.fetch=async (url,opt)=>{
  if(opt?.method==='POST'){
    fetchPosts++;
    return {ok:false,status:409,json:async()=>({success:false,message:'This date was changed by another request.'})};
  }
  fetchGets++;
  const date=new URL(url,'http://fixture.test').searchParams.get('date');
  return {ok:true,status:200,json:async()=>({
    success:true,date,id:date==='2026-11-26'?7:0,storedLimit:date==='2026-11-26'?3:0,
    effectiveLimit:date==='2026-11-26'?3:4,confirmedCount:2,isBlocked:false
  })};
};
w.eval(fs.readFileSync(source,'utf8'));
const wait=()=>new Promise(r=>setTimeout(r,30));
let checks=0;
const check=(a,label)=>{assert.ok(a,label);checks++;};
mockRow.click();
await wait();
check(shown===1 && fetchGets===1,'Row edit opens after a fresh server read');
check(d.getElementById('calendarLimitFormDate').value==='2026-11-26','Selected date is bound correctly');
check(d.getElementById('calendarLimitRecordId').value==='7' && d.getElementById('calendarLimitExpectedStored').value==='3','Existing version data is carried for concurrency protection');
check(d.getElementById('calendarLimitValue').value==='3','Current effective limit is prefilled');
check(d.getElementById('calendarLimitOccupied').textContent==='2','Existing confirmed occupancy is displayed');
d.getElementById('calendarLimitModal').dispatchEvent(new w.Event('hidden.bs.modal'));
check(fetchPosts===0,'Cancel never submits a save');
d.getElementById('calendarLimitDatePicker').value='2026-11-27';
d.getElementById('calendarLimitCreate').click();
await wait();
check(shown===2 && d.getElementById('calendarLimitValue').value==='4','New date editor reads its effective default from the server');
const form=d.getElementById('calendarLimitForm');
d.getElementById('calendarLimitValue').value='21';
form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
await wait();
check(fetchPosts===0,'Out-of-range value cannot submit');
d.getElementById('calendarLimitValue').value='3';
form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
await wait();
check(fetchPosts===1,'Valid edit sends one POST');
check(d.getElementById('calendarLimitValidation').textContent.includes('changed by another request'),'Server conflict is displayed instead of falsely reporting success');
check(!d.getElementById('calendarLimitSave').disabled,'Save button re-enables after rejected request');
dom.window.close();
// The success notice is shown once only when a prior POST received a successful response.
{
 const saved=new JSDOM(fs.readFileSync(page,'utf8'),{
   url:'http://fixture.test/Calendar/Settings',runScripts:'outside-only'
 });
 const d2=saved.window.document;
 saved.window.sessionStorage.setItem('aries.calendarLimitSuccess','Daily booking limit saved as 4.');
 saved.window.bootstrap={Modal:{getOrCreateInstance:()=>({show:()=>{},hide:()=>{}})}};
 saved.window.eval(fs.readFileSync(source,'utf8'));
 check(d2.getElementById('calendarLimitSavedNotice')?.textContent==='Daily booking limit saved as 4.' &&
   !d2.getElementById('calendarLimitSavedNotice').hidden,'Successful server response is announced after reload');
 check(saved.window.sessionStorage.getItem('aries.calendarLimitSuccess')===null,
   'Saved confirmation is consumed and cannot reappear on unrelated visits');
 saved.window.close();
}
console.log('SUCCESS: '+checks+' Calendar Settings interaction DOM checks passed.');
