/* Aries Client Round 2 action-workspace enhancements.
   Server remains the authority for eligibility, balances, and persistence. */
(() => {
 "use strict";
 const byId=id=>document.getElementById(id);
 const contexts=(()=>{try{return JSON.parse(byId("clientBookingContext")?.textContent||"[]")}catch{return []}})();
 const context=value=>contexts.find(x=>String(x.Id)===String(value));
 const money=value=>typeof value==="number"&&Number.isFinite(value)?new Intl.NumberFormat("en-PH",{style:"currency",currency:"PHP"}).format(value):"—";
 const dateText=raw=>!raw||!/^\d{4}-\d\d-\d\d$/.test(raw)?"—":new Date(raw+"T12:00:00").toLocaleDateString("en-PH",{month:"short",day:"numeric",year:"numeric"});
 const timeText=raw=>{
   if(!raw||!/^\d\d:\d\d/.test(raw))return "—";
   const dt=new Date("2000-01-01T"+raw.slice(0,5)+":00");
   return Number.isNaN(dt.valueOf())?"—":dt.toLocaleTimeString("en-PH",{hour:"numeric",minute:"2-digit"});
 };
 const write=(id,text)=>{const el=byId(id);if(el)el.textContent=text??"—";};
 const reschedule=byId("clientRescheduleForm");
 if(reschedule){
  const booking=byId("BookingId"),date=byId("RequestedDate"),time=byId("RequestedStartTime"),reason=byId("Reason");
  const first=byId("r2RescheduleEdit"),review=byId("r2RescheduleReview"),progress=[...document.querySelectorAll("#r2RescheduleSteps li")];
  function fillContext(){
   const item=context(booking.value);
   write("r2ContextId",item?.Reference||"Select a booking");
   write("r2ContextDate",item?dateText(item.Date)+" · "+timeText(item.Start):"—");
   write("r2ContextVenue",item?.Venue||"—");
   write("r2ContextPackage",item?.Package||"—");
   return item;
  }
  function showReview(show){
   first.hidden=show;review.hidden=!show;
   progress.forEach((el,i)=>{
    el.classList.toggle("is-active",i===(show?1:0));
    el.classList.toggle("is-done",show&&i===0);
    if(i===(show?1:0))el.setAttribute("aria-current","step");else el.removeAttribute("aria-current");
   });
   (show?review:first).querySelector("h2")?.focus({preventScroll:true});
   reschedule.closest(".r2-reschedule-wizard")?.scrollIntoView({behavior:"smooth",block:"start"});
  }
  booking.addEventListener("change",fillContext);fillContext();
  byId("r2ReviewReschedule")?.addEventListener("click",()=>{
   for(const field of [booking,date,time,reason]){
    if(!field||!field.checkValidity()){field?.reportValidity();return}
   }
   const status=byId("rescheduleAvailability");
   if(status?.getAttribute("aria-busy")==="true"){
    status.textContent="Finish checking availability before reviewing.";return;
   }
   if(date.validity.customError){date.reportValidity();return}
   const item=fillContext();
   write("r2ResBooking",item?.Reference||booking.selectedOptions[0]?.textContent||"—");
   write("r2ResCurrent",item?dateText(item.Date)+" · "+timeText(item.Start)+"–"+timeText(item.End):"—");
   write("r2ResRequested",dateText(date.value)+" · "+timeText(time.value));
   write("r2ResVenue",item?.Venue||"—");
   write("r2ResPackage",item?.Package||"—");
   write("r2ResReason",reason.value.trim()||"—");
   showReview(true);
  });
  byId("r2BackToSchedule")?.addEventListener("click",()=>showReview(false));
  reschedule.addEventListener("submit",event=>{
   if(review.hidden){event.preventDefault();byId("r2ReviewReschedule")?.click()}
  },true);
 }
 const payment=byId("clientPaymentForm");
 if(payment){
  const booking=byId("BookingId");
  const choice=()=>payment.querySelector('input[name="PaymentOption"]:checked')?.value;
  const update=()=>{
   const item=context(booking?.value);
   write("r2PaymentBooking",item?.Reference||"Select a booking");
   write("r2PaymentTotal",money(item?.Total));
   write("r2PaymentVerified",money(item?.Verified));
   write("r2PaymentRemaining",money(item?.Balance));
   const option=item?.PaymentOptions?.find(x=>x.Key===choice());
   write("r2PaymentSelected",option?money(option.Amount):"Choose an option");
   write("r2PaymentRemainingAfter",option?money(option.Remaining):money(item?.Balance));
   const method=byId("PaymentMethod")?.value||"GCash";
   write("r2PaymentMethod",method);
   const qr=document.querySelector(".r2-payment-qr");if(qr)qr.hidden=method!=="GCash";
  };
  booking?.addEventListener("change",()=>requestAnimationFrame(update));
  payment.addEventListener("change",()=>requestAnimationFrame(update));
  byId("continuePayment")?.addEventListener("click",()=>requestAnimationFrame(update));
  byId("backPayment")?.addEventListener("click",()=>requestAnimationFrame(update));
  const options=payment.querySelector(".cn-payment-options");
  if(options)new MutationObserver(()=>requestAnimationFrame(update)).observe(options,{childList:true});
  update();
 }
})();
