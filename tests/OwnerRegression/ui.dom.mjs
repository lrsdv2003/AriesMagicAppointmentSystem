// DOM tests only: no browser, external requests, or production data.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
const {JSDOM}=await import(process.env.JSDOM_MODULE || 'jsdom');
const snapshots=path.join(process.env.TEMP,'AriesOwnerSnapshots');
const root=process.cwd();
let checks=0;
const check=(value,message)=>{ assert(value,message); checks++; };
const tick=()=>new Promise(resolve=>setTimeout(resolve,0));
function fixture(name){
 const dom=new JSDOM(fs.readFileSync(path.join(snapshots,name+'.html'),'utf8'),{url:'http://fixture.test/',runScripts:'outside-only'});
 dom.window.HTMLElement.prototype.scrollTo=function(){};
 return dom;
}
function run(dom,script){dom.window.eval(fs.readFileSync(path.join(root,'wwwroot/js',script),'utf8'));}
for(const name of JSON.parse(fs.readFileSync(path.join(snapshots,'current-fixtures.json'),'utf8'))){
 const dom=fixture(name.slice(0,-5)),d=dom.window.document;
 const ids=[...d.querySelectorAll('[id]')].map(e=>e.id);
 check(new Set(ids).size===ids.length,name+': duplicate IDs');
 const missing=[...d.querySelectorAll('input:not([type=hidden]),select,textarea')].filter(e=>!e.labels?.length&&!e.getAttribute('aria-label')&&!e.getAttribute('aria-labelledby')&&!['submit','button'].includes(e.type));
 check(!missing.length,name+': unnamed fields '+missing.map(e=>e.id||e.name));
 check([...d.querySelectorAll('.modal')].every(e=>e.getAttribute('aria-label')||d.getElementById(e.getAttribute('aria-labelledby'))),name+': dialog lacks a title');
 check(!/â‚|â€|Â·|â†/.test(d.body.textContent),name+': corrupted display characters');
 check([...d.querySelectorAll('form[method=post]')].every(f=>f.querySelector('[name=__RequestVerificationToken]')),name+': missing antiforgery token');
 run(dom,'app-ui.js');
 check([...d.querySelectorAll('.modal')].every(m=>m.parentElement===d.body),name+': clipped dialog');
 dom.window.close();
}
console.log('PASS rendered markup, labels, dialog names, encoding and antiforgery');
for(const name of ['Services-Create','Services-Edit']){
 const dom=fixture(name),w=dom.window,d=w.document;
 run(dom,'package-editor.js');
 const add=d.getElementById('addInclusion'),container=d.getElementById('inclusions-container');
 add.click();add.click();add.click();
 container.children[1].querySelector('[data-remove-inclusion]').click();
 [...container.children].forEach((row,i)=>{
  check(row.querySelector('input[name$=".Name"]').name===`Inclusions[${i}].Name`,name+': noncontiguous model binding');
  check([...row.querySelectorAll('label')].every(l=>d.getElementById(l.htmlFor)),name+': broken inclusion label');
 });
 while(container.children.length)container.firstElementChild.querySelector('[data-remove-inclusion]').click();
 const event=new w.Event('submit',{bubbles:true,cancelable:true});
 d.getElementById('packageEditorForm').dispatchEvent(event);
 check(event.defaultPrevented,name+': empty inclusions accepted');
 dom.window.close();
}
console.log('PASS package add/remove binding and empty validation');
{
 const dom=fixture('Communications-Index-Client'),w=dom.window,d=w.document;
 let posts=0,resolveSend;
 w.fetch=async (url,options)=>{
  if(options?.method==='POST'){ posts++;return new Promise(resolve=>{resolveSend=resolve;}); }
  return {ok:true,json:async()=>[]};
 };
 run(dom,'communication-center.js');
 const form=d.getElementById('commComposer'),input=d.getElementById('commMessageInput');
 input.value='First draft';
 form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 check(posts===1,'Chat submitted the same draft twice');
 input.value='Next draft typed during send';
 resolveSend({ok:true,redirected:false});await tick();await tick();
 check(input.value==='Next draft typed during send','Chat erased the next draft');
 check(!form.hasAttribute('aria-busy'),'Chat stayed busy after send');
 w.fetch=async()=>({ok:false});
 form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await tick();
 check(input.value==='Next draft typed during send','Failed send erased draft');
 check(d.getElementById('commComposerStatus').textContent.includes('try again'),'Failed send has no feedback');
 dom.window.close();
}
console.log('PASS chat duplicate-send prevention, draft preservation and failure feedback');
{
 const dom=fixture('Bookings-Create'),w=dom.window,d=w.document;
 const pack=JSON.parse(d.getElementById('wizardPackages').textContent)[0];
 const catalog=JSON.parse(d.getElementById('wizardPackages').textContent);catalog[0].Price=12345.67;catalog[0].DurationInHours=3;d.getElementById('wizardPackages').textContent=JSON.stringify(catalog);d.getElementById('StartTime').value='23:30';
 w.fetch=async url=>({ok:true,headers:{get:()=> 'application/json'},json:async()=>String(url).includes('SearchVenue')?[{displayName:'Fixture venue'}]:String(url).includes('CheckVenueServiceability')?{success:true,isServiceable:true,distanceKm:12,travelFee:500}:{available:true,availableIds:[pack.Id],message:'Available'}});
 w.requestAnimationFrame=callback=>callback();
 const dialog=d.getElementById('fullBookingTerms');dialog.showModal=()=>dialog.open=true;dialog.close=()=>{dialog.open=false;dialog.dispatchEvent(new w.Event('close'));};
 d.getElementById('clientBookingWizard').requestSubmit=()=>d.getElementById('clientBookingWizard').dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 run(dom,'booking-experience.js');run(dom,'booking-location.js');run(dom,'client-booking-wizard.js');
 d.querySelector('[data-location-method=Manual]').click();
 for(const [id,value] of Object.entries({ManualStreet:'Fixture street',ManualCity:'Pasig',ManualProvince:'Metro Manila'})){d.getElementById(id).value=value;d.getElementById(id).dispatchEvent(new w.Event('input'));}
 d.getElementById('CustomerConfirmedLocation').checked=true;d.getElementById('CustomerConfirmedLocation').dispatchEvent(new w.Event('change'));
 d.querySelector('[data-step="0"] [data-next]').click();await tick();await tick();
 check(!d.querySelector('[data-step="1"]').hidden,'Wizard did not advance after valid location');
 const radio=d.querySelector('[name=ServiceId]');radio.checked=true;radio.dispatchEvent(new w.Event('change'));
 const removals=[...d.querySelectorAll('.ultra-inclusion-toggle')];
 removals.slice(0,3).forEach(input=>{input.checked=false;input.dispatchEvent(new w.Event('change'));});
 check(d.querySelectorAll('[name=RemovedInclusionIds]:not(:disabled)').length===2,'Wizard accepted more than two removals');
 check(d.getElementById('summaryBase').textContent.includes('12,345.67')&&d.getElementById('summaryTotal').textContent.includes('10,845.67'),'Estimate changed fractional model price or applied discount more than once');
 d.querySelector('[data-step="1"] [data-next]').click();await tick();await tick();
 d.getElementById('EventType').value='Company Events';d.getElementById('EventType').dispatchEvent(new w.Event('change'));
 d.getElementById('Motif').value='Coral';
 d.querySelector('[data-step="2"] [data-next]').click();await tick();await tick();
 check(!d.querySelector('[data-step="3"]').hidden,'Wizard did not reach review');
 check(d.querySelectorAll('#summaryContent').length===1&&d.querySelector('#reviewPricing #summaryContent'),'Review did not move the single pricing subtree');
 check(d.getElementById('bookingReview').textContent.includes('2:30')&&d.getElementById('bookingReview').textContent.includes('(next day)'),'Review lost overnight package duration');
 const ids=[...d.querySelectorAll('[id]')].map(e=>e.id);check(new Set(ids).size===ids.length,'Review duplicated IDs');
 const event=new w.Event('submit',{bubbles:true,cancelable:true});d.getElementById('clientBookingWizard').dispatchEvent(event);
 check(event.defaultPrevented,'Wizard accepted unchecked terms');
 check(dialog.open&&d.getElementById('AcceptTerms').value==='false','Terms modal did not require explicit agreement');
 const consent=d.getElementById('termsModalConsent');consent.checked=true;consent.dispatchEvent(new w.Event('change'));d.getElementById('confirmBookingTerms').click();
 const send=new w.Event('submit',{bubbles:true,cancelable:true});d.getElementById('clientBookingWizard').dispatchEvent(send);const immediate=new w.Event('submit',{bubbles:true,cancelable:true});d.getElementById('clientBookingWizard').dispatchEvent(immediate);check(immediate.defaultPrevented,'Same-tick duplicate booking submit accepted');await tick();
 check(d.getElementById('clientBookingWizard').getAttribute('aria-busy')==='true','Booking submit has no real busy state');
 const again=new w.Event('submit',{bubbles:true,cancelable:true});d.getElementById('clientBookingWizard').dispatchEvent(again);check(again.defaultPrevented,'Booking allows duplicate UI submission');
 w.dispatchEvent(new w.Event('pageshow'));check(!d.getElementById('submitBooking').disabled,'Browser restoration prevents retry');
 d.getElementById('clientBookingWizard').addEventListener('submit',e=>e.preventDefault(),{once:true});d.getElementById('clientBookingWizard').dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await tick();check(!d.getElementById('clientBookingWizard').hasAttribute('aria-busy'),'Cancelled booking submit leaves stuck controls');
 d.getElementById('ManualStreet').value='Changed venue';d.getElementById('ManualStreet').dispatchEvent(new w.Event('input'));
 const invalid=new w.Event('submit',{bubbles:true,cancelable:true});d.getElementById('clientBookingWizard').dispatchEvent(invalid);
 check(invalid.defaultPrevented&&!d.querySelector('[data-step="0"]').hidden,'Wizard accepted changed location without rechecking');
 dom.window.close();
}
console.log('PASS booking wizard progression, discount limit, review, terms and location invalidation');
{
 const dom=fixture('Payments-Upload'),w=dom.window,d=w.document;let created=0,revoked=0;
 w.URL.createObjectURL=()=>{created++;return 'blob:synthetic-preview';};w.URL.revokeObjectURL=()=>revoked++;
 run(dom,'booking-experience.js');const input=d.getElementById('ProofImage');input.required=false; // JSDOM cannot populate a native FileList; browser tests cover actual picker validity.
 const files=value=>{Object.defineProperty(input,'files',{configurable:true,value});input.dispatchEvent(new w.Event('change'));};
 files([new w.File([new Uint8Array(5*1024*1024+1)],'large.png',{type:'image/png'})]);check(!input.checkValidity()&&d.querySelector('[data-booking-preview]').hidden,'Oversize proof preview accepted');
 files([new w.File(['text'],'wrong.txt',{type:'text/plain'})]);check(!input.checkValidity(),'Wrong proof MIME accepted');
 files([new w.File(['synthetic'],'<img onerror=unsafe>.png',{type:'image/png'})]);check(input.checkValidity()&&created===1,'Valid file preview not created');check(!d.querySelector('[data-booking-preview] figcaption img'),'Selected filename inserted as HTML');
 check(d.querySelector('[data-booking-preview] [data-booking-remove]')&&d.querySelector('[data-booking-preview] [data-booking-replace]'),'Preview filename update erased change/remove actions');
 files([]);check(revoked===1&&d.querySelector('[data-booking-preview]').hidden,'Private preview URL not released on replacement');
 check(input.getAttribute('aria-describedby').includes('ProofImageError'),'Proof error lacks association');dom.window.close();
}
console.log('PASS booking busy/cancel/restoration and private upload preview type/size/encoding/lifecycle');

