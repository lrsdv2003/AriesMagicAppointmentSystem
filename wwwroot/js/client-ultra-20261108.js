/* Ultra Final: payment-window display and workflow navigation protections. */
(()=>{"use strict";
const data=document.getElementById("ultraPaymentDeadlines"),booking=document.getElementById("BookingId"),label=document.getElementById("ultraPaymentDeadline");
if(data&&booking&&label){
 let deadlines={};
 try{deadlines=JSON.parse(data.textContent||"{}");}catch{}
 const render=()=>{
 const val=deadlines[booking.value];
 label.hidden=!val;
 label.textContent=val?"Initial payment proof deadline: "+val+". Upload a valid receipt before the deadline; Owner review may follow.":"";
 };
 booking.addEventListener("change",render);render();
}
for(const link of document.querySelectorAll(".ultra-my-bookings-button")){
 const form=document.querySelector("#clientBookingWizard,#clientPaymentForm,#clientRescheduleForm");
 if(!form)continue;
 let dirty=false;
 form.addEventListener("input",()=>{dirty=true});
 form.addEventListener("change",()=>{dirty=true});
 link.addEventListener("click",event=>{
 if(dirty&&!window.confirm("Leave this form? Unsaved changes in your booking workflow will be lost.")){event.preventDefault();}
 });
}
})();
