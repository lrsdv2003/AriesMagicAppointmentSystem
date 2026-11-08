(() => {
 "use strict";
 if (!document.body.classList.contains("client-hub")) return;
 function setupFilter(searchId,selectId,attribute,emptyId,countId,label) {
   const search=document.getElementById(searchId),select=document.getElementById(selectId);
   if(!search||!select)return;
   const cssAttribute="data-"+attribute.replace(/[A-Z]/g,c=>"-"+c.toLowerCase());
   const cards=[...document.querySelectorAll("["+cssAttribute+"]")];
   function apply(){
     const q=search.value.trim().toLocaleLowerCase(),status=select.value;
     let count=0;
     cards.forEach(card=>{
       const match=(status==="all"||card.dataset[attribute]===status)&&card.textContent.toLocaleLowerCase().includes(q);
       card.hidden=!match;if(match)count++;
     });
     const empty=document.getElementById(emptyId),counter=document.getElementById(countId);
     if(empty)empty.hidden=count>0;
     if(counter)counter.textContent=count+" "+label+(count===1?"":"s")+" shown";
   }
   search.addEventListener("input",apply);
   select.addEventListener("change",apply);
   window.addEventListener("pageshow",apply);
   apply();
 }
 setupFilter("refPaymentSearch","refPaymentStatus","refPaymentStatus","refPaymentEmpty","refPaymentCount","payment");
 setupFilter("refRescheduleSearch","refRescheduleStatus","refRescheduleStatus","refRescheduleEmpty","refRescheduleCount","request");
 const filters=[...document.querySelectorAll("[data-ref-notice-filter]")];
 function matchNotice(row,val){
   const type=row.dataset.noticeCategory;
   return val==="all"||(val==="unread"&&row.classList.contains("notice-unread"))||
      (val==="booking"&&["booking","calendar"].includes(type))||
      (val==="system"&&["system","message"].includes(type))||type===val;
 }
 function applyNotice(value) {
   let count=0;
   document.querySelectorAll("#hubNotices [data-ref-notice]").forEach(row=>{
     row.hidden=!matchNotice(row,value);if(!row.hidden)count++;
   });
   document.querySelectorAll("[data-notice-group]").forEach(group=>{
     group.hidden=![...group.querySelectorAll("[data-ref-notice]")].some(row=>!row.hidden);
   });
   const empty=document.getElementById("hubNoticeEmpty"),counter=document.getElementById("hubNoticeCount");
   if(empty)empty.hidden=count>0;
   if(counter)counter.textContent=count+" notification"+(count===1?"":"s")+" shown";
 }
 filters.forEach(button=>button.addEventListener("click",()=>{
   filters.forEach(b=>b.setAttribute("aria-pressed",String(b===button)));
   applyNotice(button.dataset.refNoticeFilter);
 }));
 const select=document.getElementById("clientNotificationFilter");
 if(select){select.addEventListener("change",()=>applyNotice(select.value));applyNotice(select.value);}
 else if(filters.length)applyNotice("all");
})();