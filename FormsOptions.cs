using Microsoft.Extensions.Options;

namespace DrSharonKellyEnt.Forms;

public static class FormsEndpoints
{
    // Call ONCE from your existing Program.cs, before builder.Build() —
    // see ../Program.cs.instructions.md. Registers everything on the SAME
    // IServiceCollection Umbraco already uses.
    public static IServiceCollection AddEnquiryForms(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<SmtpOptions>(config.GetSection("Forms:Smtp"));
        services.Configure<RecaptchaOptions>(config.GetSection("Forms:Recaptcha"));
        services.Configure<RateLimitOptions>(config.GetSection("Forms:RateLimiting"));
        services.Configure<FormsLogOptions>(config.GetSection("Forms:EnquiryLog"));
        services.Configure<SiteOptions>(config.GetSection("Forms:Site"));
        services.Configure<ContactOptions>(config.GetSection("Forms:Contact"));
        services.Configure<ReferralOptions>(config.GetSection("Forms:Referral"));
        services.AddHttpClient("recaptcha");
        services.AddSingleton<RateLimiter>();
        services.AddSingleton<FormsLogger>();
        services.AddSingleton<FormsEmailSender>();
        return services;
    }

    // Call ONCE from Program.cs, anywhere before await app.RunAsync() — runs in
    // the SAME WebApplication/app pool/IIS site as the rest of the Umbraco site.
    public static WebApplication MapEnquiryForms(this WebApplication app)
    {
        app.MapPost("/api/forms/contact", async (
            HttpContext http, ContactFormRequest body, IHttpClientFactory httpClientFactory,
            IOptions<RecaptchaOptions> recaptchaOpts, IOptions<ContactOptions> contactOpts, IOptions<SiteOptions> siteOpts,
            RateLimiter rateLimiter, FormsLogger formsLogger, FormsEmailSender emailSender, ILogger<Program> logger) =>
        {
            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var fields = new (string, string)[] { ("name", $"{body.FirstName} {body.LastName}"), ("email", body.Email), ("phone", body.Phone) };

            if (!rateLimiter.Allow("contact:" + ip))
            {
                await formsLogger.LogAsync("contact", "RATE_LIMITED", ip, fields);
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }
            if (!string.IsNullOrWhiteSpace(body.Website))
            {
                // Honeypot tripped — pretend success so the bot gets no signal, but never email.
                await formsLogger.LogAsync("contact", "HONEYPOT_TRIGGERED", ip, fields);
                return Results.Ok(new { success = true });
            }
            if (body.FormLoadedAt > 0)
            {
                var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - body.FormLoadedAt;
                if (elapsedMs is > 0 and < 2000)
                {
                    await formsLogger.LogAsync("contact", "TOO_FAST_REJECTED", ip, fields);
                    return Results.BadRequest(new { success = false, error = "Please try again." });
                }
            }
            if (string.IsNullOrWhiteSpace(body.FirstName) || string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Message))
                return Results.BadRequest(new { success = false, error = "Please complete all required fields." });
            if (!RecaptchaHelper.IsLikelyEmail(body.Email))
                return Results.BadRequest(new { success = false, error = "Please enter a valid email address." });

            var recaptcha = recaptchaOpts.Value;
            if (recaptcha.MinimumRequired && !string.IsNullOrWhiteSpace(recaptcha.SecretKey))
            {
                var (passed, score) = await RecaptchaHelper.VerifyAsync(httpClientFactory.CreateClient("recaptcha"), recaptcha.SecretKey, body.RecaptchaToken, ip, recaptcha.MinimumScore, logger);
                if (!passed)
                {
                    await formsLogger.LogAsync("contact", $"RECAPTCHA_FAILED score={score:0.00}", ip, fields);
                    return Results.BadRequest(new { success = false, error = "We couldn't verify your submission. Please try again." });
                }
            }

            await formsLogger.LogAsync("contact", "ACCEPTED", ip, fields);

            try { await emailSender.SendContactEmailsAsync(body, siteOpts.Value, contactOpts.Value.WorkflowRecipients, ip); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send contact form emails");
                await formsLogger.LogAsync("contact", "EMAIL_FAILED: " + ex.Message, ip, fields);
            }

            return Results.Ok(new { success = true });
        });

