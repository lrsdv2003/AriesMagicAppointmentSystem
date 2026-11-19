import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {JSDOM} from 'jsdom';

const base=path.join(process.env.TEMP,'AriesOwnerSnapshots','History-Index.html');
const source=fs.readFileSync('wwwroot/js/owner-history.js','utf8');
let checks=0;
const check=(value,label)=>{assert.ok(value,label);checks++;};
const wait=()=>new Promise(resolve=>setTimeout(resolve,35));
function fixture() {
 const dom=new JSDOM(fs.readFileSync(base,'utf8'),{
   url:'http://owner.test/History/Index?Search=history&SortBy=Oldest',
   runScripts:'outside-only'
 });
 const d=dom.window.document;
 check(!!d.querySelector('.owner-history-workspace'),'Owner History snapshot has new dedicated workspace');
 check(!!d.querySelector('[data-owner-history-refresh]'),'Refresh button remains actionable');
 check(!d.querySelector('#historyExport'),'Retired Export menu absent');
 check(!d.querySelector('.owner-history-archives'),'Owner History no longer exposes the Archives action');
 check(!!d.querySelector('.owner-history-table-frame.owner-verification-table-frame'),
       'Owner History shares the Payment Verification table frame');
 check(!!d.querySelector('.owner-history-table.owner-audit-table'),
       'Owner History uses the shared Owner payment audit table styling');
 return dom;
}
{
 const dom=fixture(),w=dom.window,d=w.document;
 const anchor=d.querySelector('[data-owner-history-refresh]');
 let calls=0,resolveFetch;
 w.fetch=(url,options)=>{
  calls++;
  check(url.includes('/History/Index'),'Refresh reads the server History Index endpoint');
  check(options.cache==='no-store' && options.credentials==='same-origin',
        'Refresh requests uncached data with authenticated cookies');
  return new Promise(resolve=>resolveFetch=resolve);
 };
 w.eval(source);
 anchor.dispatchEvent(new w.MouseEvent('click',{bubbles:true,cancelable:true}));
 check(anchor.getAttribute('aria-busy')==='true','Refresh announces loading state');
 anchor.dispatchEvent(new w.MouseEvent('click',{bubbles:true,cancelable:true}));
 check(calls===1,'Duplicate Refresh is suppressed');
 const fresh=fs.readFileSync(base,'utf8').replaceAll('BK-2026-001','BK-2026-NEW');
 resolveFetch({
  ok:true,headers:{get:()=> 'text/html; charset=utf-8'},
  text:async()=>fresh
 });
 await wait();
 check(d.querySelector('#historyResults').textContent.includes('BK-2026-NEW'),
       'Successful Refresh replaces server-rendered table and pager in-place');
 check(d.querySelector('#ownerHistoryRefreshStatus').textContent.includes('up to date'),
       'Successful Refresh announces an accessible confirmation');
 check(anchor.getAttribute('aria-busy')===null && anchor.getAttribute('aria-disabled')===null,
       'Refresh returns to idle after successful replacement');
 dom.window.close();
}
{
 const dom=fixture(),w=dom.window,d=w.document;
 const before=d.querySelector('#historyResults').innerHTML;
 w.fetch=async()=>({ok:false,status:503,headers:{get:()=> 'text/html'}});
 w.eval(source);
 d.querySelector('[data-owner-history-refresh]').dispatchEvent(
  new w.MouseEvent('click',{bubbles:true,cancelable:true}));
 await wait();
 check(d.querySelector('#historyResults').innerHTML===before,
       'Failed Refresh never overwrites legitimate History rows with empty data');
 check(d.querySelector('#ownerHistoryRefreshStatus').classList.contains('is-error'),
       'Failed Refresh displays a distinct network/server error');
 check(!d.querySelector('[data-owner-history-refresh]').hasAttribute('aria-busy'),
       'Failed Refresh remains retryable');
 dom.window.close();
}
console.log('SUCCESS: '+checks+' Owner History Refresh and export-removal DOM checks passed.');
