import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import {JSDOM} from 'jsdom';
const base=path.join(process.env.TEMP,'AriesOwnerSnapshots');
let checks=0;
const check=(condition,label)=>{assert.ok(condition,label);checks++;};
const fixture=name=>{
 const dom=new JSDOM(fs.readFileSync(path.join(base,'SystemActivity-Index'+name+'.html'),'utf8'),{
  url:'http://owner.test/SystemActivity/Index',
  runScripts:'outside-only'
 });
 return dom;
};
const table=d=>d.querySelector('.owner-activity-table');
const heads=d=>[...table(d).querySelectorAll('thead th')].map(n=>n.textContent.trim());
const expected=['User','Role at time','Activity','Module','Date & time (UTC)','Result'];
for(const state of ['','-empty','-filtered','-five']){
 const dom=fixture(state),d=dom.window.document;
 check(d.querySelector('.owner-activity-page')!==null,'Owner activity page uses compact own workspace: '+state);
 check(!d.querySelector('header.workspace-page-heading,header.admin-heading'),'Redundant page heading removed: '+state);
 check(!d.querySelector('p.admin-note'),'Redundant note removed: '+state);
 check(!d.body.textContent.includes('Dates and times are UTC.'),'UTC promotional description absent: '+state);
 check(d.querySelector('.staff-data-table-frame[data-staff-table-capacity="5"]')!==null,
  'Shared Staff five-record table foundation reused: '+state);
 check(heads(d).join('|')===expected.join('|'),'Exactly six properly ordered activity column headers: '+state);
 check(![...table(d).querySelectorAll('th,td')].some(el=>el.textContent.trim()==='Target'||el.getAttribute('data-label')==='Target'),
  'Target column is absent without dropping database metadata: '+state);
 check(!table(d).querySelector('details,summary,button,a[href*="Details"]')&&!table(d).textContent.includes('View Details'),
  'No View Details UI control or disclosure: '+state);
 check(!!d.querySelector('.owner-activity-table-frame > .aries-five-pager[data-page-size="5"]'),
  'Shared compact pagination component present: '+state);
 check(!!d.querySelector('[name=search]')&&!!d.querySelector('[name=type]')&&
       !!d.querySelector('[name=fromDate]')&&!!d.querySelector('[name=toDate]')&&
       !!d.querySelector('[name=sortBy]'),
  'Search, type, date and sort filters preserved: '+state);
 const rows=[...table(d).querySelectorAll('tbody tr:not(.staff-data-table-empty-row)')];
 if(state==='-empty'){
  check(rows.length===0 && table(d).textContent.includes('No business activity') &&
    table(d).textContent.includes('No activity records are available.'),'Default empty table text correct');
 }else if(state==='-filtered'){
  check(rows.length===0 && table(d).textContent.includes('No matching activity') &&
    table(d).textContent.includes('Try adjusting your search or filters.') &&
    !!table(d).querySelector('a[href*="SystemActivity"]'),'Filtered empty table text and clear link correct');
 }else{
  check(rows.length===(state==='-five'?5:1),'Expected actual activity row count: '+state);
  check([...table(d).querySelectorAll('td[data-label="Activity"]')].every(el=>
    el.querySelector('.owner-activity-type')&&el.querySelector('.owner-activity-description')&&
    el.querySelector('.owner-activity-description').textContent.trim().length>0),
    'Full event descriptions appear inline without a View Details disclosure: '+state);
  check(table(d).querySelector('time[datetime$="Z"]')!==null,'UTC timestamp semantics preserved: '+state);
 }
 check(d.querySelector('.sidebar-nav a[href*="SystemActivity"]')!==null,
  'Owner sidebar Business Activity navigation remains: '+state);
 dom.window.close();
}
console.log('SUCCESS: '+checks+' Owner Business Activity DOM/empty-state checks passed.');
