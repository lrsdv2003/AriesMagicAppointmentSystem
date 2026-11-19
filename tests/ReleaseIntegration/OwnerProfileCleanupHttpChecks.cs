using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

internal static class OwnerProfileCleanupHttpChecks
{
    private static async Task<(HttpStatusCode Status,string Html)> GetAsync(HttpClient client,string path)
    {
        using var response=await client.GetAsync(path);
        return (response.StatusCode,await response.Content.ReadAsStringAsync());
    }

    public static async Task RunAsync(
        HttpClient owner,HttpClient staff,HttpClient client,string ownerToken,
        Action<bool,string> check)
    {
        var profile=await GetAsync(owner,"/InternalProfile/Index");
        check(profile.Status==HttpStatusCode.OK &&
              profile.Html.Contains("data-role=\"Owner\"",StringComparison.Ordinal) &&
              profile.Html.Contains("up-lower-single",StringComparison.Ordinal) &&
              !profile.Html.Contains("class=\"up-card up-security\"",StringComparison.Ordinal) &&
              !profile.Html.Contains("Account Security",StringComparison.Ordinal),
              "Owner Profile no longer renders Account Security card or obsolete account-menu entry");
        check(profile.Html.Contains("Personal Information",StringComparison.Ordinal) &&
              profile.Html.Contains("Account Information",StringComparison.Ordinal) &&
              profile.Html.Contains("Manage login email securely",StringComparison.Ordinal) &&
              profile.Html.Contains("Change Photo",StringComparison.Ordinal) &&
              profile.Html.Contains("Remove Photo",StringComparison.Ordinal) &&
              profile.Html.Contains("Member Since",StringComparison.Ordinal) &&
              profile.Html.Contains("Sign Out",StringComparison.Ordinal) &&
              profile.Html.Contains("InternalProfile/Password",StringComparison.Ordinal),
              "Owner profile retains personal, account, photo, logout and separate password navigation");
        check(!profile.Html.Contains("id=\"Password_CurrentPassword\"",StringComparison.Ordinal) &&
              profile.Html.Contains("id=\"Personal_FullName\"",StringComparison.Ordinal) &&
              profile.Html.Contains("id=\"Personal_PhoneNumber\"",StringComparison.Ordinal),
              "Owner Profile excludes password form while retaining editable personal information");
        check(Regex.Matches(profile.Html,"name=\"__RequestVerificationToken\"").Count>=3,
              "Owner profile retains antiforgery tokens for personal, photo and sign-out forms");

        var password=await GetAsync(owner,"/InternalProfile/Password");
        check(password.Status==HttpStatusCode.OK &&
              password.Html.Contains("class=\"unified-profile owner-password-workspace\"",StringComparison.Ordinal) &&
              password.Html.Contains("Back to Profile",StringComparison.Ordinal) &&
              password.Html.Contains("id=\"Password_CurrentPassword\"",StringComparison.Ordinal) &&
              password.Html.Contains("id=\"Password_NewPassword\"",StringComparison.Ordinal) &&
              password.Html.Contains("id=\"Password_ConfirmPassword\"",StringComparison.Ordinal) &&
              password.Html.Contains("name=\"__RequestVerificationToken\"",StringComparison.Ordinal),
              "Owner-only password page renders existing secure form with Back to Profile and antiforgery");
        check(!password.Html.Contains("Account Security",StringComparison.Ordinal) &&
              !password.Html.Contains("id=\"security\"",StringComparison.Ordinal),
              "Separate Owner password page does not restore the retired Account Security section");

        var staffProfile=await GetAsync(staff,"/InternalProfile/Index");
        check(staffProfile.Status==HttpStatusCode.OK &&
              staffProfile.Html.Contains("class=\"up-card up-security\"",StringComparison.Ordinal) &&
              staffProfile.Html.Contains("id=\"Password_CurrentPassword\"",StringComparison.Ordinal),
              "Staff unified Profile keeps its original in-page Change Password form");
        var clientProfile=await GetAsync(client,"/Profile/Index");
        check(clientProfile.Status==HttpStatusCode.OK &&
              clientProfile.Html.Contains("class=\"up-card up-security\"",StringComparison.Ordinal) &&
              clientProfile.Html.Contains("id=\"Password_CurrentPassword\"",StringComparison.Ordinal),
              "Client unified Profile keeps its original in-page Change Password form");

        var staffDenied=await GetAsync(staff,"/InternalProfile/Password");
        var clientDenied=await GetAsync(client,"/InternalProfile/Password");
        check(staffDenied.Status!=HttpStatusCode.OK &&
              clientDenied.Status!=HttpStatusCode.OK,
              "Owner-only dedicated password page denies Staff and Client direct GETs");

        using var missingCsrf=await owner.PostAsync("/InternalProfile/ChangePassword",
            new FormUrlEncodedContent(new Dictionary<string,string> {
                ["Password.CurrentPassword"]="invalid-password",
                ["Password.NewPassword"]="OwnerNew#Password2026",
                ["Password.ConfirmPassword"]="OwnerNew#Password2026"
            }));
        check(missingCsrf.StatusCode==HttpStatusCode.BadRequest,
              "Owner password POST still rejects submissions without antiforgery token");

        using var invalidCurrent=await owner.PostAsync("/InternalProfile/ChangePassword",
            new FormUrlEncodedContent(new Dictionary<string,string> {
                ["Password.CurrentPassword"]="invalid-current-password",
                ["Password.NewPassword"]="OwnerNew#Password2026",
                ["Password.ConfirmPassword"]="OwnerNew#Password2026",
                ["__RequestVerificationToken"]=ownerToken
            }));
        var invalidHtml=await invalidCurrent.Content.ReadAsStringAsync();
        check(invalidCurrent.StatusCode==HttpStatusCode.OK &&
              invalidHtml.Contains("id=\"Password_CurrentPassword\"",StringComparison.Ordinal) &&
              invalidHtml.Contains("owner-password-workspace",StringComparison.Ordinal) &&
              !invalidHtml.Contains("OwnerNew#Password2026",StringComparison.Ordinal) &&
              !invalidHtml.Contains("invalid-current-password",StringComparison.Ordinal),
              "Invalid Owner password update returns to dedicated form without echoing credentials");
    }
}
