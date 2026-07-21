using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace CloudRouter;

/// <summary>One destination candidate; maps 1:1 onto a destinations.json entry.</summary>
public sealed class Candidate
{
    public string provider { get; set; } = ""; // "OneDrive", "Dropbox", "Drive", "Manual", ...
    public string name { get; set; } = ""; // suggested menu label
    public string path { get; set; } = ""; // detected folder ("" if unknown)
    public string subfolder { get; set; } = ""; // suggested enforced subfolder (usually "")
    public string icon { get; set; } = ""; // "exe,index" or ".ico" path, best effort
    public string source { get; set; } = "auto"; // auto | manual
    public string confidence { get; set; } = "high"; // high | low (low = please confirm/edit)
    public string note { get; set; } = ""; // hint shown next to the row
}

/// <summary>Best-effort cloud/remote folder discovery. Probes are isolated;
/// detection is a convenience, the manual add is the guarantee.</summary>
public static class Detection
{
    public static List<Candidate> Detect()
    {
        var list = new List<Candidate>();
        Add(list, DetectOneDrive);
        Add(list, DetectDropbox);
        Add(list, DetectGoogleDrive);
        Add(list, DetectBox);
        Add(list, DetectProton);
        Add(list, DetectMega);
        Add(list, DetectICloud);
        Add(list, DetectDrives); // mapped network + WebDAV/NAS + extra local drives
        return list;
    }

    private static void Add(List<Candidate> list, Func<IEnumerable<Candidate>> probe)
    {
        try { list.AddRange(probe()); } catch { /* one provider miss must not break setup */ }
    }

    // Providers.

