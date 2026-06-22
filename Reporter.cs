using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace CloudRouter;

/// <summary>
/// Error logging and one-click problem reporting. Nothing is sent automatically:
/// errors go to a local log, and "Report a problem" opens a PRE-FILLED GitHub
/// issue in the browser, where the user reviews it and clicks submit. No API
/// token, no server, no telemetry - a token shipped inside the app would just
/// get extracted and abused, so the user's own GitHub login does the posting.
/// </summary>
public static class Reporter
{
    public const string RepoUrl = "https://github.com/DatJavaClass/CloudRouter";

    public const string Disclaimer =
        "Cloud Router is a tiny one-person project. I can't promise a fix or a " +
        "timeline, but I read every report and I'll do my best.";

    public static string LogPath => Path.Combine(CloudConfig.InstallDir, "cloudrouter.log");

    /// <summary>Append an error to the local log. Never throws.</summary>
    public static void Log(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(CloudConfig.InstallDir);
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
            sb.AppendLine();
            File.AppendAllText(LogPath, sb.ToString());
        }
        catch { /* logging must never break the app */ }
    }

    /// <summary>Open a pre-filled GitHub issue in the browser. The user reviews and submits.</summary>
    public static void OpenReport(string details)
    {
        try
        {
            var body = new StringBuilder();
            body.AppendLine("**What I was doing** (add anything that helps):");
            body.AppendLine();
            body.AppendLine();
            body.AppendLine("**Details** (please keep):");
            body.AppendLine();
            body.AppendLine("```");
            body.AppendLine(details);
            body.AppendLine($"App: Cloud Router {AppVersion}");
            body.AppendLine($"OS:  {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")})");
            body.AppendLine();
            body.AppendLine(TailLog(900));
            body.AppendLine("```");

            string url = $"{RepoUrl}/issues/new" +
                         "?labels=bug" +
                         $"&title={Uri.EscapeDataString("Error report")}" +
                         $"&body={Uri.EscapeDataString(body.ToString())}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* if the browser won't open, the log file still has everything */ }
    }

    private static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";

    private static string TailLog(int maxChars)
    {
        try
        {
            if (!File.Exists(LogPath)) return "(no log yet)";
            string text = File.ReadAllText(LogPath);
            return text.Length <= maxChars ? text : "..." + text[^maxChars..];
        }
        catch { return "(log unavailable)"; }
    }
}
