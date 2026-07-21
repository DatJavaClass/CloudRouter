using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace CloudRouter;

/// <summary>Explorer submenu helper: CloudRouter.exe move|copy &lt;key&gt; "&lt;path&gt;".</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();

            string verb = args.Length == 0 ? "--setup" : args[0].ToLowerInvariant();
            switch (verb)
            {
                case "--setup":
                {
                    var app = new System.Windows.Application();
                    app.Run(new SetupWindow());
                    return 0;
                }
                case "--makeicon": // build-time helper: generate CloudRouter.ico
                {
                    string outPath = args.Length >= 2
                        ? args[1]
                        : Path.Combine(CloudConfig.AppDir, "CloudRouter.ico");
                    IconMaker.Write(outPath);
                    return 0;
                }
                case "--detect": // find cloud/remote folders, write them as JSON
                {
                    string outPath = args.Length >= 2
                        ? args[1]
                        : Path.Combine(CloudConfig.AppDir, "detected.json");
                    var found = Detection.Detect();
                    File.WriteAllText(outPath,
                        JsonSerializer.Serialize(found, new JsonSerializerOptions { WriteIndented = true }));
                    return 0;
                }
                case "--install": // headless; the setup GUI calls Installer directly
                {
                    string cfgPath = args.Length >= 2 ? args[1] : Path.Combine(CloudConfig.AppDir, "destinations.json");
                    string? dir = args.Length >= 3 ? args[2] : null;
                    Installer.Install(CloudConfig.Load(cfgPath), dir);
                    return 0;
                }
                case "--uninstall":
                {
                    Installer.Uninstall(removeFiles: !(args.Length >= 2 && args[1].Equals("keepfiles", StringComparison.OrdinalIgnoreCase)));
                    return 0;
                }
            }

            if (args.Length < 3)
            {
                ShowError("Cloud Router was invoked incorrectly.\n\n" +
                          "Expected:  CloudRouter <move|copy> <destinationKey> <itemPath>");
                return 2;
            }

            string action = args[0].Trim().ToLowerInvariant(), key = args[1].Trim();
            // Path is last; re-join an unquoted tail.
            string source = string.Join(' ', args.Skip(2)).Trim().Trim('"');
            bool copy = action == "copy";

            CloudConfig cfg = CloudConfig.Load(Path.Combine(CloudConfig.AppDir, "destinations.json"));
            Destination? dest = cfg.destinations.FirstOrDefault(
                d => string.Equals(d.key, key, StringComparison.OrdinalIgnoreCase));
            if (dest is null)
            {
                ShowError($"Unknown Cloud Router destination: \"{key}\".");
                return 3;
            }

            string destDir = Environment.ExpandEnvironmentVariables(dest.path);

            // Enforced subfolder always applies; config drift cannot bypass.
            if (!string.IsNullOrWhiteSpace(dest.subfolder))
            {
                string sub = Environment.ExpandEnvironmentVariables(dest.subfolder).Trim('\\', '/');
                if (!destDir.TrimEnd('\\', '/').EndsWith(sub, StringComparison.OrdinalIgnoreCase))
                    destDir = Path.Combine(destDir, sub);
            }

            if (string.IsNullOrWhiteSpace(source) ||
                (!File.Exists(source) && !Directory.Exists(source)))
            {
                ShowError($"The item could not be found:\n{source}");
                return 5;
            }

            try
            {
                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);
            }
            catch (Exception ex)
            {
                ShowError($"\"{dest.name}\" is not available right now:\n{destDir}\n\n{ex.Message}");
                return 4;
            }

            bool isDir = Directory.Exists(source);
            string itemName = Path.GetFileName(source.TrimEnd('\\', '/'));
            string target = UniquePath(Path.Combine(destDir, itemName), isDir);

            if (isDir)
            {
                if (copy) CopyDirectory(source, target);
                else MoveDirectory(source, target);
            }
            else
            {
                if (copy) File.Copy(source, target, overwrite: false);
                else File.Move(source, target); // fast rename on-volume; copy+delete across volumes
            }

            Notify(dest.name, itemName, target, copy);
            return 0;
        }
        catch (Exception ex)
        {
            Reporter.Log("route", ex);
            var choice = MessageBox.Show(
                "Cloud Router could not complete the request:\n\n" + ex.Message +
                "\n\n" + Reporter.Disclaimer + "\n\nReport this problem? (opens a GitHub issue in your browser)",
                "Cloud Router", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (choice == DialogResult.Yes) Reporter.OpenReport(ex.ToString());
            return 1;
        }
    }

    /// <summary>Explorer-style collision handling: "name (2).ext", "name (3).ext", ...</summary>
    private static string UniquePath(string desired, bool isDir)
    {
        if (!File.Exists(desired) && !Directory.Exists(desired))
            return desired;

        string dir = Path.GetDirectoryName(desired)!;
        string stem = isDir ? Path.GetFileName(desired) : Path.GetFileNameWithoutExtension(desired);
        string ext = isDir ? "" : Path.GetExtension(desired);

        for (int i = 2; i < 100000; i++)
        {
            string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
        throw new IOException("Could not find a free name in the destination folder.");
    }

    private static void MoveDirectory(string source, string target)
    {
        try
        {
            Directory.Move(source, target); // instant on the same volume
        }
        catch (IOException) // crossing volumes (C: to a WebDAV/NAS drive)
        {
            CopyDirectory(source, target);
            Directory.Delete(source, recursive: true);
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: false);
        foreach (string sub in Directory.GetDirectories(sourceDir))
            CopyDirectory(sub, Path.Combine(destDir, Path.GetFileName(sub)));
    }

    /// <summary>Brief toast via NotifyIcon; click reveals the item.</summary>
    private static void Notify(string folder, string item, string target, bool copied)
    {
        try
        {
            using var ni = new NotifyIcon();
            string iconPath = Path.Combine(CloudConfig.AppDir, "CloudRouter.ico");
            ni.Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Information;
            ni.Visible = true;
            ni.BalloonTipTitle = "Cloud Router";
            ni.BalloonTipText = $"{(copied ? "Copied" : "Moved")} “{item}” to {folder}.";
            ni.BalloonTipIcon = ToolTipIcon.None;

            bool clicked = false;
            ni.BalloonTipClicked += (_, _) =>
            {
                clicked = true;
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"")
                    { UseShellExecute = true });
                }
                catch { /* best effort */ }
            };
            ni.ShowBalloonTip(2000);

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 2200 && !clicked)
            {
                Application.DoEvents();
                Thread.Sleep(40);
            }
            ni.Visible = false;
        }
        catch { /* never fail the move over a toast */ }
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "Cloud Router", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

