# Linux Port — Feasibility & Plan

> Status: planning document, not a commitment. Captures the technical reality of porting MoneyShot
> off WPF/Windows so the team can decide whether the cost is worth the user base.

## TL;DR

A Linux port is **possible but is effectively a rewrite of the UI layer and most of the OS-touching
service code**. About **20–25%** of the codebase ports unchanged (models, save/encode logic,
auto-update HTTP/SHA-256 plumbing, settings JSON, history file management, undo records). The
other 75% — every `Window`, every Win32 P/Invoke, every WPF brush/shape, and the entire capture
pipeline — has to be reimplemented against a new UI framework and new OS APIs.

Realistic engineering estimate for a single developer working part-time: **8–14 weeks** for a v1
that matches today's Windows feature set (region capture + annotation editor + tray icon + global
hotkeys), assuming Avalonia is chosen and Wayland support is descoped from v1.

## What ports cleanly (small or no changes)

These files have zero or trivial Windows-specific dependencies and survive a port:

| Area | Files |
|---|---|
| Models | `Models/AnnotationTool.cs`, `Models/CaptureMode.cs`, `Models/SaveDestination.cs`, `Models/AppSettings.cs`, `Models/HistoryEntry.cs` |
| Settings persistence | `Services/SettingsService.cs` — most of it. The two registry hooks (`SetStartupWithWindows`, `SetWindowsPrintScreenDisabled`) are Windows-only and need a Linux equivalent (autostart `.desktop` file in `~/.config/autostart`; the PrintScreen suppression simply has no analogue and should be a no-op) |
| Logger | `Services/Logger.cs` — `%AppData%` resolves to `~/.config/MoneyShot` via `Environment.SpecialFolder.ApplicationData` already; nothing to change |
| Auto-update HTTP & SHA-256 verification | The HttpClient + `SHA256SUMS.txt` parsing logic in `AutoUpdateService.cs`. The exe-swap batch script at the bottom is Windows-only and needs a shell-script equivalent |
| Save encoders | The `BitmapEncoder` calls in `SaveService.cs` — but only the *shape* of the code; the actual encoders come from a different namespace under Avalonia (see § UI framework) |
| History service shape | `HistoryService.cs` — the file IO and JSON survive; the BitmapSource/PngBitmapEncoder calls swap to the Avalonia equivalents |
| Undo records | `Editor/UndoController.cs`, `Editor/ElementState.cs`, `Editor/CanvasPosition.cs`, `Editor/ElementResizeMode.cs` — these are pure C# with `Canvas`/`UIElement` references that are the *same names* in Avalonia, but mapping is not 1:1 |
| Tests | `MoneyShot.Tests/` — should mostly survive once it stops targeting `net10.0-windows` |

## What does **not** port

### 1. The entire UI layer (WPF)

WPF has no Linux runtime. Microsoft has explicitly said they will not port it. Every `*.xaml`,
every `*.xaml.cs`, every `<Window>`, every `Style`, every `Canvas.SetLeft` is dead on arrival.

**The fork in the road: pick a UI framework.**

| Framework | Verdict |
|---|---|
| **Avalonia 11** | **Recommended.** XAML-based, closest API to WPF, mature on Linux (X11 + Wayland). `Canvas`, `Shape`, `Path`, `Polyline`, `TextBlock`, `RenderTargetBitmap` all exist with similar shapes. The editor's code-behind heavy style ports with the least friction here. Native Linux look via FluentTheme. ~80MB single-file publish, comparable to current Windows footprint. |
| Uno Platform | Possible but more work. WPF compatibility shim exists but has gaps. Mainly designed for cross-platform mobile/desktop with WinUI APIs, which are *less* like WPF than Avalonia is. |
| MAUI | Not a serious option — no first-party Linux desktop support as of .NET 10. Community GTK head exists but is not production-grade. |
| GTK# / Gtk4 / GtkSharp | Mature on Linux, terrible on Windows, completely different paradigm — would make the cross-platform story worse, not better. |
| Eto.Forms | Cross-platform but small ecosystem. Suitable for utility UIs, not for a heavily custom-styled editor canvas. |

