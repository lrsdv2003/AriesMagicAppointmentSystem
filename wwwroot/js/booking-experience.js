(() => {
 'use strict';
 if (!document.body.classList.contains('client-booking-page')) return;
 const tracked = new Set(), pending = new WeakSet(), buttonContents = new WeakMap();
 function reset(form) {
  pending.delete(form);
  form.removeAttribute('aria-busy');
  form.querySelectorAll('[data-booking-busy]').forEach(b=>{b.disabled=false;if(buttonContents.has(b))b.replaceChildren(...buttonContents.get(b));else b.textContent=b.dataset.bookingBusy;buttonContents.delete(b);delete b.dataset.bookingBusy;});
 }
 function busy(form,event) {
  if(form.getAttribute('aria-busy')==='true'||pending.has(form)){event.preventDefault();return;}
  tracked.add(form);pending.add(form);
  // Wait until native/jQuery validation and other cancel handlers have completed.
  setTimeout(()=>{if(!pending.has(form))return;pending.delete(form);if(event.defaultPrevented)return;form.setAttribute('aria-busy','true');form.querySelectorAll('button[type=submit]').forEach(b=>{buttonContents.set(b,[...b.childNodes]);b.dataset.bookingBusy=b.textContent;b.disabled=true;b.textContent='Submitting…';});},0);
 }
 function invalid(form) {
  const field=[...form.querySelectorAll('input,select,textarea')].find(x=>!x.disabled&&!x.checkValidity());
  if(field){for(let parent=field.parentElement;parent;parent=parent.parentElement)if(parent.tagName==='DETAILS')parent.open=true;field.setAttribute('aria-invalid','true');field.reportValidity();return true;}return false;
 }
 window.BookingExperience={busy,reset,invalid};
 window.addEventListener('pageshow',()=>tracked.forEach(reset));
 document.querySelectorAll('form').forEach(form=>{
  for(const input of form.querySelectorAll('input:not([type=hidden]),select,textarea')) {
   const error=input.id&&document.getElementById(input.id+'Error');
   if(error)input.setAttribute('aria-describedby', [...new Set([...(input.getAttribute('aria-describedby')||'').split(' ').filter(Boolean),error.id])].join(' '));
   if(input.required)input.setAttribute('aria-required','true');
   if(input.classList.contains('input-validation-error'))input.setAttribute('aria-invalid','true');
   input.addEventListener('input',()=>input.removeAttribute('aria-invalid'));
   input.addEventListener('invalid',()=>{for(let parent=input.parentElement;parent;parent=parent.parentElement)if(parent.tagName==='DETAILS')parent.open=true;input.setAttribute('aria-invalid','true');});
  }
  if(form.matches('[data-booking-submit]'))form.addEventListener('submit',event=>{if(invalid(form)){event.preventDefault();return;}busy(form,event);});
 });
 const summary=document.querySelector('.validation-summary-errors');if(summary){summary.tabIndex=-1;summary.focus();}
 if(window.jQuery)window.jQuery(document).on('invalid-form.validate',(_,validator)=>{for(const e of validator.errorList)e.element.setAttribute('aria-invalid','true');});
 for(const input of document.querySelectorAll('[data-booking-file]')) {
  let url=null;
  const form=input.form,preview=form.querySelector('[data-booking-preview]'),image=preview?.querySelector('img'),name=preview?.querySelector('[data-file-name]')||preview?.querySelector('figcaption');
  const release=()=>{if(url){URL.revokeObjectURL(url);url=null;}};
  const render=()=>{
   release();if(preview)preview.hidden=true;image?.removeAttribute('src');input.setCustomValidity('');
   const file=input.files?.[0];
   if(!file)return;
   if(file.size>5*1024*1024)input.setCustomValidity('Choose an image no larger than 5 MB.');
   else if(!['image/jpeg','image/png','image/webp'].includes(file.type))input.setCustomValidity('Choose a JPG, PNG, or WEBP image.');
   if(input.validationMessage){input.reportValidity();return;}
   if(preview){preview.hidden=false;name.textContent=file.name+' · '+(file.size/1024).toFixed(0)+' KB';url=URL.createObjectURL(file);image.src=url;}
  };
  input.addEventListener('change',render);
  image?.addEventListener('error',()=>{release();image.removeAttribute('src');name.textContent='Preview unavailable. Choose a readable JPG, PNG, or WEBP image.';});
  form.querySelector('[data-booking-replace]')?.addEventListener('click',()=>input.click());
  form.querySelector('[data-booking-remove]')?.addEventListener('click',()=>{input.value='';render();input.focus();});
  window.addEventListener('pagehide',release);window.addEventListener('pageshow',()=>{if(input.files?.length)render();});
 }
 const contexts=JSON.parse(document.getElementById('clientBookingContext')?.textContent||'[]');
 const selectedContext=id=>contexts.find(item=>String(item.Id)===String(id));
 const money=value=>new Intl.NumberFormat('en-PH',{style:'currency',currency:'PHP'}).format(value);
 const payment=document.getElementById('clientPaymentForm');
 if(payment){
  const intro=document.getElementById('paymentInstructions'),proof=document.getElementById('paymentProof'),booking=document.getElementById('BookingId'),steps=[...document.querySelectorAll('.cr-payment-steps li')];
  function show(value,focus=true){intro.hidden=value;proof.hidden=!value;steps.forEach((s,i)=>{s.classList.toggle('is-active',i===(value?1:0));s.classList.toggle('is-done',value&&i===0);if(i===(value?1:0))s.setAttribute('aria-current','step');else s.removeAttribute('aria-current');});if(value){document.getElementById('paymentSelection').textContent=booking.selectedOptions[0]?.textContent||'';}const caption=document.getElementById('paymentStepCaption');if(caption)caption.textContent=value?'Step 2 of 3 · Upload proof':'Step 1 of 3 · Payment instructions';if(focus)(value?proof:intro).querySelector('h2').focus();}
  const updateBalance=()=>{const data=selectedContext(booking.value),dl=document.getElementById('paymentFinancials');if(!dl)return;dl.replaceChildren();if(!data||data.Balance===null)return;for(const [label,amount] of [['Booking total',data.Total],['Verified payments',data.Verified],['Remaining balance',data.Balance]]){const row=document.createElement('div'),dt=document.createElement('dt'),dd=document.createElement('dd');dt.textContent=label;dd.textContent=money(amount);row.append(dt,dd);dl.append(row);}};booking.addEventListener('change',updateBalance);updateBalance();
  document.getElementById('continuePayment').onclick=()=>{if(booking.reportValidity())show(true);};
  document.getElementById('backPayment').onclick=()=>show(false);
  payment.addEventListener('submit',e=>{if(proof.hidden){e.preventDefault();return;}if(invalid(payment)){e.preventDefault();return;}busy(payment,e);});
  if(payment.dataset.hasErrors==='true'&&booking.value)show(true,false);
 }
 const reschedule=document.getElementById('clientRescheduleForm');
 if(reschedule){
  const date=document.getElementById('RequestedDate'),booking=document.getElementById('BookingId'),time=document.getElementById('RequestedStartTime'),status=document.getElementById('rescheduleAvailability'),ranges=document.getElementById('rescheduleRanges');let sequence=0,currentRanges=[];
  const minutes=value=>{const [hour,minute]=value.split(':').map(Number);return hour*60+minute;};
  function showTime(){
   ranges.replaceChildren();for(const range of currentRanges){const p=document.createElement('p');p.className='cn-help';p.textContent='Unavailable: '+range.display+' (includes buffer)';ranges.append(p);}
   const duration=selectedContext(booking.value)?.DurationHours;
   if(!time.value||!duration)return;
   const start=minutes(time.value),end=start+duration*60+60;
   if(currentRanges.some(r=>{const a=minutes(r.start),raw=minutes(r.end),b=raw<=a?raw+1440:raw;return start<b&&end>a;})){const p=document.createElement('p');p.className='cn-time-warning';p.textContent='This time may overlap a confirmed event or its buffer. Choose another time. The server checks the complete schedule when you submit.';ranges.prepend(p);}
  }
  const check=async()=>{
   const version=++sequence;currentRanges=[];ranges.replaceChildren();status.removeAttribute('aria-busy');date.setCustomValidity('');
   const current=selectedContext(booking.value);
   const clock=value=>new Date('2000-01-01T'+value).toLocaleTimeString('en-PH',{hour:'numeric',minute:'2-digit'});
   const currentDate=current?new Date(current.Date+'T12:00:00').toLocaleDateString('en-PH',{month:'short',day:'numeric',year:'numeric'}):'';
   document.getElementById('currentSchedule').textContent=current?`Current: ${currentDate} · ${clock(current.Start)}–${clock(current.End)}${current.EndDate!==current.Date?' ('+current.EndDate+')':''} · ${current.Venue}`:booking.value?booking.selectedOptions[0].textContent:'Select a booking to see its current schedule.';
   document.getElementById('rescheduleDuration').textContent=current?.DurationHours?`${current.DurationHours}-hour experience. Your package stays the same.`:'Your package duration stays the same.';
   if(!booking.value||!date.value||date.value<date.min){status.textContent='Choose a booking and a date at least three days ahead.';return;}
   status.textContent='Checking this date…';status.setAttribute('aria-busy','true');
   try{
    const query=new URLSearchParams({date:date.value,bookingId:booking.value});
    const response=await fetch(reschedule.dataset.availabilityUrl+'?'+query);
    if(!response.ok||!response.headers.get('content-type')?.includes('application/json'))throw Error();
    const data=await response.json();if(version!==sequence)return;
    document.getElementById('rescheduleCalendar')?.dispatchEvent(new CustomEvent('concierge-capacity',{detail:{date:date.value,isBlocked:data.isBlocked,isFull:data.isFull}}));
    if(data.isBlocked||data.isFull){const message=data.isBlocked?'This date is blocked. Choose another date.':'This date is full. Choose another date.';status.textContent=message;date.setCustomValidity(message);return;}
    status.textContent=`${data.remainingSlots} of ${data.maxPerDay} confirmed-booking slots available. Complete time availability is checked when you submit.`;
    const result=await fetch(reschedule.dataset.rangesUrl+'?'+new URLSearchParams({date:date.value,excludeBookingId:booking.value}));
    if(!result.ok||!result.headers.get('content-type')?.includes('application/json'))throw Error();
    const items=await result.json();if(version!==sequence)return;currentRanges=items;showTime();
   }catch{if(version===sequence)status.textContent='Availability could not be checked. Retry below. The server checks the complete schedule when you submit.';}
   finally{if(version===sequence)status.removeAttribute('aria-busy');}
  };
  booking.addEventListener('change',check);date.addEventListener('change',check);time.addEventListener('change',showTime);document.getElementById('checkReschedule').addEventListener('click',check);check();
 }
})();
