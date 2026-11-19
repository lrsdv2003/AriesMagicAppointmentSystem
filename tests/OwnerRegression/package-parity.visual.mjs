import http from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {spawn,spawnSync} from 'node:child_process';
const root=path.resolve();
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
 'http://127.0.0.1:38219/fixture/Services-Index.html'
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
 const grid=document.querySelector('.staff-package-grid');
 const card=grid?.querySelector('.staff-package-card');
 const copy=card?.cloneNode(true);
 if(grid?.children.length===1&&copy){grid.append(copy.cloneNode(true),copy.cloneNode(true));}
 const body=document.body;
 const style=e=>e?getComputedStyle(e):null;
 const dims=e=>e?.getBoundingClientRect();
 const label=e=>e?.textContent.trim().replace(/\\s+/g,' ');
 const price=card?.querySelector('.staff-package-price');
 const title=card?.querySelector('h2');
 const summary=card?.querySelector('.staff-package-facts');
 const inclusions=card?.querySelector('.staff-package-inclusions');
 const listing=inclusions?.querySelector('ul');
 const actions=card?.querySelector('.staff-package-actions');
 const links=[...actions?.querySelectorAll('a')||[]];
 const cardRect=dims(card);
 return {
  role:body.classList.contains('owner-dashboard')?'Owner':body.classList.contains('staff-dashboard')?'Staff':'unknown',
  htmlShared:!!grid&&!!card&&!!summary&&!!listing,
  cards:grid?.children.length,columns:style(grid)?.gridTemplateColumns.trim().split(/\\s+/).length,
  overflow:document.documentElement.scrollWidth>innerWidth+3,
  cardWidth:Math.round(cardRect?.width||0),
  borderWidth:style(card)?.borderTopWidth,
  radius:style(card)?.borderTopLeftRadius,
  background:style(card)?.backgroundColor,
  padding:style(card)?.paddingTop,
  gap:style(card)?.gap,
  minHeight:style(card)?.minHeight,
  titleSize:style(title)?.fontSize,
  titleColor:style(title)?.color,
  priceSize:style(price)?.fontSize,
  priceColor:style(price)?.color,
  factsDisplay:style(summary)?.display,
  inclusionsDisplay:style(inclusions)?.display,
  listMax:style(listing)?.maxHeight,
  listOverflow:style(listing)?.overflowY,
  actionNames:links.map(e=>label(e)),
  actionUrls:links.map(e=>e.getAttribute('href')),
  toolbar:[...document.querySelectorAll('.staff-package-hero a')].map(label),
  actionFits:links.every(e=>dims(e).left>=cardRect.left-3&&dims(e).right<=cardRect.right+3&&dims(e).width>30)
 };
})()`;
 const results=[];
 for(const width of [1440,1200,1120,1024,768,767,390,320]){
  await send('Emulation.setDeviceMetricsOverride',{width,height:950,deviceScaleFactor:1,mobile:false});
  const roles={};
  for(const [role,filename] of [['Owner','Services-Index.html'],['Staff','Services-Index-Staff.html']]){
   await send('Page.navigate',{url:'http://127.0.0.1:38219/fixture/'+filename});
   await delay(325);
   const v=await evalJS(inspect),issues=[];
   roles[role]=v;
   const columns=width>1350?3:width>840?2:1;
   if(v.role!==role||!v.htmlShared||v.cards!==3)issues.push('role/card structure missing');
   if(v.columns!==columns)issues.push('unexpected grid columns '+v.columns+' expected '+columns);
   if(v.cardWidth<205||v.overflow||!v.actionFits)issues.push('overflow, narrow card or clipped buttons');
   if(!v.actionNames.some(x=>x.startsWith('View Details')))issues.push('no View Details action');
   if(role==='Owner'){
    if(!v.actionNames.some(x=>x.startsWith('Edit'))||!v.actionNames.some(x=>x.startsWith('Archive')))issues.push('Owner edit/archive missing');
    if(!v.toolbar.some(x=>x.includes('Add Package'))||!v.toolbar.some(x=>x.includes('Archived Packages')))issues.push('Owner add/archive navigation missing');
    if(!v.actionUrls.some(x=>x.includes('Services/Edit'))||!v.actionUrls.some(x=>x.includes('Services/Archive')))issues.push('Owner action route missing');
   }else{
    if(v.actionNames.some(x=>x.startsWith('Edit')||x.startsWith('Archive'))||v.actionNames.length!==1)issues.push('Staff has Owner actions');
    if(v.toolbar.some(x=>x.includes('Add Package')||x.includes('Archived Packages')))issues.push('Staff header includes Owner controls');
   }
   console.log(JSON.stringify({role,width,columns:v.columns,cardWidth:v.cardWidth,radius:v.radius,priceColor:v.priceColor,listMax:v.listMax,actions:v.actionNames,problems:issues}));
   failures+=issues.length;
   if(width===1440||width===390){
    const shot=await send('Page.captureScreenshot',{format:'png'});
    await fs.writeFile(path.join(os.tmpdir(),'aries-package-parity-'+role.toLowerCase()+'-'+width+'.png'),Buffer.from(shot.data,'base64'));
   }
  }
  const x=roles.Owner,y=roles.Staff;
  const styles=['borderWidth','radius','background','padding','gap','titleSize','titleColor','priceSize','priceColor','factsDisplay','inclusionsDisplay','listMax','listOverflow'];
  const mismatch=styles.filter(key=>x[key]!==y[key]).map(key=>[key,x[key],y[key]]);
  if(mismatch.length){failures++;console.log('OWNER_STAFF_COMPUTED_STYLING_MISMATCH width='+width+' '+JSON.stringify(mismatch));}
  if(width>767&&x.minHeight!==y.minHeight){failures++;console.log('OWNER_STAFF_CARD_HEIGHT_MISMATCH width='+width+' '+x.minHeight+' / '+y.minHeight);}
  results.push({width,owner:x,staff:y});
 }
 const detailInspect=`(() => {
 const root=document.querySelector('.staff-package-details-page');
 const sections=[...root?.querySelectorAll('.staff-package-detail-grid>section h2')||[]].map(e=>e.textContent.trim());
 const edit=[...root?.querySelectorAll('a')||[]].some(e=>e.getAttribute('href')?.includes('Services/Edit'));
 const back=[...root?.querySelectorAll('a')||[]].some(e=>e.getAttribute('href')?.includes('/Services')&&e.textContent.includes('Back to Packages'));
 return {
  role:document.body.classList.contains('owner-dashboard')?'Owner':'Staff',
  root:!!root,
  sections,
  edit,
  back,
  overflow:document.documentElement.scrollWidth>innerWidth+3,
  grid:root?.querySelector('.staff-package-detail-grid')?getComputedStyle(root.querySelector('.staff-package-detail-grid')).gridTemplateColumns:''
 };
})()`;
 for(const [role,filename] of [['Owner','Services-Details.html'],['Staff','Services-Details-Staff.html']]){
  await send('Emulation.setDeviceMetricsOverride',{width:1024,height:950,deviceScaleFactor:1,mobile:false});
  await send('Page.navigate',{url:'http://127.0.0.1:38219/fixture/'+filename});
  await delay(320);
  const v=await evalJS(detailInspect);
  const issues=[];
  if(!v.root||v.role!==role||v.overflow)issues.push('details page/layout overflow');
  if(v.sections.join('|')!=='Package Summary|Package Inclusions')issues.push('detail sections changed');
  if(v.edit!==(role==='Owner'))issues.push('details permission incorrect');
  if(!v.back)issues.push('Back navigation missing');
  failures+=issues.length;
  console.log(JSON.stringify({role,details:true,...v,problems:issues}));
 }
 console.log('OWNER_STAFF_PACKAGE_BROWSER_COMPLETE problems='+failures);
 if(failures)throw Error('Owner/Staff package parity browser checks failed: '+failures);
}finally{
 if(socket&&socket.readyState===1)socket.close();
 spawnSync('taskkill',['/F','/T','/PID',String(browser.pid)],{stdio:'ignore'});
 await new Promise(resolve=>server.close(resolve));
 try{await fs.rm(profile,{recursive:true,force:true,maxRetries:3,retryDelay:200})}catch{}
}
if(failures)process.exitCode=2;