The recommendation is **Avalonia** because (a) its `Canvas`/`Shape` API maps almost line-for-line to
WPF, which means `EditorWindow`'s 1900-line code-behind ports with mostly mechanical changes;
(b) WPF-style XAML can be reused with minor namespace edits; (c) it has working hotkey, tray, and
clipboard primitives on Linux out of the box.

### 2. Capture pipeline

`ScreenshotService.cs` uses GDI+ (`Graphics.CopyFromScreen`) plus `Imaging.CreateBitmapSourceFromHBitmap`
plus `gdi32!DeleteObject`. None of this exists on Linux. Linux capture has to branch by display
server — and this is where most of the porting risk lives.

#### X11 (still ~70% of Linux desktops in 2026)

- Library: `libxcb` or `libX11` via P/Invoke, or wrap an existing helper.
- Approach: `XGetImage` against the root window for full-screen, or against a specific monitor's
  geometry obtained via `Xinerama`/`XRandR`. Returns an XImage we copy into a managed buffer.
- Multi-monitor: `XRandRGetScreenResources` enumerates outputs.
- Region capture: same as full-screen, then crop in our process (matches the "frozen bitmap"
  pattern we already use on Windows).
- Available .NET wrappers: there's no single canonical one. `Tmds.MDns` won't help here; we'd
  either use a small handwritten P/Invoke layer or pull in something like `SharpHook` or write a
  thin native helper. Estimated 200–400 lines of P/Invoke + marshalling.

#### Wayland (the headache)

- **Wayland deliberately forbids arbitrary screen capture.** A Wayland client cannot just grab
  the framebuffer the way an X11 client can. This is a security feature, not an oversight.
- The supported path is the **XDG Desktop Portal `org.freedesktop.portal.Screenshot`** D-Bus
  service. The user gets a system-rendered consent dialog the first time the app asks, and
  picks the screen/region themselves.
- Implication for MoneyShot: **the "press PrintScreen and instantly grab the screen" UX cannot
  work the same way on Wayland.** The user sees a portal dialog. This is non-negotiable from a
  Wayland-policy standpoint. We can mitigate by remembering the user's choice and using the
  `RestoreToken` mechanism (recent portal versions) to skip the dialog on subsequent grabs in
  the same session.
- The portal returns a file path or a PipeWire stream. PipeWire is needed for live-region or
  delay-and-capture. For a one-shot screenshot, the file path is fine.
- D-Bus library: `Tmds.DBus.Protocol` (current generation) — well-maintained, AOT-friendly.
- Estimated effort: 2–3 weeks alone for a robust Wayland capture path including portal restore
  tokens, error handling for users on compositors that don't expose the portal correctly
  (looking at you, sway pre-1.9), and PipeWire support for region selection.

#### Recommendation for v1

Ship **X11-only** for the first Linux release. Detect Wayland (`$XDG_SESSION_TYPE=wayland`) and
either: (a) refuse to run with a clear error pointing the user to `XWaylandVideoBridge`/X11
session, or (b) launch under XWayland which works for capture but only of the X11 surface tree
(meaning Wayland-native windows won't appear in the capture — usable on KDE/GNOME-Mutter but
broken on hyprland/sway). Pick (a) for honesty; revisit Wayland in v2.

### 3. Global hotkeys

`HotKeyService` uses `RegisterHotKey` against an HWND with `HwndSource.AddHook` to receive
`WM_HOTKEY`. Linux has no direct equivalent.

#### X11

- Use `XGrabKey` on the root window for each modifier+keysym combination, listen for
  `KeyPress` events on the X event queue.
- Subtlety: NumLock and CapsLock count as modifiers in X11. Each desired hotkey has to be
  registered four times (with each combination of NumLock/CapsLock state) or X will silently
  not deliver the event when one is on. This is the source of an enormous percentage of "my
  hotkey works sometimes" bug reports in cross-platform apps. Bake this in.
- Need a dedicated thread or async loop pumping the X event queue. `HotKeyService` today is
  synchronous + relies on the WPF dispatcher — the Linux version has its own pump.

#### Wayland

- **Global hotkeys are not part of Wayland.** Wayland clients can only receive input when their
  surface has focus. Period.
