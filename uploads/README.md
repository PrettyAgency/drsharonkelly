using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ---------- Options ----------
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<RecaptchaOptions>(builder.Configuration.GetSection("Recaptcha"));
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection("Notifications"));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("RateLimiting"));
builder.Services.Configure<EnquiryLogOptions>(builder.Configuration.GetSection("EnquiryLog"));
builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection("Site"));

builder.Services.AddHttpClient("recaptcha");
builder.Services.AddSingleton<RateLimiter>();
builder.Services.AddSingleton<EnquiryLogger>();
builder.Services.AddSingleton<EmailSender>();

// CORS — only matters if the static site is hosted on a different origin than this API.
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddPolicy("StudioSite", p =>
{
    if (allowedOrigins.Length > 0)
        p.WithOrigins(allowedOrigins).AllowAnyHeader().WithMethods("POST", "OPTIONS");
    else
        p.AllowAnyOrigin().AllowAnyHeader().WithMethods("POST", "OPTIONS");
}));

var app = builder.Build();
app.UseCors("StudioSite");

// ---------- POST /api/enquiry ----------
app.MapPost("/enquiry", async (
    HttpContext http,
    EnquiryRequest body,
    IHttpClientFactory httpClientFactory,
    Microsoft.Extensions.Options.IOptions<RecaptchaOptions> recaptchaOpts,
    Microsoft.Extensions.Options.IOptions<NotificationOptions> notifyOpts,
    Microsoft.Extensions.Options.IOptions<SiteOptions> siteOpts,
    RateLimiter rateLimiter,
    EnquiryLogger enquiryLogger,
    EmailSender emailSender,
    ILogger<Program> logger) =>
{
    var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // ---- 1. Rate limiting (basic, in-memory, per IP) ----
    if (!rateLimiter.Allow(ip))
    {
        await enquiryLogger.LogAsync("RATE_LIMITED", body, ip);
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    // ---- 2. Honeypot ----
    // The website field is invisible to real visitors; only bots fill it in.
    // We return 200 (as if it succeeded) so the bot gets no signal it was blocked.
    if (!string.IsNullOrWhiteSpace(body.Website))
    {
        await enquiryLogger.LogAsync("HONEYPOT_TRIGGERED", body, ip);
        return Results.Ok(new { success = true });
    }

    // ---- 3. Time-trap ----
    // Reject submissions filled in faster than a human plausibly could (bots often
    // submit forms in well under a second of the page loading).
    if (body.FormLoadedAt > 0)
    {
        var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - body.FormLoadedAt;
        if (elapsedMs is > 0 and < 2000)
        {
            await enquiryLogger.LogAsync("TOO_FAST_REJECTED", body, ip);
            return Results.BadRequest(new { success = false, error = "Please try again." });
        }
    }

    // ---- 4. Basic field validation ----
    if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Message))
    {
        return Results.BadRequest(new { success = false, error = "Please complete all required fields." });
    }
    if (!IsLikelyEmail(body.Email))
    {
        return Results.BadRequest(new { success = false, error = "Please enter a valid email address." });
    }

    // ---- 5. reCAPTCHA verification ----
    var recaptcha = recaptchaOpts.Value;
    if (recaptcha.MinimumRequired && !string.IsNullOrWhiteSpace(recaptcha.SecretKey))
    {
        var (passed, score) = await VerifyRecaptchaAsync(httpClientFactory.CreateClient("recaptcha"), recaptcha.SecretKey, body.RecaptchaToken, ip, recaptcha.MinimumScore, logger);
        if (!passed)
        {
            await enquiryLogger.LogAsync($"RECAPTCHA_FAILED score={score:0.00}", body, ip);
            return Results.BadRequest(new { success = false, error = "We couldn't verify your submission. Please try again." });
        }
    }

    // ---- 6. Log the (legitimate) submission ----
    await enquiryLogger.LogAsync("ACCEPTED", body, ip);

    // ---- 7. Send emails (best-effort; failure to email doesn't fail the request
    //          for the visitor, but IS logged so you notice) ----
    var site = siteOpts.Value;
    var recipients = notifyOpts.Value.WorkflowRecipients ?? Array.Empty<string>();

    try
    {
        await emailSender.SendThankYouAsync(body, site);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to send thank-you email to {Email}", body.Email);
        await enquiryLogger.LogAsync("THANKYOU_EMAIL_FAILED: " + ex.Message, body, ip);
    }

    try
    {
        await emailSender.SendWorkflowAsync(body, site, recipients, ip);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to send workflow notification email");
        await enquiryLogger.LogAsync("WORKFLOW_EMAIL_FAILED: " + ex.Message, body, ip);
    }

    return Results.Ok(new { success = true });
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

app.Run();

// ================================================================
// Helpers
// ================================================================

static bool IsLikelyEmail(string email) =>
    !string.IsNullOrWhiteSpace(email) && email.Contains('@') && email.Contains('.') && !email.Contains(' ');

static async Task<(bool passed, double score)> VerifyRecaptchaAsync(HttpClient client, string secretKey, string? token, string remoteIp, double minimumScore, ILogger logger)
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
        using var doc = JsonDocument.Parse(json);
        var success = doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
        var score = doc.RootElement.TryGetProperty("score", out var sc) ? sc.GetDouble() : 0;
        // reCAPTCHA v3 has no checkbox — success just means "a valid token for this
        // site key"; the actual bot/human signal is the score (0.0 = bot, 1.0 = human).
        return (success && score >= minimumScore, score);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "reCAPTCHA verification request failed");
        return (false, 0);
    }
}