/// <summary>Draws the cloud-with-upload-arrow app icon into a multi-size .ico.</summary>
internal static class IconMaker
{
    public static void Write(string outPath)
    {
        int[] sizes = { 256, 128, 64, 48, 32, 16 };
        var frames = sizes.Select(s => (size: s, png: RenderPng(s))).ToList();

        string? dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var fs = new FileStream(outPath, FileMode.Create);
        using var bw = new BinaryWriter(fs);

        bw.Write((ushort)0); // reserved
        bw.Write((ushort)1); // type: icon
        bw.Write((ushort)frames.Count); // image count

        int offset = 6 + 16 * frames.Count;
        foreach (var (size, png) in frames)
        {
            byte dim = (byte)(size >= 256 ? 0 : size); // 0 means 256 in ICO
            bw.Write(dim); // width
            bw.Write(dim); // height
            bw.Write((byte)0); // palette
            bw.Write((byte)0); // reserved
            bw.Write((ushort)1); // color planes
            bw.Write((ushort)32); // bits per pixel
            bw.Write((uint)png.Length); // size of data
            bw.Write((uint)offset); // offset of data
            offset += png.Length;
        }
        foreach (var (_, png) in frames)
            bw.Write(png);
    }

    private static byte[] RenderPng(int s)
    {
        using var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            DrawCloud(g, s);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static void DrawCloud(Graphics g, int s)
    {
        float S = s;
        using var path = new GraphicsPath { FillMode = FillMode.Winding };
        path.AddEllipse(S * 0.10f, S * 0.44f, S * 0.42f, S * 0.42f);
        path.AddEllipse(S * 0.30f, S * 0.30f, S * 0.46f, S * 0.46f);
        path.AddEllipse(S * 0.48f, S * 0.42f, S * 0.40f, S * 0.40f);
        AddRoundedRect(path, S * 0.14f, S * 0.56f, S * 0.72f, S * 0.30f, S * 0.14f);

        using var brush = new LinearGradientBrush(
            new RectangleF(0, S * 0.25f, S, S * 0.62f),
            Color.FromArgb(255, 88, 165, 232), // light cloud blue
            Color.FromArgb(255, 27, 110, 194), // deeper blue
            LinearGradientMode.Vertical);
        g.FillPath(brush, path);

        // Upload arrow: send it to the cloud.
        using var white = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
        using var arrow = new GraphicsPath { FillMode = FillMode.Winding };
        float cx = S * 0.50f;
        float top = S * 0.45f, bot = S * 0.74f, hw = S * 0.12f, sw = S * 0.05f;
        arrow.AddPolygon(new[]
        {
            new PointF(cx, top),
            new PointF(cx - hw, top + hw),
            new PointF(cx - sw, top + hw),
            new PointF(cx - sw, bot),
            new PointF(cx + sw, bot),
            new PointF(cx + sw, top + hw),
            new PointF(cx + hw, top + hw),
        });
        g.FillPath(white, arrow);
    }

    private static void AddRoundedRect(GraphicsPath path, float x, float y, float w, float h, float r)
    {
        float d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
    }
}
