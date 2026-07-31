namespace DrSharonKellyEnt.Forms;

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
    // reCAPTCHA v3 score threshold: 0.0 (bot) – 1.0 (human). Start around 0.5 and
    // tighten (raise) if spam still gets through, or loosen (lower) if genuine
    // enquiries are being rejected.
    public double MinimumScore { get; set; } = 0.5;
}

public class RateLimitOptions
{
    public int MaxRequestsPerIpPerWindow { get; set; } = 5;
    public int WindowMinutes { get; set; } = 10;
}

public class FormsLogOptions
{
    public string FilePath { get; set; } = "App_Data/forms-log.txt";
}

public class SiteOptions
{
    public string Name { get; set; } = "Dr Sharon Kelly — ENT Surgeon";
    public string AccentColor { get; set; } = "#AD8A54";
    public string ReplyToAddress { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";
}

public class ContactOptions
{
    public string[] WorkflowRecipients { get; set; } = Array.Empty<string>();
}

public class ReferralOptions
{
    public string[] WorkflowRecipients { get; set; } = Array.Empty<string>();
}
