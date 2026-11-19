import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import {JSDOM} from 'jsdom';
const root=path.join(process.env.TEMP,'AriesOwnerSnapshots');
let checks=0;
function check(p,label){assert.ok(p,label);checks++;}
const targets=[
 {file:'Payments-Verify.html',kind:'payment',
 headings:['Booking Information','Payment Summary','OCR Extraction','Expected vs Detected']},
 {file:'Bookings-OwnerDetails.html',kind:'confirmed',
 headings:['Client Information','Event Information','Packing Information','Venue Travel Overview','Client Notes','Booking Financial Details']}
];
for(const cfg of targets){
 const html=fs.readFileSync(path.join(root,cfg.file),'utf8');
 const dom=new JSDOM(html,{url:'http://owner.example.test',runScripts:'outside-only'});
 const d=dom.window.document;
 const page=d.querySelector('.owner-booking-detail-page');
 const content=d.querySelector('.owner-booking-detail-main');
 const aside=d.querySelector('.owner-booking-detail-timeline');
 const header=d.querySelector('.owner-booking-detail-header h1');
 check(!!page&&!!content&&!!aside&&!!header,cfg.kind+' shared Owner structure exists');
 check(/^BK-\d{4}-\d{3,}$/.test(header.textContent.trim()),cfg.kind+' uses actual generated Booking ID');
 check(!d.querySelector('.workspace-page-heading'),cfg.kind+' removes duplicate dashboard title');
 check(page.getAttribute('data-owner-booking-variant')===cfg.kind,cfg.kind+' correct role-specific page');
 check(!!d.querySelector('aside.owner-booking-detail-timeline .aries-timeline'),cfg.kind+' actual shared timeline partial');
 check(!!d.querySelector('.owner-booking-detail-header a[href]'),cfg.kind+' retains return navigation');
 const mainHeadings=[...content.querySelectorAll('h2')].map(x=>x.textContent.trim().replace(/\s+/g,' '));
 let previous=-1;
 for(const wanted of cfg.headings){
  const i=mainHeadings.indexOf(wanted);
  check(i>previous,cfg.kind+' section present in expected vertical order: '+wanted);
  previous=i;
 }
 check(!d.body.textContent.includes('View the details and recorded activity for this booking.'),cfg.kind+' removes redundant description');
 check(!d.body.textContent.includes('Available Actions'),cfg.kind+' removes redundant actions title');
 if(cfg.kind==='payment'){
  check(!!content.querySelector('.owner-booking-proof-inside img')&&!!content.querySelector('a[href*="ProofHistory"]'),
        'Payment proof remains viewable with historic submission navigation');
  check(!!content.querySelector('.owner-payment-compare')&&!!content.querySelector('.owner-detail-grid'),
        'OCR comparison and booking labels retained');
  check(!d.querySelector('form[action*="ReanalyzeProof"]')&&!d.querySelector('#rejectPaymentModal'),
        'Manual Reanalyze and Reject presentation removed');
  check(d.querySelectorAll('[data-bs-target="#approvePaymentModal"],[data-bs-target="#evidencePaymentModal"]').length===2,
        'Only two primary payment decision buttons remain');
  check(!!d.querySelector('#approvePaymentModal form[action*="Verify"]') &&
        !!d.querySelector('#evidencePaymentModal form[action*="RequestAdditionalEvidence"]'),
        'Existing protected approval and resubmission forms remain');
  check(!!d.querySelector('[name=duplicateOverrideConfirmed]') &&
        !!d.querySelector('[name=duplicateOverrideReason]') &&
        !!d.querySelector('[name=amountCorrectionReason]'),
        'Financial confirmation, duplicate-reference and correction safeguards remain in modal');
  check(!content.textContent.includes('Possible duplicate proof or transaction reference.') &&
        !content.textContent.includes('Financial actions require confirmation.'),
        'Generic duplicate and decision messages retired');
 }else{
  check(!!content.querySelector('a[href*="AddNote"]'),
        'Existing Owner internal notes and conversation actions remain');
  check(content.querySelectorAll('h2').length>=6&&!!content.querySelector('.owner-booking-financial-details'),
        'Six main confirmed information sections include financial details');
  check(content.querySelectorAll('th').length>0 &&
        ![...content.querySelectorAll('th')].some(x=>x.textContent.trim()==='Proof') &&
        !content.textContent.includes('Refund History'),
        'Proof column and Refund History section removed');
  check(content.textContent.includes('Payment Transaction History') &&
        (content.textContent.includes('No payment submissions were recorded') || !!content.querySelector('a[href*="/Payments/Verify"]')),
        'Original transaction table and authorized payment review links preserved');
  check(!content.textContent.includes('Selected Service Info'),
        'Selected Service Info not duplicated');
 }
 dom.window.close();
}
console.log('SUCCESS: '+checks+' unified Owner Booking Details DOM and action checks passed.');
