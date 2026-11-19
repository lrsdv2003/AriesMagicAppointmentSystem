(() => {
  "use strict";
  const venue=document.getElementById("confirmedVenue");
  const latitude=document.getElementById("confirmedLatitude");
  const longitude=document.getElementById("confirmedLongitude");
  const input=document.getElementById("confirmedVenueSearch");
  const action=document.getElementById("confirmedSearch");
  const results=document.getElementById("confirmedPlaces");
  if(!venue||!latitude||!longitude||!input||!action||!results)return;
  let pending;
  venue.addEventListener("input",()=>{latitude.value="";longitude.value="";});
  async function search(){
    pending?.abort();pending=new AbortController();
    const query=input.value.trim();
    results.replaceChildren();
    if(query.length<5){results.textContent="Enter at least 5 characters.";return;}
    results.textContent="Looking for precise locations…";
    try{
      const response=await fetch("/BookingChanges/VenueSuggestions?q="+encodeURIComponent(query),
        {signal:pending.signal,credentials:"same-origin",headers:{"Accept":"application/json"}});
      if(!response.ok)throw Error("Lookup unavailable");
      const places=await response.json();
      results.replaceChildren();
      if(!places.length){results.textContent="No precise address found. You can use a verified map pin below.";return;}
      for(const place of places){
        const button=document.createElement("button");button.type="button";
        button.className="confirmed-change-place";button.textContent=place.name;
        button.addEventListener("click",()=>{
          venue.value=place.name;
          latitude.value=String(place.latitude);
          longitude.value=String(place.longitude);
          results.textContent="Location selected: "+place.name;
        });
        results.append(button);
      }
    }catch(error){
      if(error.name!=="AbortError")results.textContent="Lookup unavailable. Enter a verified venue map pin below.";
    }
  }
  // Prevent repeated proposals while an authenticated POST is in flight.
  // Native form validation still runs; the server rechecks the quote and eligibility.
  const form=document.getElementById("confirmedChangeForm");
  form?.addEventListener("submit",event=>{
    if(!form.checkValidity())return;
    if(form.dataset.submitting==="true"){event.preventDefault();return;}
    form.dataset.submitting="true";
    form.setAttribute("aria-busy","true");
    const submit=form.querySelector(".confirmed-change-submit");
    if(submit){
      submit.disabled=true;
      submit.textContent=submit.dataset.loadingText||"Sending...";
    }
  });
  action.addEventListener("click",search);
  input.addEventListener("keydown",e=>{if(e.key==="Enter"){e.preventDefault();search();}});
})();
