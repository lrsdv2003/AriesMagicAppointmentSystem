// Offline Chromium visual checks for the two Staff Booking Details workflows.
// Input is actual Razor HTML from isolated authenticated SQL/MVC fixtures.
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import {setTimeout as sleep} from 'node:timers/promises';

const fixtureRoot=process.env.ARIES_STAFF_DETAILS_FIXTURE_ROOT;
const types=['pending','location-review','confirmed'];
assert(fixtureRoot && types.every(x=>fs.existsSync(path.join(fixtureRoot,x+'.html'))),
  'Run --staff-unified-booking-details-only with ARIES_STAFF_DETAILS_FIXTURE_ROOT first');
const root=path.resolve('wwwroot');
const browser=process.env.EDGE_PATH||'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
assert(fs.existsSync(browser),'Edge browser unavailable');
const shots=path.join(fixtureRoot,'screenshots');fs.mkdirSync(shots,{recursive:true});
const profile=fs.mkdtempSync(path.join(os.tmpdir(),'aries-staff-detail-edge-'));
const mime={'.css':'text/css','.js':'application/javascript','.svg':'image/svg+xml',
 '.png':'image/png','.jpeg':'image/jpeg','.jpg':'image/jpeg','.webp':'image/webp',
 '.woff':'font/woff','.woff2':'font/woff2','.ico':'image/x-icon'};
const server=http.createServer((req,res)=>{
  try{
    const url=decodeURIComponent(new URL(req.url,'http://127.0.0.1').pathname);
    let file,type;
    if(/^\/fixtures\/(pending|confirmed|location-review)\.html$/.test(url)){
      file=path.join(fixtureRoot,path.basename(url));type='text/html; charset=utf-8';
    }else{
      file=path.resolve(root,'.'+url);
      if(!file.startsWith(root+path.sep))throw Error('Outside static test root');
      type=mime[path.extname(file)]||'application/octet-stream';
    }
    const body=fs.readFileSync(file);
    res.writeHead(200,{'Content-Type':type,'Cache-Control':'no-store',
      'Content-Security-Policy':"default-src 'self' blob: data: 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self';"});
    res.end(body);
  }catch{if(!res.headersSent){res.writeHead(404);res.end('Not Found');}else res.destroy();}
});
const port=await new Promise((resolve,reject)=>{
  server.once('error',reject);
  server.listen(0,'127.0.0.1',()=>resolve(server.address().port));
});
const edge=spawn(browser,['--headless=new','--disable-gpu','--no-first-run',
  '--no-default-browser-check','--no-sandbox','--remote-debugging-port=0',
  '--user-data-dir='+profile,'about:blank'],{stdio:'ignore',windowsHide:true});
