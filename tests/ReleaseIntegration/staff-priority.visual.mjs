// Real Edge viewport checks of authenticated, isolated Razor HTML fixture.
// Generate fixture via --staff-priority-only with ARIES_STAFF_PRIORITY_FIXTURE_HTML.
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import os from 'node:os';
import {setTimeout as sleep} from 'node:timers/promises';

const fixturePath=process.env.ARIES_STAFF_PRIORITY_FIXTURE_HTML;
assert(fixturePath && fs.existsSync(fixturePath),
  'Set ARIES_STAFF_PRIORITY_FIXTURE_HTML to real synthetic Staff Razor output');
const root=path.resolve('wwwroot');
const edgeExe=process.env.EDGE_PATH ||
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
assert(fs.existsSync(edgeExe),'Microsoft Edge not installed');
const profile=fs.mkdtempSync(path.join(os.tmpdir(),'aries-priority-edge-'));
const shots=path.join(os.tmpdir(),'AriesStaffPriorityUi','screenshots');
fs.mkdirSync(shots,{recursive:true});
const types={
  '.css':'text/css','.js':'application/javascript','.svg':'image/svg+xml',
  '.png':'image/png','.jpg':'image/jpeg','.jpeg':'image/jpeg',
  '.woff2':'font/woff2','.webp':'image/webp','.ico':'image/x-icon'
};
const server=http.createServer((req,res)=>{
  try{
    const pathname=decodeURIComponent(new URL(req.url,'http://127.0.0.1').pathname);
    let data,type;
    if(pathname==='/'){
      data=fs.readFileSync(fixturePath);
      type='text/html; charset=utf-8';
    }else{
      const candidate=path.resolve(root,'.'+pathname);
      if(!candidate.startsWith(root+path.sep))throw new Error('outside static root');
      data=fs.readFileSync(candidate);
      type=types[path.extname(candidate)]||'application/octet-stream';
    }
    res.writeHead(200,{'Content-Type':type,'Cache-Control':'no-store',
      'Content-Security-Policy':"default-src 'self' data: blob: 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self';"});
    res.end(data);
  }catch{
    res.writeHead(404);res.end('Not Found');
  }
});
const port=await new Promise((resolve,reject)=>{
  server.once('error',reject);
  server.listen(0,'127.0.0.1',()=>resolve(server.address().port));
});
const edge=spawn(edgeExe,['--headless=new','--disable-gpu','--no-first-run',
  '--no-default-browser-check','--no-sandbox','--remote-debugging-port=0',
  '--user-data-dir='+profile,'about:blank'],{stdio:'ignore',windowsHide:true});