// ================================================================
// Request model
// ================================================================
public class EnquiryRequest
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("phone")] public string Phone { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("recaptchaToken")] public string? RecaptchaToken { get; set; }
    [JsonPropertyName("website")] public string? Website { get; set; } // honeypot — must stay empty
    [JsonPropertyName("formLoadedAt")] public long FormLoadedAt { get; set; } // client Date.now() at page load
}

// ================================================================
// Options (bound from appsettings.json)
// ================================================================
public class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "";
}

public class RecaptchaOptions
{
    public string SecretKey { get; set; } = "";
    public bool MinimumRequired { get; set; } = true;
    // reCAPTCHA v3 score threshold: 0.0 (bot) – 1.0 (human). Google recommends
    // starting around 0.5 and tightening (raising) it if spam still gets through,
    // or loosening (lowering) it if genuine enquiries are being rejected.
    public double MinimumScore { get; set; } = 0.5;
}

public class NotificationOptions
{
    public string[] WorkflowRecipients { get; set; } = Array.Empty<string>();
}

public class RateLimitOptions
{
    public int MaxRequestsPerIpPerWindow { get; set; } = 5;
    public int WindowMinutes { get; set; } = 10;
}

public class EnquiryLogOptions
{
    public string FilePath { get; set; } = "App_Data/enquiry-log.txt";
}

public class SiteOptions
{
    public string Name { get; set; } = "The Studio Forest Hill";
    public string LogoUrl { get; set; } = "";
    public string AccentColor { get; set; } = "#9c4a2f";
    public string ReplyToAddress { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";
}

// ================================================================
// Rate limiter — simple in-memory sliding window per IP.
// Good enough for a single-instance IIS deployment. If you later run
// multiple server instances behind a load balancer, move this to a
// shared store (e.g. SQL, Redis) instead.
// ================================================================
public class RateLimiter
{
    private readonly RateLimitOptions _options;
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new();
    private readonly object _lock = new();

    public RateLimiter(Microsoft.Extensions.Options.IOptions<RateLimitOptions> options) => _options = options.Value;

    public bool Allow(string ip)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var window = TimeSpan.FromMinutes(_options.WindowMinutes);
            var queue = _hits.GetOrAdd(ip, _ => new Queue<DateTime>());

            while (queue.Count > 0 && now - queue.Peek() > window)
                queue.Dequeue();

            if (queue.Count >= _options.MaxRequestsPerIpPerWindow)
                return false;

            queue.Enqueue(now);
            return true;
        }
    }
}