{
 const dom=fixture('RescheduleRequests-Create'),w=dom.window,d=w.document;
 w.HTMLElement.prototype.scrollIntoView=function(){};
 const booking=d.getElementById('BookingId'),date=d.getElementById('RequestedDate'),time=d.getElementById('RequestedStartTime');
 w.fetch=async url=>({ok:true,headers:{get:()=> 'application/json'},json:async()=>String(url).includes('GetUnavailableTimeRanges')?[]:{isBlocked:false,isFull:false,remainingSlots:2,maxPerDay:3}});
 run(dom,'booking-experience.js');run(dom,'client-round2-20261108.js');
 d.getElementById('r2ReviewReschedule').click();check(d.getElementById('r2RescheduleReview').hidden,'Incomplete reschedule cannot reach review');
 booking.value='1';booking.dispatchEvent(new w.Event('change'));date.value='2030-06-15';date.dispatchEvent(new w.Event('change'));time.value='15:00';d.getElementById('Reason').value='Synthetic venue conflict';await tick();await tick();
 d.getElementById('r2ReviewReschedule').click();check(!d.getElementById('r2RescheduleReview').hidden&&d.getElementById('r2ResReason').textContent==='Synthetic venue conflict','Reschedule review preserves chosen reason');
 check(d.querySelector('#r2RescheduleSteps [aria-current=step]').textContent.includes('Review'),'Current reschedule step announced');
 d.getElementById('r2BackToSchedule').click();check(!d.getElementById('r2RescheduleEdit').hidden&&date.value==='2030-06-15','Back preserves requested date');
 date.setCustomValidity('Blocked date');d.getElementById('r2ReviewReschedule').click();check(d.getElementById('r2RescheduleReview').hidden,'Blocked date cannot reach review');
 check(date.min&&d.getElementById('clientRescheduleForm').querySelector('[name=__RequestVerificationToken]'),'Native minimum date and antiforgery preserved');
 dom.window.close();
}
console.log('PASS current reschedule edit/review, validation, deliberate request and retained values');