    private static IEnumerable<Candidate> DetectOneDrive()
    {
        using var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
        if (accounts is null) yield break;

        string icon = IconFrom(Expand(@"%LOCALAPPDATA%\Microsoft\OneDrive\OneDrive.exe"), 5);
        foreach (string sub in accounts.GetSubKeyNames())
        {
            using var k = accounts.OpenSubKey(sub);
            string? folder = k?.GetValue("UserFolder") as string;
            string? display = k?.GetValue("DisplayName") as string;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;

            string name = sub.StartsWith("Business", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(display)
                ? $"OneDrive - {display}"
                : "OneDrive";
            yield return new Candidate { provider = "OneDrive", name = name, path = folder!, icon = icon };
        }
    }

    private static IEnumerable<Candidate> DetectDropbox()
    {
        string info = Expand(@"%LOCALAPPDATA%\Dropbox\info.json");
        if (!File.Exists(info)) yield break;

        string icon = IconFrom(FirstExisting(
            @"C:\Program Files (x86)\Dropbox\Client\Dropbox.exe",
            @"C:\Program Files\Dropbox\Client\Dropbox.exe"), -13001);

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(info));
        foreach (var acct in doc.RootElement.EnumerateObject())
        {
            if (!acct.Value.TryGetProperty("path", out var p)) continue;
            string? path = p.GetString();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) continue;
            string name = acct.Name.Equals("personal", StringComparison.OrdinalIgnoreCase) ? "Dropbox" : $"Dropbox ({acct.Name})";
            yield return new Candidate { provider = "Dropbox", name = name, path = path!, icon = icon };
        }
    }

    private static IEnumerable<Candidate> DetectGoogleDrive()
    {
        string icon = GoogleDriveIcon();

        string? mount = null;
        using (var dfs = Registry.CurrentUser.OpenSubKey(@"Software\Google\DriveFS"))
            mount = dfs?.GetValue("DefaultMountPoint") as string;

        // Fallback: the drive whose volume label is "Google Drive".
        if (string.IsNullOrWhiteSpace(mount))
        {
            foreach (var d in SafeDrives())
            {
                if (string.Equals(SafeLabel(d), "Google Drive", StringComparison.OrdinalIgnoreCase))
                { mount = d.RootDirectory.FullName; break; }
            }
        }
        if (string.IsNullOrWhiteSpace(mount)) yield break;

        string myDrive = Path.Combine(mount!, "My Drive"); // prefer the My Drive root
        string path = Directory.Exists(myDrive) ? myDrive : mount!;
        yield return new Candidate { provider = "Google Drive", name = "Google Drive", path = path, icon = icon };
    }

    private static IEnumerable<Candidate> DetectBox()
    {
        string box = Expand(@"%USERPROFILE%\Box");
        if (!Directory.Exists(box)) yield break;
        yield return new Candidate { provider = "Box", name = "Box", path = box, icon = IconFrom(@"C:\Program Files\Box\Box\Box.exe", 0) };
    }

    private static IEnumerable<Candidate> DetectProton()
    {
        string root = Expand(@"%USERPROFILE%\ProtonDrive");
        if (!Directory.Exists(root)) yield break;
        string myFiles = Path.Combine(root, "My files");
        string path = Directory.Exists(myFiles) ? myFiles : root;
        yield return new Candidate
        {
            provider = "Proton Drive",
            name = "Proton Drive",
            path = path,
            icon = IconFrom(Expand(@"%LOCALAPPDATA%\Programs\Proton\Drive\ProtonDrive.exe"), 0)
        };
    }

    private static IEnumerable<Candidate> DetectMega()
    {
        // MEGAsync hides its folder in a binary cache; confirm app, guess path.
        if (!Directory.Exists(Expand(@"%LOCALAPPDATA%\Mega Limited\MEGAsync"))) yield break;

        string icon = IconFrom(FirstExisting(
            @"C:\Program Files\MEGAsync\MEGAsync.exe",
            @"C:\Program Files (x86)\MEGAsync\MEGAsync.exe"), 0);
        string guess = Expand(@"%USERPROFILE%\MEGA");
        bool exists = Directory.Exists(guess);
        yield return new Candidate
        {
            provider = "Mega",
            name = "Mega",
            path = exists ? guess : "",
            icon = icon,
            confidence = "low",
            note = exists ? "guessed default folder - confirm" : "MEGAsync installed - set your sync folder"
        };
    }

    private static IEnumerable<Candidate> DetectICloud()
    {
        string p = Expand(@"%USERPROFILE%\iCloudDrive");
        if (!Directory.Exists(p)) yield break;
        yield return new Candidate { provider = "iCloud", name = "iCloud Drive", path = p };
    }

    private static IEnumerable<Candidate> DetectDrives()
    {
        string sysRoot = (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\").TrimEnd('\\');
        foreach (var d in SafeDrives())
        {
            string letter = d.Name.TrimEnd('\\');
            if (string.Equals(letter, sysRoot, StringComparison.OrdinalIgnoreCase)) continue; // skip the OS drive
            if (d.DriveType is not (DriveType.Network or DriveType.Fixed or DriveType.Removable)) continue;

            string label = SafeLabel(d);
            string name = string.IsNullOrWhiteSpace(label) ? $"Drive {letter}" : $"{label} ({letter})";
            yield return new Candidate
            {
                provider = "Drive",
                name = name,
                path = d.RootDirectory.FullName,
                source = "auto",
                confidence = d.DriveType == DriveType.Network ? "high" : "low",
                note = $"{d.DriveType} drive - consider a subfolder so files don't land at the root"
            };
        }
    }

    // Helpers.

    private static IEnumerable<DriveInfo> SafeDrives()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); } catch { yield break; }
        foreach (var d in drives)
        {
            bool ready; try { ready = d.IsReady; } catch { ready = false; }
            if (ready) yield return d;
        }
    }

    private static string SafeLabel(DriveInfo d)
    {
        try { return d.VolumeLabel ?? ""; } catch { return ""; }
    }

    private static string Expand(string p) => Environment.ExpandEnvironmentVariables(p);

    private static string IconFrom(string exe, int index)
        => !string.IsNullOrEmpty(exe) && File.Exists(exe) ? $"{exe},{index}" : "";

    private static string FirstExisting(params string[] paths)
        => paths.FirstOrDefault(File.Exists) ?? "";

    private static string GoogleDriveIcon()
    {
        foreach (string baseDir in new[]
        {
            @"C:\Program Files\Google\Drive File Stream",
            @"C:\Program Files (x86)\Google\Drive File Stream"
        })
        {
            if (!Directory.Exists(baseDir)) continue;
            string? newest = Directory.GetDirectories(baseDir).OrderByDescending(d => d).FirstOrDefault();
            if (newest is null) continue;
            string exe = Path.Combine(newest, "GoogleDriveFS.exe");
            if (File.Exists(exe)) return $"{exe},48";
        }
        return "";
    }
}
