/* Client Final Round: presentation only. Backend validates refund eligibility and amount. */
(()=>{"use strict";
const select=document.getElementById("PaymentId"),amount=document.getElementById("refundAmount");
if(!select||!amount)return;
const selected=document.getElementById("refundSelectedBooking"),selectedAmount=document.getElementById("refundSelectedAmount"),reason=document.getElementById("refundSelectedReason");
function refresh(){
 const option=select.selectedOptions[0],label=option&&option.value?option.textContent:"";
 const parts=label.split(" · ").map(x=>x.trim());
 const peso=parts.find(x=>x.includes("₱"))||"";
 const amountMatch=peso.match(/₱\s*[\d,]+(?:\.\d{2})?/);
 const formatted=amountMatch?.[0]||"Select an eligible payment";
 amount.value=formatted;
 if(selected)selected.textContent=parts.slice(0,2).join(" · ")||"Select a payment";
 if(selectedAmount)selectedAmount.textContent=amountMatch?.[0]||"—";
 if(reason)reason.textContent=parts.slice(3).join(" · ")||"—";
}
select.addEventListener("change",refresh);refresh();
const form=document.getElementById("clientFinalRefundForm");
const button=form?.querySelector('button[type="submit"]');
const originalLabel=button?.textContent||"Submit Refund Request";
let submitting=false;
form?.addEventListener("submit",event=>{
 if(submitting){event.preventDefault();return;}
 if(!form.checkValidity())return;
 if(!window.confirm("Submit this eligible refund request for Owner review? Submission does not guarantee approval.")){
  event.preventDefault();return;
 }
 submitting=true;
 // Do not disable the active submitter until native submission has started.
 // Disabling it synchronously can cancel an otherwise valid browser POST.
 setTimeout(()=>{
  if(event.defaultPrevented){submitting=false;return;}
  if(button){button.disabled=true;button.textContent="Submitting request…";}
 },0);
});
window.addEventListener("pageshow",()=>{
 submitting=false;
 if(button){button.disabled=false;button.textContent=originalLabel;}
});

})();
