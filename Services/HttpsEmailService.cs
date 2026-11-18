using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Authentication;
using AriesMagicAppointmentSystem.Models;
using Microsoft.Extensions.Options;
namespace AriesMagicAppointmentSystem.Services;
public static class EmailConfiguration
{
    public static bool IsValid(EmailSettings o) => new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(o.SenderEmail)
        && (o.Provider.Equals("Smtp",StringComparison.OrdinalIgnoreCase)
        ? !string.IsNullOrWhiteSpace(o.Host) && o.Port is >0 and <=65535 && o.EnableSsl && !string.IsNullOrWhiteSpace(o.Username) && !string.IsNullOrWhiteSpace(o.Password)
        : (o.Provider.Equals("Brevo",StringComparison.OrdinalIgnoreCase)||o.Provider.Equals("Resend",StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(o.ApiKey));
}
public static class EmailFailure
{
    public static string Classify(Exception e) => e switch {
        OperationCanceledException => "Timeout", MailKit.Security.AuthenticationException => "Authentication",
        MailKit.Security.SslHandshakeException or AuthenticationException => "Tls",
        MailKit.Net.Smtp.SmtpCommandException s => (int)s.StatusCode is 530 or 535 ? "Authentication" : "ProviderRejected",
        HttpRequestException { InnerException: AuthenticationException } => "Tls",
        SocketException or HttpRequestException or IOException => "NetworkOrTransport",
        ArgumentException or OptionsValidationException => "Configuration", _ => "ProviderFailure" };
    public static string Classify(HttpStatusCode s) => (int)s switch {401 or 403=>"AuthenticationOrSenderAuthorization",429=>"RateLimited",>=500=>"ProviderUnavailable",_=>"ProviderRejected"};
}
public sealed class HttpsEmailService(HttpClient client,IOptions<EmailSettings> options,ILogger<HttpsEmailService> logger):IEmailService
{
    public Task SendEmailAsync(string toEmail,string subject,string htmlMessage)=>Send(toEmail,subject,htmlMessage,null,null);
    public Task SendEmailWithAttachmentAsync(string toEmail,string subject,string htmlMessage,byte[] attachmentBytes,string attachmentFileName)=>Send(toEmail,subject,htmlMessage,attachmentBytes,attachmentFileName);
    async Task Send(string recipient,string subject,string html,byte[]? attachment,string? filename)
    {
        var o=options.Value;
        try {
            if(!EmailConfiguration.IsValid(o))throw new EmailDeliveryException("Configuration");
            var brevo=o.Provider.Equals("Brevo",StringComparison.OrdinalIgnoreCase);
            using var request=new HttpRequestMessage(HttpMethod.Post,brevo?"https://api.brevo.com/v3/smtp/email":"https://api.resend.com/emails");
            if(brevo)request.Headers.Add("api-key",o.ApiKey);else request.Headers.Authorization=new("Bearer",o.ApiKey);
            var body=new Dictionary<string,object>();
            if(brevo){body["sender"]=new {name=o.SenderName,email=o.SenderEmail};body["to"]=new[]{new{email=recipient}};body["htmlContent"]=html;}
            else {body["from"]=new MimeKit.MailboxAddress(o.SenderName,o.SenderEmail).ToString();body["to"]=new[]{recipient};body["html"]=html;}
            body["subject"]=subject;
            if(attachment!=null)body[brevo?"attachment":"attachments"]=new[]{new Dictionary<string,string>{{brevo?"name":"filename",Path.GetFileName(filename??"document.pdf")},{"content",Convert.ToBase64String(attachment)}}};
            request.Content=JsonContent.Create(body);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            if(!response.IsSuccessStatusCode)throw new EmailDeliveryException(EmailFailure.Classify(response.StatusCode));
            // Acceptance is not proof of inbox delivery. Never log response bodies, addresses or message content.
            logger.LogInformation("Email submission accepted. Provider={Provider} DeliveryConfirmed={DeliveryConfirmed}",brevo?"Brevo":"Resend",false);
        }catch(Exception e){var category=e is EmailDeliveryException d?d.Classification:EmailFailure.Classify(e);logger.LogWarning("Email submission failed. Classification={Classification}",category);throw new EmailDeliveryException(category);}
    }
}
