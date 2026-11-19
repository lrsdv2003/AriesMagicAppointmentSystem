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
 'http://127.0.0.1:38219/fixture/SystemActivity-Index.html'
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
 const root=document.querySelector('.owner-activity-page');
 const frame=document.querySelector('.owner-activity-table-frame');
 const wrap=frame?.querySelector('.staff-data-table-scroll');
 const table=frame?.querySelector('.owner-activity-table');
 const pager=frame?.querySelector('.aries-five-pager');
 const th=[...table?.querySelectorAll('thead th')||[]];
 const first=table?.querySelector('tbody tr:not(.staff-data-table-empty-row)');
 const td=[...first?.querySelectorAll('td')||[]];
 const box=e=>e?.getBoundingClientRect();
 const footerCount=pager?.querySelector('.aries-five-total'),footerNav=pager?.querySelector('.aries-five-actions');
 const aligned=td.length===6&&th.length===6&&th.every((h,i)=>Math.abs(box(h).left-box(td[i]).left)<2);
 return {
  width:innerWidth,
  pageOverflow:document.documentElement.scrollWidth>innerWidth+2,
  hasRoot:!!root,
  hasSharedFrame:!!frame?.classList.contains('staff-data-table-frame'),
  hasHeader:!!document.querySelector('.workspace-page-heading,.admin-heading'),
  hasUTCNotice:!!root?.textContent.includes('Dates and times are UTC.'),
  hasOldNotes:!!document.querySelector('.admin-note,.admin-table-wrap'),
  headers:th.map(x=>x.textContent.trim()),
  colCount:table?.querySelectorAll('colgroup col').length,
  rowCount:table?.querySelectorAll('tbody tr:not(.staff-data-table-empty-row)').length,
  emptyRows:table?.querySelectorAll('tbody tr.staff-data-table-empty-row').length,
  hasTarget:!!table?.querySelector('th[data-label=Target],td[data-label=Target]')||th.some(x=>x.textContent.trim()==='Target'),
  hasDetails:!!table?.querySelector('details,summary,a[href*="Details"]')||!!table?.textContent.includes('View Details'),
  descriptions:[...table?.querySelectorAll('.owner-activity-description')||[]].map(e=>e.textContent.trim().length),
  hasUTC:!!table?.querySelector('time[datetime$="Z"]'),
  scrollHeight:Math.round(box(wrap)?.height||0),
  scrollWidth:wrap?.scrollWidth,clientWidth:wrap?.clientWidth,
  pagerHeight:Math.round(box(pager)?.height||0),
  pagerFits:!!pager&&!!footerCount&&!!footerNav&&box(footerNav).right<=box(pager).right+2&&box(footerCount).right<=box(footerNav).left+2,
  frameRadius:frame?getComputedStyle(frame).borderTopLeftRadius:null,
  pagerRadius:pager?getComputedStyle(pager).borderTopLeftRadius:null,
  aligned,hasFilters:!!document.querySelector('[name="type"]')&&!!document.querySelector('[name="sortBy"]')&&!!document.querySelector('[name="search"]'),
  hasRefresh:!!document.querySelector('[aria-label="Refresh business activity"]'),
  truncated:[...table?.querySelectorAll('.owner-activity-description')||[]].some(e=>['hidden','clip'].includes(getComputedStyle(e).overflowY)||getComputedStyle(e).webkitLineClamp!=='none')
 };
})()`;
 for(const width of [1440,1024,768,390,320]){
  await send('Emulation.setDeviceMetricsOverride',{width,height:960,deviceScaleFactor:1,mobile:false});
  await send('Page.navigate',{url:'http://127.0.0.1:38219/fixture/SystemActivity-Index.html'});
  await delay(330);
  const seed=await evalJS("document.querySelector('.owner-activity-table tbody tr:not(.staff-data-table-empty-row)')?.outerHTML");
  if(!seed)throw Error('Activity table record seed missing');
  const heights=[];
  for(const rows of [0,1,2,3,4,5]){
   const empty='<tr class="staff-data-table-empty-row"><td colspan="6"><div class="staff-data-table-empty"><strong>No business activity</strong><p>No activity records are available.</p></div></td></tr>';
   const html=rows===0?empty:Array(rows).fill(seed).join('');
   await evalJS("(()=>{document.querySelector('.owner-activity-table tbody').innerHTML="+JSON.stringify(html)+";return true})()");
   await delay(55);
   const value=await evalJS(inspect),errors=[];
   heights.push(value.scrollHeight);
   if(value.pageOverflow)errors.push('unexpected document-level horizontal overflow');
   if(!value.hasRoot||!value.hasSharedFrame||value.hasHeader||value.hasUTCNotice||value.hasOldNotes)errors.push('redundant heading/page wrapper remains');
   if(value.headers.join('|')!=='User|Role at time|Activity|Module|Date & time (UTC)|Result'||value.colCount!==6)errors.push('six-column activity table layout mismatch');
   if(value.hasTarget||value.hasDetails)errors.push('retired Target/Details UI remains');
   if(value.rowCount!==rows||value.emptyRows!==(rows===0?1:0))errors.push('wrong displayed row count');
   if(value.scrollHeight!==438)errors.push('five-row viewport changed');
   if(value.pagerHeight!==50||!value.pagerFits||value.frameRadius!=='0px'||value.pagerRadius!=='0px')errors.push('footer height/fit or corner mismatch');
   if(!value.hasFilters||!value.hasRefresh||!value.aligned&&rows>0)errors.push('filters, refresh or aligned columns lost');
   if(value.truncated)errors.push('activity descriptions silently truncated');
   if(rows>0&&!value.hasUTC)errors.push('UTC datetime semantic metadata missing');
   if(width<=768&&value.scrollWidth<=value.clientWidth)errors.push('mobile table horizontal scrolling missing');
   console.log(JSON.stringify({fixture:'SystemActivity-Index',rows,...value,problems:errors}));
   failures+=errors.length;
  }
  if(heights.some(n=>n!==heights[0])){failures++;console.log('ACTIVITY_HEIGHT_JUMP',width,heights);}
  if(width===1440||width===390){
   const shot=await send('Page.captureScreenshot',{format:'png'});
   await fs.writeFile(path.join(os.tmpdir(),'aries-owner-activity-'+width+'.png'),Buffer.from(shot.data,'base64'));
  }
  await send('Page.navigate',{url:'http://127.0.0.1:38219/fixture/SystemActivity-Index-five.html'});await delay(160);
  const five=await evalJS(inspect);
  const actualIssues=[];
  if(five.rowCount!==5||five.descriptions[0]<100||five.truncated||five.pageOverflow)actualIssues.push('long descriptions and real 5-row fixture not preserved');
  console.log(JSON.stringify({fixture:'SystemActivity-Index-five',width,...five,problems:actualIssues}));failures+=actualIssues.length;
  for(const [suffix,text] of [['-empty','No business activity'],['-filtered','No matching activity']]){
   await send('Page.navigate',{url:'http://127.0.0.1:38219/fixture/SystemActivity-Index'+suffix+'.html'});await delay(160);
   const value=await evalJS(inspect);
   const present=await evalJS("document.querySelector('.staff-data-table-empty')?.textContent?.includes("+JSON.stringify(text)+")");
   if(!present||value.scrollHeight!==438||value.pageOverflow){failures++;console.log('ACTIVITY_EMPTY_FAIL',width,suffix,value);}
  }
 }
 console.log('OWNER_BUSINESS_ACTIVITY_BROWSER_COMPLETE problems='+failures);
}finally{
 if(socket&&socket.readyState===1)socket.close();
 spawnSync('taskkill',['/F','/T','/PID',String(browser.pid)],{stdio:'ignore'});
 await new Promise(resolve=>server.close(resolve));
 try{await fs.rm(profile,{recursive:true,force:true,maxRetries:3,retryDelay:200})}catch{}
}
if(failures)process.exitCode=2;