// ================================================================
// Enquiry logger — appends one line per submission to a local text file.
// ================================================================
public class EnquiryLogger
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EnquiryLogger(Microsoft.Extensions.Options.IOptions<EnquiryLogOptions> options, IWebHostEnvironment env)
    {
        var configured = options.Value.FilePath;
        _path = Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public async Task LogAsync(string status, EnquiryRequest body, string ip)
    {
        var line = string.Join(" | ", new[]
        {
            DateTimeOffset.UtcNow.ToString("u"),
            status,
            $"name=\"{Sanitize(body.Name)}\"",
            $"email=\"{Sanitize(body.Email)}\"",
            $"phone=\"{Sanitize(body.Phone)}\"",
            $"ip={ip}"
        });

        await _gate.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_path, line + Environment.NewLine, Encoding.UTF8);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Sanitize(string? value) => (value ?? "").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
}

// ================================================================
// Email sender — SMTP relay via System.Net.Mail (built into .NET, no
// extra NuGet package required). Sends the customer thank-you email
// and the internal workflow-notification email.
// ================================================================
public class EmailSender
{
    private readonly SmtpOptions _smtp;
    private readonly IWebHostEnvironment _env;

    public EmailSender(Microsoft.Extensions.Options.IOptions<SmtpOptions> smtp, IWebHostEnvironment env)
    {
        _smtp = smtp.Value;
        _env = env;
    }

    public async Task SendThankYouAsync(EnquiryRequest body, SiteOptions site)
    {
        var template = await LoadTemplateAsync("ThankYou.html");
        var html = Fill(template, new Dictionary<string, string>
        {
            ["Name"] = Encode(body.Name),
            ["SiteName"] = Encode(site.Name),
            ["AccentColor"] = site.AccentColor,
            ["WebsiteUrl"] = site.WebsiteUrl,
            ["MessageHtml"] = Encode(body.Message).Replace("\n", "<br>")
        });

        using var msg = new MailMessage
        {
            From = new MailAddress(_smtp.FromAddress, _smtp.FromName),
            Subject = $"Thanks for your enquiry — {site.Name}",
            Body = html,
            IsBodyHtml = true
        };
        msg.To.Add(new MailAddress(body.Email, body.Name));
        if (!string.IsNullOrWhiteSpace(site.ReplyToAddress))
            msg.ReplyToList.Add(new MailAddress(site.ReplyToAddress));

        await SendAsync(msg);
    }

    public async Task SendWorkflowAsync(EnquiryRequest body, SiteOptions site, string[] recipients, string ip)
    {
        if (recipients.Length == 0) return;

        var template = await LoadTemplateAsync("Workflow.html");
        var html = Fill(template, new Dictionary<string, string>
        {
            ["Name"] = Encode(body.Name),
            ["Email"] = Encode(body.Email),
            ["Phone"] = Encode(body.Phone),
            ["SiteName"] = Encode(site.Name),
            ["MessageHtml"] = Encode(body.Message).Replace("\n", "<br>"),
            ["SubmittedAt"] = DateTimeOffset.Now.ToString("f"),
            ["IpAddress"] = ip
        });

        using var msg = new MailMessage
        {
            From = new MailAddress(_smtp.FromAddress, _smtp.FromName),
            Subject = $"New enquiry — {site.Name} ({body.Name})",
            Body = html,
            IsBodyHtml = true
        };
        foreach (var r in recipients)
            if (!string.IsNullOrWhiteSpace(r)) msg.To.Add(new MailAddress(r));
        msg.ReplyToList.Add(new MailAddress(body.Email, body.Name));

        await SendAsync(msg);
    }

    private async Task SendAsync(MailMessage msg)
    {
        using var client = new SmtpClient(_smtp.Host, _smtp.Port)
        {
            EnableSsl = _smtp.EnableSsl,
            Credentials = new NetworkCredential(_smtp.Username, _smtp.Password)
        };
        await client.SendMailAsync(msg);
    }

    private async Task<string> LoadTemplateAsync(string fileName)
    {
        var path = Path.Combine(_env.ContentRootPath, "EmailTemplates", fileName);
        return await File.ReadAllTextAsync(path);
    }

    private static string Fill(string template, Dictionary<string, string> values)
    {
        foreach (var kv in values)
            template = template.Replace("{{" + kv.Key + "}}", kv.Value);
        return template;
    }

    private static string Encode(string? value) => HtmlEncoder.Default.Encode(value ?? "");
}