- The XDG Desktop Portal `org.freedesktop.portal.GlobalShortcuts` exists (added 2023) but is
  not yet ubiquitous. GNOME 45+ supports it; KDE Plasma 6 supports it; smaller compositors
  variably do not. Where it's available, the user binds the shortcut through the system
  settings, not in our app — different UX.
- **Pragmatic answer for v1: drop global hotkey support on Wayland.** Document it. Tray icon +
  `xdg-open`-style integration is the alternative.

### 4. System tray (`NotifyIcon`)

`System.Windows.Forms.NotifyIcon` doesn't exist outside Windows. Replacement options:

- **`StatusNotifierItem` (KDE/Plasma, modern GNOME with extensions)** — D-Bus protocol, well-defined.
- **Legacy XEmbed tray (older GNOME, fallback)** — being deprecated; many distros now ship without an
  XEmbed-compatible tray.
- Library: Avalonia's `TrayIcon` class wraps both protocols with reasonable graceful-degradation.
  Use it directly; don't roll our own.
- GNOME without the AppIndicator extension shows no tray at all. This is a known cultural fight;
  document it ("GNOME users may need the AppIndicator extension installed").

### 5. Clipboard image support

`Clipboard.SetImage(BitmapSource)` is WPF-specific and uses Windows clipboard formats. On Linux
the clipboard is X11/Wayland selection-based and image transfer goes through MIME types
(`image/png` typically). Avalonia's `IClipboard.SetDataObjectAsync` handles both, but only after
explicitly registering the PNG-encoded bytes against the right MIME. Roughly 30 lines of code,
plus testing across at least Plasma + GNOME because clipboard managers (Klipper, GPaste) handle
images differently.

### 6. Auto-update self-swap

`AutoUpdateService.BuildWindowsSwapScript` writes a `.bat` that waits for the parent process and
swaps in the new exe. The Linux equivalent is a small `bash` script that does the same thing
(`while kill -0 $PID 2>/dev/null; do sleep 0.1; done; mv new old; exec old`). About 40 lines.
The harder part is **packaging**: an MSI doesn't exist on Linux, so this whole flow assumes a
self-contained tarball / AppImage / portable layout, not a system-managed package. See § Packaging.

### 7. Registry settings

`HKCU\...\Run` (start with Windows) → `~/.config/autostart/moneyshot.desktop` with a
`X-GNOME-Autostart-enabled=true` entry. ~20 lines.

`HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` (suppress Windows Snipping Tool)
→ has no Linux analogue. Different DEs handle PrintScreen differently (GNOME ships its own
screenshot tool bound to PrintScreen; KDE has Spectacle). The setting becomes a no-op on Linux;
document it.

### 8. Path / filesystem assumptions

- `%AppData%` → `XDG_CONFIG_HOME` (`~/.config`). `Environment.SpecialFolder.ApplicationData`
  resolves to the right thing on .NET / Linux already, so most code is fine.
- `MyPictures` → `XDG_PICTURES_DIR` (`~/Pictures`). Also handled by `SpecialFolder.MyPictures`
  on .NET / Linux.
- File-format quirk: nothing to do; PNG/JPEG/BMP encoders are all in Avalonia.

## Architecture for the port

The cleanest way to manage a cross-platform codebase, **without** the maintenance burden of
forking, is to extract Windows-specific code behind a small set of platform interfaces and
provide per-OS implementations that are selected at startup.

```
MoneyShot.Core           // .NET 10, no UI, no Windows deps
├── IScreenCapture       // Capture full / region / monitor → returns IBitmap
├── IGlobalHotkeys       // Register / unregister by string → fires Action
├── ITrayIcon            // Show / hide / context menu
├── IAutoStart           // Enable / disable autostart at login
├── IClipboard           // SetImage(IBitmap)
├── Services/            // SettingsService, HistoryService, AutoUpdateService, Logger, SaveService — UI-free
└── Models/              // existing models

MoneyShot.UI             // Avalonia, cross-platform
├── Views/               // EditorWindow.axaml, MainWindow.axaml, etc.
└── Editor/              // UndoController, CanvasRenderer (port to Avalonia.Media)

MoneyShot.Platform.Windows
├── Win32ScreenCapture   // current GDI+ logic
├── Win32GlobalHotkeys   // current RegisterHotKey logic
├── Win32TrayIcon        // current NotifyIcon usage
└── …

MoneyShot.Platform.Linux
├── X11ScreenCapture
├── X11GlobalHotkeys
├── LinuxTrayIcon        // delegates to Avalonia.Controls.TrayIcon
├── LinuxAutoStart       // .desktop file management
└── …
```

