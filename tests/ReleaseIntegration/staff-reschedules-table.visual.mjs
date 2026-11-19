// Staff Reschedule visual and interaction regression.
// Real authenticated Razor HTML from an isolated GUID LocalDB fixture is
// served locally; Edge runs offline with no production access.
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import http from 'node:http';
import {setTimeout as sleep} from 'node:timers/promises';

const fixtureRoot=process.env.ARIES_STAFF_RESCHEDULE_FIXTURE_ROOT;
assert(fixtureRoot && fs.existsSync(path.join(fixtureRoot,'staff-reschedules-5.html')),
  'Generate isolated fixtures with --staff-reschedules-table-only');
const assetRoot=path.resolve('wwwroot');
const browser=process.env.EDGE_PATH||
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
assert(fs.existsSync(browser),'Microsoft Edge unavailable');
const tempProfile=fs.mkdtempSync(path.join(os.tmpdir(),'aries-reschedules-edge-'));
const screenRoot=path.join(fixtureRoot,'screenshots');
fs.mkdirSync(screenRoot,{recursive:true});
const mimetypes={'.css':'text/css','.js':'application/javascript','.svg':'image/svg+xml',
  '.png':'image/png','.jpg':'image/jpeg','.jpeg':'image/jpeg',
  '.woff2':'font/woff2','.webp':'image/webp','.ico':'image/x-icon'};
const requests=[];
const server=http.createServer((req,res)=>{
  try{
    const u=new URL(req.url,'http://127.0.0.1');
    const pathname=decodeURIComponent(u.pathname);
    let bytes,type;
    if(/^\/fixtures\/staff-reschedules-(?:0|1|2|3|4|5|6|10)\.html$/.test(pathname)){
      bytes=fs.readFileSync(path.join(fixtureRoot,path.basename(pathname)));
      type='text/html; charset=utf-8';
    }else if(/^\/RescheduleRequests(?:\/Index)?$/i.test(pathname)){
      requests.push(pathname+'?'+u.searchParams.toString());
      const fixture=u.searchParams.get('search')==='never-exists-synthetic'
        ? 'staff-reschedules-no-match.html'
        : u.searchParams.get('sortBy')==='oldest'
          ? 'staff-reschedules-10-oldest.html'
          : 'staff-reschedules-10.html';
      bytes=fs.readFileSync(path.join(fixtureRoot,fixture));
      type='text/html; charset=utf-8';
    }else{
      const filename=path.resolve(assetRoot,'.'+pathname);
      if(!filename.startsWith(assetRoot+path.sep))throw new Error('outside asset root');
      bytes=fs.readFileSync(filename);
      type=mimetypes[path.extname(filename)]||'application/octet-stream';
    }
    res.writeHead(200,{'Content-Type':type,'Cache-Control':'no-store',
      'Content-Security-Policy':"default-src 'self' data: blob: 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self';"});
    res.end(bytes);
  }catch{
    res.writeHead(404);
    res.end('Not Found');
  }
});
const port=await new Promise((resolve,reject)=>{
  server.once('error',reject);
  server.listen(0,'127.0.0.1',()=>resolve(server.address().port));
});
const edge=spawn(browser,['--headless=new','--disable-gpu','--no-first-run',
  '--no-default-browser-check','--no-sandbox','--remote-debugging-port=0',
  '--user-data-dir='+tempProfile,'about:blank'],{stdio:'ignore',windowsHide:true});
