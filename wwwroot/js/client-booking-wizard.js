(() => {
  'use strict';
  const form = document.getElementById('clientBookingWizard');
  if (!form) return;
  const $ = id => document.getElementById(id);
  if (location.hash.startsWith('#venue=') && !$('PartyVenue').value) {
    const venue = new URLSearchParams(location.hash.slice(1)).get('venue');
    if (venue?.length <= 500) $('PartyVenue').value = venue;
  }
  const terms = $('fullBookingTerms'), consent = $('termsModalConsent'), agree = $('confirmBookingTerms');
  let termsAccepted = false, previousOverflow = '';
  const termsText = $('bookingTermsText'), termsScrollHelp = $('termsScrollHelp');
  function updateTermsScrollGate(){
    if(!termsText || !consent || !agree)return;
    const atBottom = termsText.scrollTop + termsText.clientHeight >= termsText.scrollHeight - 4;
    const fitsWithoutScroll = termsText.scrollHeight <= termsText.clientHeight + 4;
    const reachedEnd = atBottom || fitsWithoutScroll;
    consent.disabled = !reachedEnd;
    if(!reachedEnd)consent.checked = false;
    agree.disabled = !reachedEnd || !consent.checked;
    if(termsScrollHelp)termsScrollHelp.hidden = reachedEnd;
  }
  function openTerms(){
    termsAccepted = false; $('AcceptTerms').value = 'false';
    consent.checked = false;consent.disabled = true;agree.disabled = true;
    if(termsText)termsText.scrollTop = 0;
    if(termsScrollHelp)termsScrollHelp.hidden = false;
    previousOverflow = document.body.style.overflow;
    terms.showModal();document.body.style.overflow = 'hidden';
    requestAnimationFrame(updateTermsScrollGate);
  }
  if(terms?.showModal){
    termsText?.addEventListener('scroll',updateTermsScrollGate,{passive:true});
    consent.addEventListener('change',()=>{agree.disabled=consent.disabled||!consent.checked;});
    terms.querySelectorAll('[data-close-terms]').forEach(b=>b.addEventListener('click',()=>terms.close()));
    terms.addEventListener('click',e=>{if(e.target===terms){const r=terms.getBoundingClientRect();if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)terms.close();}});
    terms.addEventListener('close',()=>{
      document.body.style.overflow=previousOverflow||'';
      if(!termsAccepted)$('submitBooking')?.focus();
    });
    agree.addEventListener('click',()=>{
      if(!consent.checked)return;
      termsAccepted = true;
      $('AcceptTerms').value = 'true';
      terms.close();
      form.requestSubmit($('submitBooking'));
    });
  }
  const packages = JSON.parse($('wizardPackages').textContent);
  const eventRules = JSON.parse($('wizardEventRules').textContent);
  const restoredRemoved = new Set(JSON.parse($('wizardRemoved').textContent).map(Number));
  const panels = [...form.querySelectorAll('[data-step]')];
  const steps = [...form.querySelectorAll('.cr-steps li')];
  const money = value => new Intl.NumberFormat('en-PH', { style:'currency', currency:'PHP' }).format(value);
  let step = 0, venueSelection = '', verifiedKey = '', venueResult = null, availableIds = [], checking = false, searchVersion = 0;
  const key = () => [window.AriesLocation.key(),$('EventDate').value,$('StartTime').value].join('|');
  const selectedPackage = () => packages.find(p => p.Id === Number(form.querySelector('[name="ServiceId"]:checked')?.value));
  const removed = () => [...form.querySelectorAll('input[name="RemovedInclusionIds"]:not(:disabled)')].map(i=>Number(i.value));
  function error(message) { $('wizardError').textContent = message; $('wizardError').hidden = !message; }
  let furthestStep = 0;
  const estimate = $('bookingEstimate'), quote = $('summaryContent');
  function show(index, focus = true, historyMode = 'push') {
    step = index; furthestStep = Math.max(furthestStep,index);
    form.classList.toggle('is-review', index === 3);form.classList.toggle('is-location', index === 0);
    panels.forEach((p,i) => p.hidden = i !== index);
    steps.forEach((s,i) => { s.classList.toggle('is-active',i === index); s.classList.toggle('is-done',i < index); if(i===index)s.setAttribute('aria-current','step');else s.removeAttribute('aria-current'); });
    if($('conciergeStep'))$('conciergeStep').textContent=`Step ${index+1} of 4 · ${['Location & date','Package','Event details','Review'][index]}`;
    if(estimate && quote){
      estimate.parentElement.hidden=index===3;
      (index===3?$('reviewPricing'):estimate).append(quote);
      quote.hidden=index===0;
      const eventPreview=$('r2EventPreview');
      if(eventPreview)eventPreview.hidden=index!==0;
      const title=$('r2SummaryTitle');
      if(title)title.textContent=index===0?'Your event':'Your estimate';
      if(index===0)updateEventPreview();
    }
    if(historyMode!=='none' && window.history)window.history[historyMode==='replace'?'replaceState':'pushState']({...window.history.state,ariesConciergeStep:index},'');
    error('');if(focus)panels[index].querySelector('h2').focus();
    if(index===3)review();
  }
  const backDialog = $('bookingBackConfirm');
  let pendingBackState = null, allowBrowserBack = false;
  function openBackConfirmation(targetState){
    pendingBackState = targetState;
    if(backDialog?.showModal)backDialog.showModal();
    else {
      if(window.confirm('Are you sure you want to go back to the previous step? Your current booking progress may be affected.')){
        allowBrowserBack = true;window.history.back();
      }
    }
  }
  $('stayOnBooking')?.addEventListener('click',()=>{pendingBackState=null;backDialog.close();});
  $('confirmBookingBack')?.addEventListener('click',()=>{
    backDialog.close();allowBrowserBack=true;window.history.back();
  });
  window.addEventListener('popstate',event=>{
    if(allowBrowserBack){
      allowBrowserBack=false;
      const index=event.state?.ariesConciergeStep;
      if(Number.isInteger(index)&&index>=0&&index<=furthestStep){
        if(index>0&&verifiedKey!==key()){show(0,true,'none');error('Check your venue and date again before continuing.');return;}
        show(index,true,'none');
      }
      return;
    }
    const targetState=event.state;
    // Restore the current wizard history entry while the user decides. No recursive popstate loop.
    window.history.pushState({...window.history.state,ariesConciergeStep:step,ariesBookingGuard:true},'');
    openBackConfirmation(targetState);
  });
  function valid(panel) { for (const input of panel.querySelectorAll('input,select')) { if(!input.disabled && !input.checkValidity()) { input.setAttribute('aria-invalid','true'); input.reportValidity(); return false; } } return true; }
  async function json(url, options) { const response = await fetch(url, options); if(!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('This check is unavailable. Please try again or sign in again.'); return response.json(); }
  function invalidate() { verifiedKey = ''; venueResult = null; availableIds = []; $('locationResult').textContent = 'Continue to check travel and schedule availability.'; summary(); }
  window.AriesLocation.init(invalidate,error);
  let availabilityRequest = 0;
  const availabilityDialog = $('bookingAvailabilityNotice');
  function closeAvailabilityNotice(){if(availabilityDialog?.open)availabilityDialog.close();}
  $('closeAvailabilityNotice')?.addEventListener('click',closeAvailabilityNotice);
  async function refreshAvailabilityNotice(){
    const date=$('EventDate').value,time=$('StartTime').value;
    const request=++availabilityRequest;
    if(!date||!time)return;
    const status=$('bookingAvailabilityStatus'),message=$('bookingAvailabilityMessage');
    status.textContent='Checking the selected date and time…';
    message.textContent='Submitting a booking request does not guarantee a reservation. Administrative review is required.';
    if(availabilityDialog?.showModal&&!availabilityDialog.open)availabilityDialog.showModal();
    try{
      const result=await json(form.dataset.scheduleUrl+'?'+new URLSearchParams({date,startTime:time}));
      if(request!==availabilityRequest)return;
      if(result.status==='invalid'){
        status.textContent='Date or time needs attention';
      }else if(!result.available){
        status.textContent='Unavailable — this slot cannot be selected';
      }else if(result.hasPendingReview){
        status.textContent='Pending administrative review — not confirmed';
      }else{
        status.textContent='No confirmed or payment-locked conflict found at this time';
      }
      message.textContent=result.message+' All requests remain subject to administrative review and final confirmation.';
    }catch{
      if(request!==availabilityRequest)return;
      status.textContent='Availability could not be verified';
      message.textContent='Please try again. Do not assume the date or time is available until the server confirms it.';
    }
  }
  ['EventDate','StartTime'].forEach(id=>{
    $(id).addEventListener('change',()=>{invalidate();refreshAvailabilityNotice();});
  });
  function updateEventPreview(){
    const eventDate=$('EventDate')?.value;
    const date=eventDate?new Date(eventDate+'T12:00:00'):null;
    const time=$('StartTime')?.value;
    if($('r2EventDate'))$('r2EventDate').textContent=date&&!Number.isNaN(date.getTime())?date.toLocaleDateString('en-PH',{month:'short',day:'numeric',year:'numeric'}):'Choose an event date';
    if($('r2EventTime'))$('r2EventTime').textContent=time||'Choose a start time';
    if($('r2EventVenue'))$('r2EventVenue').textContent=$('PartyVenue')?.value||'Choose an event venue';
    if($('r2EventAvailability'))$('r2EventAvailability').textContent=venueResult?.success
      ?'Schedule checked; subject to administrative confirmation':'Not yet verified';
  }
  for(const id of ['EventDate','StartTime','PartyVenue'])$(id)?.addEventListener('input',updateEventPreview);

  async function checkLocation(){
    if(checking)return false;
    if(!window.AriesLocation.canCheck()||!valid(panels[0]))return false;
    checking=true;$('checkLocation').disabled=true;const checkedKey=key();
    $('locationResult').textContent='Checking venue and schedule…';error('');
    try{
      const payload={...window.AriesLocation.payload(),
        eventDate:$('EventDate').value,startTime:$('StartTime').value,
        __RequestVerificationToken:form.querySelector('[name="__RequestVerificationToken"]').value};
      const venuePromise=json(form.dataset.venueUrl,{method:'POST',body:new URLSearchParams(payload)});
      const schedulePromise=json(form.dataset.scheduleUrl+'?'+new URLSearchParams({
        date:$('EventDate').value,startTime:$('StartTime').value}));
      const [venue,schedule]=await Promise.all([venuePromise,schedulePromise]);
      if(checkedKey!==key())return false;
      if(!venue.success||!venue.isServiceable)
        throw new Error(venue.serviceabilityReason||venue.message||'The location is outside current service coverage.');
      if(!schedule.available)throw new Error(schedule.message);
      venueResult=venue;availableIds=schedule.availableIds;verifiedKey=checkedKey;
      const pending=Boolean(venue.travelFeePending);
      $('locationResult').textContent=(pending?'Location received for Staff review. Travel fee pending. ':
        'Location confirmed by customer. Estimated distance: '+Number(venue.distanceKm).toFixed(2)+
        ' km · Travel fee: '+money(venue.travelFee)+'. ')+schedule.message;
      if($('bookingAvailabilityStatus')){
        $('bookingAvailabilityStatus').textContent=schedule.status==='pending-review'
          ?'Pending administrative review — not confirmed'
          :schedule.available?'No confirmed or payment-locked conflict found at this time':'Unavailable — this slot cannot be selected';
        $('bookingAvailabilityMessage').textContent=schedule.message+' Your booking request is not a confirmed reservation.';
      }
      for(const option of form.querySelectorAll('[data-package-option]')){
        const input=option.querySelector('input');
        input.disabled=!availableIds.includes(Number(input.value));option.hidden=input.disabled;
        if(input.disabled)input.checked=false;
      }
      if(!selectedPackage())$('customizePackage').hidden=true;
      $('checkedSchedule').textContent=
        new Date($('EventDate').value+'T12:00:00').toLocaleDateString('en-PH',
        {month:'short',day:'numeric',year:'numeric'})+' · '+$('StartTime').value+' · '+$('PartyVenue').value;
      summary();updateEventPreview();return true;
    }catch(e){verifiedKey='';venueResult=null;$('locationResult').textContent=e.message;error(e.message);
      summary();updateEventPreview();return false;
    }finally{checking=false;$('checkLocation').disabled=false;}
  }

  function renderInclusions(restore = false) {
    const pack=selectedPackage();$('packageInclusions').replaceChildren();
    const inclusions=pack?.Inclusions||[];
    $('customizePackage').hidden=!inclusions.length;
    for(const inclusion of inclusions){
      const label=document.createElement('label');label.className='ultra-inclusion-item';
      const name=document.createElement('span');name.className='ultra-inclusion-name';name.textContent=inclusion.Name;
      const state=document.createElement('span');state.className='ultra-inclusion-state';
      if(inclusion.IsRemovable){
        const toggle=document.createElement('input');toggle.type='checkbox';toggle.className='ultra-inclusion-toggle';
        toggle.setAttribute('role','switch');toggle.checked=!(restore&&restoredRemoved.has(inclusion.Id));
        toggle.setAttribute('aria-label','Include '+inclusion.Name);
        const hidden=document.createElement('input');hidden.type='hidden';hidden.name='RemovedInclusionIds';hidden.value=inclusion.Id;hidden.disabled=toggle.checked;
        state.textContent=toggle.checked?'Included':'Removed';
        toggle.addEventListener('change',()=>{
          hidden.disabled=toggle.checked;
          if(removed().length>2){toggle.checked=true;hidden.disabled=true;error('You can remove no more than two optional inclusions.');}
          else error('');
          state.textContent=toggle.checked?'Included':'Removed';summary();
        });
        label.append(toggle,hidden);
      }else{
        label.classList.add('ultra-inclusion-locked');
        const icon=document.createElement('i');icon.className='bi bi-check-circle-fill';icon.setAttribute('aria-hidden','true');
        state.textContent='Included · required';label.append(icon);
      }
      label.append(name,state);$('packageInclusions').append(label);
    }
    summary();
  }
  form.querySelectorAll('[name="ServiceId"]').forEach(r => r.addEventListener('change', () => renderInclusions()));
  function summary(){
    const pack=selectedPackage(),discount=removed().length?2000:0;
    const pending=Boolean(venueResult?.travelFeePending);
    $('summaryPackage').textContent=pack?.Name||'Choose a package';
    $('summaryBase').textContent=pack?money(pack.Price):'—';
    $('summaryDiscount').textContent=pack?money(discount):'—';
    $('summaryTravel').textContent=venueResult?(pending?'Pending Staff review':money(venueResult.travelFee)):'Check venue first';
    const subtotal=pack?Math.max(0,pack.Price-discount):null;
    const total=subtotal!==null&&venueResult
      ?money(pending?subtotal:Math.max(0,subtotal+Number(venueResult.travelFee||0))):'—';
    $('summaryTotal').textContent=total;
    $('summaryTotalLabel').textContent=pending?'Estimated subtotal':'Estimated total';
    $('summaryTravelPendingNote').hidden=!(pack&&venueResult&&pending);
    if($('summaryPeek'))$('summaryPeek').textContent=!pack?'Choose an experience':pending?'Travel fee pending':'Estimate ready';
  }

  // The server sends the same eight-category policy used for validation and normalization.
  // Hidden inputs keep temporary draft values only in the browser. They are disabled on POST.
  function eventFields(){
    const type=$('EventType').value;
    const rule=eventRules[type]||null;
    const fields={
      motif: ['Motif',Boolean(rule?.ShowMotif),false],
      theme: ['PartyTheme',Boolean(rule?.ShowTheme),false],
      celebrant: ['CelebrantName',Boolean(rule?.ShowCelebrant),Boolean(rule?.RequireCelebrant)],
      age: ['Age',Boolean(rule?.ShowAge),Boolean(rule?.RequireAge)]
    };
    for(const [name,[id,visible,required]] of Object.entries(fields)){
      const holder=form.querySelector('[data-event-field="'+name+'"]');
      const input=$(id);
      if(!holder||!input)continue;
      holder.hidden=!visible;
      input.disabled=!visible;
      input.required=visible&&required;
      input.setAttribute('aria-required',String(visible&&required));
      if(!visible)input.removeAttribute('aria-invalid');
      input.setCustomValidity('');
    }
  }
  $('EventType').addEventListener('change',eventFields);
  function review() {
    const container = $('bookingReview'); container.replaceChildren();
    const date = new Date($('EventDate').value+'T12:00:00');
    const [hour,minute]=$('StartTime').value.split(':').map(Number),startMinutes=hour*60+minute;
    const endMinutes=startMinutes+(selectedPackage()?.DurationInHours||0)*60;
    const clock=value=>new Intl.DateTimeFormat('en-PH',{hour:'numeric',minute:'2-digit',timeZone:'UTC'}).format(new Date(Date.UTC(2000,0,1,0,value)));
    const scheduleTime=clock(startMinutes)+'–'+clock(endMinutes)+(endMinutes>=1440?' (next day)':'');
    const groups = [['Location & date',0,[['Venue',$('PartyVenue').value],['Selection method',window.AriesLocation.methodLabel()],['Travel assessment',venueResult?.travelFeePending?'Pending Staff confirmation':'Estimate available'],['Nearby directions',$('VenueDirections').value||'None'],['Date',date.toLocaleDateString('en-PH',{year:'numeric',month:'long',day:'numeric'})],['Time',scheduleTime],['Duration',(selectedPackage()?.DurationInHours || 0)+' hours']]],['Package',1,[['Package',selectedPackage()?.Name || ''],['Package price',money(selectedPackage()?.Price||0)],['Customization discount',money(removed().length?2000:0)],['Removed inclusions',selectedPackage()?.Inclusions.filter(i=>removed().includes(i.Id)).map(i=>i.Name).join(', ') || 'None']]],['Event details',2,[['Event',$('EventType').value],['Guests',$('PaxCount').value],['Contact',$('ContactPerson').value],['Phone',$('ContactNumber').value]]]];
    for(const [id,label] of [['Motif','Motif'],['PartyTheme','Party theme'],['CelebrantName','Celebrant'],['Age','Age']]) if(!$(id).disabled && $(id).value) groups[2][2].push([label,$(id).value]);
    for(const [title,index,rows] of groups){
      const section=document.createElement('section');section.className='cn-review-block';
      const header=document.createElement('div');header.className='cn-review-heading';
      const heading=document.createElement('h3');heading.textContent=title;
      const edit=document.createElement('button');edit.type='button';edit.className='cn-edit';edit.textContent='Edit';edit.setAttribute('aria-label','Edit '+title);edit.onclick=()=>show(index);header.append(heading,edit);
      const dl=document.createElement('dl');for(const [label,value] of rows){const row=document.createElement('div'),dt=document.createElement('dt'),dd=document.createElement('dd');dt.textContent=label;dd.textContent=value;row.append(dt,dd);dl.append(row);}
      section.append(header,dl);container.append(section);
    }
  }
  form.querySelectorAll('[data-next]').forEach(button => button.addEventListener('click',async () => {
    error(''); if(!valid(panels[step])) return;
    button.disabled=true; const oldNodes=[...button.childNodes];button.textContent=step===0?'Checking availability…':'Checking…';
    try {
      if(step === 0 && (verifiedKey !== key()) && !await checkLocation()) return;
      if(step === 0 && verifiedKey !== key()) return;
      if(step === 1) { const pack=selectedPackage(); if(!pack) { error('Choose an available package.'); return; } const check=await json(form.dataset.scheduleUrl+'?'+new URLSearchParams({date:$('EventDate').value,startTime:$('StartTime').value,serviceId:pack.Id})); if(!check.available) { error(check.message); return; } if(selectedPackage()?.Id !== pack.Id || verifiedKey !== key()) { error('Your selection changed. Please check it again before continuing.'); return; } }
      show(step+1);
    } catch(e) { error(e.message); } finally { button.disabled=false;button.replaceChildren(...oldNodes); }
  }));
  form.querySelectorAll('[data-back]').forEach(button=>button.addEventListener('click',()=>{
    if(step<=0)return;
    openBackConfirmation({ariesConciergeStep:step-1,ariesBookingGuard:true});
  }));
  form.addEventListener('submit',event=>{
    if(form.getAttribute('aria-busy')==='true'){event.preventDefault();return;}
    if(step!==3||verifiedKey!==key()){event.preventDefault();show(0);error('Check your location and date before submitting.');return;}
    for(let i=0;i<panels.length;i++){
      const invalid=[...panels[i].querySelectorAll('input,select,textarea')]
        .find(input=>!input.disabled&&!input.checkValidity());
      if(invalid){event.preventDefault();show(i);invalid.reportValidity();return;}
    }
    if(!termsAccepted){
      event.preventDefault();
      if(terms?.showModal){openTerms();return;}
      error('The Terms and Conditions dialog is unavailable. Please reload and try again.');return;
    }
    if($('AcceptTerms').value!=='true'){event.preventDefault();error('You must accept the Terms and Conditions.');return;}
    window.BookingExperience?.busy(form,event);
  });
  renderInclusions(true); eventFields(); show(0,false,'replace');
  if(window.history?.replaceState)window.history.replaceState({...window.history.state,ariesConciergeStep:0,ariesBookingGuard:true},'');
  if($('EventDate')?.value&&$('StartTime')?.value)refreshAvailabilityNotice();
})();
