/* Staff Round 1: presentation controls. No changes to permissions or server writes. */
(()=>{
 "use strict";
 const workspace=document.querySelector("[data-staff-priority]");
 if(workspace){
   const tabs=[...workspace.querySelectorAll('[role="tab"][data-staff-tab]')];
   const panels=[...workspace.querySelectorAll('[role="tabpanel"][data-staff-panel]')];
   function activate(tab,focus=false){
     for(const item of tabs){
       const active=item===tab;
       item.setAttribute("aria-selected",String(active));
       item.tabIndex=active?0:-1;
     }
     for(const panel of panels) panel.hidden=panel.dataset.staffPanel!==tab.dataset.staffTab;
     if(focus)tab.focus();
   }
   tabs.forEach((tab,index)=>{
     tab.addEventListener("click",()=>activate(tab));
     tab.addEventListener("keydown",event=>{
       const direction=event.key==="ArrowRight"?1:event.key==="ArrowLeft"?-1:0;
       if(!direction&&event.key!=="Home"&&event.key!=="End")return;
       event.preventDefault();
       const target=event.key==="Home"?0:event.key==="End"?tabs.length-1:
         (index+direction+tabs.length)%tabs.length;
       activate(tabs[target],true);
     });
   });
 }
 const photoButton=document.querySelector("[data-staff-photo-trigger]");
 const photoInput=document.getElementById("profilePhoto");
 if(photoButton&&photoInput)photoButton.addEventListener("click",()=>photoInput.click());
 function createPagination(table){
   const rows=[...table.querySelectorAll("tbody tr")];
   if(rows.length<=10)return;
   const cards=table.dataset.staffPaginate==="reschedules"?
     [...document.querySelectorAll(".staff-reschedule-cards .staff-reschedule-card")]:[];
   const root=document.createElement("nav");root.className="staff-round1-pager";
   root.setAttribute("aria-label",table.dataset.staffPaginate+" pagination");
   const buttons=document.createElement("div");buttons.className="staff-round1-page-controls";
   const prev=document.createElement("button");prev.type="button";prev.textContent="Previous";
   const position=document.createElement("span");position.setAttribute("aria-live","polite");
   const next=document.createElement("button");next.type="button";next.textContent="Next";
   buttons.append(prev,position,next);root.append(buttons);
   table.closest(".table-responsive")?.after(root);
   let page=0;
   function render(){
     const size=10,total=Math.ceil(rows.length/size);
     page=Math.max(0,Math.min(page,total-1));
     rows.forEach((r,i)=>{r.hidden=(i<size*page||i>=size*(page+1))});
     cards.forEach((card,i)=>{if(i<rows.length)card.hidden=(i<size*page||i>=size*(page+1))});
     position.textContent="Page "+(page+1)+" of "+total+" · "+rows.length+" requests";
     prev.disabled=page===0;next.disabled=page===total-1;
   }
   prev.addEventListener("click",()=>{page--;render()});
   next.addEventListener("click",()=>{page++;render()});
   render();
 }
 document.querySelectorAll("table[data-staff-paginate]").forEach(createPagination);
})();
