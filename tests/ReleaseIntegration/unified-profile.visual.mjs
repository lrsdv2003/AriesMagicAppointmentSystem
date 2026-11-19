// Offline Edge verification on isolated authenticated Client/Staff/Owner Profile markup.
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import http from 'node:http';
import {setTimeout as sleep} from 'node:timers/promises';

const fixtures=process.env.ARIES_UNIFIED_PROFILE_FIXTURE_ROOT;
assert(fixtures&&['client','staff','owner'].every(r=>fs.existsSync(path.join(fixtures,r+'-profile.html'))),'Generate Profile fixtures first');
const root=path.resolve('wwwroot');
const executable=process.env.EDGE_PATH||'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
assert(fs.existsSync(executable),'Edge is unavailable');
const profile=fs.mkdtempSync(path.join(os.tmpdir(),'aries-profile-edge-'));
const shots=path.join(fixtures,'screenshots');fs.mkdirSync(shots,{recursive:true});
const mime={'.css':'text/css','.js':'application/javascript','.svg':'image/svg+xml','.png':'image/png','.webp':'image/webp','.woff2':'font/woff2','.ico':'image/x-icon'};
const server=http.createServer((req,res)=>{
  try{
    const pathname=decodeURIComponent(new URL(req.url,'http://127.0.0.1').pathname);
    let file,type;
    if(/^\/fixtures\/(client|staff|owner)-profile\.html$/.test(pathname)){
      file=path.join(fixtures,path.basename(pathname));type='text/html; charset=utf-8';
    }else{
      file=path.resolve(root,'.'+pathname);
      if(!file.startsWith(root+path.sep))throw Error('Invalid path');
      type=mime[path.extname(file)]||'application/octet-stream';
    }
    const bytes=fs.readFileSync(file);
    res.writeHead(200,{'Content-Type':type,'Cache-Control':'no-store',
      'Content-Security-Policy':"default-src 'self' data: blob: 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self';"});
    res.end(bytes);
  }catch{if(!res.headersSent){res.writeHead(404);res.end('Not Found');}else res.destroy();}
});
const port=await new Promise((resolve,reject)=>{
  server.once('error',reject);
  server.listen(0,'127.0.0.1',()=>resolve(server.address().port));
});
const edge=spawn(executable,['--headless=new','--disable-gpu','--no-first-run',
  '--no-default-browser-check','--no-sandbox','--remote-debugging-port=0',
  '--user-data-dir='+profile,'about:blank'],{stdio:'ignore',windowsHide:true});
