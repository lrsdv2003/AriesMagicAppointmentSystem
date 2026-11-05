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
for(const name of fs.readdirSync(snapshots).filter(f=>f.endsWith('.html'))){
 const dom=fixture(name.slice(0,-5)),d=dom.window.document;
 const ids=[...d.querySelectorAll('[id]')].map(e=>e.id);
 check(new Set(ids).size===ids.length,name+': duplicate IDs');
 const missing=[...d.querySelectorAll('input:not([type=hidden]),select,textarea')].filter(e=>!e.labels?.length&&!e.getAttribute('aria-label')&&!e.getAttribute('aria-labelledby')&&!['submit','button'].includes(e.type));
 check(!missing.length,name+': unnamed fields '+missing.map(e=>e.id||e.name));
 check([...d.querySelectorAll('.modal')].every(e=>e.getAttribute('aria-label')||d.getElementById(e.getAttribute('aria-labelledby'))),name+': dialog lacks a title');
 check(!/â‚|â€|Â·|â†/.test(d.body.textContent),name+': corrupted display characters');
 check([...d.querySelectorAll('form[method=post]')].every(f=>f.querySelector('[name=__RequestVerificationToken]')),name+': missing antiforgery token');
 if(name==='Communications-Index-Admin.html') check(![...d.querySelectorAll('.comm-chat-header a')].some(a=>a.textContent.includes('View Booking')),'Admin sees a booking action outside its role');
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
 run(dom,'booking-experience.js');run(dom,'client-booking-wizard.js');
 d.getElementById('PartyVenue').value='Fixture';d.getElementById('findVenue').click();await tick();
 d.querySelector('#venueResults button').click();
 d.querySelector('[data-step="0"] [data-next]').click();await tick();await tick();
 check(!d.querySelector('[data-step="1"]').hidden,'Wizard did not advance after valid location');
 const radio=d.querySelector('[name=ServiceId]');radio.checked=true;radio.dispatchEvent(new w.Event('change'));
 const removals=[...d.querySelectorAll('[name=RemovedInclusionIds]:not(:disabled)')];
 removals.slice(0,3).forEach(input=>{input.checked=true;input.dispatchEvent(new w.Event('change'));});
 check(d.querySelectorAll('[name=RemovedInclusionIds]:checked').length===2,'Wizard accepted more than two removals');
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
 d.getElementById('AcceptTerms').checked=true;
 const send=new w.Event('submit',{bubbles:true,cancelable:true});d.getElementById('clientBookingWizard').dispatchEvent(send);const immediate=new w.Event('submit',{bubbles:true,cancelable:true});d.getElementById('clientBookingWizard').dispatchEvent(immediate);check(immediate.defaultPrevented,'Same-tick duplicate booking submit accepted');await tick();
 check(d.getElementById('clientBookingWizard').getAttribute('aria-busy')==='true','Booking submit has no real busy state');
 const again=new w.Event('submit',{bubbles:true,cancelable:true});d.getElementById('clientBookingWizard').dispatchEvent(again);check(again.defaultPrevented,'Booking allows duplicate UI submission');
 w.dispatchEvent(new w.Event('pageshow'));check(!d.getElementById('submitBooking').disabled,'Browser restoration prevents retry');
 d.getElementById('clientBookingWizard').addEventListener('submit',e=>e.preventDefault(),{once:true});d.getElementById('clientBookingWizard').dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await tick();check(!d.getElementById('clientBookingWizard').hasAttribute('aria-busy'),'Cancelled booking submit leaves stuck controls');
 d.getElementById('PartyVenue').value='Changed venue';d.getElementById('PartyVenue').dispatchEvent(new w.Event('input'));
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
 const input=d.getElementById('RequestedDate'),host=d.getElementById('rescheduleCalendar');
 input.min='2030-06-10';input.value='2030-06-15';d.getElementById('rescheduleBlockedDates').textContent='["2030-06-16"]';
 run(dom,'concierge-calendar.js');
 const key=name=>d.activeElement.dispatchEvent(new w.KeyboardEvent('keydown',{key:name,bubbles:true,cancelable:true}));
 const selected=()=>d.activeElement.dataset.date;
 check(!host.hidden&&!d.getElementById('manualDate').open,'Calendar progressive enhancement or native fallback failed');
 check(host.querySelector('[data-date="2030-06-09"]').disabled&&host.querySelector('[data-date="2030-06-16"]').disabled,'Calendar permits date before lead time or known blocked date');
 check(host.querySelectorAll('button[tabindex="0"]').length===1,'Calendar has multiple roving tab stops');
 host.querySelector('[data-date="2030-06-15"]').focus();key('ArrowRight');check(selected()==='2030-06-17','Calendar arrow fails to skip blocked date');
 d.activeElement.click();check(input.value==='2030-06-17','Calendar does not update bound native date');
 host.dispatchEvent(new w.CustomEvent('concierge-capacity',{detail:{date:input.value,isBlocked:false,isFull:false}}));check(selected()==='2030-06-17','Async response loses selected calendar focus');
 host.dispatchEvent(new w.CustomEvent('concierge-capacity',{detail:{date:input.value,isBlocked:false,isFull:true}}));check(host.querySelector('[data-date="2030-06-17"]').disabled&&host.contains(d.activeElement)&&!d.activeElement.disabled,'New full date leaves focus stranded');
 host.dispatchEvent(new w.CustomEvent('concierge-capacity',{detail:{date:'2030-06-17',isBlocked:false,isFull:false}}));check(!host.querySelector('[data-date="2030-06-17"]').disabled,'Refreshed capacity retains stale full-date block');
 input.value='2030-06-16';input.dispatchEvent(new w.Event('change'));host.dispatchEvent(new w.CustomEvent('concierge-capacity',{detail:{date:input.value,isBlocked:false,isFull:false}}));check(!host.querySelector('[data-date="2030-06-16"]').disabled,'Authoritative refresh retains stale blocked-date cache');
 host.querySelector('[data-date="2030-06-17"]').focus();key('PageDown');check(selected()==='2030-07-17','Calendar PageDown changes wrong date');key('PageUp');check(selected()==='2030-06-17','Calendar PageUp loses day');
 key('Home');check(selected()==='2030-06-16','Calendar Home fails');key('End');check(selected()==='2030-06-22','Calendar End fails');
 input.value='2030-06-10';input.dispatchEvent(new w.Event('change'));host.querySelector('[data-date="2030-06-10"]').focus();key('ArrowLeft');check(selected()==='2030-06-10','Calendar keyboard bypasses minimum date');
 host.dispatchEvent(new w.CustomEvent('concierge-capacity',{detail:{date:'2030-06-17',isBlocked:false,isFull:true}}));d.getElementById('BookingId').dispatchEvent(new w.Event('change'));check(!host.querySelector('[data-date="2030-06-17"]').disabled,'Booking change retains other booking capacity cache');
 dom.window.close();
}
console.log('PASS calendar bound date, lead time, blocked/full refresh, keyboard navigation and async focus');

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
 const nav=d.querySelector('.hub-sidebar .hub-navigation');check(nav?.querySelectorAll('a').length===9&&nav.querySelectorAll('.hub-nav-group').length===3,name+' lost grouped Client destinations');
 check(d.querySelector('#hubFallback summary')&&d.querySelector('#hubDrawer[data-diagnostics]')===null,name+' loses no-script navigation or exposes diagnostics');
 check(d.querySelector('.hub-rail-footer form[action="/Account/Logout"][method=post] input[name=__RequestVerificationToken]'),name+' loses protected logout');
 dom.window.close();
}
{
 const dom=fixture('Profile-Index'),d=dom.window.document;
 check(d.querySelector('#hubProfileForm[action="/Profile/Edit"] input[name=FullName]')&&d.querySelector('#profileEmail[name=Email]')&&d.querySelector('#profilePhoneNumber[name=PhoneNumber]'),'Profile inline form lost field contracts');
 check(!d.querySelector('input[name=Role],input[name=IsActive],input[name=EmailConfirmed]'),'Profile introduces privileged fields');
 check(d.querySelector('#profileNewPassword').minLength===12,'Profile password guidance differs from existing policy');
 check(d.querySelector('#photoForm input[type=file]').name==='profileImage'&&d.querySelector('#photoForm').enctype==='multipart/form-data','Profile upload contract changed');
 dom.window.close();
}
{
 const dom=fixture('Feedback-Create'),w=dom.window,d=w.document,form=d.getElementById('clientFeedbackForm');
 check(d.querySelectorAll('.hub-feedback-extra').length===2,'Optional media/sharing disclosure missing');
 for(const name of ['OverallRating','PerformanceRating','BookingRating']){const input=d.querySelector(`[name=${name}][value="5"]`);input.checked=true;check(input.required,'Required feedback rating became optional');}
 d.querySelector('[name=TextConsent][type=checkbox]').checked=true;
 const data=new w.FormData(form);check(data.get('OverallRating')==='5'&&data.get('TextConsent')==='true','Closed disclosure drops rating/consent form values');
 check(d.querySelector('[name=Suggestions]')&&d.querySelector('[name=MediaConsent]')&&d.querySelector('[name=PublicDisplayName]'),'Feedback loses private suggestions or sharing choices');
 dom.window.close();
}
console.log('PASS Client hub navigation, original form contracts, role fields, profile policy and feedback disclosures');


