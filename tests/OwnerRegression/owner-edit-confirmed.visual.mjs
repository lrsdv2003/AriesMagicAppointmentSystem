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
 'http://127.0.0.1:38219/fixture/BookingChanges-Edit.html'
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
 const root=document.querySelector('.owner-confirmed-change-page');
 const layout=root?.querySelector('.confirmed-change-layout');
 const card=root?.querySelector('.confirmed-change-card');
 const aside=root?.querySelector('.confirmed-change-summary');
 const heading=root?.querySelector('.confirmed-change-heading');
 const form=root?.querySelector('#confirmedChangeForm');
 const submit=root?.querySelector('.confirmed-change-submit');
 const labels=[...root?.querySelectorAll('label')||[]];
 const fields=[...root?.querySelectorAll('select,textarea,input')||[]].filter(x=>x.type!=='hidden');
 const rect=e=>e?.getBoundingClientRect();
 const main=rect(card),summary=rect(aside),container=rect(layout);
 const side=main&&summary&&main.right<=summary.left+3&&Math.abs(main.top-summary.top)<4;
 const stack=main&&summary&&summary.top>=main.bottom-3&&Math.abs(main.left-summary.left)<3;
 const fits=fields.every(f=>!rect(f)||rect(f).left>=main.left-5&&rect(f).right<=main.right+5);
 const headings=[...root?.querySelectorAll('h1,h2')||[]].map(x=>x.textContent.trim());
 const words=root?.textContent.replace(/\\s+/g,' ')||'';
 return {
  width:innerWidth,pageOverflow:document.documentElement.scrollWidth>innerWidth+3,
  shell:!!root&&!!layout&&!!card&&!!aside&&!!heading&&!!form,
  noWorkspace:!document.querySelector('.workspace-page-heading'),
  code:root?.querySelector('.owner-booking-detail-header h1')?.textContent.trim(),
  layoutMode:layout?getComputedStyle(layout).gridTemplateColumns:null,
  sideBySide:!!side,stacked:!!stack,
  mainWidth:Math.round(main?.width||0),summaryWidth:Math.round(summary?.width||0),
  withinViewport:!!container&&container.left>=-2&&container.right<=innerWidth+2,
  fieldsFit:fits,submitVisible:!!submit&&rect(submit).width>=100,
  reasonRemoved:!form?.querySelector('[name=Reason]')&&!root?.textContent.includes('Reason and Staff verification'),
  paymentSafetyRemoved:!root?.textContent.includes('Payment safety'),
  properFields:!!root?.querySelector('[name=ServiceId]')&&!!root?.querySelector('[name=Venue]')&&!!root?.querySelector('[name=Latitude]')&&!!root?.querySelector('[name=Longitude]'),
  headings,readableLabels:labels.every(x=>getComputedStyle(x).fontSize!=='0px'),
  emptyWidth:root?rect(root).width:0
 };
})()`;
 for(const width of [1440,1200,1100,1024,768,390,320]){
 await send('Emulation.setDeviceMetricsOverride',{width,height:950,deviceScaleFactor:1,mobile:false});
 await send('Page.navigate',{url:'http://127.0.0.1:38219/fixture/BookingChanges-Edit.html'});
 await delay(260);
 const v=await evalJS(inspect),issues=[];
 if(!v.shell||!v.noWorkspace||!v.properFields||!v.reasonRemoved||!v.paymentSafetyRemoved)issues.push('Form structure, deleted controls or heading');
 if(!/^BK-\d{4}-\d{3,}$/.test(v.code||''))issues.push('actual booking code missing');
 if(v.pageOverflow||!v.withinViewport||!v.fieldsFit||!v.submitVisible||!v.readableLabels)issues.push('horizontal overflow or clipped fields/actions');
 if(width>1100&&!v.sideBySide)issues.push('desktop current quote not beside form');
 if(width<=1100&&!v.stacked)issues.push('tablet/mobile quote not below form');
 console.log(JSON.stringify({fixture:'BookingChanges-Edit',...v,problems:issues}));
 failures+=issues.length;
 // Context-specific errors are content-height aware and must not clip fields.
 await evalJS("(()=>{const e=document.createElement('div');e.className='alert alert-danger';e.setAttribute('role','alert');e.textContent='This quote cannot proceed while payment verification is active. Review the confirmed booking after the issue is resolved.';document.querySelector('.confirmed-change-heading').after(e);return true})()");
 const alert=await evalJS("(()=>{const a=document.querySelector('.owner-confirmed-change-page .alert');return {visible:a.getBoundingClientRect().height>0,pageOverflow:document.documentElement.scrollWidth>innerWidth+3}})()");
 if(!alert.visible||alert.pageOverflow){failures++;console.log('OWNER_EDIT_ERROR_OVERFLOW',width,alert);}
 if((width===1440||width===390)&&issues.length===0){
   const shot=await send('Page.captureScreenshot',{format:'png'});
   await fs.writeFile(path.join(os.tmpdir(),'aries-owner-edit-confirmed-'+width+'.png'),Buffer.from(shot.data,'base64'));
 }
}
console.log('OWNER_EDIT_CONFIRMED_BROWSER_COMPLETE problems='+failures);
}finally{
 if(socket&&socket.readyState===1)socket.close();
 spawnSync('taskkill',['/F','/T','/PID',String(browser.pid)],{stdio:'ignore'});
 await new Promise(resolve=>server.close(resolve));
 try{await fs.rm(profile,{recursive:true,force:true,maxRetries:3,retryDelay:200})}catch{}
}
if(failures)process.exitCode=2;