let ws,seq=0,checks=0;
const pending=new Map();
function cmd(method,params={}){
  return new Promise((resolve,reject)=>{
    const id=++seq;pending.set(id,{resolve,reject});
    ws.send(JSON.stringify({id,method,params}));
  });
}
async function evaluate(expr){
  const response=await cmd('Runtime.evaluate',{expression:expr,returnByValue:true,awaitPromise:true});
  if(response.exceptionDetails)throw Error(response.exceptionDetails.text);
  return response.result?.value;
}
function inspect(){
  const root=document.querySelector('.unified-profile');
  const grid=root?.querySelector('.up-grid');
  const identity=grid?.querySelector('.up-identity');
  const management=grid?.querySelector('.up-management');
  const lower=management?.querySelector('.up-lower');
  if(!root||!grid||!identity||!management||!lower)return{issues:['missing profile grid']};
  const rect=e=>e.getBoundingClientRect();
  const a=rect(identity),b=rect(management),g=rect(grid);
  const fields=[...root.querySelectorAll('.up-field .up-input')];
  const cards=[...root.querySelectorAll('.up-card')];
  const logout=root.querySelector('form[action*="Account/Logout"]');
  const issues=[];
  if(document.documentElement.scrollWidth>innerWidth+3)issues.push('outer horizontal overflow');
  if(g.left<-2||g.right>innerWidth+3)issues.push('profile outside viewport');
  if(cards.length!==4)issues.push('missing cards: '+cards.length);
  if(innerWidth>800) {
    const ratio=a.width/g.width;
    if(ratio<.245||ratio>.34)issues.push('left width ratio: '+ratio);
    if(Math.abs(a.top-b.top)>2)issues.push('desktop vertical alignment');
    if(b.left<a.right+4)issues.push('overlapping two columns');
  }else {
    if(b.top<a.bottom-2)issues.push('mobile card stacking');
  }
  const l=[...lower.children].map(rect);
  if(innerWidth>1200&&Math.abs(l[0].top-l[1].top)>2)issues.push('desktop lower cards not in columns');
  if(innerWidth<=1200&&l[1].top<l[0].bottom-2)issues.push('responsive lower cards overlapping');
  if(fields.length!==5)issues.push('wrong editable field count '+fields.length);
  if(fields.some(f=>rect(f).right>innerWidth+3))issues.push('input overflows viewport');
  if(root.querySelector('#Password_NewPassword')?.minLength!==12)issues.push('password minimum not 12');
  if(root.querySelector('#choosePhoto')?.type!=='button')issues.push('Change Photo must open file picker');
  if(!logout?.querySelector('input[name="__RequestVerificationToken"]'))issues.push('sign-out antiforgery missing');
  if(!root.querySelector('#profilePhoto')?.accept.includes('image/webp'))issues.push('WEBP input not supported');
  if(!root.querySelector('#profilePhoto')?.classList.contains('visually-hidden'))issues.push('duplicate native photo chooser displayed');
  if(getComputedStyle(cards[0]).backgroundColor==='rgb(255, 255, 255)')issues.push('dark theme missing');
  return {issues,width:innerWidth,role:root.dataset.role,left:Math.round(a.width),right:Math.round(b.width)};
}
function controls(){
  const form=document.querySelector('.up-personal form');
  const name=form.querySelector('#Personal_FullName');
  const phone=form.querySelector('#Personal_PhoneNumber');
  name.value='Unsaved Name';phone.value='09999999999';
  form.querySelector('[type=reset]').click();
  const restored=name.value===name.dataset.savedValue&&phone.value===phone.dataset.savedValue;
  const password=document.querySelector('#Password_CurrentPassword');
  const toggle=document.querySelector('[data-toggle-password="Password_CurrentPassword"]');
  toggle.click();const shown=password.type==='text'&&toggle.getAttribute('aria-pressed')==='true';
  toggle.click();const hidden=password.type==='password'&&toggle.getAttribute('aria-pressed')==='false';
  const photo=document.querySelector('#profilePhoto');
  const old=photo.click.bind(photo);let opened=0;
  photo.click=()=>{opened++};document.querySelector('#choosePhoto').click();photo.click=old;
  const remove=document.querySelector('[data-confirm-remove-photo]');
  const originalConfirm=window.confirm;
  let confirms=0;
  window.confirm=()=>{confirms++;return false;};
  const cancelled=!remove.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));
  window.confirm=originalConfirm;
  return{restored,shown,hidden,opened,removalCancelled:cancelled&&confirms===1&&!remove.dataset.submitting};
}
async function load(role){
  await cmd('Page.navigate',{url:'http://127.0.0.1:'+port+'/fixtures/'+role+'-profile.html'});
  let ok=false;
  for(let i=0;i<90;i++){
    ok=Boolean(await evaluate("document.readyState==='complete' && !!document.querySelector('[data-unified-profile]')"));
    if(ok)break;
    await sleep(90);
  }
  assert(ok,'Profile fixture failed '+role);
}
try{
  let debugPort;
  for(let i=0;i<100;i++){
    const file=path.join(profile,'DevToolsActivePort');
    if(fs.existsSync(file)){debugPort=Number(fs.readFileSync(file,'utf8').split(/\r?\n/)[0]);break;}
    if(edge.exitCode!==null)throw Error('Edge exited');
    await sleep(100);
  }
  assert(debugPort,'No browser debug port');
  let targets;
  for(let i=0;i<50;i++){
    try{
      targets=await(await fetch('http://127.0.0.1:'+debugPort+'/json/list')).json();
      if(targets.find(x=>x.type==='page'))break;
    }catch{}
    await sleep(100);
  }
  const page=targets.find(x=>x.type==='page');
  assert(page?.webSocketDebuggerUrl,'No browser page');
  ws=new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((resolve,reject)=>{
    ws.addEventListener('open',resolve,{once:true});
    ws.addEventListener('error',reject,{once:true});
  });
  ws.addEventListener('message',e=>{
    const packet=JSON.parse(e.data);if(!packet.id)return;
    const request=pending.get(packet.id);if(!request)return;
    pending.delete(packet.id);
    packet.error?request.reject(Error(JSON.stringify(packet.error))):request.resolve(packet.result);
  });
  await cmd('Page.enable');await cmd('Runtime.enable');await cmd('Network.enable');
  await cmd('Network.setBlockedURLs',{urls:['https://*']});
  for(const width of [1440,1280,1024,768,390,320]){
    await cmd('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:false});
    for(const role of ['client','staff','owner']){
      await load(role);
      const state=await evaluate('('+inspect.toString()+')()');
      assert.deepEqual(state.issues,[],'Profile '+JSON.stringify(state));
      assert.equal(state.role.toLowerCase(),role);
      checks++;console.log('PASS Profile '+role+' width='+width+' left='+state.left+' right='+state.right);
      if(width===1440||width===390){
        const png=await cmd('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});
        fs.writeFileSync(path.join(shots,role+'-'+width+'.png'),Buffer.from(png.data,'base64'));
      }
    }
  }
  await cmd('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false});
  for(const role of ['client','staff','owner']){
    await load(role);
    const result=await evaluate('('+controls.toString()+')()');
    assert.deepEqual(result,{restored:true,shown:true,hidden:true,opened:1,removalCancelled:true},
      role+' controls '+JSON.stringify(result));
    checks++;console.log('PASS Profile '+role+' reset saved values, password toggle, photo picker');
  }
  console.log('UNIFIED_PROFILE_EDGE_PASS: '+checks+' checks');
}finally{
  for(const x of pending.values())x.reject(Error('Browser closed'));
  pending.clear();
  if(ws&&ws.readyState===WebSocket.OPEN)ws.close();
  edge.kill();
  await new Promise(resolve=>server.close(resolve));
}
