import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {JSDOM} from 'jsdom';

const base=path.join(process.env.TEMP,'AriesOwnerSnapshots');
let checks=0;
const check=(value,label)=>{assert.ok(value,label);checks++;};
const expected=['Booking','Client','Package','Event Date','Booking Value','Collected','Outstanding','Status','Action'];
const data=new Map();
for(const [suffix,state] of [['','populated'],['-empty','empty'],['-filtered','filtered'],['-five','five']]){
 const html=fs.readFileSync(path.join(base,'Reports-Index'+suffix+'.html'),'utf8');
 const dom=new JSDOM(html,{url:'http://owner.test/Reports/Index',runScripts:'outside-only'});
 const d=dom.window.document,frame=d.querySelector('.owner-financial-records-frame');
 const table=d.querySelector('table.owner-financial-records-table'),records=d.querySelector('.owner-report-records-section');
 check(!!records&&!!table&&frame?.classList.contains('staff-data-table-frame'),state+' uses the shared financial records table');
 check(!d.querySelector('.workspace-page-heading'),state+' hides redundant Owner workspace/Financial Summary heading');
 check(d.querySelector('.dashboard-content')?.getAttribute('aria-label')==='Financial Summary',state+' retains accessible page name');
 check(d.querySelector('.dashboard-sidebar a[href*="/Reports"]')!==null,state+' retains Owner Financial Summary navigation');
 check(frame?.getAttribute('data-staff-table-capacity')==='5',state+' opts into shared five-record table');
 check([...table.querySelectorAll('thead th')].map(el=>el.textContent.trim()).join('|')===expected.join('|'),state+' retains all nine financial columns');
 check(table.querySelectorAll('colgroup col').length===9,state+' has one semantic sizing column per financial field');
 check(!!records.querySelector('.staff-data-table-scroll[tabindex="0"]'),state+' has keyboard accessible horizontal scrolling');
 check(!!frame.querySelector(':scope > .aries-five-pager[data-page-size="5"]'),state+' places shared compact pager directly beneath the table');
 check(!!records.querySelector('form[role="search"][data-aries-round3-filter] [name=recordSearch]') &&
       !!records.querySelector('[name=recordStatus]') &&
       !!records.querySelector('input[name=startDate]') &&
       !!records.querySelector('input[name=bookingStatus]'),state+' preserves record search/status and outer report filters');
 check(!!d.querySelector('a[href*="ExportCsv"]')&&!!d.querySelector('button[onclick="window.print()"]'),state+' retains financial CSV and Print');
 check(!!d.querySelector('#revenueTrendChart')&&!!d.querySelector('#paymentStatusChart')&&
       !!d.querySelector('#reportChartData')&&!!d.querySelector('.owner-report-metrics'),state+' preserves financial summary cards and charts');
 check(!records.querySelector('.owner-empty-state'),state+' removed old shrinking outside-table empty placeholder');
 const rows=[...table.querySelectorAll('tbody tr:not(.staff-data-table-empty-row)')];
 if(state==='empty') {
  check(rows.length===0 && table.textContent.includes('No financial records') &&
        table.textContent.includes('No financial records are available to display.'),state+' shows requested default in-table empty state');
 } else if(state==='filtered'){
  check(rows.length===0 && table.textContent.includes('No matching financial records') &&
        table.textContent.includes('Try adjusting your search or filters.') &&
        !!table.querySelector('.staff-data-table-reset'),state+' shows dedicated filtered in-table empty state');
 } else{
  check(rows.length===(state==='five'?5:rows.length)&&rows.length>0,state+' renders real report rows without fillers');
  check(rows.every(row=>[...row.querySelectorAll('td')].length===9 &&
       row.querySelector('td[data-label="Action"] a[href*="Bookings/Details"]')!==null),
       state+' has all nine cells and preserves Details navigation');
  check(rows.every(row=>['Booking Value','Collected','Outstanding'].every(name=>
       row.querySelector('td.owner-financial-amount[data-label="'+name+'"]')!==null)),
       state+' preserves three full-precision monetary fields');
 }
 if(state==='five'){
  check(rows.some(row=>row.textContent.includes('12,500,000.50')),state+' displays a large monetary amount without frontend abbreviation');
  check(rows.some(row=>row.textContent.includes('Very long client name')),state+' retains long client names without hiding details');
 }
 data.set(state,{
  summary:d.querySelector('.owner-report-metrics')?.textContent.replace(/\s+/g,' ').trim(),
  chart:d.querySelector('#reportChartData')?.textContent.replace(/\s+/g,' ').trim(),
  tableText:table.textContent.replace(/\s+/g,' ').trim()
 });
 dom.window.close();
}
check(data.get('empty').summary===data.get('filtered').summary &&
      data.get('five').summary===data.get('filtered').summary,
      'Empty or paginated records never zero/modify the original full-scope financial metrics');
check(data.get('empty').chart===data.get('filtered').chart &&
      data.get('five').chart===data.get('filtered').chart,
      'Chart data remain independent of financial-record table pagination');
console.log('SUCCESS: '+checks+' Owner Financial Summary DOM, monetary, filters, and global-totals checks passed.');
