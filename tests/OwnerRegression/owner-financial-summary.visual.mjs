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
 const root=document.querySelector('.owner-reports-page');
 const section=document.querySelector('.owner-report-records-section');
 const frame=document.querySelector('.owner-financial-records-frame');
 const wrap=frame?.querySelector('.owner-financial-records-scroll');
 const table=frame?.querySelector('.owner-financial-records-table');
 const pager=frame?.querySelector('.aries-five-pager');
 const th=[...table?.querySelectorAll('thead th')||[]];
 const first=table?.querySelector('tbody tr:not(.staff-data-table-empty-row)');
 const td=[...first?.querySelectorAll('td')||[]];
 const rect=e=>e?.getBoundingClientRect();
 const count=pager?.querySelector('.aries-five-total');
 const nav=pager?.querySelector('.aries-five-actions');
 const money=td.filter(e=>e.classList.contains('owner-financial-amount'));
 const checkMoney=th.filter(e=>e.classList.contains('owner-financial-amount'));
 const headingAlignment=td.length===9&&th.length===9&&th.every((h,i)=>Math.abs(rect(h).left-rect(td[i]).left)<2);
 const moneyAligned=money.length===3&&checkMoney.length===3&&money.every(e=>getComputedStyle(e).textAlign==='right'&&getComputedStyle(e).whiteSpace==='nowrap');
 const moneyReadable=money.every(e=>e.scrollWidth<=e.clientWidth+2);
 const pageHasFilters=!!document.querySelector('form[aria-label="Report filters"]')&&!!document.querySelector('form[aria-label="Search financial booking records"]');
 return {
  width:innerWidth,
  pageOverflow:document.documentElement.scrollWidth>innerWidth+2,
  hasRoot:!!root,hasFrame:!!frame?.classList.contains('staff-data-table-frame'),
  redundantHeading:!!document.querySelector('.workspace-page-heading'),
  hasSummary:!!document.querySelector('.owner-report-metrics'),
  hasCharts:!!document.querySelector('#revenueTrendChart'),
  hasExport:!!document.querySelector('a[href*="ExportCsv"]'),
  hasPrint:!!document.querySelector('button[onclick="window.print()"]'),
  headers:th.map(x=>x.textContent.trim()),colCount:table?.querySelectorAll('colgroup col').length,
  rowCount:table?.querySelectorAll('tbody tr:not(.staff-data-table-empty-row)').length,
  emptyRows:table?.querySelectorAll('tbody tr.staff-data-table-empty-row').length,
  scrollHeight:Math.round(rect(wrap)?.height||0),
  scrollWidth:wrap?.scrollWidth,clientWidth:wrap?.clientWidth,
  pagerHeight:Math.round(rect(pager)?.height||0),
  pagerFits:!!pager&&!!count&&!!nav&&rect(nav).right<=rect(pager).right+2&&rect(count).right<=rect(nav).left+2,
  frameRadius:frame?getComputedStyle(frame).borderTopLeftRadius:null,
  pagerRadius:pager?getComputedStyle(pager).borderTopLeftRadius:null,
  aligned:headingAlignment,moneyAligned,moneyReadable,
  frameFullWidth:!!frame&&!!section&&Math.abs(rect(frame).left-rect(section).left)<3&&Math.abs(rect(frame).right-rect(section).right)<3,
  hasFilters:pageHasFilters,
  largeAmount:!!table?.textContent.includes('12,500,000.50'),
  longClient:!!table?.textContent.includes('Very long client name'),
  totalMetrics:document.querySelector('.owner-report-metrics')?.textContent.replace(/\\s+/g,' ').trim().slice(0,160),
  debug:{
    sectionRect:section?[rect(section).left,rect(section).right,rect(section).width]:null,
    frameRect:frame?[rect(frame).left,rect(frame).right,rect(frame).width]:null,
    sectionPadding:section?getComputedStyle(section).padding:null,
    sectionBorder:section?getComputedStyle(section).border:null,
    frameMargin:frame?getComputedStyle(frame).margin:null,
    framePadding:frame?getComputedStyle(frame).padding:null,
    pagerRect:pager?[rect(pager).left,rect(pager).right,rect(pager).width]:null,
    totalRect:count?[rect(count).left,rect(count).right,rect(count).width]:null,
    navRect:nav?[rect(nav).left,rect(nav).right,rect(nav).width]:null,
    scrollRect:wrap?[rect(wrap).left,rect(wrap).right,rect(wrap).width]:null,
    tableMinWidth:table?getComputedStyle(table).minWidth:null
  },
  clipping:td.filter(e=>getComputedStyle(e).overflow==='hidden'&&e.scrollWidth>e.clientWidth+2).length
 };
})()`;
 for(const width of [1440,1024,768,390,320]){
  await send('Emulation.setDeviceMetricsOverride',{width,height:950,deviceScaleFactor:1,mobile:false});
  await send('Page.navigate',{url:'http://127.0.0.1:38219/fixture/Reports-Index-five.html'});
  await delay(320);
  const seed=await evalJS("document.querySelector('.owner-financial-records-table tbody tr:not(.staff-data-table-empty-row)')?.outerHTML");
  if(!seed)throw Error('Finance record seed missing');
  const heights=[];
  for(const rows of [0,1,2,3,4,5]){
   const empty='<tr class="staff-data-table-empty-row"><td colspan="9"><div class="staff-data-table-empty"><strong>No financial records</strong><p>No financial records are available to display.</p></div></td></tr>';
   const html=rows===0?empty:Array(rows).fill(seed).join('');
   await evalJS("(()=>{document.querySelector('.owner-financial-records-table tbody').innerHTML="+JSON.stringify(html)+";return true})()");
   await delay(45);
   const v=await evalJS(inspect),issues=[];
   heights.push(v.scrollHeight);
   if(v.pageOverflow)issues.push('page horizontal overflow');
   if(!v.hasRoot||!v.hasFrame||v.redundantHeading||!v.hasSummary||!v.hasCharts||!v.hasExport||!v.hasPrint)issues.push('reports shell or financial tools removed');
   if(v.headers.join('|')!=='Booking|Client|Package|Event Date|Booking Value|Collected|Outstanding|Status|Action'||v.colCount!==9)issues.push('financial columns changed');
   if(v.rowCount!==rows||v.emptyRows!==(rows===0?1:0))issues.push('incorrect row state');
   if(v.scrollHeight!==438||v.pagerHeight!==50||!v.pagerFits||v.frameRadius!=='0px'||v.pagerRadius!=='0px')issues.push('unstable table/footer or rounded corners');
   if(!v.frameFullWidth||!v.hasFilters)issues.push('table/toolbar alignment or filter access');
   if(rows>0&&(!v.aligned||!v.moneyAligned||!v.moneyReadable||v.clipping))issues.push('financial cells misaligned, clipped or unreadable');
   if(width<=768&&v.scrollWidth<=v.clientWidth)issues.push('mobile horizontal table scrolling missing');
   console.log(JSON.stringify({fixture:'Reports-Index',rows,...v,problems:issues}));
   failures+=issues.length;
  }
  if(heights.some(h=>h!==heights[0])){failures++;console.log('FINANCIAL_TABLE_HEIGHT_JUMP',width,heights);}
  if(width===1440||width===390){
   const shot=await send('Page.captureScreenshot',{format:'png'});
   await fs.writeFile(path.join(os.tmpdir(),'aries-owner-financial-'+width+'.png'),Buffer.from(shot.data,'base64'));
  }
  for(const [suffix,rows,phrase] of [['-empty',0,'No financial records'],['-filtered',0,'No matching financial records'],['-five',5,'12,500,000.50']]){
   await send('Page.navigate',{url:'http://127.0.0.1:38219/fixture/Reports-Index'+suffix+'.html'});await delay(135);
   const v=await evalJS(inspect);
   const includes=await evalJS("document.querySelector('.owner-financial-records-table')?.textContent.includes("+JSON.stringify(phrase)+")");
   const issues=[];
   if(!includes||v.rowCount!==rows||v.scrollHeight!==438||v.pagerHeight!==50||!v.pagerFits||v.pageOverflow)issues.push('fixture content, footer or layout invalid');
   if(suffix==='-five'&&(!v.moneyReadable||!v.moneyAligned||!v.aligned||!v.longClient||!v.frameFullWidth))issues.push('real large amounts, long names, columns or frame alignment');
   console.log(JSON.stringify({fixture:'Reports-Index'+suffix,width,...v,problems:issues}));
   failures+=issues.length;
  }
 }
 console.log('OWNER_FINANCIAL_SUMMARY_BROWSER_COMPLETE problems='+failures);
}finally{
 if(socket&&socket.readyState===1)socket.close();
 spawnSync('taskkill',['/F','/T','/PID',String(browser.pid)],{stdio:'ignore'});
 await new Promise(resolve=>server.close(resolve));
 try{await fs.rm(profile,{recursive:true,force:true,maxRetries:3,retryDelay:200})}catch{}
}
if(failures)process.exitCode=2;