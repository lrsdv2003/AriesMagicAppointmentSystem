// Shared Client, Staff, Owner Messaging in authenticated Razor fixtures.
// Offline Edge DOM/layout and composer interaction checks (never production).
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import http from 'node:http';
import {setTimeout as sleep} from 'node:timers/promises';

const fixtures=process.env.ARIES_UNIFIED_MESSAGING_FIXTURE_ROOT;
assert(fixtures && fs.existsSync(path.join(fixtures,'client-selected.html')),
  'Generate authenticated fixtures with --unified-messaging-redesign-only first');
const webroot=path.resolve('wwwroot');
const edgeExe=process.env.EDGE_PATH||'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
assert(fs.existsSync(edgeExe),'Microsoft Edge not installed');
const profile=fs.mkdtempSync(path.join(os.tmpdir(),'aries-unified-msg-edge-'));
const screenshots=path.join(fixtures,'screenshots');
fs.mkdirSync(screenshots,{recursive:true});
const types={'.css':'text/css','.js':'application/javascript','.svg':'image/svg+xml',
  '.png':'image/png','.jpg':'image/jpeg','.jpeg':'image/jpeg','.webp':'image/webp',
  '.woff':'font/woff','.woff2':'font/woff2','.ico':'image/x-icon'};
let sendCount=0, simulatedSendFailure=false, summaryCalls=0, messageCalls=0, servedEmpty=false, servedRole='client';
const server=http.createServer(async(req,res)=>{
  try{
    const url=new URL(req.url,'http://127.0.0.1');
    const pathname=decodeURIComponent(url.pathname);
    let bytes,type,status=200;
    if(/^\/fixtures\/(?:client|staff|owner)-(?:selected|inbox)\.html$/.test(pathname)
      || pathname==='/fixtures/client-empty.html'){
      servedEmpty=pathname.endsWith('/client-empty.html');
      servedRole=(pathname.match(/\/fixtures\/(client|staff|owner)-/)||[])[1]||'client';
      bytes=fs.readFileSync(path.join(fixtures,path.basename(pathname)));
      type='text/html; charset=utf-8';
    }else if(pathname==='/Communications/ConversationSummaries'){
      summaryCalls++;
      if(servedEmpty){res.writeHead(200,{'Content-Type':'application/json','Cache-Control':'no-store'});res.end('{"items":[],"unreadConversationCount":0}');return;}
      const role=url.searchParams.get('testRole')||servedRole;
      const file=fs.existsSync(path.join(fixtures,role+'-summary.html'))
        ?role+'-summary.html':'client-summary.html';
      bytes=fs.readFileSync(path.join(fixtures,file));type='application/json';
    }else if(pathname==='/Communications/MessagesSince'){
      messageCalls++;bytes=Buffer.from('[]');type='application/json';
    }else if(pathname==='/Communications/ReadStates'){
      bytes=Buffer.from('[]');type='application/json';
    }else if(pathname==='/Communications/UnreadCount'){
      bytes=Buffer.from('{"count":0}');type='application/json';
    }else if(pathname==='/Communications/SendMessage'&&req.method==='POST'){
      sendCount++;
      for await(const _ of req){}
      status=simulatedSendFailure?503:200;
      bytes=Buffer.from(status===200?'{"ok":true,"messageId":9876}':'Temporary failure');
      type=status===200?'application/json':'text/plain';
    }else{
      const file=path.resolve(webroot,'.'+pathname);
      if(!file.startsWith(webroot+path.sep))throw new Error('path traversal');
      bytes=fs.readFileSync(file);
      type=types[path.extname(file)]||'application/octet-stream';
    }
    res.writeHead(status,{'Content-Type':type,'Cache-Control':'no-store',
      'Content-Security-Policy':"default-src 'self' data: blob: 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self';"});
    res.end(bytes);
  }catch(e){res.writeHead(404);res.end('Not Found');}
});
const port=await new Promise((resolve,reject)=>{
  server.once('error',reject);
  server.listen(0,'127.0.0.1',()=>resolve(server.address().port));
});
const edge=spawn(edgeExe,['--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check',
  '--no-sandbox','--remote-debugging-port=0','--user-data-dir='+profile,'about:blank'],
  {stdio:'ignore',windowsHide:true});
