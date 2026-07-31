using System.Text;

namespace DrSharonKellyEnt.Forms;

// Appends one line per submission (accepted, blocked, or failed) to a local
// text file — shared by both forms, tagged with which form it was.
public class FormsLogger
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FormsLogger(Microsoft.Extensions.Options.IOptions<FormsLogOptions> options, IWebHostEnvironment env)
    {
        var configured = options.Value.FilePath;
        _path = Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public async Task LogAsync(string formType, string status, string ip, params (string Key, string Value)[] fields)
    {
        var parts = new List<string> { DateTimeOffset.UtcNow.ToString("u"), formType, status };
        foreach (var (key, value) in fields) parts.Add($"{key}=\"{Sanitize(value)}\"");
        parts.Add($"ip={ip}");
        var line = string.Join(" | ", parts);

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
