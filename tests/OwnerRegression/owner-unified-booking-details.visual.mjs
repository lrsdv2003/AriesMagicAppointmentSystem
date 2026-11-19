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
 'http://127.0.0.1:38219/fixture/Payments-Verify.html'
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
 const root=document.querySelector('.owner-booking-detail-page');
 const layout=root?.querySelector('.owner-booking-detail-layout');
 const primary=root?.querySelector('.owner-booking-detail-main');
 const aside=root?.querySelector('.owner-booking-detail-timeline');
 const header=root?.querySelector('.owner-booking-detail-header');
 const title=header?.querySelector('h1');
 const actions=primary?.querySelector('.owner-booking-detail-actions,.owner-action-bar');
 const timeline=aside?.querySelector('.aries-timeline');
 const rect=e=>e?.getBoundingClientRect();
 const pr=rect(primary),ar=rect(aside),rr=rect(root),lr=rect(layout),hr=rect(header);
 const sections=[...primary?.querySelectorAll(':scope > .owner-review-card, :scope > .staff-unified-section-stack > .staff-detail-section, :scope > .owner-booking-financial-details')||[]];
 const compare=primary?.querySelector('.owner-payment-compare');
 const table=primary?.querySelector('.owner-booking-financial-details table');
 const comparisonWrap=compare?.closest('.table-responsive');
 const tableWrap=table?.closest('.table-responsive');
 const sides=primary&&aside&&pr.right<=ar.left+2&&Math.abs(pr.top-ar.top)<4;
 const stacked=primary&&aside&&ar.top>=pr.bottom-3;
 const proof=primary?.querySelector('.owner-booking-proof-inline');
 const buttons=[...actions?.querySelectorAll('a,button')||[]];
 const actionFits=buttons.every(x=>rect(x).left>=pr.left-3&&rect(x).right<=pr.right+3);
 const collapsed=primary&&aside&&Math.abs(pr.left-ar.left)<3&&Math.abs(pr.width-ar.width)<4;
 const missingFonts=[...root?.querySelectorAll('h1,h2')||[]].filter(x=>getComputedStyle(x).display==='none').length;
 return {
  width:innerWidth,
  variant:root?.dataset.ownerBookingVariant,
  pageOverflow:document.documentElement.scrollWidth>innerWidth+3,
  hasRoot:!!root,hasLayout:!!layout,
  noWorkspaceTitle:!document.querySelector('.workspace-page-heading'),
  code:title?.textContent.trim(),
  hasTimeline:!!timeline,
  mainWidth:Math.round(pr?.width||0),
  timelineWidth:Math.round(ar?.width||0),
  mainRight:Math.round(pr?.right||0),
  asideLeft:Math.round(ar?.left||0),
  mainBottom:Math.round(pr?.bottom||0),
  asideTop:Math.round(ar?.top||0),
  isSideBySide:!!sides,
  isStacked:!!stacked,
  stackedAligned:!!collapsed,
  rootFillsContent:!!root&&rr.width>innerWidth*0.55,
  actionFits,
  sections:sections.length,
  hasProof:!!proof,
  hasComparison:!!compare,
  hasFinance:!!table,
  internalTableScroll:(!table||!!tableWrap&&tableWrap.scrollWidth>tableWrap.clientWidth),
  comparisonScroll:(!compare||!!comparisonWrap&&comparisonWrap.scrollWidth>comparisonWrap.clientWidth),
  overrun:!!rr&&(rr.left< -2||rr.right>innerWidth+3),
  missingFonts,
  styleColumns:layout?getComputedStyle(layout).gridTemplateColumns:null,
  detailsBtnCount:buttons.length
 };
})()`;
 for(const width of [1440,1200,1100,1024,768,390,320]){
 await send('Emulation.setDeviceMetricsOverride',{width,height:950,deviceScaleFactor:1,mobile:false});
 for(const [kind,filename,expectedSections] of [
   ['payment','Payments-Verify.html',4],
   ['confirmed','Bookings-OwnerDetails.html',6]
 ]){
  await send('Page.navigate',{url:'http://127.0.0.1:38219/fixture/'+filename});
  await delay(270);
  const v=await evalJS(inspect),issues=[];
  if(!v.hasRoot||!v.hasLayout||!v.hasTimeline||!v.noWorkspaceTitle)issues.push('Owner shell, timeline or header missing');
  if(!/^BK-\d{4}-\d{3,}$/.test(v.code||''))issues.push('actual Booking ID missing');
  if(v.pageOverflow||v.overrun)issues.push('unexpected page overflow');
  if(v.sections!==expectedSections)issues.push('incorrect number of stacked main information sections');
  if(v.missingFonts)issues.push('hidden section headings');
  if(!v.actionFits||!v.rootFillsContent)issues.push('actions/content clipped or width wasted');
  if(width>1100&&!v.isSideBySide)issues.push('desktop timeline not beside content');
  if(width<=1100&&(!v.isStacked||!v.stackedAligned))issues.push('tablet/mobile timeline not stacked after content');
  if(kind==='payment'&&(!v.hasProof||!v.hasComparison))issues.push('payment OCR or proof lost');
  if(kind==='confirmed'&&!v.hasFinance)issues.push('confirmed financial history lost');
  if(width<=390&&v.mainWidth<250)issues.push('mobile main content too narrow');
  console.log(JSON.stringify({fixture:filename,...v,problems:issues}));
  failures+=issues.length;
  if((width===1440||width===390)&&issues.length===0){
    const shot=await send('Page.captureScreenshot',{format:'png'});
    await fs.writeFile(path.join(os.tmpdir(),'aries-owner-unified-'+kind+'-'+width+'.png'),Buffer.from(shot.data,'base64'));
  }
 }
}
console.log('OWNER_UNIFIED_BOOKING_DETAILS_BROWSER_COMPLETE problems='+failures);
}finally{
 if(socket&&socket.readyState===1)socket.close();
 spawnSync('taskkill',['/F','/T','/PID',String(browser.pid)],{stdio:'ignore'});
 await new Promise(resolve=>server.close(resolve));
 try{await fs.rm(profile,{recursive:true,force:true,maxRetries:3,retryDelay:200})}catch{}
}
if(failures)process.exitCode=2;