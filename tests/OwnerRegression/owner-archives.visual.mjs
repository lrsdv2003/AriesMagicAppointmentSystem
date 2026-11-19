import http from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {spawn,spawnSync} from 'node:child_process';
const root='C:\\Users\\EJ Ridge\\AriesRecovery\\owner-dashboard-final-20261119';
const snaps=path.join(os.tmpdir(),'AriesOwnerSnapshots');
const types={'.html':'text/html','.css':'text/css','.js':'application/javascript','.svg':'image/svg+xml','.woff2':'font/woff2','.png':'image/png','.jpg':'image/jpeg','.ico':'image/x-icon'};
const server=http.createServer((req,res)=>{
 const u=new URL(req.url,'http://127.0.0.1');
 const fixture=u.pathname.startsWith('/fixture/');
 const base=fixture?snaps:path.join(root,'wwwroot');
 const file=path.resolve(base,fixture?u.pathname.slice(9):u.pathname.slice(1));
 if(!file.startsWith(base)){res.writeHead(403);return res.end();}
 fs.readFile(file).then(data=>{res.writeHead(200,{'Content-Type':types[path.extname(file)]||'application/octet-stream'});res.end(data)}).catch(()=>{res.writeHead(404);res.end('not found')});
});
await new Promise(resolve=>server.listen(38219,'127.0.0.1',resolve));
const profile=await fs.mkdtemp(path.join(os.tmpdir(),'aries-browser-'));
const browser=spawn('C:\\Program Files\\BraveSoftware\\Brave-Browser\\Application\\brave.exe',[
 '--headless=new','--no-first-run','--disable-gpu','--disable-extensions','--disable-background-networking',
 '--user-data-dir='+profile,'--remote-debugging-port=39219','--remote-allow-origins=*',
 'http://127.0.0.1:38219/fixture/TrashHistory-Index.html'
],{stdio:'ignore'});
const delay=ms=>new Promise(r=>setTimeout(r,ms));
let socket,id=0,failures=0;
const pending=new Map();
try{
 let wsUrl;
 for(let i=0;i<50;i++){
  try{const pages=await(await fetch('http://127.0.0.1:39219/json')).json();wsUrl=pages.find(p=>p.type==='page')?.webSocketDebuggerUrl;if(wsUrl)break;}catch{}
  await delay(300);
 }
 if(!wsUrl)throw Error('Brave remote debugging unavailable');
 socket=new WebSocket(wsUrl);
 await new Promise((resolve,reject)=>{socket.addEventListener('open',resolve,{once:true});socket.addEventListener('error',()=>reject(Error('CDP socket failed')),{once:true})});
 socket.addEventListener('message',event=>{const d=JSON.parse(event.data);if(!d.id)return;const p=pending.get(d.id);if(!p)return;pending.delete(d.id);d.error?p.reject(Error(JSON.stringify(d.error))):p.resolve(d.result)});
 const send=(method,params={})=>new Promise((resolve,reject)=>{const n=++id;pending.set(n,{resolve,reject});socket.send(JSON.stringify({id:n,method,params}))});
 await send('Page.enable');await send('Runtime.enable');
 const evalJS=async expression=>{const r=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw Error(r.exceptionDetails.exception?.description||r.exceptionDetails.text);return r.result.value};
 const inspect=`(() => {
 const root=document.querySelector('.owner-archives-page');
 const frame=document.querySelector('.owner-archives-table-frame');
 const wrap=frame?.querySelector('.staff-data-table-scroll');
 const table=frame?.querySelector('.owner-archives-table');
 const pager=frame?.querySelector('.aries-five-pager');
 const th=[...table?.querySelectorAll('thead th')||[]];
 const first=table?.querySelector('tbody tr:not(.staff-data-table-empty-row)');
 const td=[...first?.querySelectorAll('td')||[]];
 const rect=e=>e?.getBoundingClientRect();
 const count=pager?.querySelector('.aries-five-total');
 const nav=pager?.querySelector('.aries-five-actions');
 const refresh=document.querySelector('[data-owner-archives-refresh]');
 return {
  width:innerWidth,
  pageOverflow:document.documentElement.scrollWidth>innerWidth+2,
  hasRoot:!!root,
  hasSharedFrame:!!frame?.classList.contains('staff-data-table-frame'),
  hasHeader:!!document.querySelector('.workspace-page-heading,.admin-heading'),
  hasOldDescription:!!root?.textContent.includes('Read-only records of cancelled, declined and expired requests.'),
  hasArchivedPackages:!!document.querySelector('section.admin-panel,[aria-label="Archived package records"]') ||
    !!root?.textContent.includes('Archived packages'),
  headers:th.map(x=>x.textContent.trim()),
  colCount:table?.querySelectorAll('colgroup col').length,
  rowCount:table?.querySelectorAll('tbody tr:not(.staff-data-table-empty-row)').length,
  emptyRows:table?.querySelectorAll('tbody tr.staff-data-table-empty-row').length,
  hasActions:!!table?.querySelector('td[data-label="Actions"],td[data-label="Archived by"],td[data-label="Type"],a[href*="Details"]'),
  hasDate:!!table?.querySelector('td[data-label="Date and Time"] time,tbody .staff-data-table-empty-row'),
  hasArchiveDate:!!table?.querySelector('td[data-label="Archive Date"],tbody .staff-data-table-empty-row'),
  scrollHeight:Math.round(rect(wrap)?.height||0),
  scrollWidth:wrap?.scrollWidth,clientWidth:wrap?.clientWidth,
  pagerHeight:Math.round(rect(pager)?.height||0),
  pagerFits:!!pager&&!!count&&!!nav&&rect(nav).right<=rect(pager).right+2&&rect(count).right<=rect(nav).left+2,
  frameRadius:frame?getComputedStyle(frame).borderTopLeftRadius:null,
  pagerRadius:pager?getComputedStyle(pager).borderTopLeftRadius:null,
  aligned:td.length===5&&th.length===5&&th.every((h,i)=>Math.abs(rect(h).left-rect(td[i]).left)<2),
  refreshLabel:refresh?.getAttribute('aria-label'),
  refreshTitle:refresh?.getAttribute('title'),
  refreshText:refresh?.textContent.trim(),
  refreshIcon:!!refresh?.querySelector('i.bi-arrow-clockwise'),
  filters:!!document.querySelector('form[data-aries-round3-filter] [name=Search]'),
  sortOptions:[...document.querySelectorAll('#archiveSort option')].map(o=>o.value),
  showAllColumns:!!table?.querySelector('colgroup col.owner-archives-col-status'),
  footerRight:Math.round(rect(pager)?.right||0),
  navRight:Math.round(rect(nav)?.right||0)
 };
})()`;
 for(const width of [1440,1024,768,390,320]){
  await send('Emulation.setDeviceMetricsOverride',{width,height:960,deviceScaleFactor:1,mobile:false});
  await send('Page.navigate',{url:'http://127.0.0.1:38219/fixture/TrashHistory-Index.html'});
  await delay(320);
  const seed=await evalJS("document.querySelector('.owner-archives-table tbody tr:not(.staff-data-table-empty-row)')?.outerHTML");
  if(!seed)throw Error('Archives record fixture is missing');
  const heights=[];
  for(const rows of [0,1,2,3,4,5]){
    const empty='<tr class="staff-data-table-empty-row"><td colspan="5"><div class="staff-data-table-empty"><strong>No archived bookings</strong><p>There are no archived booking records to display.</p></div></td></tr>';
    const html=rows===0?empty:Array(rows).fill(seed).join('');
    await evalJS("(()=>{document.querySelector('.owner-archives-table tbody').innerHTML="+JSON.stringify(html)+";return true})()");
    await delay(50);
    const value=await evalJS(inspect),errors=[];
    heights.push(value.scrollHeight);
    if(value.pageOverflow)errors.push('page horizontal overflow');
    if(!value.hasRoot||!value.hasSharedFrame||value.hasHeader||value.hasOldDescription||value.hasArchivedPackages)errors.push('redundant UI or archived packages visible');
    if(value.headers.join('|')!=='Booking ID|Client Name|Date and Time|Archive Date|Final Status'||value.colCount!==5)errors.push('incorrect column order');
    if(value.hasActions||!value.showAllColumns)errors.push('actions/type/actor columns leaked');
    if(value.rowCount!==rows||value.emptyRows!==(rows===0?1:0))errors.push('incorrect number of rows');
    if(value.scrollHeight!==438||value.pagerHeight!==50||!value.pagerFits||value.frameRadius!=='0px'||value.pagerRadius!=='0px')errors.push('unstable table/footer or non-square corners');
    if(rows>0&&!value.aligned)errors.push('header and body columns misaligned');
    if(value.refreshLabel!=='Refresh archived bookings'||value.refreshTitle!=='Refresh archived bookings'||value.refreshText!==''||!value.refreshIcon)errors.push('icon-only Refresh inaccessible');
    if(!value.filters||value.sortOptions.join('|')!=='newest|oldest')errors.push('search or two date-sorts missing');
    if(width<=768&&value.scrollWidth<=value.clientWidth)errors.push('controlled table horizontal scrolling missing');
    console.log(JSON.stringify({fixture:'TrashHistory-Index',rows,...value,problems:errors}));
    failures+=errors.length;
  }
  if(heights.some(h=>h!==heights[0])){failures++;console.log('ARCHIVES_TABLE_ROW_JUMP',width,heights);}
  if(width===1440||width===390){
    const screenshot=await send('Page.captureScreenshot',{format:'png'});
    await fs.writeFile(path.join(os.tmpdir(),'aries-owner-archives-'+width+'.png'),Buffer.from(screenshot.data,'base64'));
  }
  for(const [suffix,expectedRows,expectedText] of [['-empty',0,'No archived bookings'],['-filtered',0,'No matching archives'],['-five',5,'BK-2026-9800']]){
    await send('Page.navigate',{url:'http://127.0.0.1:38219/fixture/TrashHistory-Index'+suffix+'.html'});await delay(165);
    const value=await evalJS(inspect);
    const hasText=await evalJS("document.querySelector('.owner-archives-table')?.textContent.includes("+JSON.stringify(expectedText)+")");
    const issues=[];
    if(!hasText||value.rowCount!==expectedRows||value.scrollHeight!==438||value.pagerHeight!==50||!value.pagerFits||value.pageOverflow)issues.push('fixture dimensions or content failed');
    if(suffix==='-five'){
      const missingDate=await evalJS("document.querySelector('.owner-archives-table')?.textContent.includes('Not recorded')");
      const longName=await evalJS(`document.querySelector('td[data-label="Client Name"]')?.textContent.includes('Very long client name')`);
      if(!missingDate||!longName||!value.aligned)issues.push('long name, missing archive stamp or column alignment not retained');
    }
    console.log(JSON.stringify({fixture:'TrashHistory-Index'+suffix,width,...value,problems:issues}));
    failures+=issues.length;
  }
 }
 console.log('OWNER_ARCHIVES_BROWSER_COMPLETE problems='+failures);
}finally{
 if(socket&&socket.readyState===1)socket.close();
 spawnSync('taskkill',['/F','/T','/PID',String(browser.pid)],{stdio:'ignore'});
 await new Promise(resolve=>server.close(resolve));
 try{await fs.rm(profile,{recursive:true,force:true,maxRetries:3,retryDelay:200})}catch{}
}
if(failures)process.exitCode=2;