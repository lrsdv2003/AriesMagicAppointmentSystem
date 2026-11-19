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
 'http://127.0.0.1:38219/fixture/Dashboard-Owner.html'
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
 const owner=document.body.classList.contains('owner-dashboard');
 const role=owner?'Owner':'Staff';
 const root=document.querySelector(owner?'.owner-round1-dashboard':'.staff-round1-dashboard');
 const metricGrid=root?.querySelector(owner?'.owner-round3-metrics':'.staff-round1-metrics');
 const metrics=[...metricGrid?.querySelectorAll(owner?'.owner-round3-metric':'.staff-round1-metric')||[]];
 const main=root?.querySelector(owner?'.owner-round3-grid':'.staff-round1-columns');
 const priority=root?.querySelector(owner?'.owner-round1-requests':'.staff-round1-priority');
 const upcoming=root?.querySelector(owner?'.owner-round3-upcoming':'.staff-round1-upcoming');
 const tablist=priority?.querySelector(owner?'.owner-round1-tabs':'.staff-round1-tabs');
 const tabs=[...tablist?.querySelectorAll('[role=tab]')||[]];
 const firstTask=priority?.querySelector(owner?'.owner-round1-request-row':'.staff-round1-task');
 const eventList=upcoming?.querySelector(owner?'.owner-round3-event-list':'.staff-round1-event-list');
 const event=eventList?.querySelector(owner?'.owner-round3-event':'.staff-round1-event');
 const button=firstTask?.querySelector(owner?'.staff-round1-action':'.staff-round1-action');
 const style=e=>e?getComputedStyle(e):null;
 const rect=e=>e?.getBoundingClientRect();
 const cols=e=>style(e)?.gridTemplateColumns.trim().split(/\\s+/).filter(Boolean).length;
 const a=rect(priority),b=rect(upcoming);
 const value=e=>e?.textContent.trim().replace(/\\s+/g,' ');
 return {
  role,hasRoot:!!root,metrics:metrics.length,metricLabels:metrics.map(e=>value(e.querySelector('h2'))),
  metricColumns:cols(metricGrid),contentColumns:cols(main),
  summaryStyle:{
   bg:style(metrics[0])?.backgroundColor,border:style(metrics[0])?.borderTopColor,
   borderWidth:style(metrics[0])?.borderTopWidth,radius:style(metrics[0])?.borderTopLeftRadius,
   padding:style(metrics[0])?.paddingTop,height:Math.round(rect(metrics[0])?.height||0),
   iconSize:Math.round(rect(metrics[0]?.querySelector('.staff-round1-metric-icon'))?.width||0),
   labelFont:style(metrics[0]?.querySelector('h2'))?.fontSize,
   labelColor:style(metrics[0]?.querySelector('h2'))?.color,
   amountFont:style(metrics[0]?.querySelector('strong'))?.fontSize
  },
  panelStyle:{
   bg:style(priority)?.backgroundColor,border:style(priority)?.borderTopColor,
   borderWidth:style(priority)?.borderTopWidth,radius:style(priority)?.borderTopLeftRadius,
   padding:style(priority)?.paddingTop
  },
  tabs:tabs.length,selectedTab:tabs.find(t=>t.getAttribute('aria-selected')==='true')?.textContent.trim(),
  tabStyle:{
   border:style(tabs[0])?.borderTopColor,
   bg:style(tabs[0])?.backgroundColor,radius:style(tabs[0])?.borderTopLeftRadius,
   font:style(tabs[0])?.fontSize,padding:style(tabs[0])?.paddingTop
  },
  rowStyle:{
   bg:style(firstTask)?.backgroundColor,radius:style(firstTask)?.borderTopLeftRadius,
   border:style(firstTask)?.borderTopColor,padding:style(firstTask)?.paddingTop,
   button:style(button)?.backgroundColor
  },
  rowCount:priority?.querySelectorAll(owner?'.owner-round1-request-row':'.staff-round1-task').length,
  upcomingStyle:{
   height:Math.round(rect(upcoming)?.height||0),bg:style(upcoming)?.backgroundColor,
   radius:style(upcoming)?.borderTopLeftRadius,padding:style(upcoming)?.paddingTop,
   scroll:style(eventList)?.overflowY,eventStyle:style(event)?.display,
   eventIconWidth:Math.round(rect(event?.querySelector(owner?'.owner-round3-event-icon':'.staff-round2-event-icon'))?.width||0)
  },
  eventCount:eventList?.children.length,
  mainSideBySide:!!a&&!!b&&b.left>=a.right-4&&Math.abs(a.top-b.top)<4,
  mainStacked:!!a&&!!b&&b.top>=a.bottom-4,
  pageOverflow:document.documentElement.scrollWidth>innerWidth+3,
  contentFits:[...root?.querySelectorAll(':scope .staff-round1-metric,:scope .staff-round1-panel,:scope .owner-round1-request-row,:scope .owner-round3-event')||[]].every(e=>{
   const v=rect(e);return v.left>=-4&&v.right<=innerWidth+4
  }),
  reviewLinks:[...priority?.querySelectorAll('a[href*="/Payments/"]')||[]].map(e=>e.getAttribute('href')),
  allButtons:[...priority?.querySelectorAll('.owner-round1-view-all')||[]].length,
  allText:/View all (payment|refund) requests/i.test(priority?.textContent||''),
  reviewActions:priority?.querySelectorAll('.staff-round1-action').length,
  financialValues:metrics.map(e=>value(e.querySelector('strong'))),
  tabPanels:priority?.querySelectorAll('[role=tabpanel]').length,
  ownerMarkup:owner?{
   amountCount:priority?.querySelectorAll('.owner-dashboard-request-facts strong').length,
   reviewCount:priority?.querySelectorAll('a[href*="/Payments/Verify"],a[href*="/Payments/RefundReview"]').length,
   proofIndicators:priority?.querySelectorAll('.owner-round1-indicator').length,
   upcomingBookingLinks:upcoming?.querySelectorAll('a[href*="/Bookings/Details"]').length
  }:null
 };
})()`;
 const tabTest=`(() => {
 const a=document.getElementById('ownerPaymentsTab');
 const b=document.getElementById('ownerRefundsTab');
 const x=document.getElementById('ownerPaymentsPanel');
 const y=document.getElementById('ownerRefundsPanel');
 if(!a||!b||!x||!y)return {passed:false,reason:'tab elements missing'};
 b.click();
 const refundSelected=b.getAttribute('aria-selected')==='true' &&
     a.getAttribute('aria-selected')==='false' && a.tabIndex===-1 &&
     b.tabIndex===0 && x.hidden && !y.hidden && getComputedStyle(x).display==='none';
 b.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowLeft',bubbles:true}));
 const keyboardSelected=a.getAttribute('aria-selected')==='true' &&
   !x.hidden && y.hidden && document.activeElement===a;
 return {passed:refundSelected&&keyboardSelected,refundSelected,keyboardSelected,
         reviewLinks:[...document.querySelectorAll('.owner-round1-tabpanel a')].map(e=>e.getAttribute('href'))};
})()`;
 for(const width of [1440,1200,1100,1050,1024,800,768,600,390,320]){
  await send('Emulation.setDeviceMetricsOverride',{width,height:950,deviceScaleFactor:1,mobile:false});
  const roles={};
  for(const [role,file] of [['Owner','Dashboard-Owner.html'],['Staff','Dashboard-Staff.html']]){
   await send('Page.navigate',{url:'http://127.0.0.1:38219/fixture/'+file});
   await delay(320);
   const v=await evalJS(inspect),issues=[];
   roles[role]=v;
   if(v.role!==role||!v.hasRoot||v.metrics!==3||v.tabPanels!==(role==='Owner'?2:3))issues.push('dashboard structure');
   const metricColumns=width>800?3:1;
   const mainColumns=width>1050?2:1;
   if(v.metricColumns!==metricColumns)issues.push('metrics columns '+v.metricColumns+'/'+metricColumns);
   if(v.contentColumns!==mainColumns)issues.push('content columns '+v.contentColumns+'/'+mainColumns);
   if(mainColumns===2&&!v.mainSideBySide)issues.push('main/aside not aligned');
   if(mainColumns===1&&!v.mainStacked)issues.push('main/aside not stacked');
   if(v.pageOverflow||!v.contentFits)issues.push('page overflow/content clipping');
   if(v.upcomingStyle.height!==(width<=767?410:560)||v.upcomingStyle.scroll!=='auto')issues.push('upcoming card height/scroll '+JSON.stringify(v.upcomingStyle));
   if(role==='Owner'){
    if(v.allButtons||v.allText)issues.push('removed view-all links still visible');
    if(v.reviewActions<1||v.ownerMarkup.reviewCount<1)issues.push('review actions removed');
    if(v.ownerMarkup.amountCount<1||v.ownerMarkup.proofIndicators<1)issues.push('financial/audit facts removed');
    if(v.ownerMarkup.upcomingBookingLinks!==4)issues.push('owner upcoming bookings changed');
    if(v.tabs!==2||!v.financialValues.every(x=>x.includes('₱')))issues.push('owner payments/refunds or PHP metrics changed');
    const tab=await evalJS(tabTest);
    if(!tab.passed)issues.push('tab click/keyboard state '+JSON.stringify(tab));
    if(v.reviewLinks.some(x=>x.includes('PendingVerification')||x.includes('RefundRequests')))issues.push('old view all URL');
   }else{
    if(v.tabs!==3||v.reviewLinks.length!==0)issues.push('staff workflow changed');
   }
   console.log(JSON.stringify({role,width,metricColumns:v.metricColumns,mainColumns:v.contentColumns,
     summary:v.summaryStyle,panel:v.panelStyle,upcoming:v.upcomingStyle,
     row:v.rowStyle,allText:v.allText,problems:issues}));
   failures+=issues.length;
   if(width===1440||width===390){
    const shot=await send('Page.captureScreenshot',{format:'png'});
    await fs.writeFile(path.join(os.tmpdir(),'aries-owner-staff-dashboard-'+role.toLowerCase()+'-'+width+'.png'),Buffer.from(shot.data,'base64'));
   }
  }
  const a=roles.Owner,b=roles.Staff;
  const comparable=['bg','border','borderWidth','radius','padding','labelFont','labelColor','iconSize'];
  const delta=comparable.filter(k=>a.summaryStyle[k]!==b.summaryStyle[k]).map(k=>[k,a.summaryStyle[k],b.summaryStyle[k]]);
  if(delta.length){failures++;console.log('DASHBOARD_METRIC_SKIN_DIFFERENCE '+width+' '+JSON.stringify(delta));}
  const panelProps=['bg','border','borderWidth','radius','padding'];
  const pd=panelProps.filter(k=>a.panelStyle[k]!==b.panelStyle[k]).map(k=>[k,a.panelStyle[k],b.panelStyle[k]]);
  if(pd.length){failures++;console.log('DASHBOARD_PANEL_SKIN_DIFFERENCE '+width+' '+JSON.stringify(pd));}
  const td=['bg','border','radius','font','padding'];
  const tdelta=td.filter(k=>a.tabStyle[k]!==b.tabStyle[k]).map(k=>[k,a.tabStyle[k],b.tabStyle[k]]);
  if(tdelta.length){failures++;console.log('DASHBOARD_TABS_DIFFERENCE '+width+' '+JSON.stringify(tdelta));}
 }
 for(const state of ['empty','error']){
  await send('Emulation.setDeviceMetricsOverride',{width:390,height:800,deviceScaleFactor:1,mobile:false});
  await send('Page.navigate',{url:'http://127.0.0.1:38219/fixture/Dashboard-Owner-'+state+'.html'});
  await delay(240);
  const v=await evalJS(inspect);
  const noLinks=!v.allText&&!v.allButtons;
  const safe=!v.pageOverflow&&v.contentFits&&v.mainStacked;
  if(!noLinks||!safe){failures++;console.log('OWNER_'+state.toUpperCase()+'_DASHBOARD_FAIL',JSON.stringify({noLinks,safe,v}));}
  else console.log('OWNER_'+state.toUpperCase()+'_DASHBOARD_PASSED');
 }
 console.log('OWNER_STAFF_DASHBOARD_BROWSER_COMPLETE failures='+failures);
 if(failures)throw Error('Dashboard parity browser check failed: '+failures);
}finally{
 if(socket&&socket.readyState===1)socket.close();
 spawnSync('taskkill',['/F','/T','/PID',String(browser.pid)],{stdio:'ignore'});
 await new Promise(resolve=>server.close(resolve));
 try{await fs.rm(profile,{recursive:true,force:true,maxRetries:3,retryDelay:200})}catch{}
}
if(failures)process.exitCode=2;