let ws,seq=0,checks=0;
const pending=new Map();
function command(method,params={}){
  return new Promise((resolve,reject)=>{
    const id=++seq;pending.set(id,{resolve,reject});
    ws.send(JSON.stringify({id,method,params}));
  });
}
async function evaluate(expr){
  const data=await command('Runtime.evaluate',{expression:expr,returnByValue:true,awaitPromise:true});
  if(data.exceptionDetails)throw Error(data.exceptionDetails.text);
  return data.result?.value;
}
function inspect(){
  const root=document.querySelector('.staff-unified-booking-details');
  const shell=root?.querySelector('.staff-final-booking-shell');
  const left=shell?.querySelector('.staff-final-booking-primary');
  const right=shell?.querySelector('.staff-final-booking-timeline');
  const sections=left?[...left.querySelectorAll('.staff-unified-section-stack > section')]:[];
  const action=left?.querySelector('.staff-unified-actions');
  const rect=x=>x.getBoundingClientRect();
  if(!root||!shell||!left||!right||!action)return {issues:['Missing unified shell or timeline']};
  const l=rect(left),r=rect(right),a=rect(action),f=rect(root),ss=sections.map(rect);
  const errors=[];
  if(document.documentElement.scrollWidth>innerWidth+3)
    errors.push('Document horizontal scroll '+document.documentElement.scrollWidth+' viewport '+innerWidth);
  if(f.left<-2||f.right>innerWidth+3)errors.push('Root outside viewport');
  if(sections.length!==5)errors.push('Wrong section count '+sections.length);
  const headings=sections.map(s=>s.querySelector('h2')?.textContent?.trim());
  const expected=['Client Information','Event Information','Packing Information','Venue Travel Overview','Client Notes'];
  if(headings.some((x,i)=>x!==expected[i]))errors.push('Section order '+JSON.stringify(headings));
  for(let i=1;i<ss.length;i++){
    if(ss[i].top<ss[i-1].bottom-2)errors.push('Overlapping stacked section '+i);
    if(Math.abs(ss[i].left-ss[0].left)>3)errors.push('Misaligned section '+i);
  }
  if(a.top<ss[4]?.bottom-2)errors.push('Actions overlap Client Notes');
  if(innerWidth>1180){
    if(l.right+5>r.left)errors.push('Timeline overlaps information');
    if(Math.abs(l.top-r.top)>3)errors.push('Timeline not aligned at top');
    if(l.width<=r.width)errors.push('Timeline is wider than information');
  }else{
    if(r.top<l.bottom-2)errors.push('Timeline overlaps below information');
    if(Math.abs(l.left-r.left)>3)errors.push('Timeline not aligned below');
  }
  const controls=[...root.querySelectorAll('button,a,input,textarea')];
  for(const control of controls){
    const cr=rect(control);
    if(cr.width>0&&cr.left<-3)errors.push('Control clipped on left');
    if(cr.width>0&&cr.right>innerWidth+4)errors.push('Control clipped on right');
  }
  const timeline=[...right.querySelectorAll('.aries-timeline-list li')];
  if(timeline.length!==2)errors.push('Timeline entries missing');
  for(const entry of timeline){
    const er=rect(entry),time=entry.querySelector('time'),stamp=time?rect(time):null;
    if(er.right>r.right+3 || (stamp && stamp.right>r.right+3))
      errors.push('Timeline entry text clipped');
  }
  const packageLabels=[...left.querySelectorAll('.staff-detail-list span')].map(e=>e.textContent.trim());
  if(packageLabels.filter(x=>x==='Selected Package').length!==1 ||
    packageLabels.includes('Selected Service'))errors.push('Package duplicate');
  if(!root.querySelector('.staff-unified-booking-header h1')?.textContent.trim().startsWith('BK-'))
    errors.push('Missing real booking identifier');
  const approve=root.querySelector('.staff-unified-approve');
  const isConfirmed=!approve;
  if(!isConfirmed){
    const bg=getComputedStyle(approve).backgroundColor;
    const m=bg.match(/(\d+)/g)?.map(Number)||[];
    if(!approve.disabled && (m.length<3 || !(m[1]>m[0] && m[1]>m[2])))
      errors.push('Enabled Approve button not green '+bg);
    if(approve.disabled && !approve.matches(':disabled'))
      errors.push('Disabled approval guard ineffective');
    if(innerWidth<650 && rect(approve).width<left.width*.75)errors.push('Mobile approve button too narrow');
  }
  if(innerWidth<=650){
    const metrics=root.querySelector('.booking-venue-metrics');
    if(metrics && getComputedStyle(metrics).gridTemplateColumns.split(' ').length>1)
      errors.push('Venue metrics not stacked on mobile');
  }
  if(!right.querySelector('.aries-timeline-list'))errors.push('No timeline');
  return {issues:errors,width:innerWidth,state:isConfirmed?'confirmed':'pending',
    main:Math.round(l.width),timeline:Math.round(r.width),
    height:Math.round(f.height)};
}
async function navigate(name){
  await command('Page.navigate',{url:'http://127.0.0.1:'+port+'/fixtures/'+name+'.html'});
  let done=false;
  for(let i=0;i<100;i++){
    done=!!(await evaluate("document.readyState==='complete' && !!document.querySelector('.staff-unified-booking-details')"));
    if(done)break;await sleep(95);
  }
  assert(done,'Fixture page could not load: '+name);
}
try{
  let debugPort;
  for(let i=0;i<110;i++){
    const file=path.join(profile,'DevToolsActivePort');
    if(fs.existsSync(file)){debugPort=Number(fs.readFileSync(file,'utf8').split(/\r?\n/)[0]);break;}
    if(edge.exitCode!==null)throw Error('Edge exited before ready');
    await sleep(100);
  }
  assert(debugPort>0,'Edge debug port unavailable');
  let pages;
  for(let i=0;i<50;i++){
    try{
      pages=await(await fetch('http://127.0.0.1:'+debugPort+'/json/list')).json();
      if(pages.find(p=>p.type==='page'))break;
    }catch{}
    await sleep(100);
  }
  const page=pages.find(p=>p.type==='page');
  assert(page?.webSocketDebuggerUrl,'No Edge page target');
  ws=new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((resolve,reject)=>{
    ws.addEventListener('open',resolve,{once:true});ws.addEventListener('error',reject,{once:true});
  });
  ws.addEventListener('message',event=>{
    const packet=JSON.parse(event.data);if(!packet.id)return;
    const request=pending.get(packet.id);if(!request)return;
    pending.delete(packet.id);
    packet.error?request.reject(Error(JSON.stringify(packet.error))):request.resolve(packet.result);
  });
  await command('Page.enable');await command('Runtime.enable');await command('Network.enable');
  await command('Network.setBlockedURLs',{urls:['https://*']});
  for(const width of [1440,1280,1180,1024,768,390,320]){
    await command('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:false});
    for(const name of types){
      await navigate(name);
      const state=await evaluate('('+inspect.toString()+')()');
      assert.deepEqual(state.issues,[],'Booking Details '+name+' '+JSON.stringify(state));
      checks++;console.log('PASS Booking Details '+name+' width='+width+
        ' left='+state.main+' timeline='+state.timeline);
      if(width===1440||width===390){
        const png=await command('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});
        fs.writeFileSync(path.join(shots,name+'-'+width+'.png'),Buffer.from(png.data,'base64'));
      }
    }
  }
  console.log('STAFF_UNIFIED_BOOKING_DETAILS_EDGE_PASS: '+checks+' responsive browser checks');
}finally{
  for(const p of pending.values())p.reject(Error('Browser closed'));
  pending.clear();
  if(ws?.readyState===WebSocket.OPEN)ws.close();
  edge.kill();
  await new Promise(resolve=>server.close(resolve));
}
