// Isolated real-browser Staff Bookings 5-row table and pager verification.
// Generate real Razor fixture files with --staff-bookings-table-only.
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import http from 'node:http';
import {setTimeout as sleep} from 'node:timers/promises';

const root=path.resolve('wwwroot');
const fixtures=process.env.ARIES_STAFF_BOOKINGS_FIXTURE_ROOT;
assert(fixtures && fs.existsSync(path.join(fixtures,'staff-bookings-5.html')),
  'Run --staff-bookings-table-only with ARIES_STAFF_BOOKINGS_FIXTURE_ROOT first');
const edgePath=process.env.EDGE_PATH ||
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
assert(fs.existsSync(edgePath),'Edge browser unavailable');
const profile=fs.mkdtempSync(path.join(os.tmpdir(),'aries-bookings-edge-'));
const types={'.css':'text/css','.js':'application/javascript','.svg':'image/svg+xml',
  '.png':'image/png','.jpg':'image/jpeg','.jpeg':'image/jpeg','.woff2':'font/woff2',
  '.webp':'image/webp','.ico':'image/x-icon'};
const fetchPaths=[];
const server=http.createServer((req,res)=>{
  try{
    const pathname=decodeURIComponent(new URL(req.url,'http://127.0.0.1').pathname);
    if(/^\/Bookings(?:\/Index)?$/i.test(pathname)){
      fetchPaths.push(pathname+'?'+new URL(req.url,'http://127.0.0.1').searchParams.toString());
    }
    let data,type;
    if(pathname.endsWith('.html') && pathname.startsWith('/fixtures/')){
      const file=path.basename(pathname);
      if(!/^staff-bookings-(?:[0-9]+(?:-page2)?|no-match)\.html$/.test(file))
        throw new Error('bad fixture name');
      data=fs.readFileSync(path.join(fixtures,file));type='text/html';
    }else if(/^\/Bookings(?:\/Index)?$/i.test(pathname)){
      data=fs.readFileSync(path.join(fixtures,'staff-bookings-10.html'));type='text/html';
    }else{
      const file=path.resolve(root,'.'+pathname);
      if(!file.startsWith(root+path.sep))throw new Error('outside static root');
      data=fs.readFileSync(file);type=types[path.extname(file)]||'application/octet-stream';
    }
    res.writeHead(200,{'Content-Type':type,'Cache-Control':'no-store',
      'Content-Security-Policy':"default-src 'self' data: blob: 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self';"});
    res.end(data);
  }catch{
    res.writeHead(404);res.end('Not Found');
  }
});
const port=await new Promise((resolve,reject)=>{
  server.once('error',reject);server.listen(0,'127.0.0.1',()=>resolve(server.address().port));
});
const edge=spawn(edgePath,['--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check',
  '--no-sandbox','--remote-debugging-port=0','--user-data-dir='+profile,'about:blank'],
  {stdio:'ignore',windowsHide:true});
