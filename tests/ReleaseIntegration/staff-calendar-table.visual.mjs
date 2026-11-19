// Real Chromium Edge verification of isolated authenticated Staff Calendar Razor output.
// No live website or production data. Generate fixture HTML via --staff-calendar-table-only.
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import http from 'node:http';
import {setTimeout as sleep} from 'node:timers/promises';

const fixtures=process.env.ARIES_STAFF_CALENDAR_FIXTURE_ROOT;
assert(fixtures && fs.existsSync(path.join(fixtures,'staff-calendar-10.html')),
  'Generate isolated Calendar fixtures first');
const root=path.resolve('wwwroot');
const edgePath=process.env.EDGE_PATH||
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
assert(fs.existsSync(edgePath),'Edge browser is unavailable');
const profile=fs.mkdtempSync(path.join(os.tmpdir(),'aries-calendar-edge-'));
const screenshots=path.join(fixtures,'screenshots');
fs.mkdirSync(screenshots,{recursive:true});
const types={'.css':'text/css','.js':'application/javascript','.svg':'image/svg+xml',
  '.png':'image/png','.jpg':'image/jpeg','.jpeg':'image/jpeg',
  '.woff2':'font/woff2','.webp':'image/webp','.ico':'image/x-icon'};
const server=http.createServer((request,response)=>{
  try{
    const pathname=decodeURIComponent(new URL(request.url,'http://127.0.0.1').pathname);
    let data,type;
    if(/^\/fixtures\/(?:staff|owner)-calendar-(?:0|1|2|3|4|5|6|10)\.html$/.test(pathname)){
      data=fs.readFileSync(path.join(fixtures,path.basename(pathname)));type='text/html; charset=utf-8';
    }else{
      const file=path.resolve(root,'.'+pathname);
      if(!file.startsWith(root+path.sep))throw new Error('outside static root');
      data=fs.readFileSync(file);type=types[path.extname(file)]||'application/octet-stream';
    }
    response.writeHead(200,{'Content-Type':type,'Cache-Control':'no-store',
      'Content-Security-Policy':"default-src 'self' data: blob: 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self';"});
    response.end(data);
  }catch(e){response.writeHead(404);response.end('Missing fixture/static asset');}
});
const port=await new Promise((resolve,reject)=>{
  server.once('error',reject);server.listen(0,'127.0.0.1',()=>resolve(server.address().port));
});
const edge=spawn(edgePath,['--headless=new','--disable-gpu','--no-first-run',
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
  const packet=await cmd('Runtime.evaluate',{expression:expr,returnByValue:true,awaitPromise:true});
  if(packet.exceptionDetails)throw new Error(packet.exceptionDetails.text);
  return packet.result?.value;
}
function sample(){
  const frame=document.querySelector('.staff-upcoming-table-frame');
  const scroll=frame?.querySelector('.staff-data-table-scroll');
  const table=frame?.querySelector('.staff-data-table');
  const pager=frame?.querySelector('.staff-round2-calendar-pager');
  const legend=document.querySelector('#calendarPackageChips');
  const toolbar=document.querySelector('.staff-calendar-toolbar');
  const monthrow=document.querySelector('.staff-calendar-month-row');
  if(!frame||!scroll||!table||!pager||!legend||!toolbar||!monthrow)
    return {issues:['missing Staff Calendar table/header/legend']};
  const rect=el=>el.getBoundingClientRect();
  const fr=rect(frame),sr=rect(scroll),pr=rect(pager);
  const all=[...table.querySelectorAll('tbody tr.js-upcoming-row')];
  const visible=all.filter(x=>getComputedStyle(x).display!=='none');
  const empty=table.querySelector('#upcomingFilteredEmpty');
  const emptyVisible=empty&&getComputedStyle(empty).display!=='none';
  const heads=[...table.querySelectorAll('thead th')];
  const cells=[...visible[0]?.querySelectorAll('td')||[]];
  const active=document.querySelector('.package-legend-filter[aria-pressed=true]');
  const legButtons=[...legend.querySelectorAll('.package-legend-filter')];
  const legBorders=legButtons.map(x=>getComputedStyle(x).borderTopWidth);
  const dotColors=[...legend.querySelectorAll('.package-dot')].map(x=>getComputedStyle(x).backgroundColor);
  const count=Number((location.pathname.match(/(?:staff|owner)-calendar-(\d+)\.html/)||[])[1]||0);
  const info=document.querySelector('#staffUpcomingPageInfo')?.textContent.trim()||'';
  const total=document.querySelector('#staffUpcomingTotal')?.textContent.trim()||'';
  const prev=document.querySelector('#staffUpcomingPrev');
  const next=document.querySelector('#staffUpcomingNext');
  const issues=[];
  if(document.documentElement.scrollWidth>innerWidth+3)issues.push('horizontal document overflow');
  if(fr.right>innerWidth+3||fr.left<-2||sr.width>innerWidth+3)
    issues.push('table frame outside viewport');
  if(getComputedStyle(frame).borderTopLeftRadius!=='0px'
    ||getComputedStyle(pager).borderBottomRightRadius!=='0px')
    issues.push('rounded table corners');
  if(Math.abs(pr.top-sr.bottom)>3)issues.push('displaced footer '+(pr.top-sr.bottom));
  if(heads.length!==6||table.querySelectorAll('colgroup col').length!==6)
    issues.push('bad semantic table column count');
  if(all.length!==count)issues.push('wrong complete dataset row count '+all.length);
  if(visible.length!==Math.min(count,5))issues.push('wrong page size '+visible.length);
  if(!total.startsWith(count+' matching record'))issues.push('wrong full count '+total);
  if(info!=='Page 1 of '+Math.max(1,Math.ceil(count/5)))
    issues.push('wrong first page '+info);
  if(count<=5 && (!prev.hidden||!next.hidden))
    issues.push('redundant pagination for one page');
  if(count>5 && (prev.disabled!==true||next.disabled!==false))
    issues.push('incorrect first-page pagination boundaries');
  if(!count && !emptyVisible)issues.push('empty state not visible');
  if(count && emptyVisible)issues.push('empty state visible alongside bookings');
  if(legBorders.some(x=>x!=='0px'))issues.push('legend outline/border visible: '+legBorders.join(','));
  const deluxeDot=legend.querySelector('[data-package-filter="Deluxe Magic"] .package-dot');
  if(!deluxeDot || getComputedStyle(deluxeDot).backgroundColor!=='rgb(225, 75, 105)')
    issues.push('shared Deluxe red color missing');
  if(dotColors.length===0||dotColors.every(x=>x==='rgba(0, 0, 0, 0)'))
    issues.push('lost legend status colors');
  if(getComputedStyle(legend.querySelector('.blocked-legend-mark')).borderTopWidth!=='0px')
    issues.push('blocked legend still bordered');
  // Owner adds one authorized Settings action; wrapping it on narrow phones is
  // intentional as long as all four controls remain visible and tappable.
  const owner=Boolean(document.querySelector('body.owner-dashboard'));
  const toolbarLimit=owner&&innerWidth<=600?112:64;
  if(rect(toolbar).height>toolbarLimit)issues.push('calendar toolbar too tall '+rect(toolbar).height);
  if(innerWidth>=1024 && rect(monthrow).height>80)
    issues.push('desktop month/legend strip too tall '+rect(monthrow).height);
  if(!document.querySelector('.fc-daygrid'))
    issues.push('FullCalendar grid failed to render');
  if(document.querySelector('#calendarTitle')?.textContent.trim().length<4)
    issues.push('month title missing');
  if(cells.length===6){
    for(let i=0;i<6;i++){
      if(Math.abs(rect(heads[i]).left-rect(cells[i]).left)>2||
         Math.abs(rect(heads[i]).right-rect(cells[i]).right)>2)
        issues.push('column alignment '+i);
    }
    const action=cells[5].querySelector('a');
    if(!action||rect(action).height<38)issues.push('booking Details click target too small');
  }
  if(innerWidth<=900 && scroll.scrollWidth<=scroll.clientWidth+25)
    issues.push('mobile/tablet lacks inner horizontal table scrolling');
  const cardContainer=document.querySelector('.staff-upcoming-cards');
  if(cardContainer && getComputedStyle(cardContainer).display!=='none')
    issues.push('duplicate mobile card container visible');
  return{issues,count,visible:visible.length,tableRegionH:Math.round(sr.height),
    headH:Math.round(rect(table.tHead).height),bodyH:Math.round(rect(table.tBodies[0]).height),
    rowsH:[...table.tBodies[0].rows].map(r=>Math.round(rect(r).height)),
    emptyH:empty?Math.round(rect(empty).height):0,
    emptyPad:empty?getComputedStyle(empty.querySelector('td')).padding:'',
    footerH:Math.round(pr.height),monthTitle:document.querySelector('#calendarTitle').textContent.trim(),
    width:innerWidth,toolbarH:Math.round(rect(toolbar).height),
    monthrowH:Math.round(rect(monthrow).height),activeFilter:active?.dataset.packageFilter||''};
}
async function load(name){
  await cmd('Page.navigate',{url:'http://127.0.0.1:'+port+'/fixtures/'+name});
  let ready=false;
  for(let i=0;i<105;i++){
    if(await evaluate("document.readyState==='complete' &&"+
      "!!document.querySelector('.staff-upcoming-table-frame') &&"+
      "!!document.querySelector('.fc-daygrid') &&"+
      "!!document.querySelector('#calendarTitle')?.textContent.trim()")){
      ready=true;break;
    }
    await sleep(85);
  }
  assert(ready,'Staff Calendar fixture failed to render '+name);
}
try{
  let debugPort;
  for(let i=0;i<100;i++){
    const p=path.join(profile,'DevToolsActivePort');
    if(fs.existsSync(p)){debugPort=Number(fs.readFileSync(p,'utf8').split(/\r?\n/)[0]);break;}
    if(edge.exitCode!==null)throw new Error('Edge exited before DevTools');
    await sleep(100);
  }
  assert(debugPort>0,'Cannot attach to Edge DevTools');
  let targets;
  for(let i=0;i<50;i++){
    try{
      targets=await(await fetch('http://127.0.0.1:'+debugPort+'/json/list')).json();
      if(targets.find(x=>x.type==='page'))break;
    }catch{}
    await sleep(100);
  }
  const target=targets.find(x=>x.type==='page');
  assert(target?.webSocketDebuggerUrl,'No page DevTools URL');
  ws=new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve,reject)=>{
    ws.addEventListener('open',resolve,{once:true});
    ws.addEventListener('error',reject,{once:true});
  });
  ws.addEventListener('message',event=>{
    const packet=JSON.parse(event.data);
    if(!packet.id)return;
    const item=pending.get(packet.id);if(!item)return;
    pending.delete(packet.id);
    packet.error?item.reject(new Error(JSON.stringify(packet.error))):item.resolve(packet.result);
  });
  await cmd('Page.enable');await cmd('Runtime.enable');await cmd('Network.enable');
  await cmd('Network.setBlockedURLs',{urls:['https://*']});
  for(const width of [1440,1024,768,390,320]){
    await cmd('Emulation.setDeviceMetricsOverride',
      {width,height:900,deviceScaleFactor:1,mobile:false});
    let baseline=null;
    for(const n of [0,1,2,3,4,5,6,10]){
      await load('staff-calendar-'+n+'.html');
      const state=await evaluate('('+sample.toString()+')()');
      assert.deepEqual(state.issues,[],JSON.stringify(state));
      if(width>=768 && n<=5){
        if(baseline==null)baseline=state.tableRegionH;
        assert(Math.abs(state.tableRegionH-baseline)<=1,
          'table shifts between 0-5 records: '+JSON.stringify(state));
      }
      checks++;
      console.log('PASS Calendar Edge width='+width+' count='+n+
        ' visible='+state.visible+' tableH='+state.tableRegionH+
        ' footerH='+state.footerH);
      if(n===0||n===5){
        const screenshot=await cmd('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});
        fs.writeFileSync(path.join(screenshots,'calendar-'+width+'-'+n+'.png'),
          Buffer.from(screenshot.data,'base64'));
      }
    }
  }
  // Owner must render the exact Staff table/legend/grid at all tested widths.
  // This is generated by actual Owner Identity cookies + GUID LocalDB data.
  for(const width of [1440,1024,768,390,320]){
    await cmd('Emulation.setDeviceMetricsOverride',
      {width,height:900,deviceScaleFactor:1,mobile:false});
    let baseline=null;
    for(const n of [0,1,2,3,4,5,6,10]){
      await load('owner-calendar-'+n+'.html');
      const state=await evaluate('('+sample.toString()+')()');
      assert.deepEqual(state.issues,[],'Owner '+JSON.stringify(state));
      const ownerSettings=await evaluate(
        "document.querySelector('.staff-calendar-nav a[href*=\"Calendar/Settings\"]')?.textContent.trim()");
      assert(ownerSettings?.includes('Calendar Settings'),
        'Owner lost its exclusive Calendar Settings button');
      if(width>=768 && n<=5){
        if(baseline==null)baseline=state.tableRegionH;
        assert(Math.abs(state.tableRegionH-baseline)<=1,
          'Owner table footer shifts 0-5 bookings: '+JSON.stringify(state));
      }
      checks++;
      console.log('PASS Owner Calendar Edge width='+width+' count='+n+
        ' visible='+state.visible+' tableH='+state.tableRegionH);
      if(n===0||n===5){
        const screenshot=await cmd('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});
        fs.writeFileSync(path.join(screenshots,'owner-calendar-'+width+'-'+n+'.png'),
          Buffer.from(screenshot.data,'base64'));
      }
    }
  }
  await cmd('Emulation.setDeviceMetricsOverride',
    {width:1440,height:900,deviceScaleFactor:1,mobile:false});
  await load('staff-calendar-10.html');
  const monthStart=await evaluate("document.querySelector('#calendarTitle').textContent.trim()");
  const monthNext=await evaluate("document.querySelector('#calNext').click();document.querySelector('#calendarTitle').textContent.trim()");
  assert.notEqual(monthStart,monthNext,'Next month not working');checks++;
  const monthPrev=await evaluate("document.querySelector('#calPrev').click();document.querySelector('#calendarTitle').textContent.trim()");
  assert.equal(monthStart,monthPrev,'Previous month not working');checks++;
  await evaluate("document.querySelector('#calNext').click();");
  const monthToday=await evaluate("document.querySelector('#calToday').click();document.querySelector('#calendarTitle').textContent.trim()");
  assert.equal(monthStart,monthToday,'Today month not working');checks++;
  console.log('PASS Calendar prev/next/today navigation');
  const before=await evaluate("document.querySelector('#staffUpcomingPageInfo').textContent.trim()");
  assert.equal(before,'Page 1 of 2');
  await evaluate("document.querySelector('#staffUpcomingNext').click();true");
  const afterPage=await evaluate("document.querySelector('#staffUpcomingPageInfo').textContent.trim()");
  assert.equal(afterPage,'Page 2 of 2');checks++;
  const lastDisabled=await evaluate("document.querySelector('#staffUpcomingNext').disabled");
  assert.equal(lastDisabled,true);checks++;
  await evaluate("document.querySelector('#staffUpcomingPrev').click();true");
  assert.equal(await evaluate("document.querySelector('#staffUpcomingPageInfo').textContent.trim()"),
    'Page 1 of 2');checks++;
  console.log('PASS Upcoming list next/previous + boundary disabling');
  const pkgCount=await evaluate("(() => {document.querySelector('[data-package-filter=\"Deluxe Magic\"]').click();return {total:document.querySelector('#staffUpcomingTotal').textContent.trim(),visible:[...document.querySelectorAll('#upcomingTable tbody .js-upcoming-row')].filter(r=>getComputedStyle(r).display!=='none').length,page:document.querySelector('#staffUpcomingPageInfo').textContent.trim(),pressed:document.querySelector('[data-package-filter=\"Deluxe Magic\"]').getAttribute('aria-pressed')};})()");
  assert.equal(pkgCount.total,'5 matching records');
  assert.equal(pkgCount.visible,5);
  assert.equal(pkgCount.page,'Page 1 of 1');
  assert.equal(pkgCount.pressed,'true');checks++;
  const empty=await evaluate("(() => {document.querySelector('[data-package-filter=\"Unused Magic Package\"]').click();return {visible:[...document.querySelectorAll('#upcomingTable tbody .js-upcoming-row')].filter(r=>getComputedStyle(r).display!=='none').length,empty:getComputedStyle(document.querySelector('#upcomingFilteredEmpty')).display!=='none',title:document.querySelector('#staffUpcomingEmptyTitle').textContent.trim(),total:document.querySelector('#staffUpcomingTotal').textContent.trim(),page:document.querySelector('#staffUpcomingPageInfo').textContent.trim()};})()");
  assert.equal(empty.visible,0);
  assert.equal(empty.empty,true);
  assert.equal(empty.title,'No matching upcoming bookings');
  assert.equal(empty.total,'0 matching records');
  assert.equal(empty.page,'Page 1 of 1');checks++;
  const reset=await evaluate("(() => {document.querySelector('[data-package-filter=\"\"]').click();return {count:[...document.querySelectorAll('#upcomingTable tbody .js-upcoming-row')].filter(r=>getComputedStyle(r).display!=='none').length,total:document.querySelector('#staffUpcomingTotal').textContent.trim(),page:document.querySelector('#staffUpcomingPageInfo').textContent.trim(),empty:getComputedStyle(document.querySelector('#upcomingFilteredEmpty')).display!=='none'};})()");
  assert.deepEqual(reset,{count:5,total:'10 matching records',page:'Page 1 of 2',empty:false});checks++;
  console.log('PASS Package filters: five matches, zero-match empty state, reset');
  // Compare computed styles for calendar components that should be identical
  // across Staff and Owner. The extra Owner Settings button is intentionally
  // excluded from the nav layout fingerprint.
  const sharedStyleProbe = () => {
    const fields = ['display','flexDirection','gap','padding','color','backgroundColor',
      'borderTopWidth','borderTopLeftRadius','fontSize','height','minHeight','maxHeight'];
    const selectors = [
      '#calendarPackageChips .package-dot.package-red',
      '.staff-calendar-page','.staff-round1-calendar-layout','.staff-calendar-panel',
      '.staff-calendar-month-row','#calendar .fc-daygrid-day-frame',
      '.staff-upcoming-panel','.staff-final-upcoming-title',
      '.staff-upcoming-table-frame','.staff-upcoming-table thead th',
      '.staff-upcoming-table tbody tr.js-upcoming-row',
      '#calendarPackageChips .package-legend-filter[aria-pressed=true]',
      '#bookingPreviewModal .modal-content','#blockedDateModal .modal-content'
    ];
    return Object.fromEntries(selectors.map(selector => {
      const el=document.querySelector(selector);
      const css=el&&getComputedStyle(el);
      return [selector,css?Object.fromEntries(fields.map(field=>[field,css[field]])):null];
    }));
  };
  for (const width of [1440,390]) {
    await cmd('Emulation.setDeviceMetricsOverride',
      {width,height:900,deviceScaleFactor:1,mobile:false});
    await load('staff-calendar-10.html');
    const staffStyles=await evaluate('('+sharedStyleProbe.toString()+')()');
    await load('owner-calendar-10.html');
    const ownerStyles=await evaluate('('+sharedStyleProbe.toString()+')()');
    const differences=[];
    for(const selector of Object.keys(staffStyles)){
      for(const field of Object.keys(staffStyles[selector]||{})){
        if(staffStyles[selector][field]!==ownerStyles[selector]?.[field])
          differences.push(selector+' '+field+': staff='+staffStyles[selector][field]+
            ' owner='+ownerStyles[selector]?.[field]);
      }
    }
    console.log('CALENDAR_STYLE_PARITY_DIFFERENCES_'+width+': '+JSON.stringify(differences));
    const allowedHeightDifferences=[
      '.staff-calendar-page height:',
      '.staff-round1-calendar-layout height:',
      '.staff-calendar-panel height:'
    ];
    assert.deepEqual(differences.filter(x=>!allowedHeightDifferences.some(prefix=>x.startsWith(prefix))),[],
      'Owner Calendar visual parity: '+JSON.stringify(differences));
    checks++;
  }
  console.log('STAFF_CALENDAR_EDGE_PASS: '+checks+' responsive/navigation/filter checks');
}finally{
  for(const {reject} of pending.values())reject(new Error('Edge closing'));
  pending.clear();
  if(ws && ws.readyState===WebSocket.OPEN)ws.close();
  edge.kill();
  await new Promise(resolve=>server.close(resolve));
}
