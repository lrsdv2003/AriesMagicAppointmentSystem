(() => {
  'use strict';
  const form=document.getElementById('clientBookingWizard');
  if(!form)return;
  const el=id=>document.getElementById(id);
  const manualIds=['ManualStreet','ManualBarangay','ManualCity','ManualProvince','ManualPostalCode','ManualVenueName'];
  let method=el('VenueSelectionMethod').value||'Search', selected=null, map=null, pin=null, searchSeq=0;
  let onChange=()=>{}, onError=()=>{};
  const selectionField=el('PartyVenue'), confirm=el('CustomerConfirmedLocation');
  const methods={Search:'Search result',MapPin:'Customer map pin',Manual:'Manual address'};
  function invalidate(){onChange();}
  function clearConfirm(){confirm.checked=false;invalidate();}
  function formatManual(){
    return [el('ManualVenueName').value,el('ManualStreet').value,
      el('ManualBarangay').value?'Brgy. '+el('ManualBarangay').value:'',
      el('ManualCity').value,el('ManualProvince').value,el('ManualPostalCode').value]
      .map(x=>x.trim()).filter(Boolean).join(', ');
  }
  function setCoordinates(lat,lon){
    if(!Number.isFinite(Number(lat))||!Number.isFinite(Number(lon)))return;
    el('VenueLatitude').value=Number(lat).toFixed(7);
    el('VenueLongitude').value=Number(lon).toFixed(7);
    el('pinCoordinates').textContent='Selected pin: '+Number(lat).toFixed(5)+', '+Number(lon).toFixed(5);
  }
  function initializeMap(){
    if(map)return;
    if(!window.L){
      el('venueSelectionNotice').textContent='Map is temporarily unavailable. Search an address or use manual entry to continue.';
      return;
    }
    map=L.map('venuePinMap',{scrollWheelZoom:false}).setView([12.8797,121.7740],6);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',{
      maxZoom:19,attribution:'&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap contributors</a>'
    }).addTo(map);
    map.on('click',event=>setPin(event.latlng.lat,event.latlng.lng,true));
  }
  function setPin(lat,lon,changed){
    initializeMap();if(!map)return;
    if(!pin){
      pin=L.marker([lat,lon],{draggable:true,autoPan:true,title:'Selected event venue'}).addTo(map);
      pin.on('dragend',()=>{const p=pin.getLatLng();setPin(p.lat,p.lng,true);});
    }else pin.setLatLng([lat,lon]);
    setCoordinates(lat,lon);map.setView([lat,lon],Math.max(map.getZoom(),15));
    if(changed){
      switchMethod('MapPin',true);
      selected=null;el('mapVenueAddress').value=selectionField.value;
      el('venueSelectionNotice').textContent='Pin moved. Confirm the position before continuing.';
    }
  }
  function showMap(){
    el('venueMapSection').hidden=false;
    initializeMap();requestAnimationFrame(()=>map?.invalidateSize());
  }
  function switchMethod(next,retainCoordinates=false){
    if(!Object.prototype.hasOwnProperty.call(methods,next))return;
    method=next;el('VenueSelectionMethod').value=next;
    form.querySelectorAll('[data-location-panel]').forEach(panel=>panel.hidden=panel.dataset.locationPanel!==next);
    form.querySelectorAll('[data-location-method]').forEach(button=>
      button.setAttribute('aria-pressed',String(button.dataset.locationMethod===next)));
    if(next==='Manual'){
      selectionField.value=formatManual();el('VenueLatitude').value='';el('VenueLongitude').value='';
      el('venueMapSection').hidden=true;selected=null;
    }else if(next==='MapPin'){
      el('mapVenueAddress').value=selectionField.value;showMap();
      if(!retainCoordinates){
        el('VenueLatitude').value='';el('VenueLongitude').value='';
        if(pin){map.removeLayer(pin);pin=null;}
        selected=null;el('pinCoordinates').textContent='Click or tap the map to place your pin.';
      }
    }else{
      el('venueMapSection').hidden=!(retainCoordinates&&el('VenueLatitude').value);
      if(!retainCoordinates){selected=null;el('VenueLatitude').value='';el('VenueLongitude').value='';}
    }
    el('venueResults').replaceChildren();
    el('venueSelectionNotice').textContent=next==='Manual'
      ?'Staff will verify service coverage and any travel fee before payment.'
      :next==='MapPin'?'Place your map pin and enter the matching written address.'
      :'Choose the best search result. You can adjust the pin if necessary.';
    clearConfirm();
  }
  function manualValid(){
    return Boolean(el('ManualCity').value.trim()&&el('ManualProvince').value.trim()&&
      (el('ManualStreet').value.trim()||el('ManualBarangay').value.trim()));
  }
  function canCheck(){
    if(!confirm.checked){onError('Please confirm the selected event location or address.');return false;}
    if(method==='Search'&&(!selected||!el('VenueLatitude').value)){
      onError('Select a search result or use the map/manual address options.');return false;}
    if(method==='MapPin'&&(!el('VenueLatitude').value||selectionField.value.trim().length<5)){
      onError('Place a map pin and provide a detailed written venue address.');return false;}
    if(method==='Manual'&&!manualValid()){
      onError('Enter a city, province, and street/subdivision or barangay.');return false;}
    return true;
  }
  async function search(){
    const query=selectionField.value.trim();
    if(query.length<3){onError('Enter at least three characters to search.');return;}
    const version=++searchSeq;const list=el('venueResults'),button=el('findVenue');
    button.disabled=true;list.textContent='Searching Philippine venues…';onError('');
    const controller=new AbortController();const timer=setTimeout(()=>controller.abort(),14000);
    try{
      const response=await fetch(form.dataset.searchUrl+'?query='+encodeURIComponent(query),{signal:controller.signal});
      if(!response.ok)throw new Error('Location search is temporarily unavailable.');
      const results=await response.json();
      if(version!==searchSeq||method!=='Search')return;
      list.replaceChildren();
      if(!results.length)list.textContent='No matching venue. Try a landmark or enter your address manually.';
      results.forEach(result=>{
        const option=document.createElement('button');option.type='button';option.className='cn-search-result';
        option.textContent=result.displayName+(result.isPrecise?'':' · Approximate area — Staff review');
        option.addEventListener('click',()=>{
          selected=result;selectionField.value=result.displayName;
          list.replaceChildren();showMap();setPin(result.latitude,result.longitude,false);
          el('venueChoice').hidden=false;el('venueChoice').textContent='Selected: '+result.displayName;
          el('venueSelectionNotice').textContent=result.isPrecise?'Confirm this venue or adjust the marker.':
            'Approximate result. Staff must review the location, or you can move the pin.';
          clearConfirm();
        });list.append(option);
      });
    }catch(e){
      if(version===searchSeq){
        list.replaceChildren();
        onError(e.name==='AbortError'?'Location search timed out. Enter the address manually.':
          'Search unavailable. You can still select a pin or enter an address manually.');
      }
    }finally{clearTimeout(timer);button.disabled=false;}
  }
  function init(change,error){
    onChange=change;onError=error;
    form.querySelectorAll('[data-location-method]').forEach(button=>
      button.addEventListener('click',()=>switchMethod(button.dataset.locationMethod)));
    manualIds.forEach(id=>el(id)?.addEventListener('input',()=>{
      if(method==='Manual'){selectionField.value=formatManual();clearConfirm();}
    }));
    el('mapVenueAddress').addEventListener('input',()=>{
      if(method==='MapPin'){selectionField.value=el('mapVenueAddress').value;clearConfirm();}
    });
    selectionField.addEventListener('input',()=>{
      if(method==='Search'){
        selected=null;searchSeq++;el('venueResults').replaceChildren();
        el('venueChoice').hidden=true;el('venueMapSection').hidden=true;
        el('VenueLatitude').value='';el('VenueLongitude').value='';clearConfirm();
      }
    });
    el('VenueDirections').addEventListener('input',invalidate);
    confirm.addEventListener('change',invalidate);
    selectionField.addEventListener('keydown',event=>{
      if(method==='Search'&&event.key==='Enter'){event.preventDefault();search();}
    });
    el('findVenue').addEventListener('click',search);
    switchMethod(method,true);
  }
  function payload(){
    const fields={partyVenue:selectionField.value,venueSelectionMethod:method,
      venueLatitude:el('VenueLatitude').value,venueLongitude:el('VenueLongitude').value};
    for(const id of manualIds)fields[id.charAt(0).toLowerCase()+id.slice(1)]=el(id).value;
    return fields;
  }
  function key(){
    const all=[el('VenueSelectionMethod'),selectionField,el('VenueLatitude'),el('VenueLongitude'),
      el('VenueDirections'),confirm,...manualIds.map(el)];
    return all.map(i=>i.type==='checkbox'?String(i.checked):i.value).join('|');
  }
  window.AriesLocation={init,canCheck,payload,key,method:()=>method,methodLabel:()=>methods[method]};
})();