{
 const empty=fixture('Home-Index-Guest'),published=fixture('Home-Index-Guest-published');
 check(!empty.window.document.querySelector('.cx-testimonials'),'Empty homepage feedback leaves an unnecessary section');
 const d=published.window.document,stories=[...d.querySelectorAll('.cx-testimonial')];
 check(stories.length===2,'Approved-feedback presentation was lost');
 check(stories[0].textContent.includes('Consented fixture name'),'Consented public name was lost');
 check(stories.every(x=>x.querySelector('.cx-story-rating')?.getAttribute('aria-label')?.includes('out of 5')),'Approved ratings lost accessible labels');
 check(d.querySelector('.cx-story-photo')?.getAttribute('src')==='/FeedbackMedia/Public?id=701','Approved media route changed');
 check(empty.window.document.querySelector('#gallery .ws-kicker').textContent.trim().startsWith('03 /'),'Empty feedback leaves a section-number gap');
 check(d.querySelector('#gallery .ws-kicker').textContent.trim().startsWith('04 /'),'Published feedback leaves incorrect gallery numbering');
 empty.window.close();published.window.close();
}
console.log('PASS homepage feedback omission, approved reviews, ratings, media and numbering');
{
 const populated=fixture('Home-Packages'),edge=fixture('Home-Packages-edge'),empty=fixture('Home-Packages-empty'),detail=fixture('Home-Package');
 const cards=[...populated.window.document.querySelectorAll('.we-card')];
 check(cards.length===3,'Experiences lost model packages');
 for(const card of cards){
  check(card.querySelectorAll('.we-inclusions li').length===5,'Experiences truncates stored inclusions');
  const id=card.querySelector('h2').id.replace('package-','');
  check(card.querySelector('.we-card-book').getAttribute('href')===`/Bookings/CreateStepOne?serviceId=${id}`,'Booking lost package ID');
  check(card.querySelector('.we-card-details').getAttribute('href')===`/Home/Package?id=${id}`,'Details lost package ID');
 }
 const edited=edge.window.document.querySelector('.we-card');
 check(edited.querySelector('h2').textContent==='Edited <experience> & a longer catalog name','Stored package name was replaced');
 check(edited.querySelector('.we-price strong').textContent==='₱12,345.67','Fractional stored price was rounded');
 check(edited.querySelector('.we-duration').textContent.trim().startsWith('1 hour '),'Single-hour duration lost');
 check(edited.querySelector('.we-description').textContent.includes('<markup> & text'),'Stored description was replaced');
 check(!edited.querySelector('experience,markup,script'),'Catalog content was rendered as markup');
 check(edited.querySelectorAll('.we-inclusions li').length===8,'Long inclusion list was truncated');
 check(edge.window.document.querySelectorAll('.we-card')[1].textContent.includes('Inclusion details are not available'),'Missing inclusions are fabricated');
 check(empty.window.document.querySelector('.we-empty')&&!empty.window.document.querySelector('.we-card'),'Empty catalog is misleading');
 check(detail.window.document.querySelectorAll('.we-detail-inclusions li').length===5,'Detail lost inclusions');
 check(new URL(detail.window.document.querySelector('.we-detail-book').href).searchParams.has('serviceId'),'Detail lost preselected booking route');
 check(detail.window.document.querySelectorAll('.we-faq details').length===4,'Existing booking FAQ was lost');
 for(const dom of [populated,edge,empty,detail])dom.window.close();
}
console.log('PASS Experiences model content, complete inclusions, encoding, prices, empty state and booking links');
{
 const empty=fixture('Home-About-Guest'),published=fixture('Home-About-Guest-published'),d=empty.window.document,p=published.window.document;
 check(d.querySelectorAll('h1').length===1&&d.body.classList.contains('client-about-page'),'About composition or main heading missing');
 check(d.querySelector('.client-nav-links [aria-current=page]')?.textContent==='About','About current navigation is wrong');
 check(!d.querySelector('.wa-feedback'),'About invents an empty testimonial section');
 check(p.querySelectorAll('.wa-feedback-card').length===2,'About loses approved public reviews');
 check(p.querySelector('.wa-feedback-card').textContent.includes('Consented fixture name'),'About loses consented public attribution');
 check(p.querySelector('.wa-feedback-photo')?.getAttribute('src')==='/FeedbackMedia/Public?id=701','About changes approved media authorization route');
 check(d.querySelectorAll('picture source[srcset]').length===6,'About lacks responsive source sets');
 check(d.querySelectorAll('.wa-gallery-item').length===3,'About gallery is not curated');
 check([...d.querySelectorAll('main a[href*="Bookings/"]')].every(x=>x.getAttribute('href')==='/Bookings/CreateStepOne'),'About alters booking entry route');
 check(d.querySelectorAll('.wa-faq details').length===2,'About loses booking FAQ');
 check(!/\b(?:years of experience|award-winning|guaranteed confirmation)\b/i.test(d.querySelector('main').textContent),'About invents credentials or confirmation guarantees');
 empty.window.close();published.window.close();
}
console.log('PASS About responsive media, navigation, real feedback presentation, FAQ and booking links');
{
 const dom=fixture('Home-Gallery-Guest'),d=dom.window.document;
 check(d.querySelectorAll('h1').length===1&&d.body.classList.contains('client-gallery-page'),'Gallery heading/composition missing');
 check(d.querySelector('.client-nav-links [aria-current=page]')?.textContent==='Gallery','Gallery current navigation wrong');
 check(d.querySelectorAll('[data-portfolio-src]').length===7,'Gallery loses curated collection');
 check([...d.querySelectorAll('[data-portfolio-src]')].every(x=>x.dataset.portfolioSrc.startsWith('/images/client/')&&x.dataset.portfolioAlt.length>20),'Gallery exposes non-project/private images or missing descriptions');
 check(d.querySelectorAll('picture source[srcset]').length===7,'Gallery lacks responsive sources');
 check(d.querySelectorAll('img[loading=eager]').length===1,'Gallery eagerly loads too many images');
 check(d.querySelector('.wg-card-fire figcaption').textContent.includes('Monarch / Rio Jones'),'Gallery loses photographer attribution');
 check(d.querySelector('#portfolioViewer')?.getAttribute('aria-labelledby')==='portfolioTitle','Viewer lacks accessible name');
 check(d.querySelector('[data-portfolio-next]')&&d.querySelector('[data-portfolio-prev]')&&d.querySelector('[data-portfolio-close]'),'Viewer controls missing');
 check(d.querySelector('main a[href="/Bookings/CreateStepOne"]'),'Gallery booking entry changed');
 check(d.querySelectorAll('script[src*="gallery-portfolio.js"]').length===1,'Portfolio script missing or duplicated');
 dom.window.close();
}
console.log('PASS Gallery curated sources, credits, semantic viewer, responsive media and booking link');
for(const name of ['Login','Register','VerifyEmail','ForgotPassword','ResetPassword','EmailVerified','AccessDenied']) {
 const dom=fixture('Account-'+name),d=dom.window.document;
 check(d.body.classList.contains('client-auth-page')&&d.querySelectorAll('h1').length===1,name+' lacks scoped auth composition or one H1');
 check([...d.querySelectorAll('input:not([type=hidden])')].every(x=>x.labels?.length),name+' missing field label');
 check([...d.querySelectorAll('input[aria-describedby]')].every(x=>x.getAttribute('aria-describedby').split(' ').every(id=>d.getElementById(id))),name+' error/help association broken');
 check([...d.querySelectorAll('[data-password-target]')].every(x=>x.type==='button'&&x.hidden&&d.getElementById(x.dataset.passwordTarget)?.type==='password'),name+' reveal not progressive or bound');
 if(!['EmailVerified','AccessDenied'].includes(name)){run(dom,'auth-experience.js');check(d.querySelectorAll('[data-password-target][hidden]').length===0,name+' enhanced reveal controls missing');}
 if(name==='Login'){check(!!d.querySelector('input[name=returnUrl]'),'Login loses ReturnUrl');check(d.querySelector('#Password').autocomplete==='current-password'&&d.querySelector('#Email').autocomplete==='username','Login password manager hints wrong');const b=d.querySelector('[data-password-target=Password]');b.click();check(d.querySelector('#Password').type==='text'&&b.getAttribute('aria-pressed')==='true','Reveal fails');b.click();check(d.querySelector('#Password').type==='password','Hide fails');}
 if(name==='Register'){check(['FullName','PhoneNumber','Email','Password','ConfirmPassword'].every(n=>d.querySelector(`[name=${n}]`)),'Registration fields changed');check(d.querySelector('#passwordGuidance').textContent.includes('12'),'Registration password help differs from configured fixture policy');check(d.querySelector('#PhoneNumber').autocomplete==='tel-national','Phone autocomplete missing');}
 if(name==='VerifyEmail'){check(d.querySelectorAll('input[name=Code]').length===1&&!d.querySelector('.otp-digit'),'Verification is not a single bound code input');check(d.querySelector('#Code').autocomplete==='one-time-code'&&d.querySelector('#Code').maxLength===6,'Code UX changes six-digit contract');check(d.querySelectorAll('form').length===3&&d.querySelector('form[action="/Account/ChangeVerificationEmail"] input[name=currentPassword]'),'Verification loses resend/change-email contracts');check(d.querySelector('#expiryCountdown').dataset.expiresAt.endsWith('Z'),'UTC expiry is ambiguous');}
 if(name==='ResetPassword')check(d.querySelector('input[type=hidden][name=Email]')&&d.querySelector('input[type=hidden][name=Token]'),'Reset hidden bindings lost');
 dom.window.close();
}
for(const [name,text] of [['Login-recovery-sent','If an account with that email exists'],['Login-reset-success','reset successfully'],['Login-invalid-link','Invalid password reset request.'],['Login-locked','Your account is locked.']]){const dom=fixture('Account-'+name);check(dom.window.document.querySelector('.au-panel').textContent.includes(text),name+' notice missing');dom.window.close();}
{
 const locked=fixture('Account-VerifyEmail-locked');check(locked.window.document.querySelector('#Code').matches(':disabled')&&locked.window.document.querySelector('#verifyOtpButton').disabled,'Locked verification controls enabled');locked.window.close();
}
console.log('PASS authentication form contracts, progressive controls, state feedback, password guidance and error associations');
for(const name of ['Bookings-ClientDashboard','Bookings-MyBookings','Bookings-ClientDetails','Payments-MyUploads','Profile-Index','Notifications-Index-Client','Communications-Index-Client','Feedback-Index','Feedback-Create','Feedback-Details']){
 const dom=fixture(name),d=dom.window.document;
 check(d.body.classList.contains('client-hub'),name+' lacks Client-only hub shell');
 check(!d.querySelector('.client-navbar-wrap,.cx-account-nav,.client-footer'),name+' duplicates marketing/account navigation');
 const nav=d.querySelector('.hub-sidebar .hub-navigation');check(nav?.querySelectorAll('a').length===4&&nav.querySelectorAll('.hub-nav-group').length===2,name+' lost grouped Client destinations');
 check(d.querySelector('#hubFallback summary')&&d.querySelector('#hubDrawer[data-diagnostics]')===null,name+' loses no-script navigation or exposes diagnostics');
 check(d.querySelector('.hub-account-dropdown form[action="/Account/Logout"][method=post] input[name=__RequestVerificationToken]'),name+' loses protected logout');
 dom.window.close();
}
{
 const dom=fixture('Profile-Index'),d=dom.window.document;
 check(d.querySelector('#hubProfileForm[action="/Profile/Edit"] input[name="Personal.FullName"]')&&
       d.querySelector('#hubProfileForm input[name="Personal.PhoneNumber"]')&&
       d.querySelector('#upInformationHeading'),'Unified Profile inline form and read-only account section lost field contracts');
 check(!d.querySelector('input[name=Role],input[name=IsActive],input[name=EmailConfirmed]'),'Profile introduces privileged fields');
 check(d.querySelector('input[name="Password.NewPassword"]')?.minLength===12,
       'Unified Profile password guidance differs from existing policy');
 const photo=d.querySelector('form[action="/Profile/UploadPhoto"]');
 check(photo?.querySelector('input[type=file][name=photo]')&&photo.enctype==='multipart/form-data',
       'Unified Profile photo upload contract changed');
 dom.window.close();
}
{
 const dom=fixture('Feedback-Create'),w=dom.window,d=w.document,form=d.getElementById('clientFeedbackForm');
 check(d.querySelectorAll('.hub-feedback-extra').length===1&&!d.querySelector('[name=Files]')&&d.body.textContent.includes('temporarily unavailable'),'Feedback-Off must disclose unavailable media without a picker');
 for(const name of ['OverallRating','PerformanceRating','BookingRating']){const input=d.querySelector(`[name=${name}][value="5"]`);input.checked=true;check(input.required,'Required feedback rating became optional');}
 check(d.querySelectorAll('[name=SharingPreference][required]').length===2&&!d.querySelector('[name=SharingPreference]:checked'),'Feedback requires a deliberate public/private decision with no default');
 d.querySelector('[name=SharingPreference][value=Public]').checked=true;
 const data=new w.FormData(form);check(data.get('OverallRating')==='5'&&data.get('SharingPreference')==='Public','Public/private choice remains associated with the feedback form');
 check(d.querySelector('[name=Suggestions]')&&!d.querySelector('[name=MediaConsent]')&&d.querySelector('[name=PublicDisplayName]'),'Feedback-Off preserves text/name sharing without new media consent');
 dom.window.close();
}
console.log('PASS Client hub navigation, original form contracts, role fields, profile policy and feedback disclosures');