let socket,counter=0;
const pending=new Map();
function send(method,params={}){
  const id=++counter;
  return new Promise((resolve,reject)=>{
    pending.set(id,{resolve,reject});
    socket.send(JSON.stringify({id,method,params}));
  });
}
function inspectEval(){
  const priority=document.querySelector('.staff-round1-priority');
  const head=document.querySelector('#staffPriorityTitle');
  const tabs=document.querySelector('.staff-round1-tabs');
  const active=document.querySelector('.staff-round1-tab-panel:not([hidden])');
  if(!priority||!head||!tabs||!active)return {issues:['missing Priority UI']};
  const rows=[...active.querySelectorAll('.staff-round1-task')];
  const rect=e=>e.getBoundingClientRect();
  const pr=rect(priority),hr=rect(head),tr=rect(tabs);
  const actions=rows.map(row=>row.querySelector('.staff-round1-action')).filter(Boolean);
  const issues=[];
  if(document.documentElement.scrollWidth>innerWidth+2)
    issues.push('document horizontal overflow '+document.documentElement.scrollWidth);
  if(getComputedStyle(priority).maxHeight!=='none')
    issues.push('priority max-height not removed');
  if(priority.scrollHeight>priority.clientHeight+2)
    issues.push('priority internal vertical clipping '+priority.scrollHeight+'/'+priority.clientHeight);
  if(tr.top<hr.bottom-1||rect(active).top<tr.bottom-1)
    issues.push('header/tabs overlap');
  if(rows.length!==5)issues.push('expected 5 cards, got '+rows.length);
  for(const [i,row] of rows.entries()){
    const rr=rect(row),ar=actions[i]?rect(actions[i]):null;
    if(rr.width<80||rr.height<40)issues.push('collapsed row '+i);
    if(rr.left < -1||rr.right>innerWidth+2)issues.push('row outside viewport '+i);
    if(rr.right>pr.right+2||rr.left<pr.left-2)
      issues.push('row exceeds Priority container '+i);
    if(!ar)issues.push('missing action '+i);
    else{
      if(ar.left<rr.left-2||ar.right>rr.right+2||ar.top<rr.top-2||ar.bottom>rr.bottom+2)
        issues.push('action outside record '+i);
      if(ar.height<39)issues.push('small action target '+i+':'+ar.height);
    }
    if(getComputedStyle(row.querySelector('.staff-round1-task-copy p')).whiteSpace==='nowrap')
      issues.push('truncated text '+i);
  }
  if(rows.length&&rect(rows.at(-1)).bottom>pr.bottom+2)
    issues.push('fifth record outside Priority container');
  return {
    issues,rows:rows.length,active:active.dataset.staffPanel,
    counts:[...document.querySelectorAll('[data-staff-tab]')]
      .map(t=>t.querySelector('span')?.textContent.trim()),
    viewport:innerWidth,documentWidth:document.documentElement.scrollWidth,
    priorityHeight:Math.round(pr.height),priorityScrollHeight:priority.scrollHeight
  };
}
try{
  let debugPort;
  for(let i=0;i<100;i++){
    const active=path.join(profile,'DevToolsActivePort');
    if(fs.existsSync(active)){debugPort=Number(fs.readFileSync(active,'utf8').split(/\r?\n/)[0]);break;}
    if(edge.exitCode!==null)throw new Error('Edge exited before opening debugger');
    await sleep(100);
  }
  assert(debugPort>0,'Edge remote debugging unavailable');
  let pages;
  for(let i=0;i<50;i++){
    try{
      pages=await(await fetch('http://127.0.0.1:'+debugPort+'/json/list')).json();
      if(pages.find(p=>p.type==='page'))break;
    }catch{}
    await sleep(100);
  }
  const target=pages.find(p=>p.type==='page');
  assert(target?.webSocketDebuggerUrl,'Edge did not provide a page target');
  socket=new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve,reject)=>{
    socket.addEventListener('open',resolve,{once:true});
    socket.addEventListener('error',reject,{once:true});
  });
  socket.addEventListener('message',event=>{
    const msg=JSON.parse(event.data);
    if(!msg.id)return;
    const entry=pending.get(msg.id);if(!entry)return;
    pending.delete(msg.id);
    msg.error?entry.reject(new Error(JSON.stringify(msg.error))):entry.resolve(msg.result);
  });
  await send('Page.enable');
  await send('Runtime.enable');
  await send('Network.enable');
  await send('Network.setBlockedURLs',{urls:['https://*']});
  let checks=0;
  for(const width of [1440,1024,768,390,320]){
    await send('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:false});
    await send('Page.navigate',{url:'http://127.0.0.1:'+port+'/'});
    let ready=false;
    for(let attempt=0;attempt<75;attempt++){
      const result=await send('Runtime.evaluate',{
        expression:"document.readyState==='complete' && !!document.querySelector('[data-staff-tab]')",
        returnByValue:true
      });
      if(result.result?.value){ready=true;break;}
      await sleep(100);
    }
    assert(ready,'fixture did not load at '+width);
    for(const category of ['messages','bookings','reschedules']){
      await send('Runtime.evaluate',{expression:
        'document.querySelector(\'[data-staff-tab="'+category+'"]\').click();true',
        returnByValue:true});
      const output=await send('Runtime.evaluate',{
        expression:'('+inspectEval.toString()+')()',returnByValue:true,awaitPromise:true
      });
      const state=output.result?.value;
      assert(state,'no layout result');
      assert.deepEqual(state.issues,[],
        'viewport '+width+' category '+category+': '+JSON.stringify(state));
      assert.equal(state.active,category);
      assert.deepEqual(state.counts,['10','10','10']);
      checks++;
      console.log('PASS Staff Priority Edge viewport='+width+' category='+category+
        ' items='+state.rows+' total=10 height='+state.priorityHeight);
    }
    const keyboard=await send('Runtime.evaluate',{
      expression:"(() => {const first=document.querySelector('[data-staff-tab=messages]');first.click();first.focus();first.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowRight',bubbles:true}));const next=document.querySelector('[data-staff-tab=bookings]');return next.getAttribute('aria-selected')==='true' && document.activeElement===next && !document.querySelector('[data-staff-panel=bookings]').hidden;})()",
      returnByValue:true
    });
    assert.equal(keyboard.result?.value,true,'keyboard navigation failed at '+width);
    checks++;
    const screenshot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});
    fs.writeFileSync(path.join(shots,'staff-priority-'+width+'.png'),
      Buffer.from(screenshot.data,'base64'));
  }
  console.log('STAFF_PRIORITY_EDGE_VISUAL_PASS: '+checks+' viewport/category checks');
}finally{
  for(const {reject} of pending.values())reject(new Error('Browser closing'));
  pending.clear();
  if(socket && socket.readyState===WebSocket.OPEN)socket.close();
  edge.kill();
  await new Promise(resolve=>server.close(resolve));
}
