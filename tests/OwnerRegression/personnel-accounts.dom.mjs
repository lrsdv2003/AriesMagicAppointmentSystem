import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {JSDOM} from 'jsdom';

const base=path.join(process.env.TEMP,'AriesOwnerSnapshots');
const source=fs.readFileSync('wwwroot/js/personnel-accounts.js','utf8');
let checks=0;
const check=(value,label)=>{assert.ok(value,label);checks++;};
const open=name=>{
  const dom=new JSDOM(fs.readFileSync(path.join(base,name+'.html'),'utf8'),{
    url:'https://localhost/Personnel/'+(name.endsWith('Access')?'Access':'Index'),
    runScripts:'outside-only'
  });
  dom.window.confirm=()=>true;
  dom.window.eval(source);
  return dom;
};
const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));

{
 const dom=open('Personnel-Index'),d=dom.window.document;
 const form=d.querySelector('[data-personnel-filters]');
 let requests=0;
 form.requestSubmit=()=>{requests++;};
 d.querySelector('#accountRole').value='Client';
 d.querySelector('#accountRole').dispatchEvent(new dom.window.Event('change',{bubbles:true}));
 check(requests===1,'Changing role applies filter automatically without an Apply button');
 check(d.querySelector('#accountRole').value==='Client','Chosen role remains selected');
 check(!d.querySelector('[name=statusFilter]'),'Removed status filter cannot change account eligibility');
 dom.window.close();
}
{
 const dom=open('Personnel-Index'),d=dom.window.document;
 let requests=0;
 const form=d.querySelector('[data-personnel-filters]');
 form.requestSubmit=()=>{requests++;};
 const input=d.querySelector('#ownerPersonnelSearch');
 input.value='staff';
 input.dispatchEvent(new dom.window.Event('input',{bubbles:true}));
 await wait(120);
 input.value='staff@example.test';
 input.dispatchEvent(new dom.window.Event('input',{bubbles:true}));
 await wait(470);
 check(requests===1,'Debounced search submits only once after typing ends');
 check(input.value==='staff@example.test','Debounced search preserves the full entered query');
 dom.window.close();
}
{
 const dom=open('Personnel-Index'),d=dom.window.document;
 const button=d.querySelector('[data-personnel-refresh]');
 check(button.getAttribute('aria-label')==='Refresh personnel accounts',
       'Icon-only refresh has accessible name');
 const event=new dom.window.MouseEvent('click',{bubbles:true,cancelable:true});
 button.dispatchEvent(event);
 check(button.getAttribute('aria-busy')==='true'&&button.dataset.loading==='true',
       'Refresh communicates loading while navigating to the current filtered URL');
 const repeat=new dom.window.MouseEvent('click',{bubbles:true,cancelable:true});
 button.dispatchEvent(repeat);
 check(repeat.defaultPrevented,'Duplicate refresh clicks are ignored');
 dom.window.close();
}
{
 const dom=open('Personnel-Access'),d=dom.window.document;
 let confirms=0;
 dom.window.confirm=()=>{confirms++;return false;};
 const form=d.querySelector('form[data-personnel-confirm]');
 const declined=new dom.window.Event('submit',{bubbles:true,cancelable:true});
 form.dispatchEvent(declined);
 check(declined.defaultPrevented&&confirms===1,'Staff destructive action prompts and supports cancellation');
 check(!form.querySelector('[type=submit]').disabled,'Cancelled operation leaves action usable');
 dom.window.confirm=()=>true;
 const accepted=new dom.window.Event('submit',{bubbles:true,cancelable:true});
 accepted.preventDefault();
 form.dispatchEvent(accepted);
 check(form.querySelector('[type=submit]').disabled &&
       form.querySelector('[type=submit]').getAttribute('aria-busy')==='true',
       'Confirmed Staff operation prevents duplicate submissions while posting');
 dom.window.close();
}
console.log('SUCCESS: '+checks+' Personnel filtering, Refresh and Staff action interaction checks passed.');
