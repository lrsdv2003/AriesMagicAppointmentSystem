// Staff History real-browser verification using real authenticated Razor
// rendered from a fresh GUID LocalDB. Uses only an ephemeral local web server
// with local CSS/JS assets, never the deployed website.
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import http from 'node:http';
import {setTimeout as sleep} from 'node:timers/promises';

const fixtures=process.env.ARIES_STAFF_HISTORY_FIXTURE_ROOT;
assert(fixtures && fs.existsSync(path.join(fixtures,'staff-history-5.html')),
  'Generate History fixtures with --staff-history-table-only first');
const assetRoot=path.resolve('wwwroot');
const edgePath=process.env.EDGE_PATH||
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
assert(fs.existsSync(edgePath),'Edge browser is unavailable');
const profile=fs.mkdtempSync(path.join(os.tmpdir(),'aries-history-edge-'));
const screenshots=path.join(fixtures,'screenshots');
fs.mkdirSync(screenshots,{recursive:true});
const types={'.css':'text/css','.js':'application/javascript','.svg':'image/svg+xml',
  '.png':'image/png','.jpg':'image/jpeg','.jpeg':'image/jpeg',
  '.woff2':'font/woff2','.webp':'image/webp','.ico':'image/x-icon'};
const routedRequests=[];
const server=http.createServer((req,res)=>{
  try{
    const url=new URL(req.url,'http://127.0.0.1');
    const pathname=decodeURIComponent(url.pathname);
    let bytes,type;
    if(/^\/fixtures\/staff-history-(?:0|1|2|3|4|5|6|10)\.html$/.test(pathname)){
      bytes=fs.readFileSync(path.join(fixtures,path.basename(pathname)));
      type='text/html; charset=utf-8';
    }else if(/^\/History(?:\/Index)?$/i.test(pathname)){
      routedRequests.push(pathname+'?'+url.searchParams.toString());
      const search=url.searchParams.get('Search');
      if(search==='force-synthetic-error'){
        res.writeHead(503,{'Content-Type':'text/plain'});res.end('Synthetic database unavailable');return;
      }
      const fixture=search==='never-exists-history-synthetic'
        ? 'staff-history-no-match.html'
        : url.searchParams.get('ServiceId') && url.searchParams.get('ServiceId')!==''
          ? 'staff-history-10-filtered.html'
          : url.searchParams.get('SortBy')==='Oldest'
            ? 'staff-history-10-oldest.html'
            : 'staff-history-10.html';
      bytes=fs.readFileSync(path.join(fixtures,fixture));type='text/html; charset=utf-8';
    }else{
      const file=path.resolve(assetRoot,'.'+pathname);
      if(!file.startsWith(assetRoot+path.sep))throw new Error('outside static root');
      bytes=fs.readFileSync(file);
      type=types[path.extname(file)]||'application/octet-stream';
    }
    res.writeHead(200,{'Content-Type':type,'Cache-Control':'no-store',
      'Content-Security-Policy':"default-src 'self' data: blob: 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self';"});
    res.end(bytes);
  }catch{
    res.writeHead(404);res.end('Not Found');
  }
});
const port=await new Promise((resolve,reject)=>{
  server.once('error',reject);
  server.listen(0,'127.0.0.1',()=>resolve(server.address().port));
});
const edge=spawn(edgePath,['--headless=new','--disable-gpu','--no-first-run',
  '--no-default-browser-check','--no-sandbox','--remote-debugging-port=0',
  '--user-data-dir='+profile,'about:blank'],{stdio:'ignore',windowsHide:true});
