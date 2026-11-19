import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {JSDOM} from 'jsdom';
const file=path.join(process.env.TEMP,'AriesOwnerSnapshots','BookingChanges-Edit.html');
const html=fs.readFileSync(file,'utf8');
const dom=new JSDOM(html,{url:'http://owner.test/BookingChanges/Edit/1',runScripts:'outside-only'});
const d=dom.window.document;
let checks=0;
const check=(ok,msg)=>{assert.ok(ok,msg);checks++};
const page=d.querySelector('.owner-confirmed-change-page');
const form=d.querySelector('#confirmedChangeForm');
check(!!page&&!!form&&!!d.querySelector('.confirmed-change-layout'),'Owner edit form and shared layout render');
check(page.querySelector('.owner-booking-detail-header h1')?.textContent.trim()==='BK-2026-001','actual booking ID appears in compact shared-style header');
check(!d.querySelector('.workspace-page-heading'),'no Staff/Owner workspace heading');
const clean=page.textContent.replace(/\s+/g,' ');
for(const removed of ['Edit Confirmed Booking','Propose package or venue changes','The booking will stay confirmed and unchanged until','Reason and Staff verification notes','Payment safety','Verified payments remain untouched. A change is blocked']){
 check(!clean.includes(removed),'removed specified heading/copy: '+removed);
}
check(!form.querySelector('[name=Reason]')&&!form.querySelector('[id=confirmedReason]'),'no Reason/Staff Verification input, label or required validation');
for(const key of ['BookingId','ServiceId','Venue','Latitude','Longitude','__RequestVerificationToken']){
 check(!!form.querySelector('[name="'+key+'"]'),'preserved '+key+' input');
}
check(form.querySelector('select[name=ServiceId]')?.options.length>=2,'existing/alternative package options preserved');
check(form.querySelector('textarea[name=Venue]')?.getAttribute('minlength')==='8','venue text length validation retained');
check(!!form.querySelector('.confirmed-change-actions button[type=submit]')&&
      !!page.querySelector('a[href*="/Bookings/Details"]'),'proposal and return actions retained');
check(page.querySelector('.confirmed-change-summary')?.textContent.includes('Verified payments')&&
      page.querySelector('.confirmed-change-summary')?.textContent.includes('Booking total')&&
      page.querySelector('.confirmed-change-summary')?.textContent.includes('Remaining balance'),
      'existing financial/current quotation summary preserved');
check(!!form.querySelector('#confirmedVenueSearch')&&!!form.querySelector('#confirmedPlaces')&&
      !!form.querySelector('#confirmedSearch'),'venue geolocation search preserved');
const script=fs.readFileSync('wwwroot/js/confirmed-booking-edit-20261109.js','utf8');
dom.window.eval(script);
const place=d.querySelector('#confirmedVenue');
const latitude=d.querySelector('#confirmedLatitude');
const longitude=d.querySelector('#confirmedLongitude');
place.value='Edited venue in Metro Manila Philippines';
place.dispatchEvent(new dom.window.Event('input',{bubbles:true}));
check(latitude.value===''&&longitude.value==='','changing venue invalidates stale latitude and longitude pin');
place.value='A confirmed venue in Pasig City Metro Manila, Philippines';
latitude.value='14.58125';longitude.value='121.1258';
const button=form.querySelector('.confirmed-change-submit');
check(button.disabled===false,'initial submission remains available');
form.dispatchEvent(new dom.window.Event('submit',{bubbles:true,cancelable:true}));
check(button.disabled===true&&button.textContent.includes('Sending')&&form.getAttribute('aria-busy')==='true',
      'submission locks repeated clicks and exposes loading state');
const repeated=new dom.window.Event("submit",{bubbles:true,cancelable:true});
form.dispatchEvent(repeated);
check(repeated.defaultPrevented,"repeated submission is prevented after loading state is set");
dom.window.close();
console.log('SUCCESS: '+checks+' Owner Edit Confirmed Booking DOM, removed-field, venue-pin and submit-state checks passed.');
