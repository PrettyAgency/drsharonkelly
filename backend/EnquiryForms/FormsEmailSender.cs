using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;

namespace DrSharonKellyEnt.Forms;

// SMTP relay via System.Net.Mail (built into .NET, no extra NuGet package).
// Sends a branded thank-you to the submitter and an internal workflow
// notification to the relevant recipients, for both the contact form and the
// refer-a-patient form.
public class FormsEmailSender
{
    private readonly SmtpOptions _smtp;
    private readonly IWebHostEnvironment _env;

    public FormsEmailSender(Microsoft.Extensions.Options.IOptions<SmtpOptions> smtp, IWebHostEnvironment env)
    {
        _smtp = smtp.Value;
        _env = env;
    }

    public async Task SendContactEmailsAsync(ContactFormRequest body, SiteOptions site, string[] workflowRecipients, string ip)
    {
        var fullName = $"{body.FirstName} {body.LastName}".Trim();

        var thankYou = Fill(await LoadTemplateAsync("ContactThankYou.html"), new Dictionary<string, string>
        {
            ["Name"] = Encode(body.FirstName),
            ["SiteName"] = Encode(site.Name),
            ["AccentColor"] = site.AccentColor,
            ["WebsiteUrl"] = site.WebsiteUrl,
            ["MessageHtml"] = Encode(body.Message).Replace("\n", "<br>")
        });
        await SendAsync(site, body.Email, fullName, $"Thanks for your enquiry — {site.Name}", thankYou);

        if (workflowRecipients.Length > 0)
        {
            var workflow = Fill(await LoadTemplateAsync("ContactWorkflow.html"), new Dictionary<string, string>
            {
                ["Name"] = Encode(fullName),
                ["Email"] = Encode(body.Email),
                ["Phone"] = Encode(body.Phone),
                ["EnquirerType"] = Encode(body.EnquirerType),
                ["PreferredLocation"] = Encode(string.IsNullOrWhiteSpace(body.PreferredLocation) ? "No preference" : body.PreferredLocation),
                ["SiteName"] = Encode(site.Name),
                ["MessageHtml"] = Encode(body.Message).Replace("\n", "<br>"),
                ["SubmittedAt"] = DateTimeOffset.Now.ToString("f"),
                ["IpAddress"] = ip
            });
            await SendToRecipientsAsync(workflowRecipients, $"New website enquiry — {fullName}", workflow, body.Email, fullName);
        }
    }

    public async Task SendReferralEmailsAsync(ReferralFormRequest body, SiteOptions site, string[] workflowRecipients, string ip)
    {
        var gpFullName = $"{body.GpFirstName} {body.GpLastName}".Trim();
        var gpTitledName = $"Dr {gpFullName}";

        var thankYou = Fill(await LoadTemplateAsync("ReferralThankYou.html"), new Dictionary<string, string>
        {
            ["GpName"] = Encode(gpFullName),
            ["PatientName"] = Encode(body.PatientName),
            ["SiteName"] = Encode(site.Name),
            ["AccentColor"] = site.AccentColor,
            ["WebsiteUrl"] = site.WebsiteUrl
        });
        await SendAsync(site, body.GpEmail, gpTitledName, $"Referral received — {site.Name}", thankYou);

        if (workflowRecipients.Length > 0)
        {
            var workflow = Fill(await LoadTemplateAsync("ReferralWorkflow.html"), new Dictionary<string, string>
            {
                ["GpName"] = Encode(gpTitledName),
                ["GpEmail"] = Encode(body.GpEmail),
                ["GpPhone"] = Encode(body.GpPhone),
                ["ProviderNumber"] = Encode(body.ProviderNumber),
                ["PracticeName"] = Encode(body.PracticeName),
                ["PracticeAddress"] = Encode($"{body.PracticeAddress}, {body.City} {body.State} {body.Postcode}"),
                ["PatientName"] = Encode(body.PatientName),
                ["PatientPhone"] = Encode(body.PatientPhone),
                ["ClinicalConditionHtml"] = Encode(body.ClinicalCondition).Replace("\n", "<br>"),
                ["SiteName"] = Encode(site.Name),
                ["SubmittedAt"] = DateTimeOffset.Now.ToString("f"),
                ["IpAddress"] = ip
            });
            await SendToRecipientsAsync(workflowRecipients, $"New patient referral — {body.PatientName} (from {gpTitledName})", workflow, body.GpEmail, gpTitledName);
        }
    }

    private async Task SendAsync(SiteOptions site, string toEmail, string toName, string subject, string html)
    {
        using var msg = new MailMessage
        {
            From = new MailAddress(_smtp.FromAddress, _smtp.FromName),
            Subject = subject,
            Body = html,
            IsBodyHtml = true
        };
        msg.To.Add(new MailAddress(toEmail, toName));
        if (!string.IsNullOrWhiteSpace(site.ReplyToAddress))
            msg.ReplyToList.Add(new MailAddress(site.ReplyToAddress));
        await Send(msg);
    }

    private async Task SendToRecipientsAsync(string[] recipients, string subject, string html, string replyToEmail, string replyToName)
    {
        using var msg = new MailMessage
        {
            From = new MailAddress(_smtp.FromAddress, _smtp.FromName),
            Subject = subject,
            Body = html,
            IsBodyHtml = true
        };
        foreach (var r in recipients)
            if (!string.IsNullOrWhiteSpace(r)) msg.To.Add(new MailAddress(r));
        msg.ReplyToList.Add(new MailAddress(replyToEmail, replyToName));
        await Send(msg);
    }

    private async Task Send(MailMessage msg)
    {
        using var client = new SmtpClient(_smtp.Host, _smtp.Port)
        {
            EnableSsl = _smtp.EnableSsl,
            Credentials = new NetworkCredential(_smtp.Username, _smtp.Password)
        };
        await client.SendMailAsync(msg);
    }

    private async Task<string> LoadTemplateAsync(string fileName) =>
        await File.ReadAllTextAsync(Path.Combine(_env.ContentRootPath, "EnquiryForms", "EmailTemplates", fileName));

    private static string Fill(string template, Dictionary<string, string> values)
    {
        foreach (var kv in values)
            template = template.Replace("{{" + kv.Key + "}}", kv.Value);
        return template;
    }

    private static string Encode(string? value) => HtmlEncoder.Default.Encode(value ?? "");
}