let ws,seq=0,checks=0;
const pending=new Map();
const send=(method,params={})=>new Promise((resolve,reject)=>{
  const id=++seq;pending.set(id,{resolve,reject});
  ws.send(JSON.stringify({id,method,params}));
});
async function evaluate(expr){
  const response=await send('Runtime.evaluate',{expression:expr,returnByValue:true,awaitPromise:true});
  if(response.exceptionDetails)throw new Error(response.exceptionDetails.text);
  return response.result?.value;
}
function state(){
  const frame=document.querySelector('.staff-history-table-frame');
  const scroll=frame?.querySelector('.staff-data-table-scroll');
  const table=frame?.querySelector('.staff-history-table.staff-data-table');
  const footer=frame?.querySelector('.aries-five-pager');
  const form=document.querySelector('form[data-staff-autosearch=history]');
  const refresh=form?.querySelector('.staff-history-refresh');
  if(!frame||!scroll||!table||!footer||!form||!refresh)
    return{issues:['missing table, footer, form or refresh']};
  const rect=e=>e.getBoundingClientRect();
  const fr=rect(frame),sr=rect(scroll),pr=rect(footer),rr=rect(refresh);
  const headers=[...table.querySelectorAll('thead th')];
  const rows=[...table.querySelectorAll('tbody tr:not(.staff-data-table-empty-row)')];
  const cells=[...rows[0]?.querySelectorAll('td')||[]];
  const n=Number((location.pathname.match(/staff-history-(\d+)\.html/)||[])[1]||0);
  const issues=[];
  if(document.documentElement.scrollWidth>innerWidth+3)
    issues.push('outer page horizontal overflow '+document.documentElement.scrollWidth);
  if(fr.right>innerWidth+3||fr.left<-2||sr.width>innerWidth+3)
    issues.push('History table outside viewport');
  if(getComputedStyle(frame).borderTopLeftRadius!=='0px'||
    getComputedStyle(footer).borderBottomRightRadius!=='0px')
    issues.push('rounded table corners');
  if(Math.abs(pr.top-sr.bottom)>3)issues.push('footer gap '+(pr.top-sr.bottom));
  if(headers.length!==6||table.querySelectorAll('colgroup col').length!==6)
    issues.push('invalid semantic column count');
  if(rows.length!==Math.min(n,5))
    issues.push('wrong History row count '+rows.length);
  if((footer.querySelector('.aries-five-total')?.textContent||'').trim()
    .startsWith(n+' matching record')===false)
    issues.push('incorrect total count');
  if((footer.querySelector('.aries-five-page')?.textContent||'').trim()!==
    'Page 1 of '+Math.max(1,Math.ceil(n/5)))
    issues.push('incorrect page indicator');
  const nav=footer.querySelectorAll('[aria-label="Next page"],[aria-label="Previous page"]');
  if(n<=5&&nav.length)issues.push('single-page controls not hidden');
  if(n>5&&nav.length!==2)issues.push('multi-page controls missing');
  if(n===0&&!table.querySelector('.staff-data-table-empty'))
    issues.push('missing in-table empty state');
  if(n>0&&table.querySelector('.staff-data-table-empty'))
    issues.push('filler empty row with real History records');
  if(Math.abs(rr.width-40)>2||Math.abs(rr.height-40)>2)
    issues.push('refresh icon wrong dimensions');
  if(refresh.getAttribute('aria-label')!=='Refresh history'||
    refresh.getAttribute('title')!=='Refresh history'||
    !refresh.querySelector('.bi-arrow-clockwise'))
    issues.push('Refresh not icon-only and accessible');
  const caption=refresh.querySelector('span');
  if(!caption?.classList.contains('visually-hidden'))
    issues.push('Refresh text still visually displayed');
  if(Math.abs(rect(form.querySelector('#historySearch')).height-40)>2)
    issues.push('search toolbar control height mismatch');
  if(getComputedStyle(scroll).overflowX!=='auto')
    issues.push('inner table scrolling disabled');
  if(innerWidth<=1024&&scroll.scrollWidth<=scroll.clientWidth+30)
    issues.push('table cannot scroll internally on narrow layout');
  if(getComputedStyle(table.tHead).display==='none')
    issues.push('History headings hidden on mobile');
  if(cells.length===6){
    for(let i=0;i<6;i++){
      if(Math.abs(rect(headers[i]).left-rect(cells[i]).left)>2||
        Math.abs(rect(headers[i]).right-rect(cells[i]).right)>2)
        issues.push('header vs data alignment '+i);
    }
    const detail=cells[5].querySelector('a');
    if(!detail||rect(detail).height<39)
      issues.push('Details action too small');
    else if(rect(detail).right>rect(cells[5]).right+3)
      issues.push('Details button clipped');
  }
  return{issues,count:n,visible:rows.length,width:innerWidth,
    tableH:Math.round(sr.height),footerH:Math.round(pr.height),
    documentW:document.documentElement.scrollWidth,
    headerDisplay:getComputedStyle(table.tHead).display,
    bodyDisplay:getComputedStyle(table.tBodies[0]).display,
    rowHeights:[...table.tBodies[0].rows].map(x=>Math.round(rect(x).height))};
}
async function load(name){
  await send('Page.navigate',{url:'http://127.0.0.1:'+port+'/fixtures/'+name});
  let loaded=false;
  for(let i=0;i<80;i++){
    loaded=!!(await evaluate("document.readyState==='complete' && !!document.querySelector('.staff-history-table-frame')"));
    if(loaded)break;
    await sleep(90);
  }
  assert(loaded,'History fixture did not load '+name);
}
try{
  let debugPort;
  for(let i=0;i<100;i++){
    const file=path.join(profile,'DevToolsActivePort');
    if(fs.existsSync(file)){debugPort=Number(fs.readFileSync(file,'utf8').split(/\r?\n/)[0]);break;}
    if(edge.exitCode!==null)throw new Error('Edge exited before debugger');
    await sleep(100);
  }
  assert(debugPort>0,'Edge remote debugging unavailable');
  let targets;
  for(let i=0;i<50;i++){
    try{
      targets=await(await fetch('http://127.0.0.1:'+debugPort+'/json/list')).json();
      if(targets.find(x=>x.type==='page'))break;
    }catch{}
    await sleep(100);
  }
  const target=targets.find(x=>x.type==='page');
  assert(target?.webSocketDebuggerUrl,'Edge target unavailable');
  ws=new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve,reject)=>{
    ws.addEventListener('open',resolve,{once:true});
    ws.addEventListener('error',reject,{once:true});
  });
  ws.addEventListener('message',event=>{
    const packet=JSON.parse(event.data);
    if(!packet.id)return;
    const response=pending.get(packet.id);
    if(!response)return;
    pending.delete(packet.id);
    packet.error?response.reject(new Error(JSON.stringify(packet.error))):response.resolve(packet.result);
  });
  await send('Page.enable');
  await send('Runtime.enable');
  await send('Network.enable');
  await send('Network.setBlockedURLs',{urls:['https://*']});
  for(const width of [1440,1024,768,390,320]){
    await send('Emulation.setDeviceMetricsOverride',
      {width,height:900,deviceScaleFactor:1,mobile:false});
    let baseline=null;
    for(const n of [0,1,2,3,4,5,6,10]){
      await load('staff-history-'+n+'.html');
      const report=await evaluate('('+state.toString()+')()');
      assert.deepEqual(report.issues,[],'History responsive '+JSON.stringify(report));
      if(width>=768&&n<=5){
        if(baseline===null)baseline=report.tableH;
        assert(Math.abs(report.tableH-baseline)<=1,
          'History footer jumps with 0-5 records: '+JSON.stringify(report));
      }
      checks++;
      console.log('PASS History Edge width='+width+' count='+n+
        ' visible='+report.visible+' tableH='+report.tableH+' footerH='+report.footerH);
      if(n===0||n===5){
        const png=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});
        fs.writeFileSync(path.join(screenshots,'history-'+width+'-'+n+'.png'),
          Buffer.from(png.data,'base64'));
      }
    }
  }
  await send('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false});
  await load('staff-history-10.html');
  const firstBefore=await evaluate("document.querySelector('.staff-history-table tbody tr:not(.staff-data-table-empty-row) td strong')?.textContent.trim()");
  const oldCount=routedRequests.length;
  await evaluate("document.querySelector('#historySort').value='Oldest';"+
    "document.querySelector('#historySort').dispatchEvent(new Event('change',{bubbles:true}));true");
  for(let i=0;i<50&&routedRequests.length===oldCount;i++)await sleep(100);
  await sleep(400);
  assert.equal(routedRequests.length-oldCount,1,'History sorting made duplicate AJAX requests');
  const firstAfter=await evaluate("document.querySelector('.staff-history-table tbody tr:not(.staff-data-table-empty-row) td strong')?.textContent.trim()");
  assert.notEqual(firstBefore,firstAfter,'History sorting did not replace visible table rows');
  assert(routedRequests.at(-1).includes('SortBy=Oldest'),'History sort value not preserved');
  checks++;console.log('PASS History sorting makes one AJAX request and updates records');
  await load('staff-history-10.html');
  const service=await evaluate("document.querySelector('#historyPackage option:not([value=\"\"])')?.value");
  assert(service,'fixture lacks a service filter option');
  const filterBefore=routedRequests.length;
  await evaluate("document.querySelector('#historyPackage').value="+JSON.stringify(service)+";"+
    "document.querySelector('#historyPackage').dispatchEvent(new Event('change',{bubbles:true}));true");
  for(let i=0;i<50&&routedRequests.length===filterBefore;i++)await sleep(100);
  await sleep(400);
  assert.equal(routedRequests.length-filterBefore,1,'History filter generated duplicate requests');
  assert.equal(await evaluate("document.querySelector('.aries-five-total')?.textContent.trim()"),
    '5 matching records','History filter did not replace total and table together');
  checks++;console.log('PASS History package filter updates table/total without leaving first page');
  const searchBefore=routedRequests.length;
  await evaluate("document.querySelector('#historySearch').value='never-exists-history-synthetic';"+
    "document.querySelector('#historySearch').dispatchEvent(new Event('input',{bubbles:true}));true");
  for(let i=0;i<50&&routedRequests.length===searchBefore;i++)await sleep(100);
  await sleep(400);
  assert.equal(routedRequests.length-searchBefore,1,'History debounced search made duplicate requests');
  assert.equal(await evaluate("document.querySelector('.staff-data-table-empty strong')?.textContent.trim()"),
    'No matching records','History empty state not injected after AJAX search');
  assert.equal(await evaluate("document.querySelector('.aries-five-total')?.textContent.trim()"),
    '0 matching records','History footer not updated after AJAX search');
  checks++;console.log('PASS History debounced search updates contextual empty state and footer');
  const currentUrl=await evaluate('location.href');
  await evaluate("document.querySelector('.staff-history-refresh').click();true");
  for(let i=0;i<60;i++){
    if(routedRequests.some(x=>x.includes('Search=never-exists-history-synthetic')))break;
    await sleep(100);
  }
  assert(routedRequests.some(x=>x.includes('Search=never-exists-history-synthetic')),
    'Refresh did not preserve current history search when reloading');
  checks++;console.log('PASS icon-only Refresh uses existing GET and preserves active query');
  await load('staff-history-10.html');
  const errorBefore=routedRequests.length;
  await evaluate("document.querySelector('#historySearch').value='force-synthetic-error';"+
    "document.querySelector('#historySearch').dispatchEvent(new Event('input',{bubbles:true}));true");
  for(let i=0;i<50&&routedRequests.length===errorBefore;i++)await sleep(100);
  await sleep(400);
  assert.equal(await evaluate("document.querySelector('.staff-round2-search-status')?.textContent.trim()"),
    'Search unavailable. Press Enter to retry.',
    'Retrieval error must not masquerade as empty History');
  assert.equal(await evaluate("document.querySelectorAll('.staff-history-table tbody tr:not(.staff-data-table-empty-row)').length"),
    5,'Error replaced valid records rather than leaving last known result');
  checks++;console.log('PASS History AJAX error retains previous records and signals retrieval failure');
  console.log('STAFF_HISTORY_EDGE_PASS: '+checks+' responsive/interaction checks');
}finally{
  for(const {reject} of pending.values())reject(new Error('Browser closing'));
  pending.clear();
  if(ws&&ws.readyState===WebSocket.OPEN)ws.close();
  edge.kill();
  await new Promise(resolve=>server.close(resolve));
}
