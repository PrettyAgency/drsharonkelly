# Wiring into your existing Umbraco 17 site — ONE application, ONE app pool

Unlike the Studio Forest Hill build, these two endpoints do **not** need a
separate ASP.NET Core app, IIS site, application, or app pool. They run
inside the exact same process as Umbraco itself.

## 1. Copy files in

Copy the `EnquiryForms` folder (this whole folder, including
`EmailTemplates/`) into your Umbraco project, next to your existing
`Program.cs`.

## 2. Add two lines to your existing `Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

builder.Services.AddEnquiryForms(builder.Configuration);   // <-- add this line

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

app.MapEnquiryForms();                                     // <-- add this line

await app.RunAsync();
```

Add `using DrSharonKellyEnt.Forms;` at the top of `Program.cs`.

## 3. Merge config

Merge `appsettings.snippet.json`'s `Forms` section into your existing
`appsettings.json` (or `appsettings.Production.json`). Fill in:

- **`Forms:Smtp`** — your SMTP relay host/port/username/password/from address.
- **`Forms:Recaptcha:SecretKey`** — the reCAPTCHA v3 **secret** key (server-side).
  The **site key** (public) goes in each webpage's Tweaks panel, not here.
- **`Forms:Contact:WorkflowRecipients`** / **`Forms:Referral:WorkflowRecipients`**
  — who gets each form's internal notification email. Independent lists, so
  referrals can go to different people than general enquiries.

**Never commit real secrets.** Prefer IIS's Configuration Editor (encrypted)
or environment variables in production, e.g. `Forms__Smtp__Password`,
`Forms__Recaptcha__SecretKey` — these override the JSON file automatically.

## 4. Deploy exactly as you already do

Publish and deploy the whole Umbraco site as normal —
`dotnet publish -c Release -o <your existing site folder>`. There's nothing
extra to stand up: no new IIS site, no new application, no new app pool.
One deployment, one process, one set of logs.

## 5. Permissions

Make sure the existing app pool identity has **Modify** rights on
`App_Data/` (needed to write `forms-log.txt`) — it likely already does if
Umbraco itself writes there.

## 6. Test

Browse to `https://your-domain/api/forms/health` — should return
`{"status":"ok", ...}`.

## Front end

`contact.dc.html` and `refer-a-patient.dc.html` already POST JSON to
`/api/forms/contact` and `/api/forms/refer-a-patient` respectively, and load
the reCAPTCHA v3 script once you set **`recaptchaSiteKey`** in each page's
Tweaks panel (the public site key). If the API ever needs to live at a
different path, override **`formEndpoint`** there too.
