using System;
using System.IO;
using System.Text.Json;

namespace CloudRouter;

/// <summary>One routing destination (one entry in the Cloud Router submenu).</summary>
public sealed class Destination
{
    public string key { get; set; } = ""; // unique id (no spaces); used in verb command
    public string name { get; set; } = ""; // menu label
    public string path { get; set; } = ""; // target folder (env vars expanded at run time)
    public string subfolder { get; set; } = ""; // optional enforced subfolder
    public string icon { get; set; } = ""; // optional "exe,index" or ".ico" path
}

/// <summary>The Cloud Router configuration, persisted as destinations.json.</summary>
public sealed class CloudConfig
{
    public string action { get; set; } = "move"; // "move" or "copy"
    public Destination[] destinations { get; set; } = Array.Empty<Destination>();

    /// <summary>Where the installed copy of Cloud Router lives (%LOCALAPPDATA%\CloudRouter).</summary>
    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloudRouter");

    /// <summary>Folder the running .exe lives in.</summary>
    public static string AppDir =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory; // ProcessPath handles single-file builds

    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true }, WriteOpts = new() { WriteIndented = true };

    public static CloudConfig Load(string path)
    {
        if (!File.Exists(path)) return new CloudConfig();
        return JsonSerializer.Deserialize<CloudConfig>(File.ReadAllText(path), ReadOpts) ?? new CloudConfig();
    }

    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, WriteOpts));
    }
}
