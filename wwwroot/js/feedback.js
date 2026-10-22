(() => {
 'use strict';
 const form=document.getElementById('clientFeedbackForm');
 if(!form)return;
 const files=form.querySelector('[name="Files"]'),previews=document.getElementById('feedbackPreviews');
 const status=document.getElementById('feedbackUploadStatus'),progress=document.getElementById('feedbackUploadProgress');
 const errors=document.getElementById('feedbackFormErrors'),submit=form.querySelector('button[type="submit"]');
 let urls=[],sending=false;
 const clear=()=>{urls.forEach(URL.revokeObjectURL);urls=[];previews.replaceChildren();};
 files.addEventListener('change',()=>{
  clear();files.setCustomValidity('');
  const selected=[...files.files];
  if(selected.length>Number(form.dataset.maxFiles)||selected.some(f=>f.size>Number(form.dataset.maxBytes)))
   files.setCustomValidity('Choose up to '+form.dataset.maxFiles+' photos within the stated size limit.');
  for(const file of selected.slice(0,Number(form.dataset.maxFiles))){
   const figure=document.createElement('figure'),caption=document.createElement('figcaption');caption.textContent=file.name;
   if(['image/jpeg','image/png','image/webp'].includes(file.type)){const image=document.createElement('img');image.src=URL.createObjectURL(file);urls.push(image.src);image.alt='Local preview of '+file.name;figure.append(image);}
   figure.append(caption);previews.append(figure);
  }
 });
 window.addEventListener('beforeunload',clear);
 form.addEventListener('submit',event=>{
  if(!window.XMLHttpRequest)return;
  event.preventDefault();
  if(sending||!form.reportValidity())return;
  sending=true;submit.disabled=true;form.setAttribute('aria-busy','true');progress.hidden=false;status.textContent='Uploading your feedback…';
  const request=new XMLHttpRequest();
  request.open('POST',form.action);request.timeout=180000;
  request.upload.onprogress=event=>{if(event.lengthComputable){progress.value=Math.round(event.loaded/event.total*100);status.textContent='Uploading · '+progress.value+'%';}else progress.removeAttribute('value');};
  request.upload.onload=()=>{status.textContent='Upload complete. Waiting for the server to finish validation…';};
  const finish=()=>{sending=false;submit.disabled=false;form.removeAttribute('aria-busy');progress.hidden=true;};
  request.onload=()=>{
   if(request.status>=200&&request.status<300&&new URL(request.responseURL).pathname.includes('/Feedback/Details')){
    clear();window.location.assign(request.responseURL);return;
   }
   const documentResult=new DOMParser().parseFromString(request.responseText,'text/html');
   const summary=documentResult.querySelector('#feedbackFormErrors');
   const message=summary?.textContent.trim()||(new URL(request.responseURL).pathname.includes('/Account/Login')?'Your session expired. Sign in again before sending.':request.status===429?'Too many attempts. Please wait a minute and try again.':request.status===409?'Another submission changed this event. Open My feedback before trying again.':'Your feedback was not saved. Check the form and try again.');
   errors.textContent=message;errors.classList.remove('validation-summary-valid');errors.hidden=false;errors.focus();
   status.textContent='Your answers and selected files are still here.';finish();
  };
  request.onerror=request.ontimeout=request.onabort=()=>{status.textContent='The upload was interrupted. Your answers and selected files are still here. Try again when your connection returns.';finish();};
  request.send(new FormData(form));
 });
})();
