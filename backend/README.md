# Contact & Refer-a-Patient form backend

Two POST endpoints — `/api/forms/contact` and `/api/forms/refer-a-patient` —
that run **inside your existing Umbraco 17 application**: same process, same
app pool, same IIS site. No separate deployment, unlike the earlier
StudioContactApi build. See `Program.cs.instructions.md` for the two lines to
add to your existing `Program.cs`.

Each submission goes through:

1. **Honeypot** — an invisible field only bots fill in; tripped submissions
   are accepted silently (no signal to the bot) but never emailed.
2. **Time-trap** — rejects submissions completed in under 2 seconds.
3. **Google reCAPTCHA v3** — invisible, score-based (0.0 bot – 1.0 human),
   verified server-side.
4. **Rate limiting** — per-IP, per-form, in-memory sliding window.
5. **Logging** — every submission appended to `App_Data/forms-log.txt`.
6. **Two emails via your SMTP relay:**
   - a branded thank-you to the person who submitted the form
   - an internal workflow notification to everyone configured for that form

Contact form and Refer a Patient form have independent recipient lists
(`Forms:Contact:WorkflowRecipients` / `Forms:Referral:WorkflowRecipients`) and
their own email templates in `EnquiryForms/EmailTemplates/`.

## Files

- `EnquiryForms/FormsModels.cs` — request DTOs for both forms.
- `EnquiryForms/FormsOptions.cs` — strongly-typed config (SMTP, reCAPTCHA, etc).
- `EnquiryForms/RateLimiter.cs`, `FormsLogger.cs`, `FormsEmailSender.cs` — services.
- `EnquiryForms/FormsEndpoints.cs` — the two `MapPost` endpoints + DI wiring
  (`AddEnquiryForms` / `MapEnquiryForms` extension methods).
- `EnquiryForms/EmailTemplates/*.html` — the four branded email templates.
- `appsettings.snippet.json` — config block to merge into your appsettings.
- `Program.cs.instructions.md` — the exact two lines to add, and where.

## Notes

- `System.Net.Mail.SmtpClient` needs zero extra NuGet packages but is
  considered legacy by Microsoft. Swap in **MailKit** later if you want —
  `FormsEmailSender`'s public methods can keep the same shape.
- The rate limiter is in-memory and per-instance — fine for a single IIS
  instance; move to a shared store if this ever scales to multiple servers.
- Consider rotating `forms-log.txt` (scheduled task) if volume is high.
