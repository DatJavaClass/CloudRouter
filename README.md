<p align="center"><img width="651" height="701" alt="ChatGPT Image Jun 11, 2026, 12_49_40 PM" src="https://github.com/user-attachments/assets/99da5453-5993-4993-8aac-51c0a4b6510c" /></p>

# Cloud Router

Send any file to the cloud straight from the right-click menu. Cloud Router adds
a single **Cloud Router** entry to Explorer's context menu that fans out into your
cloud and network folders. Pick one, and the file (or folder) is moved there. No
dragging across windows, no hunting through a Save As dialog.

It finds your cloud folders for you (Dropbox, OneDrive, Google Drive, Box, Mega,
Proton Drive, iCloud, plus mapped and network drives), and you can add any other
folder by hand, including UNC shares and WebDAV.

*Built and vibed with Victor S. (DatJavaClass) and his trusty AI assistant who isn't afraid of Ghosts.*

## Download

**[Download Cloud Router](https://github.com/DatJavaClass/CloudRouter/releases/latest/download/CloudRouterSetup.exe)** &nbsp;|&nbsp; Windows 10 / 11

One file, nothing else to install. Double-click it, tick the folders you want,
and you're done. (Windows may show a SmartScreen prompt the first time, since the
app isn't signed yet: choose **More info**, then **Run anyway**.)

## What you get

- A **Cloud Router** submenu on right-click, each destination shown with its real icon.
- Move or copy, your choice.
- An optional **subfolder** rule per destination, so a drive root can quietly
  funnel everything into, say, an `Inbox` instead of dumping files at the top.
- Sensible handling: duplicate names become `file (2).ext`, and moves across
  different drives just work.
- Per-user install. No admin rights. Uninstalls clean.

## Install

Download `CloudRouterSetup.exe` above and run it. The **setup window** opens and
lists every cloud folder it found: tick the ones you want, hit **Add folder...**
for a network or shared folder it missed, choose Move or Copy, and click
**Install**. That registers the menu under `%LOCALAPPDATA%\CloudRouter` (per-user,
no admin rights).

Now right-click any file. On Windows 11 the menu lives under **Show more options**
(or tap `Shift+F10` to jump straight there).

### Build from source

Prefer to build it yourself? You'll need the
[.NET SDK](https://dotnet.microsoft.com/download) (9 or newer):

```powershell
git clone https://github.com/DatJavaClass/CloudRouter.git
cd CloudRouter
./build.ps1
./publish/CloudRouter.exe
```

The exe also takes `--install [config] [dir]` and `--uninstall` for the command
line, and `./install.ps1` builds and registers from `destinations.json` directly.

## Adding folders by hand

Detection covers the common apps, but it can't guess a random network share.
Click **Add folder...**, then browse to it or paste a path directly:

- A local folder: `D:\Archive`
- A network share: `\\nas\backups`
- A drive root plus a subfolder, so files land in `S:\Inbox` instead of `S:\`

Give it a name, set a subfolder if you want one, and it joins the menu like any
other destination.

## Tweaking it later

Everything lives in `destinations.json` next to the app. The setup window writes
it for you, but it's plain text if you'd rather edit by hand:

```json
{
  "action": "move",
  "destinations": [
    { "key": "dropbox", "name": "Dropbox", "path": "%USERPROFILE%\\Dropbox" },
    { "key": "backup",  "name": "Backup",  "path": "W:\\", "subfolder": "Inbox" }
  ]
}
```

`action` is `move` or `copy` for the whole menu. Each destination needs a `name`
and a `path` (environment variables work). `subfolder` and a custom `icon` are
optional. Re-run setup, or `install.ps1`, to apply changes.

## Uninstall

```powershell
./uninstall.ps1
```

Or `CloudRouter.exe --uninstall`. Either way, the menu and the installed copy are
removed.

## Under the hood

Cloud Router is one small executable. With no arguments it shows the setup window;
the context menu runs it as `CloudRouter.exe move <destination> "<file>"`. It
registers a per-user cascading menu (plain registry entries, no background
service), so nothing is running when you're not using it.

## Good to know

- It uses the classic context menu, which is why Windows 11 keeps it under "Show
  more options". That's the trade-off for an install with no signing and no fuss.
- Select several files and each one is routed in turn (a quick notification each).
- The app isn't code-signed, so the first launch may show a SmartScreen prompt.
  Choose **More info**, then **Run anyway**.
- Folder icons are read from the apps you already have installed; no logos are
  bundled with the project.

## Reporting issues

Found a bug? When an error pops up, click **Report a problem**: it opens a new
**GitHub issue** in your browser with the details already filled in. Give it a
read and submit (you'll need a free GitHub account). You can also open an issue
by hand from the **Issues** tab.

Cloud Router is a tiny one-person project, so I can't promise a fix or a timeline,
but I read every report and I'll do my best.

## License

MIT (c) Victor S. (DatJavaClass). See [LICENSE](LICENSE).