let ws,sequence=0,checks=0;
const pending=new Map();
function cmd(method,params={}){
  return new Promise((resolve,reject)=>{
    const id=++sequence;pending.set(id,{resolve,reject});
    ws.send(JSON.stringify({id,method,params}));
  });
}
function inspectTable(){
  const frame=document.querySelector('.staff-data-table-frame');
  const scroll=document.querySelector('.staff-data-table-scroll');
  const table=document.querySelector('.staff-data-table');
  const pager=frame?.querySelector('.aries-five-pager');
  const sort=document.querySelector('#staffBookingSort');
  if(!frame||!scroll||!table||!pager||!sort)return {issues:['missing table/frame/pager/sort']};
  const count=Number((location.pathname.match(/staff-bookings-(\d+)\.html/)||[])[1]||0);
  const rows=[...table.querySelectorAll('tbody tr:not(.staff-data-table-empty-row)')];
  const headers=[...table.querySelectorAll('thead th')];
  const cells=[...rows[0]?.querySelectorAll('td')||[]];
  const rect=e=>e.getBoundingClientRect();
  const fr=rect(frame),sr=rect(scroll),pr=rect(pager);
  const issues=[];
  const mobile=innerWidth<768;
  if(document.documentElement.scrollWidth>innerWidth+3)
    issues.push('outer document horizontal overflow: '+document.documentElement.scrollWidth);
  if(getComputedStyle(frame).borderTopLeftRadius!=='0px'||
    getComputedStyle(pager).borderBottomRightRadius!=='0px')
    issues.push('round corners remain');
  if(Math.abs(pr.top-sr.bottom)>3)
    issues.push('pager displaced from table region '+Math.round(pr.top-sr.bottom));
  if(sr.width>innerWidth+3||fr.right>innerWidth+3||fr.left < -2)
    issues.push('table frame outside viewport');
  if(headers.length!==6||table.querySelectorAll('colgroup col').length!==6)
    issues.push('invalid column count');
  if(rows.length!==Math.min(count,5))
    issues.push('rows '+rows.length+' expected '+Math.min(count,5));
  // Check cross-record height stability against the first fixture per viewport.
  if(getComputedStyle(scroll).overflowX!=='auto')
    issues.push('horizontal scrolling disabled');
  if(mobile && scroll.scrollWidth<=scroll.clientWidth+50)
    issues.push('narrow table missing inner horizontal scroll');
  if(!mobile && (getComputedStyle(table).tableLayout!=='fixed'))
    issues.push('columns not fixed');
  if(count===0 && !table.querySelector('.staff-data-table-empty'))
    issues.push('missing semantic empty state');
  if(count>0 && table.querySelector('.staff-data-table-empty'))
    issues.push('filler empty state among real records');
  if(cells.length===6){
    for(let i=0;i<6;i++){
      if(Math.abs(rect(cells[i]).left-rect(headers[i]).left)>2 ||
        Math.abs(rect(cells[i]).right-rect(headers[i]).right)>2)
        issues.push('header vs cell misalignment '+i);
    }
    const action=cells[5].querySelector('a');
    if(!action || rect(action).height<38)
      issues.push('action click target too small');
    else if(rect(action).right>rect(cells[5]).right+2)
      issues.push('action button overflow');
  }
  if(getComputedStyle(sort).height!=='40px')
    issues.push('sort control not compact');
  const actions=pager.querySelectorAll('a[aria-label="Next page"], a[aria-label="Previous page"], button[aria-label="Next page"], button[aria-label="Previous page"]');
  if(count<=5 && actions.length>0) issues.push('single-page navigation visible');
  if(count>5 && actions.length===0) issues.push('multi-page navigation missing');
  const footerCount=pager.querySelector('.aries-five-total')?.textContent.trim()||'';
  if(!footerCount.startsWith(count+' matching record'))
    issues.push('wrong count '+footerCount);
  return {issues,count,rows:rows.length,width:innerWidth,frameH:Math.round(fr.height),
    tableRegionH:Math.round(sr.height),pagerH:Math.round(pr.height),
    headH:Math.round(rect(table.tHead).height),
    bodyH:Math.round(rect(table.tBodies[0]).height),
    rowHeights:[...table.tBodies[0].rows].map(r=>Math.round(rect(r).height)),
    emptyCellPadding:table.querySelector('.staff-data-table-empty-row td')?getComputedStyle(table.querySelector('.staff-data-table-empty-row td')).padding:'',
    emptyBlockH:table.querySelector('.staff-data-table-empty')?Math.round(rect(table.querySelector('.staff-data-table-empty')).height):0,
    documentWidth:document.documentElement.scrollWidth};
}
async function evalBrowser(expression){
  const output=await cmd('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});
  if(output.exceptionDetails)throw new Error(output.exceptionDetails.text);
  return output.result?.value;
}
async function go(file){
  await cmd('Page.navigate',{url:'http://127.0.0.1:'+port+'/fixtures/'+file});
  let ready=false;
  for(let tries=0;tries<70;tries++){
    if(await evalBrowser("document.readyState==='complete' && !!document.querySelector('.staff-data-table-frame')")){
      ready=true;break;
    }
    await sleep(80);
  }
  assert(ready,'fixture did not render '+file);
}
try{
  let debugPort;
  for(let i=0;i<100;i++){
    const file=path.join(profile,'DevToolsActivePort');
    if(fs.existsSync(file)){debugPort=Number(fs.readFileSync(file,'utf8').split(/\r?\n/)[0]);break;}
    if(edge.exitCode!==null)throw new Error('Edge closed before opening debugger');
    await sleep(100);
  }
  assert(debugPort>0,'Edge debugging port not available');
  let pages;
  for(let i=0;i<50;i++){
    try{
      pages=await(await fetch('http://127.0.0.1:'+debugPort+'/json/list')).json();
      if(pages.find(x=>x.type==='page'))break;
    }catch{}
    await sleep(100);
  }
  const target=pages.find(x=>x.type==='page');
  assert(target?.webSocketDebuggerUrl,'no DevTools page socket');
  ws=new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve,reject)=>{
    ws.addEventListener('open',resolve,{once:true});
    ws.addEventListener('error',reject,{once:true});
  });
  ws.addEventListener('message',event=>{
    const packet=JSON.parse(event.data);
    if(!packet.id)return;
    const entry=pending.get(packet.id);if(!entry)return;
    pending.delete(packet.id);
    packet.error?entry.reject(new Error(JSON.stringify(packet.error))):entry.resolve(packet.result);
  });
  await cmd('Page.enable');await cmd('Runtime.enable');await cmd('Network.enable');
  await cmd('Network.setBlockedURLs',{urls:['https://*']});
  const captures=path.join(fixtures,'screenshots');
  fs.mkdirSync(captures,{recursive:true});
  for(const width of [1440,1024,768,390,320]){
    await cmd('Emulation.setDeviceMetricsOverride',{
      width,height:900,deviceScaleFactor:1,mobile:false
    });
    let fiveRowReference=null;
    for(const n of [0,1,3,5,6,10]){
      await go('staff-bookings-'+n+'.html');
      const state=await evalBrowser('('+inspectTable.toString()+')()');
      assert(state,'no DOM layout result');
      assert.deepEqual(state.issues,[],JSON.stringify(state));
      if(width>=768 && n<=5){
        if(fiveRowReference===null)fiveRowReference=state.tableRegionH;
        assert(Math.abs(state.tableRegionH-fiveRowReference)<=1,
          'table shifts with record count at width '+width+': '+JSON.stringify(state));
      }
      checks++;
      console.log('PASS Booking table Edge width='+width+' records='+n+
        ' height='+state.tableRegionH+' footer='+state.pagerH);
      if(n===0||n===5){
        const picture=await cmd('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});
        fs.writeFileSync(path.join(captures,'bookings-'+width+'-'+n+'.png'),
          Buffer.from(picture.data,'base64'));
      }
    }
  }
  // One AJAX request for Staff sort: aries-final-ui must not trigger a duplicate
  // full-page submit while staff-round2 updates table/pager.
  await cmd('Emulation.setDeviceMetricsOverride',{
    width:1440,height:900,deviceScaleFactor:1,mobile:false
  });
  await go('staff-bookings-10.html');
  const before=fetchPaths.length;
  await evalBrowser("document.querySelector('#staffBookingSort').value='oldest';"+
    "document.querySelector('#staffBookingSort').dispatchEvent(new Event('change',{bubbles:true}));true");
  for(let i=0;i<35;i++){
    if(fetchPaths.length>before)break;
    await sleep(100);
  }
  await sleep(400);
  assert.equal(fetchPaths.length-before,1,
    'Sort should trigger exactly one AJAX request, not submit plus AJAX');
  assert(fetchPaths.at(-1).includes('sortBy=oldest'),
    'Sort request carries current selected option');
  checks++;
  console.log('PASS Staff sort fires one fetch and preserves selection');
  console.log('STAFF_BOOKINGS_EDGE_PASS: '+checks+' responsive/interaction checks');
}finally{
  for(const {reject} of pending.values())reject(new Error('Edge closing'));
  pending.clear();
  if(ws && ws.readyState===WebSocket.OPEN)ws.close();
  edge.kill();
  await new Promise(resolve=>server.close(resolve));
}