A `PlatformServices.Resolve()` static returns the right implementations based on
`OperatingSystem.IsWindows()` / `IsLinux()`. No DI container needed; matches the existing
"new the services in MainWindow constructor" pattern from `CLAUDE.md`.

## Migration phases

**Phase 0 — extraction (no behavior change, Windows-only).** Pull `IScreenCapture`,
`IGlobalHotkeys`, etc. interfaces out of the existing concrete services. Keep WPF, keep all
existing tests green. ~1 week.

**Phase 1 — UI port to Avalonia, still Windows-only.** Move `MainWindow`, `EditorWindow`,
`HistoryWindow`, `RegionSelector`, `SettingsWindow` to `*.axaml`. Use Avalonia's `Canvas`/`Shape`
hierarchy — it tracks WPF closely enough that the editor's drawing/hit-test/resize logic is
mostly find-and-replace. Re-prove all 95 existing tests pass. The output here is a
*Windows-only Avalonia build* that behaves like today's MoneyShot. **2–3 weeks.** This is the
biggest single chunk and the highest-risk one — if Avalonia turns out to have a blocker (e.g. its
`RenderTargetBitmap` doesn't behave like WPF's for the pixelate brush), this is where we find
out.

**Phase 2 — Linux platform implementations.** X11 capture, X11 hotkeys, Linux autostart, Linux
clipboard, Linux tray. Test on at least: Ubuntu 24.04 + KDE Plasma 6, Fedora + GNOME, Arch + i3.
**3–4 weeks.**

**Phase 3 — Packaging.** AppImage for portability, `.deb` for Ubuntu/Debian users, `.rpm` for
Fedora users. AUR PKGBUILD for Arch is community-maintainable. Add a `release-linux.yml` GitHub
Actions workflow that builds these on `ubuntu-latest`. Sign the AppImage. **1–2 weeks.**

**Phase 4 — Wayland (deferred).** Portal capture, GlobalShortcuts portal where available, MMB
panning still works (already added in this branch). **2–3 weeks if pursued.**

## Packaging on Linux

| Format | Audience | Effort |
|---|---|---|
| **AppImage** | Distro-agnostic, easy for end users. The "MSI equivalent" closest to today's UX. Ships its own .NET runtime in the bundle. | Low — `linuxdeploy` + `appimagetool` in CI |
| **`.deb`** | Ubuntu, Debian, Mint | Low — `dpkg-deb --build` of a templated package skeleton |
| **`.rpm`** | Fedora, openSUSE | Low — `rpmbuild` |
| **Flatpak** | Modern desktops, sandboxed | Medium — needs a manifest, integrates with portals "for free" which would help Wayland support |
| **Snap** | Ubuntu primarily | Medium — and politically charged; many Linux users dislike snaps |
| **AUR (PKGBUILD)** | Arch | Trivial — community-maintained |

Recommendation: ship **AppImage + `.deb` + `.rpm`** in v1, plus an AUR PKGBUILD recipe. Skip
Flatpak/Snap until there's user demand.

## Risks & open questions

- **Avalonia drawing fidelity.** The pixelate effect uses `RenderTargetBitmap.Render(Visual)` to
  rasterise the scene at native resolution and sample colour blocks. Avalonia's
  `RenderTargetBitmap` exists with the same API but I have not verified that
  `BitmapSource.CopyPixels`-style sampling works identically. Needs a spike in Phase 1.
- **GDI handle leak pattern.** `ScreenshotService.ConvertToBitmapSource` does the canonical
  `GetHbitmap` → `CreateBitmapSourceFromHBitmap` → `DeleteObject` dance. There is no equivalent
  on Linux because there are no GDI handles; the X11 path produces a managed pixel buffer
  directly. Less complex, but it means the existing comment about handle leaks doesn't carry
  over and fresh testing for native-memory leaks is needed.