{
 const dom=fixture('Bookings-Create'),d=dom.window.document;
 check(d.querySelector('#fullBookingTerms')?.tagName==='DIALOG'&&d.querySelector('#termsModalConsent')?.type==='checkbox','Complete terms require deliberate modal consent');
 check(d.querySelector('#bookingTermsText').textContent.length>500,'Full booking terms are present');
 check(d.querySelector('#AcceptTerms')?.type==='hidden'&&d.querySelector('#AcceptTerms').value==='false'&&!d.querySelector('#termsModalConsent').checked,'Consent is false until the customer explicitly agrees');
 check(d.querySelector('[name=AcceptedTermsVersion]').value==='2026-11-08-v3','Current approved terms version preserved');
 check(!d.querySelector('.cn-package img'),'No photograph assigned to unverified package inclusions');
 dom.window.close();
}
console.log('PASS Midnight Concierge agreement and grouped navigation contracts');

for(const role of ['Staff','Owner']){
 const dom=fixture('Dashboard-'+role),d=dom.window.document,w=dom.window;
 check(d.body.classList.contains('midnight-workspace'),role+': scoped workspace shell');
 check(d.querySelector('link[href*="/css/internal-core.css"]')&&d.querySelector('link[href*="/css/'+role.toLowerCase()+'.css"]'),role+': consolidated workspace stylesheets');
 check(d.querySelector('.staff-round2-header-identity strong')?.textContent===role+' Dashboard',role+': accessible workspace identity');
 check(!d.querySelector('.dashboard-topbar input[type=search]'),role+': no unsupported global search');
 const nav=[...d.querySelectorAll('.sidebar-nav a')].map(a=>a.getAttribute('href'));
 for(const controller of role==='Staff'?['Bookings','Calendar','Services','RescheduleRequests','History']:['Payments','Calendar','Reports','Services','History','Personnel','SystemActivity','TrashHistory'])
  check(nav.some(h=>h.startsWith('/'+controller)),role+': preserves '+controller+' navigation');
 for(const controller of role==='Staff'?['Payments','Reports','UserManagement','SystemActivity','Personnel','TrashHistory']:['UserManagement'])
  check(!nav.some(h=>h.startsWith('/'+controller)),role+': excludes unauthorized '+controller+' navigation');
 check(!d.querySelector('.role-workspace form[method=post]'),role+': dashboard review links do not make instant decisions');
 check(d.querySelector('.sidebar-link.active')?.getAttribute('aria-current')==='page',role+': active destination announced');
 run(dom,'internal-workspaces.js');
 check([...d.querySelectorAll('table th')].every(th=>th.getAttribute('scope')&&th.getAttribute('role')==='columnheader'),role+': responsive table headers retain explicit associations');
 d.getElementById('internalSidebar').dispatchEvent(new w.Event('shown.bs.offcanvas'));
 check(d.querySelector('.ui-menu-toggle').getAttribute('aria-expanded')==='true',role+': drawer expanded state');
 d.getElementById('internalSidebar').dispatchEvent(new w.Event('hidden.bs.offcanvas'));
 check(d.querySelector('.ui-menu-toggle').getAttribute('aria-expanded')==='false',role+': drawer collapsed state');
 dom.window.close();
}
for(const name of ['Home-Index-Guest','Account-Login','Bookings-ClientDashboard','Bookings-Create']){
 const dom=fixture(name),d=dom.window.document;
 check(!d.body.classList.contains('midnight-workspace')&&!d.querySelector('link[href*="/css/internal-core.css"]'),name+': internal theme does not leak into approved Client UI');
 dom.window.close();
}
console.log('PASS internal workspaces role navigation, review-only dashboards, drawer semantics and Client isolation');