        app.MapPost("/api/forms/refer-a-patient", async (
            HttpContext http, ReferralFormRequest body, IHttpClientFactory httpClientFactory,
            IOptions<RecaptchaOptions> recaptchaOpts, IOptions<ReferralOptions> referralOpts, IOptions<SiteOptions> siteOpts,
            RateLimiter rateLimiter, FormsLogger formsLogger, FormsEmailSender emailSender, ILogger<Program> logger) =>
        {
            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var fields = new (string, string)[] { ("gp", $"{body.GpFirstName} {body.GpLastName}"), ("gpEmail", body.GpEmail), ("patient", body.PatientName) };

            if (!rateLimiter.Allow("referral:" + ip))
            {
                await formsLogger.LogAsync("referral", "RATE_LIMITED", ip, fields);
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }
            if (!string.IsNullOrWhiteSpace(body.Website))
            {
                await formsLogger.LogAsync("referral", "HONEYPOT_TRIGGERED", ip, fields);
                return Results.Ok(new { success = true });
            }
            if (body.FormLoadedAt > 0)
            {
                var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - body.FormLoadedAt;
                if (elapsedMs is > 0 and < 2000)
                {
                    await formsLogger.LogAsync("referral", "TOO_FAST_REJECTED", ip, fields);
                    return Results.BadRequest(new { success = false, error = "Please try again." });
                }
            }
            if (string.IsNullOrWhiteSpace(body.GpEmail) || string.IsNullOrWhiteSpace(body.PatientName) || string.IsNullOrWhiteSpace(body.ClinicalCondition))
                return Results.BadRequest(new { success = false, error = "Please complete all required fields." });
            if (!RecaptchaHelper.IsLikelyEmail(body.GpEmail))
                return Results.BadRequest(new { success = false, error = "Please enter a valid email address." });

            var recaptcha = recaptchaOpts.Value;
            if (recaptcha.MinimumRequired && !string.IsNullOrWhiteSpace(recaptcha.SecretKey))
            {
                var (passed, score) = await RecaptchaHelper.VerifyAsync(httpClientFactory.CreateClient("recaptcha"), recaptcha.SecretKey, body.RecaptchaToken, ip, recaptcha.MinimumScore, logger);
                if (!passed)
                {
                    await formsLogger.LogAsync("referral", $"RECAPTCHA_FAILED score={score:0.00}", ip, fields);
                    return Results.BadRequest(new { success = false, error = "We couldn't verify your submission. Please try again." });
                }
            }

            await formsLogger.LogAsync("referral", "ACCEPTED", ip, fields);

            try { await emailSender.SendReferralEmailsAsync(body, siteOpts.Value, referralOpts.Value.WorkflowRecipients, ip); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send referral form emails");
                await formsLogger.LogAsync("referral", "EMAIL_FAILED: " + ex.Message, ip, fields);
            }

            return Results.Ok(new { success = true });
        });

        app.MapGet("/api/forms/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

        return app;
    }
}

public static class RecaptchaHelper
{
    public static bool IsLikelyEmail(string email) =>
        !string.IsNullOrWhiteSpace(email) && email.Contains('@') && email.Contains('.') && !email.Contains(' ');

    public static async Task<(bool passed, double score)> VerifyAsync(HttpClient client, string secretKey, string? token, string remoteIp, double minimumScore, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(token)) return (false, 0);
        try
        {
            var resp = await client.PostAsync("https://www.google.com/recaptcha/api/siteverify",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["secret"] = secretKey,
                    ["response"] = token,
                    ["remoteip"] = remoteIp
                }));
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var success = doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
            var score = doc.RootElement.TryGetProperty("score", out var sc) ? sc.GetDouble() : 0;
            // reCAPTCHA v3 is invisible/score-based (0.0 = bot, 1.0 = human) — no checkbox.
            return (success && score >= minimumScore, score);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "reCAPTCHA verification request failed");
            return (false, 0);
        }
    }
}
