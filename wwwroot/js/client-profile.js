(() => {
 'use strict';const form=document.getElementById('hubProfileForm');if(!form)return;
 const file=document.getElementById('profileImage'),preview=document.getElementById('photoPreview'),placeholder=document.getElementById('uploadPlaceholder'),submit=document.getElementById('photoSubmit'),error=document.getElementById('profilePhotoError');let url=null,dirty=false;
 const release=()=>{if(url){URL.revokeObjectURL(url);url=null;}};
 const update=()=>{release();preview.removeAttribute('src');preview.classList.add('d-none');placeholder.classList.remove('d-none');submit.disabled=true;error.hidden=true;file.setCustomValidity('');const image=file.files?.[0];if(!image)return;if(image.size>2*1024*1024||!['image/jpeg','image/png','image/webp'].includes(image.type)){error.textContent='Choose a JPG, PNG, or WEBP image no larger than 2 MB.';error.hidden=false;file.setCustomValidity(error.textContent);return;}url=URL.createObjectURL(image);preview.src=url;preview.classList.remove('d-none');placeholder.classList.add('d-none');submit.disabled=false;};
 file?.addEventListener('change',update);if(submit)submit.disabled=!file?.files?.length;window.addEventListener('pagehide',release);window.addEventListener('pageshow',()=>{if(file?.files?.length)update();});
 form.addEventListener('input',()=>dirty=true);form.addEventListener('submit',()=>{if(form.checkValidity())dirty=false;});window.addEventListener('beforeunload',event=>{if(dirty){event.preventDefault();event.returnValue='';}});
 const password=document.getElementById('profileNewPassword'),confirm=document.getElementById('profileConfirmPassword');const match=()=>confirm.setCustomValidity(confirm.value&&confirm.value!==password.value?'Passwords must match.':'');password?.addEventListener('input',match);confirm?.addEventListener('input',match);
 document.getElementById('passwordModal')?.addEventListener('hidden.bs.modal',()=>{document.querySelector('#passwordModal form').reset();confirm.setCustomValidity('');});
})();
