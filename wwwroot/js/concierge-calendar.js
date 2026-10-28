// Progressive inline date picker. Native input and all server validation remain available.
(() => {
 'use strict';
 const host=document.getElementById('rescheduleCalendar'),input=document.getElementById(host?.dataset.calendarFor);if(!host||!input)return;
 const blocked=new Set(JSON.parse(document.getElementById('rescheduleBlockedDates')?.textContent||'[]')),full=new Set();
 const iso=d=>`${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
 const parse=value=>/^\d{4}-\d{2}-\d{2}$/.test(value||'')?new Date(value+'T12:00:00'):null;
 const minimum=parse(input.min),initial=parse(input.value);let cursor=initial&&initial>=minimum?initial:new Date(minimum),month=new Date(cursor.getFullYear(),cursor.getMonth(),1);
 const unavailable=d=>iso(d)<input.min||blocked.has(iso(d))||full.has(iso(d));
 host.hidden=false;host.className='cn-calendar';
 const manual=document.getElementById('manualDate');if(manual)manual.open=input.classList.contains('input-validation-error');
 const header=document.createElement('div');header.className='cn-calendar-header';
 const prev=document.createElement('button'),next=document.createElement('button'),title=document.createElement('h3');
 prev.type=next.type='button';prev.className=next.className='cn-calendar-nav';prev.textContent='‹';next.textContent='›';prev.setAttribute('aria-label','Previous month');next.setAttribute('aria-label','Next month');title.id='conciergeMonth';title.setAttribute('aria-live','polite');header.append(prev,title,next);
 const grid=document.createElement('div');grid.className='cn-calendar-grid';grid.setAttribute('role','grid');grid.setAttribute('aria-labelledby',title.id);
 const legend=document.createElement('p');legend.className='cn-help cn-calendar-legend';legend.textContent='Selected date is burgundy. Unavailable dates are crossed out. Capacity is checked when you select a date.';host.append(header,grid,legend);
 function moveMonth(offset){month=new Date(month.getFullYear(),month.getMonth()+offset,1);cursor=new Date(month);if(cursor<minimum)cursor=new Date(minimum);render();}
 prev.onclick=()=>moveMonth(-1);next.onclick=()=>moveMonth(1);
 function choose(date,focus){input.value=iso(date);input.dispatchEvent(new Event('change',{bubbles:true}));cursor=date;render(focus);}
 function render(focus=false){
  const priorFocus=host.contains(document.activeElement)?document.activeElement.dataset.date:null;
  title.textContent=month.toLocaleDateString('en-PH',{month:'long',year:'numeric'});prev.disabled=new Date(month.getFullYear(),month.getMonth()+1,0)<minimum||month.getFullYear()===minimum.getFullYear()&&month.getMonth()===minimum.getMonth();next.disabled=month.getFullYear()>=9999&&month.getMonth()===11;
  grid.replaceChildren();const labels=document.createElement('div');labels.className='cn-calendar-week';labels.setAttribute('role','row');
  for(const day of ['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday']){const label=document.createElement('span');label.setAttribute('role','columnheader');label.setAttribute('aria-label',day);label.textContent=day.slice(0,2);labels.append(label);}grid.append(labels);
  const start=new Date(month.getFullYear(),month.getMonth(),1),days=new Date(month.getFullYear(),month.getMonth()+1,0).getDate();let row;
  let tab=iso(cursor);if(unavailable(cursor)) {const first=Array.from({length:days},(_,i)=>new Date(month.getFullYear(),month.getMonth(),i+1)).find(d=>!unavailable(d));tab=first?iso(first):'';}
  for(let cell=0;cell<Math.ceil((start.getDay()+days)/7)*7;cell++){
   if(cell%7===0){row=document.createElement('div');row.className='cn-calendar-week';row.setAttribute('role','row');grid.append(row);}
   const box=document.createElement('div');box.setAttribute('role','gridcell');row.append(box);const day=cell-start.getDay()+1;if(day<1||day>days)continue;
   const date=new Date(month.getFullYear(),month.getMonth(),day),value=iso(date),button=document.createElement('button');button.type='button';button.textContent=day;button.dataset.date=value;button.disabled=unavailable(date);button.tabIndex=value===tab?0:-1;
   const selected=value===input.value;box.setAttribute('aria-selected',String(selected));button.className=selected?'is-selected':'';
   button.setAttribute('aria-label',date.toLocaleDateString('en-PH',{weekday:'long',month:'long',day:'numeric',year:'numeric'})+(selected?', selected':'')+(button.disabled?', unavailable':''));
   button.onclick=()=>choose(date,true);button.onkeydown=event=>{
    let target=new Date(date),direction=1;
    if(event.key==='ArrowRight')target.setDate(day+1);else if(event.key==='ArrowLeft'){target.setDate(day-1);direction=-1;}else if(event.key==='ArrowDown')target.setDate(day+7);else if(event.key==='ArrowUp'){target.setDate(day-7);direction=-1;}
    else if(event.key==='Home'){target.setDate(day-date.getDay());direction=1;}else if(event.key==='End'){target.setDate(day+6-date.getDay());direction=-1;}
    else if(event.key==='PageDown'||event.key==='PageUp'){direction=event.key==='PageDown'?1:-1;const m=date.getMonth()+direction;target=new Date(date.getFullYear(),m,Math.min(day,new Date(date.getFullYear(),m+1,0).getDate()));}else return;
    event.preventDefault();if(target<minimum)target=new Date(minimum);let attempts=0;while(unavailable(target)&&attempts++<370)target.setDate(target.getDate()+direction);if(target<minimum||target.getFullYear()>9999||attempts>=370)return;
    cursor=target;month=new Date(target.getFullYear(),target.getMonth(),1);render(true);
   };box.append(button);
  }
  if(focus)grid.querySelector(`[data-date="${iso(cursor)}"]:not(:disabled)`)?.focus();else if(priorFocus)(grid.querySelector(`[data-date="${priorFocus}"]:not(:disabled)`)||grid.querySelector('button[tabindex="0"]'))?.focus();
 }
 input.addEventListener('change',()=>{const date=parse(input.value);if(date&&date>=minimum){cursor=date;month=new Date(date.getFullYear(),date.getMonth(),1);render();}});
 document.getElementById('BookingId')?.addEventListener('change',()=>{full.clear();render();});
 host.addEventListener('concierge-capacity',event=>{const {date,isBlocked,isFull}=event.detail;if(isBlocked)blocked.add(date);else blocked.delete(date);if(isFull)full.add(date);else full.delete(date);render();});
 render();
})();