{
 const dom=fixture('Personnel-Index'),d=dom.window.document;
 check(d.querySelectorAll('#ownerPersonnelSearch').length===1
     && d.querySelector('#ownerPersonnelSearch').name==='search'
     && !!d.querySelector('[data-personnel-filters]')
     && !!d.querySelector('[data-personnel-refresh]'),
     'Personnel toolbar retains search, role selection and accessible refresh');
 check([...d.querySelectorAll('#accountRole option')].map(x=>x.value).join('|')==='|Owner|Staff|Client',
     'Personnel role dropdown contains only supported three roles plus All Roles');
 check(!d.querySelector('#accountStatus,[name=statusFilter]')
    && !d.body.textContent.includes('Account management audit')
    && ![...d.querySelectorAll('#ownerPersonnelTable thead th')].some(x=>x.textContent.trim()==='Actions'),
     'Personnel excludes retired status filters, audit panel and Actions column');
 check(!!d.querySelector('.staff-data-table-frame[data-staff-table-capacity="5"]')
     && !!d.querySelector('.aries-five-pager[data-page-size="5"]'),
     'Personnel table reuses five-record Staff foundation and pagination');
 check(!d.querySelector('a[href*="EditStaff"]')
     && !d.body.textContent.includes('Edit Staff profile'),
     'Removed Staff profile editing is not visible');
 dom.window.close();
}
{
 const dom=fixture('Personnel-Access'),d=dom.window.document;
 check(!!d.querySelector('form[action="/Personnel/Resend"]') &&
       !!d.querySelector('form[action="/Personnel/Manage"]'),
       'Authorized account lifecycle controls remain on separate Staff Access screen');
 check(!d.querySelector('form[action="/Personnel/EditStaff"]'),
       'Separate lifecycle page does not recreate retired profile editing');
 dom.window.close();
}
{
 const dom=fixture('Calendar-Settings'),d=dom.window.document;
 check(d.querySelectorAll('form[data-admin-confirm]').length>=1,
       'Calendar blocked-date changes retain confirmation');
 check(!!d.querySelector('#calendarLimitForm') && !!d.querySelector('[name=newLimit]'),
       'Calendar date-specific limit editor remains available');
 dom.window.close();
}
console.log(`SUCCESS: ${checks} UI DOM checks passed.`);
