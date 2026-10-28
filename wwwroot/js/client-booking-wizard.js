(() => {
  'use strict';
  const form = document.getElementById('clientBookingWizard');
  if (!form) return;
  const $ = id => document.getElementById(id);
  const terms = $('fullBookingTerms'), termsLink = $('openBookingTerms');
  if (terms?.showModal && termsLink) {
    let previousOverflow;
    termsLink.addEventListener('click',event=>{event.preventDefault();previousOverflow=document.body.style.overflow;terms.showModal();document.body.style.overflow='hidden';});
    terms.querySelectorAll('[data-close-terms]').forEach(button=>button.addEventListener('click',()=>terms.close()));
    terms.addEventListener('click',event=>{if(event.target===terms){const r=terms.getBoundingClientRect();if(event.clientX<r.left||event.clientX>r.right||event.clientY<r.top||event.clientY>r.bottom)terms.close();}});
    terms.addEventListener('close',()=>{document.body.style.overflow=previousOverflow||'';termsLink.focus();});
  }
  const packages = JSON.parse($('wizardPackages').textContent);
  const restoredRemoved = new Set(JSON.parse($('wizardRemoved').textContent).map(Number));
  const panels = [...form.querySelectorAll('[data-step]')];
  const steps = [...form.querySelectorAll('.cr-steps li')];
  const money = value => new Intl.NumberFormat('en-PH', { style:'currency', currency:'PHP' }).format(value);
  let step = 0, venueSelection = '', verifiedKey = '', venueResult = null, availableIds = [], checking = false, searchVersion = 0;
  const key = () => [$('PartyVenue').value, $('EventDate').value, $('StartTime').value].join('|');
  const selectedPackage = () => packages.find(p => p.Id === Number(form.querySelector('[name="ServiceId"]:checked')?.value));
  const removed = () => [...form.querySelectorAll('[name="RemovedInclusionIds"]:checked')].map(i => Number(i.value));
  function error(message) { $('wizardError').textContent = message; $('wizardError').hidden = !message; }
  let furthestStep = 0;
  const estimate = $('bookingEstimate'), quote = $('summaryContent');
  const mobile = window.matchMedia?.('(max-width: 991px)');
  if(estimate && mobile) { estimate.open = !mobile.matches; mobile.addEventListener?.('change',event=>{estimate.open=!event.matches;}); }
  function show(index, focus = true, historyMode = 'push') {
    step = index; furthestStep = Math.max(furthestStep,index);
    form.classList.toggle('is-review', index === 3);form.classList.toggle('is-location', index === 0);
    panels.forEach((p,i) => p.hidden = i !== index);
    steps.forEach((s,i) => { s.classList.toggle('is-active',i === index); s.classList.toggle('is-done',i < index); if(i===index)s.setAttribute('aria-current','step');else s.removeAttribute('aria-current'); });
    if($('conciergeStep'))$('conciergeStep').textContent=`Step ${index+1} of 4 · ${['Location & date','Package','Event details','Review'][index]}`;
    if(estimate && quote){estimate.parentElement.hidden=index===0||index===3;(index===3?$('reviewPricing'):estimate).append(quote);}
    if(historyMode!=='none' && window.history)window.history[historyMode==='replace'?'replaceState':'pushState']({...window.history.state,ariesConciergeStep:index},'');
    error('');if(focus)panels[index].querySelector('h2').focus();
    if(index===3)review();
  }
  window.addEventListener('popstate',event=>{
    const index=event.state?.ariesConciergeStep;
    if(!Number.isInteger(index)||index<0||index>furthestStep)return;
    if(index>0&&verifiedKey!==key()){show(0,true,'replace');error('Check your venue and date again before continuing.');return;}
    show(index,true,'none');
  });
  function valid(panel) { for (const input of panel.querySelectorAll('input,select')) { if(!input.disabled && !input.checkValidity()) { input.setAttribute('aria-invalid','true'); input.reportValidity(); return false; } } return true; }
  async function json(url, options) { const response = await fetch(url, options); if(!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('This check is unavailable. Please try again or sign in again.'); return response.json(); }
  function invalidate() { verifiedKey = ''; venueResult = null; availableIds = []; $('locationResult').textContent = 'Continue to check travel and schedule availability.'; summary(); }
  $('PartyVenue').addEventListener('input', () => { venueSelection = ''; if($('venueChoice'))$('venueChoice').hidden=true; ++searchVersion; $('venueResults').replaceChildren(); invalidate(); });
  ['EventDate','StartTime'].forEach(id => $(id).addEventListener('change', invalidate));
  $('PartyVenue').addEventListener('keydown', event => { if(event.key === 'Enter') { event.preventDefault(); $('findVenue').click(); } });
  $('findVenue').addEventListener('click', async () => {
    const query = $('PartyVenue').value.trim(); if(query.length < 3) { error('Enter at least three characters to search for a venue.'); return; }
    const version = ++searchVersion; $('findVenue').disabled = true; error(''); $('venueResults').textContent = 'Searching…';
    try {
      const results = await json(form.dataset.searchUrl + '?query=' + encodeURIComponent(query));
      if(version !== searchVersion) return;
      $('venueResults').replaceChildren();
      if(!results.length) $('venueResults').textContent = 'No venue found. Try a more complete address.';
      for(const result of results) { const button = document.createElement('button'); button.type = 'button'; button.textContent = result.displayName; button.className = 'cn-search-result'; button.onclick = () => { $('PartyVenue').value = result.displayName; venueSelection = result.displayName; $('venueResults').replaceChildren(); invalidate(); if($('venueChoice')){$('venueChoice').hidden=false;$('venueChoice').textContent='Selected: '+result.displayName;} $('PartyVenue').focus(); }; $('venueResults').append(button); }
    } catch(e) { if(version === searchVersion) { $('venueResults').replaceChildren(); error(e.message); } }
    finally { $('findVenue').disabled = false; }
  });
  async function checkLocation() {
    if(checking) return false;
    if(!valid(panels[0])) return false;
    if(!venueSelection) { error('Search for your venue and select a result so its location can be checked.'); return false; }
    checking = true; $('checkLocation').disabled = true; const checkingKey = key(); $('locationResult').textContent = 'Checking venue, travel estimate, and schedule…'; error('');
    try {
      const data = new URLSearchParams({ partyVenue:venueSelection, eventDate:$('EventDate').value, startTime:$('StartTime').value, __RequestVerificationToken:form.querySelector('[name="__RequestVerificationToken"]').value });
      const [venue,schedule] = await Promise.all([json(form.dataset.venueUrl, { method:'POST',body:data }), json(form.dataset.scheduleUrl + '?' + new URLSearchParams({date:$('EventDate').value,startTime:$('StartTime').value}))]);
      if(checkingKey !== key()) return false;
      if(!venue.success || !venue.isServiceable) throw new Error(venue.serviceabilityReason || venue.message || 'This venue cannot be served. Choose another venue.');
      if(!schedule.available) throw new Error(schedule.message);
      venueResult = venue; availableIds = schedule.availableIds; verifiedKey = checkingKey;
      const minutes = Number(venue.estimatedTravelTimeMinutes);
      const travelTime = Number.isFinite(minutes) && minutes > 0 ? ` · Estimated travel time: ${Math.floor(minutes / 60)} hr ${Math.round(minutes % 60)} min` : '';
      $('locationResult').textContent = `${venue.requiresManualReview ? 'Serviceable — staff review required' : 'Serviceable'} · Estimated distance: ${Number(venue.distanceKm).toFixed(2)} km${travelTime} · Travel fee: ${money(venue.travelFee)}. ${schedule.message}`;
      for(const option of form.querySelectorAll('[data-package-option]')) { const input = option.querySelector('input'); input.disabled = !availableIds.includes(Number(input.value)); option.hidden = input.disabled; if(input.disabled) input.checked = false; }
      if(!selectedPackage()) $('customizePackage').hidden = true;
      if($('checkedSchedule'))$('checkedSchedule').textContent=`${new Date($('EventDate').value+'T12:00:00').toLocaleDateString('en-PH',{month:'short',day:'numeric',year:'numeric'})} · ${new Date($('EventDate').value+'T'+$('StartTime').value).toLocaleTimeString('en-PH',{hour:'numeric',minute:'2-digit'})} · ${venueSelection}`; summary(); return true;
    } catch(e) { verifiedKey = ''; venueResult = null; $('locationResult').textContent = e.message; error(e.message); summary(); return false; }
    finally { checking = false; $('checkLocation').disabled = false; }
  }

  function renderInclusions(restore = false) {
    const pack=selectedPackage();$('packageInclusions').replaceChildren();
    const eligible=pack?.Inclusions.filter(i=>i.IsRemovable)||[];
    $('customizePackage').hidden=!eligible.length;
    for(const inclusion of eligible){
      const label=document.createElement('label');label.className='cn-check';
      const input=document.createElement('input');input.type='checkbox';input.name='RemovedInclusionIds';input.value=inclusion.Id;input.checked=restore&&restoredRemoved.has(inclusion.Id);
      input.addEventListener('change',()=>{if(removed().length>2){input.checked=false;error('You can remove up to two eligible inclusions.');}else error('');summary();});
      const span=document.createElement('span');span.textContent='Remove '+inclusion.Name;label.append(input,span);$('packageInclusions').append(label);
    }
    summary();
  }
  form.querySelectorAll('[name="ServiceId"]').forEach(r => r.addEventListener('change', () => renderInclusions()));
  function summary() { const pack = selectedPackage(), discount = removed().length ? 2000 : 0; $('summaryPackage').textContent = pack?.Name || 'Choose a package'; $('summaryBase').textContent = pack ? money(pack.Price) : '—'; $('summaryDiscount').textContent = pack ? money(discount) : '—'; $('summaryTravel').textContent = venueResult ? money(venueResult.travelFee) : 'Check venue first'; const total=pack&&venueResult?money(Math.max(0,pack.Price-discount+venueResult.travelFee)):'—';$('summaryTotal').textContent=total;if($('summaryPeek'))$('summaryPeek').textContent=pack?total:'Choose an experience'; }
  function eventFields() { const type = $('EventType').value, birthday = type === 'Birthday', bridal = type === 'Bridal Shower'; const flags = {theme: !['','Company Events','School Events'].includes(type), celebrant:birthday || bridal, age:birthday}; for(const [name,visible] of Object.entries(flags)) { const box = form.querySelector(`[data-event-field="${name}"]`); box.hidden = !visible; const input = box.querySelector('input'); input.disabled = !visible; input.required = visible; } }
  $('EventType').addEventListener('change',eventFields);
  function review() {
    const container = $('bookingReview'); container.replaceChildren();
    const date = new Date($('EventDate').value+'T12:00:00');
    const [hour,minute]=$('StartTime').value.split(':').map(Number),startMinutes=hour*60+minute;
    const endMinutes=startMinutes+(selectedPackage()?.DurationInHours||0)*60;
    const clock=value=>new Intl.DateTimeFormat('en-PH',{hour:'numeric',minute:'2-digit',timeZone:'UTC'}).format(new Date(Date.UTC(2000,0,1,0,value)));
    const scheduleTime=clock(startMinutes)+'–'+clock(endMinutes)+(endMinutes>=1440?' (next day)':'');
    const groups = [['Location & date',0,[['Venue',$('PartyVenue').value],['Date',date.toLocaleDateString('en-PH',{year:'numeric',month:'long',day:'numeric'})],['Time',scheduleTime],['Duration',(selectedPackage()?.DurationInHours || 0)+' hours']]],['Package',1,[['Package',selectedPackage()?.Name || ''],['Removed inclusions',selectedPackage()?.Inclusions.filter(i=>removed().includes(i.Id)).map(i=>i.Name).join(', ') || 'None']]],['Event details',2,[['Event',$('EventType').value],['Motif',$('Motif').value],['Guests',$('PaxCount').value],['Contact',$('ContactPerson').value],['Phone',$('ContactNumber').value]]]];
    for(const [id,label] of [['PartyTheme','Party theme'],['CelebrantName','Celebrant'],['Age','Age']]) if(!$(id).disabled && $(id).value) groups[2][2].push([label,$(id).value]);
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
  form.querySelectorAll('[data-back]').forEach(button=>button.addEventListener('click',()=>{if(window.history.state?.ariesConciergeStep===step&&step>0)window.history.back();else show(step-1,true,'replace');}));
  form.addEventListener('submit',event=> { if(form.getAttribute('aria-busy')==='true'){event.preventDefault();return;} if(step !== 3 || verifiedKey !== key()) { event.preventDefault(); show(0); error('Check your location and date before submitting.'); return; } for(let i=0;i<panels.length;i++){const invalid=[...panels[i].querySelectorAll('input,select')].find(input=>!input.disabled&&!input.checkValidity());if(invalid){event.preventDefault();show(i);invalid.reportValidity();return;}} window.BookingExperience?.busy(form,event); });
  renderInclusions(true); eventFields(); show(0,false,'replace');
})();
