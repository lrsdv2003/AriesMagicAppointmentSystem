(() => {
 'use strict';
 const form=document.getElementById('clientFeedbackForm');
 if(!form)return;
 const files=form.querySelector('[name="Files"]'),previews=document.getElementById('feedbackPreviews');
 const status=document.getElementById('feedbackUploadStatus'),progress=document.getElementById('feedbackUploadProgress');
 const errors=document.getElementById('feedbackFormErrors'),submit=document.getElementById('openFeedbackConsent');
 const dialog=document.getElementById('feedbackConsentDialog'),choiceError=document.getElementById('sharingChoiceError');
 const closeDialog=()=>dialog?.close();
 document.getElementById('openFeedbackConsent')?.addEventListener('click',()=>{
  const choices=[...dialog.querySelectorAll('input[name="SharingPreference"]')];
  choices.forEach(choice=>choice.disabled=true);
  const valid=form.reportValidity();
  choices.forEach(choice=>choice.disabled=false);
  if(valid){dialog.showModal();dialog.querySelector('input[type="radio"]')?.focus();}
 });
 document.querySelectorAll('[data-close-feedback-consent]').forEach(button=>button.addEventListener('click',closeDialog));
 document.getElementById('confirmFeedbackConsent')?.addEventListener('click',()=>{
  if(!dialog.querySelector('input[name="SharingPreference"]:checked')){choiceError.hidden=false;dialog.querySelector('fieldset')?.focus();return;}
  choiceError.hidden=true;closeDialog();form.requestSubmit();
 });
 let urls=[],sending=false;
 const clear=()=>{urls.forEach(URL.revokeObjectURL);urls=[];previews.replaceChildren();};
 files.addEventListener('change',()=>{
  clear();files.setCustomValidity('');
  const selected=[...files.files];
  const videos=selected.filter(f=>f.type==='video/mp4'&&f.name.toLowerCase().endsWith('.mp4'));
  const images=selected.filter(f=>['image/jpeg','image/png','image/webp'].includes(f.type));
  if(images.length>Number(form.dataset.maxFiles)||videos.length>1||selected.length!==images.length+videos.length
    ||images.some(f=>f.size>Number(form.dataset.maxBytes))||videos.some(f=>f.size>Number(form.dataset.maxVideoBytes)))
   files.setCustomValidity('Choose up to '+form.dataset.maxFiles+' photos and one MP4 video within the separate size limits.');
  for(const file of selected){
   const figure=document.createElement('figure'),caption=document.createElement('figcaption');caption.textContent=file.name;
   if(['image/jpeg','image/png','image/webp'].includes(file.type)){const image=document.createElement('img');image.src=URL.createObjectURL(file);urls.push(image.src);image.alt='Local preview of '+file.name;figure.append(image);}
   else if(file.type==='video/mp4'&&file.name.toLowerCase().endsWith('.mp4')){const video=document.createElement('video');video.src=URL.createObjectURL(file);urls.push(video.src);video.controls=true;video.preload='metadata';video.setAttribute('aria-label','Local preview of '+file.name);figure.append(video);}
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