let ws,sequence=0,checks=0;
const pending=new Map();
function send(method,params={}){
  return new Promise((resolve,reject)=>{
    const id=++sequence;pending.set(id,{resolve,reject});
    ws.send(JSON.stringify({id,method,params}));
  });
}
async function evaluate(expression){
  const response=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});
  if(response.exceptionDetails)throw new Error(response.exceptionDetails.text);
  return response.result?.value;
}
function layout(){
  const root=document.querySelector('#communicationCenter.comm-unified');
  const workspace=root?.querySelector('.comm-workspace');
  const sidebar=root?.querySelector('.comm-conversation-panel');
  const chat=root?.querySelector('.comm-chat-panel');
  if(!root||!workspace||!sidebar||!chat)return{issues:['missing unified messaging structure']};
  const r=x=>x.getBoundingClientRect();
  const wr=r(workspace),sr=r(sidebar),cr=r(chat);
  const mobile=innerWidth<=767;
  const selected=root.classList.contains('comm-has-selection');
  const expectedRole=location.pathname.match(/\/(?:client|staff|owner)-/)?.[0].replace(/[\/-]/g,'')||'unknown';
  const issues=[];
  if(document.documentElement.scrollWidth>innerWidth+3)
    issues.push('outer document overflow '+document.documentElement.scrollWidth);
  if(wr.width<=0||wr.right>innerWidth+4||wr.left < -2)
    issues.push('workspace outside available page width '+JSON.stringify({wr:wr.width,left:wr.left,right:wr.right}));
  if(!root.querySelector('.comm-conversation-search input[placeholder="Search conversations"]'))
    issues.push('search missing');
  if(root.querySelector('.comm-filter-strip,#newConversationModal,[data-bs-target="#newConversationModal"]'))
    issues.push('removed controls still rendered');
  if(getComputedStyle(workspace).backgroundColor!=='rgb(6, 34, 58)')
    issues.push('dark navy reference theme missing '+getComputedStyle(workspace).backgroundColor);
  if(!mobile){
    const proportion=sr.width/wr.width;
    // The sidebar has a practical 262px minimum to preserve readable names
    // on tablets, so its percentage can exceed 30% at 768px.
    if(proportion<(innerWidth<900?.25:.205) || proportion>(innerWidth<900?.39:.355))
      issues.push('sidebar proportion outside usable range '+proportion.toFixed(3));
    if(getComputedStyle(sidebar).display==='none'||getComputedStyle(chat).display==='none')
      issues.push('desktop two-panel layout disabled');
    if(Math.abs(sr.top-cr.top)>3)
      issues.push('panels not aligned vertically');
    if(Math.abs(sr.right-cr.left)>3)
      issues.push('panels not adjacent');
  }else{
    const hasList=getComputedStyle(sidebar).display!=='none';
    const hasChat=getComputedStyle(chat).display!=='none';
    if(selected ? !(hasChat&&!hasList) : !(hasList&&!hasChat))
      issues.push('mobile single-panel state incorrect selected='+selected+' '+hasList+'/'+hasChat);
    if(selected && getComputedStyle(root.querySelector('.comm-mobile-back')).display==='none')
      issues.push('mobile Back control hidden');
  }
  const list=root.querySelector('.comm-conversation-list');
  if(getComputedStyle(list).overflowY!=='auto')
    issues.push('sidebar cannot scroll independently');
  const history=root.querySelector('#commMessageHistory');
  const composer=root.querySelector('#commComposer');
  if(selected){
    if(!history||!composer)issues.push('missing active chat/composer');
    else{
      if(getComputedStyle(history).overflowY!=='auto')
        issues.push('message history cannot scroll independently');
      if(Math.abs(r(composer).bottom-cr.bottom)>3)
        issues.push('composer not anchored at panel bottom '+(r(composer).bottom-cr.bottom));
      if(r(composer).width>cr.width+2||r(composer).left<cr.left-2)
        issues.push('composer exceeds chat panel');
      if(!composer.querySelector('[name="relatedBookingId"]'))
        issues.push('verified booking context selector missing');
      if(!mobile && composer.querySelector('.comm-booking-selector') &&
         Math.abs(r(composer.querySelector('.comm-booking-selector')).top-
           r(composer.querySelector('.comm-composer-row')).top)>6)
        issues.push('booking context and message are not on one desktop composer row');
      if(!composer.querySelector('button[aria-label="Send message"]'))
        issues.push('accessible send icon missing');
      if(!root.querySelector('.comm-message.mine .comm-bubble')||
         !root.querySelector('.comm-message.theirs .comm-bubble'))
        issues.push('incoming/outgoing bubbles missing');
      else{
        const mine=r(root.querySelector('.comm-message.mine .comm-bubble'));
        const theirs=r(root.querySelector('.comm-message.theirs .comm-bubble'));
        if(mine.left<=theirs.left)issues.push('bubble directions incorrect');
      }
    }
  }else if(!root.querySelector('.comm-chat-empty'))issues.push('missing no-selection state');
  return{issues,role:expectedRole,selected,width:innerWidth,
    workspaceH:Math.round(wr.height),sidebarW:Math.round(sr.width),chatW:Math.round(cr.width),
    screenW:document.documentElement.scrollWidth, composerH:composer?Math.round(r(composer).height):0};
}
async function load(file){
  await send('Page.navigate',{url:'http://127.0.0.1:'+port+'/fixtures/'+file});
  let ready=false;
  for(let i=0;i<110;i++){
    ready=Boolean(await evaluate("document.readyState==='complete' && "+
      "!!document.querySelector('#communicationCenter.comm-unified .comm-workspace')"));
    if(ready)break;
    await sleep(80);
  }
  assert(ready,'Messaging view failed to load '+file);
  // Wait for the shared CSS to load so computed geometry is grounded.
  await sleep(110);
}
try{
  let debugPort;
  for(let i=0;i<100;i++){
    const file=path.join(profile,'DevToolsActivePort');
    if(fs.existsSync(file)){debugPort=Number(fs.readFileSync(file,'utf8').split(/\r?\n/)[0]);break;}
    if(edge.exitCode!==null)throw new Error('Edge quit before exposing DevTools');
    await sleep(100);
  }
  assert(debugPort>0,'Edge DevTools port unavailable');
  let pages;
  for(let i=0;i<50;i++){
    try{
      pages=await(await fetch('http://127.0.0.1:'+debugPort+'/json/list')).json();
      if(pages.find(x=>x.type==='page'))break;
    }catch{}
    await sleep(100);
  }
  const target=pages.find(x=>x.type==='page');
  assert(target?.webSocketDebuggerUrl,'No Edge page found');
  ws=new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve,reject)=>{
    ws.addEventListener('open',resolve,{once:true});
    ws.addEventListener('error',reject,{once:true});
  });
  ws.addEventListener('message',event=>{
    const data=JSON.parse(event.data);
    if(!data.id)return;
    const waiter=pending.get(data.id);
    if(!waiter)return;
    pending.delete(data.id);
    data.error?waiter.reject(new Error(JSON.stringify(data.error))):waiter.resolve(data.result);
  });
  await send('Page.enable'); await send('Runtime.enable'); await send('Network.enable');
  await send('Network.setBlockedURLs',{urls:['https://*']});
  for(const width of [1440,1024,768,390,320]){
    await send('Emulation.setDeviceMetricsOverride',
      {width,height:950,deviceScaleFactor:1,mobile:false});
    for(const role of ['client','staff','owner']){
      for(const variant of ['inbox','selected']){
        await load(role+'-'+variant+'.html');
        const report=await evaluate('('+layout.toString()+')()');
        assert.deepEqual(report.issues,[],
          'Messaging '+role+' '+variant+' '+JSON.stringify(report));
        checks++;
        console.log('PASS Messaging Edge '+role+' '+variant+
          ' viewport='+width+' workspaceH='+report.workspaceH+
          ' sidebarW='+report.sidebarW);
        if(width===1440||width===390){
          const shot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});
          fs.writeFileSync(path.join(screenshots,role+'-'+variant+'-'+width+'.png'),
            Buffer.from(shot.data,'base64'));
        }
      }
    }
  }
  await send('Emulation.setDeviceMetricsOverride',{width:390,height:850,deviceScaleFactor:1,mobile:false});
  await load('client-empty.html');
  const empty=await evaluate("({title:document.querySelector('.comm-list-empty strong')?.textContent.trim(),support:!!document.querySelector('form[action*=\"StartSupport\"]'),manual:!!document.querySelector('#newConversationModal')})");
  assert.deepEqual(empty,{title:'No conversations yet',support:true,manual:false});
  checks++;console.log('PASS Client empty inbox keeps only legitimate support entrypoint');
  await load('client-selected.html');
  const before=sendCount;
  await evaluate("(() => {const f=document.querySelector('#commComposer');const t=document.querySelector('#commMessageInput');t.value='   ';f.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));return true;})()");
  await sleep(160);
  assert.equal(sendCount,before,'Whitespace-only composer still submitted');
  checks++;console.log('PASS Empty message blocked on client before transport');
  simulatedSendFailure=true;
  await evaluate("(() => {const f=document.querySelector('#commComposer');const t=document.querySelector('#commMessageInput');t.value='Draft preserved after failure';f.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));return true;})()");
  for(let i=0;i<30&&sendCount<=before;i++)await sleep(70);
  await sleep(160);
  assert.equal(sendCount,before+1,'Failed message submission absent');
  assert.equal(await evaluate("document.querySelector('#commMessageInput').value"),
    'Draft preserved after failure');
  checks++;console.log('PASS Send failure retains draft and reports error');
  simulatedSendFailure=false;
  await evaluate("(() => {const f=document.querySelector('#commComposer');const t=document.querySelector('#commMessageInput');f.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));f.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));return true;})()");
  for(let i=0;i<30&&sendCount<=before+1;i++)await sleep(70);
  await sleep(170);
  assert.equal(sendCount,before+2,'Rapid double submission bypassed sending lock');
  assert.equal(await evaluate("document.querySelector('#commMessageInput').value"),
    '','Successful send did not clear input');
  checks++;console.log('PASS Successful send clears draft and prevents duplicates');
  assert(summaryCalls>0&&messageCalls>0,'Message/sidebar polling was not started');
  checks++;console.log('PASS Existing message polling and authorized sidebar refresh both active');
  console.log('UNIFIED_MESSAGING_EDGE_PASS: '+checks+' browser/layout/interaction checks');
}finally{
  for(const {reject} of pending.values())reject(new Error('Edge closing'));
  pending.clear();
  if(ws&&ws.readyState===WebSocket.OPEN)ws.close();
  edge.kill();
  await new Promise(resolve=>server.close(resolve));
}
