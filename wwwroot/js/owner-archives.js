// Owner Archives: refresh server-authoritative rows and pager without changing data.
(() => {
  'use strict';
  const page=document.querySelector('.owner-archives-page');
  if(!page)return;
  const refresh=page.querySelector('[data-owner-archives-refresh]');
  const feedback=page.querySelector('#ownerArchivesRefreshStatus');
  if(!refresh||!feedback)return;
  let busy=false;
  refresh.addEventListener('click',async event=>{
    event.preventDefault();
    if(busy)return;
    busy=true;
    refresh.classList.add('is-loading');
    refresh.setAttribute('aria-disabled','true');
    refresh.setAttribute('aria-busy','true');
    feedback.hidden=false;
    feedback.classList.remove('is-error');
    feedback.textContent='Refreshing archived bookings...';
    try{
      const url=new URL(refresh.href,window.location.href);
      if(url.origin!==window.location.origin)throw new Error('Invalid refresh origin');
      const response=await fetch(url.href,{
        method:'GET',credentials:'same-origin',cache:'no-store',
        headers:{Accept:'text/html','X-Requested-With':'XMLHttpRequest'}
      });
      if(!response.ok||!response.headers.get('content-type')?.includes('text/html')){
        throw new Error('Archive retrieval failed');
      }
      const documentResponse=new DOMParser().parseFromString(await response.text(),'text/html');
      const next=documentResponse.querySelector('#ownerArchivesResults');
      const current=page.querySelector('#ownerArchivesResults');
      if(!next||!current||!next.querySelector('.owner-archives-table-frame')){
        throw new Error('Archive response was incomplete');
      }
      // Server clamping may adjust Page. Carry the canonical query into the next Refresh.
      const nextRefresh=documentResponse.querySelector('[data-owner-archives-refresh]');
      if(nextRefresh?.getAttribute('href')){
        refresh.setAttribute('href',nextRefresh.getAttribute('href'));
      }
      current.replaceWith(document.importNode(next,true));
      feedback.textContent='Archived bookings are up to date.';
    }catch{
      feedback.textContent='Archived bookings could not be refreshed. Check your connection and try again.';
      feedback.classList.add('is-error');
    }finally{
      busy=false;
      refresh.classList.remove('is-loading');
      refresh.removeAttribute('aria-disabled');
      refresh.removeAttribute('aria-busy');
    }
  });
})();