let ws,requestSeq=0,passed=0;
const waiting=new Map();
const command=(method,params={})=>new Promise((resolve,reject)=>{
  const id=++requestSeq;
  waiting.set(id,{resolve,reject});
  ws.send(JSON.stringify({id,method,params}));
});
async function evaluate(expr){
  const result=await command('Runtime.evaluate',{expression:expr,returnByValue:true,awaitPromise:true});
  if(result.exceptionDetails)throw new Error(result.exceptionDetails.text);
  return result.result?.value;
}
function inspect(){
  const frame=document.querySelector('.staff-reschedule-table-frame');
  const scroll=frame?.querySelector('.staff-data-table-scroll');
  const table=frame?.querySelector('.staff-reschedule-table.staff-data-table');
  const pager=frame?.querySelector('.aries-five-pager');
  const toolbar=document.querySelector('.staff-final-filter-toolbar');
  const sort=document.querySelector('#rescheduleSort');
  if(!frame||!scroll||!table||!pager||!toolbar||!sort)
    return {issues:['missing Staff table/frame/pager/toolbar/sort']};
  const rect=x=>x.getBoundingClientRect();
  const fr=rect(frame),sr=rect(scroll),pr=rect(pager);
  const rows=[...table.querySelectorAll('tbody tr:not(.staff-data-table-empty-row)')];
  const headers=[...table.querySelectorAll('thead th')];
  const cells=[...rows[0]?.querySelectorAll('td')||[]];
  const count=Number((location.pathname.match(/staff-reschedules-(\d+)\.html/)||[])[1]||0);
  const issues=[];
  if(document.documentElement.scrollWidth>innerWidth+3)
    issues.push('page horizontal overflow: '+document.documentElement.scrollWidth);
  if(fr.right>innerWidth+3||fr.left < -2||sr.width>innerWidth+3)
    issues.push('table frame exceeds viewport');
  if(getComputedStyle(frame).borderTopLeftRadius!=='0px'||
    getComputedStyle(pager).borderBottomRightRadius!=='0px')
    issues.push('table/frame/footer rounded');
  if(Math.abs(pr.top-sr.bottom)>3)
    issues.push('footer separated from table '+(pr.top-sr.bottom));
  if(headers.length!==7||table.querySelectorAll('colgroup col').length!==7)
    issues.push('not seven aligned columns');
  if(rows.length!==Math.min(count,5))
    issues.push('wrong visible request count '+rows.length);
  if((pager.querySelector('.aries-five-total')?.textContent||'').trim().startsWith(
    count+' matching record')===false)
    issues.push('incorrect total count');
  if((pager.querySelector('.aries-five-page')?.textContent||'').trim()!==
    'Page 1 of '+Math.max(1,Math.ceil(count/5)))
    issues.push('incorrect page indicator');
  const nav=pager.querySelectorAll('[aria-label="Previous page"],[aria-label="Next page"]');
  if(count<=5&&nav.length)issues.push('unneeded single-page navigation');
  if(count>5 && nav.length!==2)issues.push('missing multi-page actions');
  if(count===0&&!table.querySelector('.staff-data-table-empty'))
    issues.push('no in-table empty state');
  if(count>0 && table.querySelector('.staff-data-table-empty'))
    issues.push('fake empty row');
  if(table.querySelector('.reschedule-schedule,.reschedule-availability'))
    issues.push('date highlight/availability marker still present');
  if(document.querySelector('.staff-reschedule-cards'))
    issues.push('duplicate Staff mobile cards still rendered');
  if(Math.abs(rect(sort).height-40)>1)
    issues.push('sort control not compact');
  if(getComputedStyle(scroll).overflowX!=='auto')
    issues.push('internal table horizontal scrolling disabled');
  if(innerWidth<=1024&&scroll.scrollWidth<=scroll.clientWidth+30)
    issues.push('no inner horizontal scroll on narrow viewport');
  if(cells.length===7){
    for(let i=0;i<7;i++){
      if(Math.abs(rect(headers[i]).left-rect(cells[i]).left)>2||
         Math.abs(rect(headers[i]).right-rect(cells[i]).right)>2)
        issues.push('heading/data misalignment '+i+':'+Math.round(rect(headers[i]).left)+'/'+Math.round(rect(cells[i]).left));
    }
    for(const i of [2,3]){
      const plain=cells[i].querySelector('.staff-reschedule-date-plain');
      if(!plain)issues.push('date is not ordinary text '+i);
      else if(getComputedStyle(plain).backgroundColor!=='rgba(0, 0, 0, 0)'||
        getComputedStyle(plain).borderTopWidth!=='0px')
        issues.push('decorative date block persists '+i);
    }
    if(!cells[5].querySelector('.status-badge'))
      issues.push('legitimate status missing');
    const details=cells[6].querySelector('a');
    if(!details||rect(details).height<39)
      issues.push('Details action too small');
    else if(rect(details).right>rect(cells[6]).right+2)
      issues.push('Details action clipping');
  }
  return{issues,count,visible:rows.length,
    frameH:Math.round(fr.height),tableH:Math.round(sr.height),
    footerH:Math.round(pr.height),docW:document.documentElement.scrollWidth,
    windowW:innerWidth,tableBodyH:Math.round(rect(table.tBodies[0]).height),
    headDisplay:getComputedStyle(table.tHead).display,bodyDisplay:getComputedStyle(table.tBodies[0]).display,
    headerX:headers.map(h=>Math.round(rect(h).left)),cellX:cells.map(c=>Math.round(rect(c).left)),
    headerW:headers.map(h=>Math.round(rect(h).width)),cellW:cells.map(c=>Math.round(rect(c).width))};
}
async function load(name){
  await command('Page.navigate',{url:'http://127.0.0.1:'+port+'/fixtures/'+name});
  let loaded=false;
  for(let attempt=0;attempt<90;attempt++){
    loaded=Boolean(await evaluate("document.readyState==='complete' && !!document.querySelector('.staff-reschedule-table-frame')"));
    if(loaded)break;
    await sleep(90);
  }
  assert(loaded,'Razor fixture unavailable '+name);
}
try{
  let debugPort;
  for(let i=0;i<100;i++){
    const p=path.join(tempProfile,'DevToolsActivePort');
    if(fs.existsSync(p)){
      debugPort=Number(fs.readFileSync(p,'utf8').split(/\r?\n/)[0]);break;
    }
    if(edge.exitCode!==null)throw new Error('Edge quit before debugger');
    await sleep(100);
  }
  assert(debugPort>0,'Edge debugging port missing');
  let targets;
  for(let i=0;i<50;i++){
    try{
      targets=await(await fetch('http://127.0.0.1:'+debugPort+'/json/list')).json();
      if(targets.find(x=>x.type==='page'))break;
    }catch{}
    await sleep(100);
  }
  const target=targets.find(x=>x.type==='page');
  assert(target?.webSocketDebuggerUrl,'No Edge page target');
  ws=new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve,reject)=>{
    ws.addEventListener('open',resolve,{once:true});
    ws.addEventListener('error',reject,{once:true});
  });
  ws.addEventListener('message',event=>{
    const data=JSON.parse(event.data);
    if(!data.id)return;
    const pending=waiting.get(data.id);
    if(!pending)return;
    waiting.delete(data.id);
    data.error?pending.reject(new Error(JSON.stringify(data.error))):pending.resolve(data.result);
  });
  await command('Page.enable');
  await command('Runtime.enable');
  await command('Network.enable');
  await command('Network.setBlockedURLs',{urls:['https://*']});
  for(const width of [1440,1024,768,390,320]){
    await command('Emulation.setDeviceMetricsOverride',{
      width,height:900,deviceScaleFactor:1,mobile:false
    });
    let baseline=null;
    for(const n of [0,1,2,3,4,5,6,10]){
      await load('staff-reschedules-'+n+'.html');
      const state=await evaluate('('+inspect.toString()+')()');
      assert.deepEqual(state.issues,[],'width='+width+' count='+n+' '+JSON.stringify(state));
      if(width>=768&&n<=5){
        if(baseline===null)baseline=state.tableH;
        assert(Math.abs(state.tableH-baseline)<=1,
          'five-row frame shifts width='+width+' count='+n+': '+JSON.stringify(state));
      }
      passed++;
      console.log('PASS Reschedule Edge width='+width+' count='+n+
        ' visible='+state.visible+' tableH='+state.tableH+' footerH='+state.footerH);
      if(n===0||n===5){
        const shot=await command('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});
        fs.writeFileSync(path.join(screenRoot,'reschedules-'+width+'-'+n+'.png'),
          Buffer.from(shot.data,'base64'));
      }
    }
  }
  // Both sorting and debounced search share the AJAX pipeline.
  await command('Emulation.setDeviceMetricsOverride',
    {width:1440,height:900,deviceScaleFactor:1,mobile:false});
  await load('staff-reschedules-10.html');
  const before=requests.length;
  await evaluate("document.querySelector('#rescheduleSort').value='oldest';"+
    "document.querySelector('#rescheduleSort').dispatchEvent(new Event('change',{bubbles:true}));true");
  for(let i=0;i<50&&requests.length===before;i++)await sleep(100);
  await sleep(500);
  assert.equal(requests.length-before,1,
    'Sort change must generate one AJAX update (not duplicate form submit)');
  assert(requests.at(-1).includes('sortBy=oldest'),'Oldest sort parameter missing');
  const oldestFirst=await evaluate("document.querySelector('.staff-reschedule-table tbody tr:not(.staff-data-table-empty-row) .staff-booking-code')?.textContent.trim()");
  const firstExpected=await fs.promises.readFile(path.join(fixtureRoot,'staff-reschedules-10-oldest.html'),'utf8');
  assert.equal(oldestFirst,firstExpected.match(/class=\"staff-booking-code\">([^<]+)/)?.[1],
    'AJAX sorting did not replace first visible booking');
  passed++;
  console.log('PASS Staff Reschedules sorting makes one AJAX request and replaces rows');
  const searchBefore=requests.length;
  await evaluate("document.querySelector('#rescheduleSearch').value='never-exists-synthetic';"+
    "document.querySelector('#rescheduleSearch').dispatchEvent(new Event('input',{bubbles:true}));true");
  for(let i=0;i<50&&requests.length===searchBefore;i++)await sleep(100);
  await sleep(500);
  assert.equal(requests.length-searchBefore,1,'Search should trigger one debounced fetch');
  assert.equal(await evaluate("document.querySelector('.aries-five-total')?.textContent.trim()"),
    '0 matching records','AJAX search did not replace footer count');
  assert.equal(await evaluate("document.querySelector('.staff-data-table-empty strong')?.textContent.trim()"),
    'No matching requests','AJAX search did not replace table empty state');
  passed++;
  console.log('PASS Staff Reschedules debounced search replaces table and pager consistently');
  console.log('STAFF_RESCHEDULE_EDGE_PASS: '+passed+' checks');
}finally{
  for(const {reject} of waiting.values())reject(new Error('Browser closing'));
  waiting.clear();
  if(ws&&ws.readyState===WebSocket.OPEN)ws.close();
  edge.kill();
  await new Promise(resolve=>server.close(resolve));
}
