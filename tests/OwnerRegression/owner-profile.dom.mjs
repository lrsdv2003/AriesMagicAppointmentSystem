import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import {JSDOM} from 'jsdom';
const root=process.cwd();
const fixtures=path.join(process.env.TEMP,'AriesOwnerSnapshots');
const script=fs.readFileSync(path.join(root,'wwwroot/js/internal-profile.js'),'utf8');
let checks=0;
const check=(ok,label)=>{assert.ok(ok,label);checks++;};
const get=(name)=>{
  const dom=new JSDOM(fs.readFileSync(path.join(fixtures,name+'.html'),'utf8'),{
    url:'http://owner.test/'+name,runScripts:'outside-only'
  });
  dom.window.confirm=()=>true;
  dom.window.eval(script);
  dom.window.document.dispatchEvent(new dom.window.Event('DOMContentLoaded',{bubbles:true}));
  return dom;
};
{
 const dom=get('InternalProfile-Index'),d=dom.window.document;
 const profile=d.querySelector('.unified-profile');
 check(profile?.dataset.role==='Staff' || profile?.dataset.role==='Owner','Owner shared Profile renders valid role metadata');
 check(!d.querySelector('.up-security,#security'),'Owner profile has no Account Security/Change Password card');
 check(!d.body.textContent.includes('Account Security'),'Owner has no obsolete Account Security heading or menu entry');
 check(!!d.querySelector('.up-lower-single .up-information'),'Account Information reflows in a single-column area');
 check(!!d.querySelector('a[href="/InternalProfile/Password"]'),'Owner has a dedicated Change Password link');
 check(!!d.querySelector('a[href="/OwnerEmail/Index"]'),'Secure Owner email management remains accessible');
 check(!!d.querySelector('#hubProfileForm'),'Owner can still edit name and contact number');
 check(!!d.querySelector('form[action="/InternalProfile/UploadPhoto"]') &&
       !!d.querySelector('form[action="/InternalProfile/RemovePhoto"]'),'Owner photo management intact');
 check(!!d.querySelector('form[action="/Account/Logout"]'),'Owner Sign Out available');
 const name=d.querySelector('#Personal_FullName');
 const saved=name.dataset.savedValue;
 name.value='Unsaved Owner Edit';
 d.querySelector('#hubProfileForm').dispatchEvent(new dom.window.Event('reset',{cancelable:true,bubbles:true}));
 check(name.value===saved,'Owner Cancel restores saved name without persisting changes');
 dom.window.close();
}
{
 const dom=get('InternalProfile-Password'),d=dom.window.document;
 check(!d.querySelector('#security,.up-security'),'Dedicated password page is not the removed Owner security section');
 check(!!d.querySelector('a[href="/InternalProfile/Index"]'),'Password page offers return to Profile');
 const form=d.querySelector('form[action="/InternalProfile/ChangePassword"]');
 check(!!form?.querySelector('[name="__RequestVerificationToken"]'),'Owner password change retains antiforgery');
 check(['Password_CurrentPassword','Password_NewPassword','Password_ConfirmPassword'].every(id=>!!d.getElementById(id)),
       'Original password-management fields preserved');
 const toggle=d.querySelector('[data-toggle-password="Password_CurrentPassword"]');
 const input=d.querySelector('#Password_CurrentPassword');
 toggle.click();
 check(input.type==='text'&&toggle.getAttribute('aria-pressed')==='true','Owner password show/hide control works');
 toggle.click();
 check(input.type==='password','Owner password can be hidden again');
 input.value='owner-typing';
 form.dispatchEvent(new dom.window.Event('reset',{bubbles:true,cancelable:true}));
 check(input.type==='password','Owner password reset restores concealed field');
 dom.window.close();
}
for(const name of ['InternalProfile-Index-Staff','Profile-Index']){
 const dom=get(name),d=dom.window.document;
 check(!!d.querySelector('.up-security#security'),'Staff/Client still have original security form: '+name);
 check(!!d.querySelector('form[action$="/ChangePassword"] [name="Password.NewPassword"]'),'Staff/Client password management retained: '+name);
 check(!!d.querySelector('form[action$="/Edit"] [name="Personal.FullName"]'),'Staff/Client personal editing retained: '+name);
 check(!!d.querySelector('[name="photo"]'),'Staff/Client photo upload retained: '+name);
 dom.window.close();
}
console.log('SUCCESS: '+checks+' Owner/Staff/Client Profile and password interaction checks passed.');