{
 const dom=fixture('Bookings-Create'),d=dom.window.document;
 check(d.querySelector('.cn-terms')?.tagName==='SECTION'&&!d.querySelector('.cn-terms details,.cn-terms summary'),'Primary agreement must stay permanently visible');
 check(d.querySelectorAll('.cn-terms-clauses h3').length===6,'All existing terms clauses remain visible');
 check(d.querySelector('#openBookingTerms')?.getAttribute('href')==='#fullBookingTerms'&&d.querySelector('#fullBookingTerms')?.tagName==='DIALOG','Full terms remain available before agreement');
 check(d.querySelector('#AcceptTerms')?.required&&!d.querySelector('#AcceptTerms').checked,'Consent remains an explicit required unchecked choice');
 check(d.querySelector('.cn-terms-clauses').textContent.replace(/\s+/g,' ').trim()===d.querySelector('.cn-terms-full-text').textContent.replace(/\s+/g,' ').trim(),'Inline and full terms use identical source text');
 check(!d.querySelector('.cn-package img'),'No photograph assigned to unverified package inclusions');
 dom.window.close();
}
console.log('PASS Midnight Concierge agreement and grouped navigation contracts');

for(const role of ['Staff','Admin','Owner']){
 const dom=fixture('Dashboard-'+role),d=dom.window.document,w=dom.window;
 check(d.body.classList.contains('midnight-workspace'),role+': scoped workspace shell');
 check(d.querySelector('link[href*="internal-workspaces.css"]'),role+': shared workspace stylesheet');
 check(d.querySelector('h1')?.textContent===role+' Dashboard',role+': clear page heading');
 check(!d.querySelector('.dashboard-topbar input[type=search]'),role+': no unsupported global search');
 const nav=[...d.querySelectorAll('.sidebar-nav a')].map(a=>a.getAttribute('href'));
 for(const controller of role==='Staff'?['Bookings','Calendar','Services','RescheduleRequests','History','Testimonials']:role==='Admin'?['UserManagement','Calendar','Services','TrashHistory','SystemActivity']:['Payments','Bookings','Calendar','Reports','Services','History'])
  check(nav.some(h=>h.startsWith('/'+controller)),role+': preserves '+controller+' navigation');
 for(const controller of role==='Admin'?['Payments','Reports','Bookings','History']:role==='Staff'?['Payments','Reports','UserManagement','SystemActivity']:['UserManagement','SystemActivity'])
  check(!nav.some(h=>h.startsWith('/'+controller)),role+': excludes unauthorized '+controller+' navigation');
 check(!d.querySelector('.role-workspace form[method=post]'),role+': dashboard review links do not make instant decisions');
 check(d.querySelector('.sidebar-link.active')?.getAttribute('aria-current')==='page',role+': active destination announced');
 run(dom,'internal-workspaces.js');
 check([...d.querySelectorAll('table th')].every(th=>th.getAttribute('scope')&&th.getAttribute('role')==='columnheader'),role+': responsive table headers retain explicit associations');
 d.getElementById('internalSidebar').dispatchEvent(new w.Event('shown.bs.offcanvas'));
 check(d.querySelector('.ui-menu-toggle').getAttribute('aria-expanded')==='true',role+': drawer expanded state');
 d.getElementById('internalSidebar').dispatchEvent(new w.Event('hidden.bs.offcanvas'));
 check(d.querySelector('.ui-menu-toggle').getAttribute('aria-expanded')==='false',role+': drawer collapsed state');
 if(role==='Staff'){
  const buttons=[...d.querySelectorAll('[data-workspace-date]')];
  check(buttons.filter(b=>b.tabIndex===0).length===1,'Calendar has one tab entry');
  const first=buttons[0];first.click();
  check(first.getAttribute('aria-pressed')==='true'&&d.querySelectorAll('[data-workspace-date][aria-pressed=true]').length===1,'Calendar date selection is exclusive');
  first.dispatchEvent(new w.KeyboardEvent('keydown',{key:'ArrowRight',bubbles:true,cancelable:true}));
  check(d.activeElement===buttons[1]&&buttons[1].getAttribute('aria-pressed')==='true','Calendar supports keyboard date selection');
  check(d.querySelector('[data-workspace-calendar-status]').textContent===buttons[1].getAttribute('aria-label'),'Calendar announces the selected date and count');
 }
 dom.window.close();
}
for(const name of ['Home-Index-Guest','Account-Login','Bookings-ClientDashboard','Bookings-Create']){
 const dom=fixture(name),d=dom.window.document;
 check(!d.body.classList.contains('midnight-workspace')&&!d.querySelector('link[href*="internal-workspaces.css"]'),name+': internal theme does not leak into approved Client UI');
 dom.window.close();
}
console.log('PASS internal workspaces role navigation, review-only dashboards, calendar keyboard behavior and Client isolation');
console.log(`SUCCESS: ${checks} UI DOM checks passed.`);
