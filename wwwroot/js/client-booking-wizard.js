(() => {
  'use strict';
  const form = document.getElementById('clientBookingWizard');
  if (!form) return;
  const $ = id => document.getElementById(id);
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
  function show(index) {
    step = index; form.classList.toggle('is-review', index === 3); panels.forEach((p,i) => p.hidden = i !== index);
    steps.forEach((s,i) => { s.classList.toggle('is-active',i === index); s.classList.toggle('is-done',i < index); if (i === index) s.setAttribute('aria-current','step'); else s.removeAttribute('aria-current'); });
    error(''); panels[index].querySelector('h2').focus();
    if(index === 3) review();
  }
  function valid(panel) { for (const input of panel.querySelectorAll('input,select')) { if(!input.disabled && !input.checkValidity()) { input.reportValidity(); return false; } } return true; }
  async function json(url, options) { const response = await fetch(url, options); if(!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('This check is unavailable. Please try again or sign in again.'); return response.json(); }
  function invalidate() { verifiedKey = ''; venueResult = null; availableIds = []; $('locationResult').textContent = 'Check the updated location and date before continuing.'; summary(); }
  $('PartyVenue').addEventListener('input', () => { venueSelection = ''; ++searchVersion; $('venueResults').replaceChildren(); invalidate(); });
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
      for(const result of results) { const button = document.createElement('button'); button.type = 'button'; button.textContent = result.displayName; button.className = 'cr-search-result'; button.onclick = () => { $('PartyVenue').value = result.displayName; venueSelection = result.displayName; $('venueResults').replaceChildren(); invalidate(); }; $('venueResults').append(button); }
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
      $('locationResult').textContent = `${venue.requiresManualReview ? 'Serviceable — staff review required' : 'Serviceable'} · Estimated distance: ${Number(venue.distanceKm).toFixed(2)} km · Travel fee: ${money(venue.travelFee)}. ${schedule.message}`;
      for(const option of form.querySelectorAll('[data-package-option]')) { const input = option.querySelector('input'); input.disabled = !availableIds.includes(Number(input.value)); option.hidden = input.disabled; if(input.disabled) input.checked = false; }
      if(!selectedPackage()) $('customizePackage').hidden = true;
      summary(); return true;
    } catch(e) { verifiedKey = ''; venueResult = null; $('locationResult').textContent = e.message; error(e.message); summary(); return false; }
    finally { checking = false; $('checkLocation').disabled = false; }
  }
  $('checkLocation').addEventListener('click',checkLocation);
  function renderInclusions(restore = false) {
    const pack = selectedPackage(); $('packageInclusions').replaceChildren(); $('customizePackage').hidden = !pack;
    if(!pack) return;
    for(const inclusion of pack.Inclusions) { const label = document.createElement('label'); label.className = 'cr-check'; const input = document.createElement('input'); input.type = 'checkbox'; input.name = 'RemovedInclusionIds'; input.value = inclusion.Id; input.disabled = !inclusion.IsRemovable; input.checked = restore && inclusion.IsRemovable && restoredRemoved.has(inclusion.Id); input.addEventListener('change', () => { if(removed().length > 2) { input.checked = false; error('You can remove up to two eligible inclusions.'); } else error(''); summary(); }); const span = document.createElement('span'); span.textContent = inclusion.Name + (inclusion.IsRemovable ? ' — select to remove' : ' — included'); label.append(input,span); $('packageInclusions').append(label); }
    summary();
  }
  form.querySelectorAll('[name="ServiceId"]').forEach(r => r.addEventListener('change', () => renderInclusions()));
  function summary() { const pack = selectedPackage(), discount = removed().length ? 2000 : 0; $('summaryPackage').textContent = pack?.Name || 'Choose a package'; $('summaryBase').textContent = pack ? money(pack.Price) : '—'; $('summaryDiscount').textContent = pack ? money(discount) : '—'; $('summaryTravel').textContent = venueResult ? money(venueResult.travelFee) : 'Check venue first'; $('summaryTotal').textContent = pack && venueResult ? money(Math.max(0, pack.Price - discount + venueResult.travelFee)) : '—'; }
  function eventFields() { const type = $('EventType').value, birthday = type === 'Birthday', bridal = type === 'Bridal Shower'; const flags = {theme: !['','Company Events','School Events'].includes(type), celebrant:birthday || bridal, age:birthday}; for(const [name,visible] of Object.entries(flags)) { const box = form.querySelector(`[data-event-field="${name}"]`); box.hidden = !visible; const input = box.querySelector('input'); input.disabled = !visible; input.required = visible; } }
  $('EventType').addEventListener('change',eventFields);
  function review() {
    const container = $('bookingReview'); container.replaceChildren();
    const groups = [['Location & date',0,[['Venue',$('PartyVenue').value],['Date',$('EventDate').value],['Time',$('StartTime').value]]],['Package',1,[['Package',selectedPackage()?.Name || ''],['Removed inclusions',selectedPackage()?.Inclusions.filter(i=>removed().includes(i.Id)).map(i=>i.Name).join(', ') || 'None']]],['Event details',2,[['Event',$('EventType').value],['Motif',$('Motif').value],['Guests',$('PaxCount').value],['Contact',$('ContactPerson').value],['Phone',$('ContactNumber').value]]]];
    for(const [id,label] of [['PartyTheme','Party theme'],['CelebrantName','Celebrant'],['Age','Age']]) if(!$(id).disabled && $(id).value) groups[2][2].push([label,$(id).value]);
    const oldSummary=panels[3].querySelector('.cr-mobile-summary'); if(oldSummary)oldSummary.remove();
    const mobileSummary=document.querySelector('.cr-price-summary').cloneNode(true); mobileSummary.className='cr-card cr-mobile-summary'; mobileSummary.removeAttribute('aria-label'); mobileSummary.querySelectorAll('[id]').forEach(el=>el.removeAttribute('id')); panels[3].insertBefore(mobileSummary,panels[3].querySelector('.cr-terms'));
    for(const [title,index,rows] of groups) { const section=document.createElement('section'); section.className='cr-review-section'; const heading=document.createElement('h3'); heading.textContent=title; const edit=document.createElement('button'); edit.type='button'; edit.className='btn btn-sm btn-outline-secondary'; edit.textContent='Edit'; edit.setAttribute('aria-label','Edit '+title); edit.onclick=()=>show(index); section.append(heading,edit); const dl=document.createElement('dl'); for(const [label,value] of rows) { const row=document.createElement('div'),dt=document.createElement('dt'),dd=document.createElement('dd'); dt.textContent=label; dd.textContent=value; row.append(dt,dd); dl.append(row); } section.append(dl); container.append(section); }
  }
  form.querySelectorAll('[data-next]').forEach(button => button.addEventListener('click',async () => {
    error(''); if(!valid(panels[step])) return;
    button.disabled=true;
    try {
      if(step === 0 && (verifiedKey !== key()) && !await checkLocation()) return;
      if(step === 0 && verifiedKey !== key()) return;
      if(step === 1) { const pack=selectedPackage(); if(!pack) { error('Choose an available package.'); return; } const check=await json(form.dataset.scheduleUrl+'?'+new URLSearchParams({date:$('EventDate').value,startTime:$('StartTime').value,serviceId:pack.Id})); if(!check.available) { error(check.message); return; } if(selectedPackage()?.Id !== pack.Id || verifiedKey !== key()) { error('Your selection changed. Please check it again before continuing.'); return; } }
      show(step+1);
    } catch(e) { error(e.message); } finally { button.disabled=false; }
  }));
  form.querySelectorAll('[data-back]').forEach(button=>button.addEventListener('click',()=>show(step-1)));
  form.addEventListener('submit',event=> { if(step !== 3 || verifiedKey !== key()) { event.preventDefault(); show(0); error('Check your location and date before submitting.'); return; } for(let i=0;i<panels.length;i++){const invalid=[...panels[i].querySelectorAll('input,select')].find(input=>!input.disabled&&!input.checkValidity());if(invalid){event.preventDefault();show(i);invalid.reportValidity();return;}} $('submitBooking').disabled=true; $('submitBooking').textContent='Submitting…'; });
  renderInclusions(true); eventFields(); show(0);
})();