- **Hotkey collisions with the desktop environment.** PrintScreen is bound by every major DE
  to its own screenshot tool. On Windows we have a registry switch to disable Snipping Tool;
  on Linux we'd have to instruct the user to unbind it themselves. This is friction we cannot
  eliminate.
- **Single-instance enforcement.** `App.OnStartup` uses a named `Mutex` for single-instance
  detection. Named mutexes are local-machine and global on Windows; on Linux the equivalent is
  a pidfile under `XDG_RUNTIME_DIR` or a `flock`'d file under `~/.config`. ~10 lines.
- **Auto-update under package managers.** If a user installs via `.deb`/`.rpm`, the auto-updater
  must not silently overwrite a system-managed binary. The portable AppImage flow can self-update;
  the deb/rpm flow should check whether the binary is writable and bail out with "use your
  package manager" if not. This logic is missing today (Windows: always writable inside MSI's
  install dir under our user) and needs adding.
- **HiDPI / fractional scaling.** WPF handles this transparently. Avalonia mostly does, but
  fractional scaling on KDE/GNOME has known rough edges, especially around `RenderTargetBitmap`
  resolution selection. Worth an early spike.

## Decision matrix for the team

| Effort | Reach | Recommendation |
|---|---|---|
| 8–14 weeks dev + ongoing maintenance of two platforms | Adds an estimated low-single-digit % of users (Linux desktop share) | **Worth it only if** there's strategic value (e.g. a corporate deployment that requires it), the team has Linux expertise, or contributor enthusiasm exists. For a hobby/small project, the maintenance tax across two display servers and three init flavors is real and ongoing. |

If a port is greenlit, **start with Phase 0** (interface extraction) — that work is valuable
even if Linux is later cancelled, because it makes the codebase testable and removes implicit
Win32 coupling.

## Files that will need to change (concrete list)

These paths exist today and will be touched in any port. Not exhaustive but covers the bulk:

- `MoneyShot/MoneyShot.csproj` — change TFM to `net10.0` (no `-windows`), drop
  `<UseWPF>`, add Avalonia package references, remove `Microsoft.WindowsDesktop.App.WindowsForms`
- `MoneyShot/Services/ScreenshotService.cs` — full rewrite per platform
- `MoneyShot/Services/HotKeyService.cs` — full rewrite per platform
- `MoneyShot/Services/SettingsService.cs` — strip `Microsoft.Win32.Registry` calls; route
  `SetStartupWithWindows` through a new `IAutoStart` interface
- `MoneyShot/Services/AutoUpdateService.cs` — replace `BuildWindowsSwapScript` with
  per-platform script generation; respect package-manager-managed installs
- `MoneyShot/Services/SaveService.cs` — swap `BitmapEncoder` namespace from WPF to Avalonia
- `MoneyShot/Services/HistoryService.cs` — same encoder swap
- `MoneyShot/MainWindow.xaml` + `.cs` — port to Avalonia `axaml`; replace `NotifyIcon` with
  `Avalonia.Controls.TrayIcon`
- `MoneyShot/Views/EditorWindow.xaml` + `.cs` — port to Avalonia; verify `Canvas`/`Shape`/`Path`
  semantics match; `Clipboard.SetImage` swap; `RenderTargetBitmap` API delta
- `MoneyShot/Views/HistoryWindow.xaml` + `.cs` — port to Avalonia
- `MoneyShot/Views/RegionSelector.xaml` + `.cs` — port to Avalonia; X11 needs special handling
  for "fullscreen overlay across multiple monitors" (it works, but `WindowState=Fullscreen`
  semantics differ)
- `MoneyShot/Views/SettingsWindow.xaml` + `.cs` — port to Avalonia
- `MoneyShot/App.xaml` + `.cs` — replace `Application` base with `Avalonia.Application`;
  switch single-instance mutex to pidfile on Linux
- `Installer/Product.wxs` — Windows-only, leave alone; add new packaging templates beside it
- `.github/workflows/release.yml` — add Linux build matrix (ubuntu-latest job producing
  AppImage + deb + rpm)
- `MoneyShot.Tests/` — drop the `-windows` TFM, audit any test that touches `System.Windows`
  types directly
