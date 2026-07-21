using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CloudRouter;

/// <summary>Setup window: pick detected or manual folders, then install.</summary>
public partial class SetupWindow : Window
{
    /// <summary>A grid row. Plain mutable POCO; the DataGrid two-way binds into it.</summary>
    public sealed class Row
    {
        public bool Include { get; set; }
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Subfolder { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Icon { get; set; } = "";
    }

    private readonly ObservableCollection<Row> _rows = new();

    public SetupWindow()
    {
        InitializeComponent();
        DestGrid.ItemsSource = _rows;

        try
        {
            string ico = System.IO.Path.Combine(AppContext.BaseDirectory, "CloudRouter.ico");
            if (File.Exists(ico)) Icon = new BitmapImage(new Uri(ico));
        }
        catch { /* window icon is cosmetic */ }

        Loaded += (_, _) => LoadRows();
    }

    private void LoadRows()
    {
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Pre-fill from an existing install.
            string cfgPath = System.IO.Path.Combine(CloudConfig.InstallDir, "destinations.json");
            if (File.Exists(cfgPath))
            {
                var existing = CloudConfig.Load(cfgPath);
                bool copy = string.Equals(existing.action, "copy", StringComparison.OrdinalIgnoreCase);
                MoveRadio.IsChecked = !copy;
                CopyRadio.IsChecked = copy;
                foreach (var d in existing.destinations)
                {
                    if (!seen.Add(RowKey(d.path, d.subfolder))) continue;
                    _rows.Add(new Row { Include = true, Name = d.name, Path = d.path, Subfolder = d.subfolder, Provider = "Saved", Icon = d.icon });
                }
            }

            // Add anything detection finds not already listed.
            foreach (var c in Detection.Detect())
            {
                if (string.IsNullOrWhiteSpace(c.path)) continue;
                if (!seen.Add(RowKey(c.path, c.subfolder))) continue;
                _rows.Add(new Row
                {
                    Include = c.confidence != "low" && c.provider != "Drive",
                    Name = c.name,
                    Path = c.path,
                    Subfolder = c.subfolder,
                    Provider = c.provider,
                    Icon = c.icon
                });
            }

            Status.Text = _rows.Count == 0
                ? "No folders detected. Use \"Add folder...\" to point at one."
                : $"{_rows.Count} folder(s) listed. Tick the ones you want, then Install.";
        }
        catch (Exception ex)
        {
            Reporter.Log("setup-detect", ex);
            Status.Foreground = Brushes.Firebrick;
            Status.Text = ex.Message;
        }
    }

    private static string RowKey(string path, string sub) => (path.TrimEnd('\\', '/') + "|" + sub).ToLowerInvariant();

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        // Dialog also accepts a pasted UNC path.
        var dlg = new OpenFolderDialog { Title = "Pick a folder (or paste a UNC / network path)" };
        if (dlg.ShowDialog(this) == true)
        {
            string p = dlg.FolderName;
            _rows.Add(new Row
            {
                Include = true,
                Name = System.IO.Path.GetFileName(p.TrimEnd('\\', '/')),
                Path = p,
                Provider = "Manual"
            });
            DestGrid.SelectedItem = _rows.Last();
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (DestGrid.SelectedItem is Row r) _rows.Remove(r);
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        DestGrid.CommitEdit(); // flush any in-progress cell edit
        try
        {
            var chosen = _rows.Where(r => r.Include && !string.IsNullOrWhiteSpace(r.Path)).ToList();
            if (chosen.Count == 0)
            {
                Status.Foreground = Brushes.Firebrick;
                Status.Text = "Tick at least one folder to install.";
                return;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cfg = new CloudConfig
            {
                action = CopyRadio.IsChecked == true ? "copy" : "move",
                destinations = chosen.Select(r => new Destination
                {
                    key = UniqueKey(r.Name, used),
                    name = string.IsNullOrWhiteSpace(r.Name) ? r.Path : r.Name,
                    path = r.Path.Trim(),
                    subfolder = (r.Subfolder ?? "").Trim(),
                    icon = (r.Icon ?? "").Trim()
                }).ToArray()
            };

            Installer.Install(cfg);

            Status.Foreground = Brushes.SeaGreen;
            Status.Text = $"Installed {cfg.destinations.Length} destination(s). Right-click a file and look under \"Show more options\" → Cloud Router.";
        }
        catch (Exception ex)
        {
            Reporter.Log("setup-install", ex);
            var choice = MessageBox.Show(this,
                ex.Message + "\n\n" + Reporter.Disclaimer + "\n\nReport this problem? (opens a GitHub issue in your browser)",
                "Cloud Router", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Yes) Reporter.OpenReport(ex.ToString());
        }
    }

    private static string UniqueKey(string name, HashSet<string> used)
    {
        string slug = new((name ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(slug)) slug = "dest";
        string key = slug;
        int n = 1;
        while (!used.Add(key)) key = slug + (++n);
        return key;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
