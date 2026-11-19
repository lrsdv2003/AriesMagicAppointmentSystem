import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {JSDOM} from 'jsdom';

const fixtures=path.join(process.env.TEMP,'AriesOwnerSnapshots');
const source=fs.readFileSync('wwwroot/js/owner-archives.js','utf8');
let checks=0;
const check=(value,label)=>{assert.ok(value,label);checks++;};
const wait=()=>new Promise(resolve=>setTimeout(resolve,45));
const fixture=(suffix='')=>new JSDOM(
  fs.readFileSync(path.join(fixtures,'TrashHistory-Index'+suffix+'.html'),'utf8'),
  {url:'http://owner.test/TrashHistory/Index?Search=archived&SortBy=oldest&Page=2',runScripts:'outside-only'}
);
const table=d=>d.querySelector('.owner-archives-table');
const cols=['Booking ID','Client Name','Date and Time','Archive Date','Final Status'];

for(const suffix of ['','-empty','-filtered','-five']){
 const dom=fixture(suffix),d=dom.window.document;
 check(!!d.querySelector('.owner-archives-page'),'Owner Archives page rendered '+suffix);
 check(!d.querySelector('header.workspace-page-heading,header.admin-heading'),'Redundant page header removed '+suffix);
 check(!d.body.textContent.includes('Read-only records of cancelled, declined and expired requests.'),
       'Old read-only description removed '+suffix);
 check(!d.querySelector('section.admin-panel')&&!d.body.textContent.includes('Archived packages'),
       'Archived Packages presentation absent '+suffix);
 check(!!d.querySelector('.staff-data-table-frame.owner-archives-table-frame[data-staff-table-capacity="5"]'),
       'Shared Staff five-record frame used '+suffix);
 check([...table(d).querySelectorAll('thead th')].map(e=>e.textContent.trim()).join('|')===cols.join('|'),
       'Exact five Archive columns in required order '+suffix);
 check(table(d).querySelectorAll('colgroup col').length===5 &&
       table(d).querySelectorAll('td[data-label="Type"],td[data-label="Archived by"],td[data-label="Actions"]').length===0,
       'Retired Type, Archived By and Actions cells absent '+suffix);
 check(!table(d).querySelector('a[href*="/Details/"],a[href*="/Restore/"],button'),
       'No View/Restore/Delete actions in Archive table '+suffix);
 check(!!d.querySelector('.owner-archives-table-frame>.aries-five-pager[data-page-size="5"]'),
       'Shared five-record pager preserved '+suffix);
 check(!!d.querySelector('[name=Search]') && !!d.querySelector('[name=Reason]') &&
       !!d.querySelector('[name=DateFrom]') && !!d.querySelector('[name=DateTo]'),
       'Search/reason/date filters preserved '+suffix);
 check([...d.querySelectorAll('#archiveSort option')].map(o=>o.textContent.trim()).join('|')===
       'Newest to Oldest|Oldest to Newest','Exactly two date-sort options '+suffix);
 const refresh=d.querySelector('[data-owner-archives-refresh]');
 check(refresh?.getAttribute('aria-label')==='Refresh archived bookings' &&
       refresh?.title==='Refresh archived bookings' &&
       refresh?.querySelector('.bi-arrow-clockwise') &&
       refresh?.textContent.trim()==='',
       'Icon-only Refresh has accessible name/tooltip '+suffix);
 check(!!d.querySelector('.sidebar-nav a[href*="/TrashHistory"]'),'Archives sidebar navigation preserved '+suffix);
 const rows=[...table(d).querySelectorAll('tbody tr:not(.staff-data-table-empty-row)')];
 if(suffix==='-empty') {
   check(rows.length===0 &&
         table(d).textContent.includes('No archived bookings') &&
         table(d).textContent.includes('There are no archived booking records to display.'),
         'Default empty-state matches requested copy');
 } else if(suffix==='-filtered'){
   check(rows.length===0 && table(d).textContent.includes('No matching archives') &&
         table(d).textContent.includes('Try adjusting your search or sorting.') &&
         !!table(d).querySelector('.staff-data-table-reset'),'Filtered empty state and clear link match requested copy');
 } else{
   check(rows.length===(suffix==='-five'?5:1),'Actual archived records rendered '+suffix);
   check(rows.every(r=>r.querySelector('td[data-label="Booking ID"] strong') &&
          r.querySelector('td[data-label="Client Name"]') &&
          r.querySelector('td[data-label="Date and Time"] time') &&
          r.querySelector('td[data-label="Date and Time"] .owner-archives-cell-secondary') &&
          r.querySelector('td[data-label="Archive Date"]') &&
          r.querySelector('td[data-label="Final Status"] .owner-archives-status')),
          'Archive records contain ID, client, event/time, archive stamp, status '+suffix);
   if(suffix==='-five')
     check(table(d).textContent.includes('Not recorded'),'Missing historical archive date shown accurately');
 }
 dom.window.close();
}
{
 const dom=fixture(),w=dom.window,d=w.document;
 let resolver,calls=0;
 w.fetch=(url,options)=>{
   calls++;
   check(url.includes('SortBy=')&&url.includes('Search='),'Refresh preserves search and date sort');
   check(options.method==='GET' && options.credentials==='same-origin' &&
         options.cache==='no-store','Refresh performs same-origin uncached read-only GET');
   return new Promise(resolve=>resolver=resolve);
 };
 w.eval(source);
 const refresh=d.querySelector('[data-owner-archives-refresh]');
 refresh.dispatchEvent(new w.MouseEvent('click',{bubbles:true,cancelable:true}));
 check(refresh.getAttribute('aria-busy')==='true'&&refresh.getAttribute('aria-disabled')==='true',
       'Loading and disabled accessibility state set on Refresh');
 refresh.dispatchEvent(new w.MouseEvent('click',{bubbles:true,cancelable:true}));
 check(calls===1,'Repeated click cannot trigger duplicate Refresh');
 const updated=fs.readFileSync(path.join(fixtures,'TrashHistory-Index-five.html'),'utf8')
    .replaceAll('BK-2026-9800','BK-2026-UPDATED');
 resolver({
   ok:true,headers:{get:()=> 'text/html;charset=utf-8'},
   text:async()=>updated
 });
 await wait();
 check(d.querySelector('#ownerArchivesResults').textContent.includes('BK-2026-UPDATED'),
       'Successful refresh replaces server-rendered rows and pager in place');
 check(d.querySelector('#ownerArchivesRefreshStatus').textContent.includes('up to date'),
       'Refresh success announces live status');
 check(!refresh.hasAttribute('aria-busy')&&!refresh.hasAttribute('aria-disabled'),
       'Refresh returns to enabled state after success');
 dom.window.close();
}
{
 const dom=fixture(),w=dom.window,d=w.document;
 const before=d.querySelector('#ownerArchivesResults').innerHTML;
 w.fetch=async()=>({ok:false,status:503,headers:{get:()=> 'text/html'}});
 w.eval(source);
 d.querySelector('[data-owner-archives-refresh]').dispatchEvent(new w.MouseEvent('click',{bubbles:true,cancelable:true}));
 await wait();
 check(d.querySelector('#ownerArchivesResults').innerHTML===before,
       'Retrieval failure never overwrites genuine historical rows with empty state');
 check(d.querySelector('#ownerArchivesRefreshStatus').classList.contains('is-error'),
       'Failed Refresh reports server/network error instead of false no-records claim');
 check(!d.querySelector('[data-owner-archives-refresh]').hasAttribute('aria-busy'),
       'Refresh can be retried after failure');
 dom.window.close();
}
console.log('SUCCESS: '+checks+' Owner Archives table, empty-state and Refresh DOM checks passed.');
