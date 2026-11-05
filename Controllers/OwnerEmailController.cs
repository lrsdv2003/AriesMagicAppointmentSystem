using System.ComponentModel.DataAnnotations;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace AriesMagicAppointmentSystem.Controllers;
public sealed class OwnerEmailInput {
 [Required,EmailAddress,StringLength(254)]public string NewEmail{get;set;}="";
 [Required,Compare(nameof(NewEmail))]public string ConfirmEmail{get;set;}="";
 [StringLength(16)]public string? AuthenticatorCode{get;set;}
 [Required,DataType(DataType.Password)]public string CurrentPassword{get;set;}="";
}
[Authorize(Roles="Owner"),EnableRateLimiting("account"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class OwnerEmailController(UserManager<ApplicationUser> users,SignInManager<ApplicationUser> signIn,OwnerEmailChangeService changes):Controller
{
 async Task<ApplicationUser> Actor()=>await users.GetUserAsync(User)??throw new UnauthorizedAccessException();
 [HttpGet]public async Task<IActionResult> Index(){var u=await Actor();ViewBag.CurrentEmail=u.Email;ViewBag.Pending=await changes.Pending(u);return View(new OwnerEmailInput());}
 [HttpPost,ValidateAntiForgeryToken]public async Task<IActionResult> SendVerification(OwnerEmailInput input){var u=await Actor();
 if(ModelState.IsValid&&(!await users.GetTwoFactorEnabledAsync(u)||await users.VerifyTwoFactorTokenAsync(u,users.Options.Tokens.AuthenticatorTokenProvider,(input.AuthenticatorCode??" ").Replace(" ","")))&&(await signIn.CheckPasswordSignInAsync(u,input.CurrentPassword,true)).Succeeded){
 try{await changes.Request(u,input.NewEmail.Trim());TempData["SuccessMessage"]="Verification submitted. Your current login remains active until confirmation.";}catch{await changes.Audit(u,"request refused or delivery failed");TempData["ErrorMessage"]="Unable to submit verification. Check the address, cooldown and delivery configuration.";}}
 else {await changes.Audit(u,"reauthentication or validation failed");TempData["ErrorMessage"]="Verification refused. Check your details. If two-factor authentication is enabled, enter your authenticator code.";}
 return RedirectToAction(nameof(Index));}
 [HttpPost,ValidateAntiForgeryToken]public async Task<IActionResult> Cancel(){await changes.Cancel(await Actor());return RedirectToAction(nameof(Index));}
 [HttpGet]public IActionResult Confirm(){Response.Headers["Referrer-Policy"]="no-referrer";return View();}
 [HttpPost,ValidateAntiForgeryToken]public async Task<IActionResult> Confirm(string token){
 Response.Headers["Referrer-Policy"]="no-referrer";bool ok=false;try{ok=token?.Length is >0 and <8192&&await changes.Confirm(await Actor(),token);}catch{ok=false;}
 if(ok){await signIn.SignOutAsync();TempData["SuccessMessage"]="Email verified. Sign in using your new email.";return RedirectToAction("Login","Account");}
 await changes.Audit(await Actor(),"confirmation refused");TempData["ErrorMessage"]="Verification expired, was replaced, or could not be completed. Request a new email.";return RedirectToAction(nameof(Index));}
}
