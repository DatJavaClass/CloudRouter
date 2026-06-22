using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CloudRouter;

/// <summary>
/// Deploys Cloud Router and registers / unregisters its shell context menu.
/// Everything is per-user (HKCU + %LOCALAPPDATA%), so no admin rights and no
/// PowerShell are required - the setup GUI calls straight into this.
/// </summary>
public static class Installer
{
    private const string SubMenuKey = "CloudRouter.Sub";
    private const string FileVerb   = @"Software\Classes\*\shell\CloudRouter";
    private const string FolderVerb = @"Software\Classes\Directory\shell\CloudRouter";

    /// <summary>
    /// Copy the helper into the install dir, write destinations.json, and register
    /// the submenu. <paramref name="installDir"/> is overridable for testing.
    /// </summary>
    public static void Install(CloudConfig cfg, string? installDir = null)
    {
        installDir ??= CloudConfig.InstallDir;
        Directory.CreateDirectory(installDir);

        // Keys become command-line arguments and registry sub-key names, so make
        // them safe, unique slugs and store the same value we register.
        NormalizeKeys(cfg);

        string exe = Path.Combine(installDir, "CloudRouter.exe");
        string ico = Path.Combine(installDir, "CloudRouter.ico");

        // Deploy the helper. The single-file release is just the .exe (no loose deps);
        // a dev build also has a few siblings. Copy ONLY the exe and those known
        // siblings - never "every file beside me", since the single-file exe may be
        // run straight from a Downloads folder full of unrelated files.
        string exeSource = Environment.ProcessPath ?? exe;
        string srcDir = Path.GetDirectoryName(exeSource) ?? CloudConfig.AppDir;
        if (!string.Equals(srcDir.TrimEnd('\\'), installDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(exeSource, exe, overwrite: true);
            foreach (string name in new[] { "CloudRouter.dll", "CloudRouter.deps.json", "CloudRouter.runtimeconfig.json", "CloudRouter.ico" })
            {
                string sib = Path.Combine(srcDir, name);
                if (File.Exists(sib)) File.Copy(sib, Path.Combine(installDir, name), overwrite: true);
            }
        }

        // The single-file build ships no loose .ico, so generate it if absent.
        if (!File.Exists(ico))
        {
            try { IconMaker.Write(ico); } catch { /* icon is cosmetic */ }
        }

        cfg.Save(Path.Combine(installDir, "destinations.json"));

        WriteRegistry(cfg, exe, ico);
        RefreshShell();
    }

    public static void Uninstall(bool removeFiles = true)
    {
        RemoveTree(FileVerb);
        RemoveTree(FolderVerb);
        RemoveTree($@"Software\Classes\{SubMenuKey}");
        if (removeFiles)
        {
            try { if (Directory.Exists(CloudConfig.InstallDir)) Directory.Delete(CloudConfig.InstallDir, true); }
            catch { /* a running helper may hold a lock; ignore */ }
        }
        RefreshShell();
    }

    private static void WriteRegistry(CloudConfig cfg, string exe, string ico)
    {
        // Start clean so renamed/removed destinations don't leave orphans.
        RemoveTree(FileVerb);
        RemoveTree(FolderVerb);
        RemoveTree($@"Software\Classes\{SubMenuKey}");

        foreach (string parent in new[] { FileVerb, FolderVerb })
        {
            using var k = Registry.CurrentUser.CreateSubKey(parent);
            k!.SetValue("MUIVerb", "Cloud Router");
            if (File.Exists(ico)) k.SetValue("Icon", ico);
            k.SetValue("ExtendedSubCommandsKey", SubMenuKey);
        }

        string action = string.Equals(cfg.action, "copy", StringComparison.OrdinalIgnoreCase) ? "copy" : "move";
        int i = 0;
        foreach (var d in cfg.destinations)
        {
            i++;
            string baseKey = $@"Software\Classes\{SubMenuKey}\shell\{i:00}{d.key}";
            using (var k = Registry.CurrentUser.CreateSubKey(baseKey))
            {
                k!.SetValue("MUIVerb", d.name);
                if (!string.IsNullOrWhiteSpace(d.icon)) k.SetValue("Icon", d.icon);
            }
            using var c = Registry.CurrentUser.CreateSubKey(baseKey + @"\command");
            c!.SetValue("", $"\"{exe}\" {action} {d.key} \"%1\"");
        }
    }

    private static void NormalizeKeys(CloudConfig cfg)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in cfg.destinations)
        {
            string slug = Sanitize(d.key);
            if (string.IsNullOrEmpty(slug)) slug = Sanitize(d.name);
            if (string.IsNullOrEmpty(slug)) slug = "dest";
            string key = slug;
            int n = 1;
            while (!used.Add(key.ToLowerInvariant())) key = slug + (++n);
            d.key = key;
        }
    }

    private static string Sanitize(string key) => new(key.Where(char.IsLetterOrDigit).ToArray());

    private static void RemoveTree(string sub)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(sub, throwOnMissingSubKey: false); } catch { }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr a, IntPtr b);

    private static void RefreshShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
}
