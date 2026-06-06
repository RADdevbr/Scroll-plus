# SmoothScroll

A lightweight Windows tray utility (.NET 8 / WinForms) that intercepts the mouse
wheel and replaces raw, "steppy" scrolling with smooth, decaying (inertial)
scrolling. It is specifically designed to work with applications that do their
own rendering / input handling and ignore normal smooth-scroll injection — most
notably **RadiAnt DICOM Viewer** and **Weasis** (Java Swing).

## How it works

1. **Capture** — A global low-level mouse hook (`SetWindowsHookEx` + `WH_MOUSE_LL`)
   intercepts every `WM_MOUSEWHEEL` on the desktop.
2. **Target detection** — The foreground window's title (`GetForegroundWindow` +
   `GetWindowText`) and process name are matched against a configurable list. In
   *target-only* mode, only matching apps are smoothed.
3. **Swallow + smooth** — For a targeted event, the original wheel message is
   swallowed (the hook returns a non-zero value) and handed to the physics
   engine.
4. **Inject** — The engine breaks the motion into many small `WM_MOUSEWHEEL`
   messages posted with **`PostMessage` directly to the target window handle**
   (rather than `SendInput`). Posting directly bypasses the app's own input
   pipeline quirks and OS hit-testing, which is what makes self-rendering
   viewers cooperate. `SendInput` is used only as a fallback.
5. **Inertia** — Each frame (~8 ms, ~120 fps) emits a fraction of the current
   velocity and then decays it: `velocity *= friction`.

### Project layout

| File | Responsibility |
|------|----------------|
| `SmoothScroll/Program.cs` | Entry point + system-tray application context |
| `SmoothScroll/HookManager.cs` | `WH_MOUSE_LL` hook, event capture |
| `SmoothScroll/ScrollEngine.cs` | Physics: velocity, inertia decay, frame timer |
| `SmoothScroll/WindowTargeting.cs` | Active-window detection, target matching |
| `SmoothScroll/InjectionService.cs` | `PostMessage` injection, `SendInput` fallback |
| `SmoothScroll/SettingsManager.cs` | Settings model + JSON persistence |
| `SmoothScroll/UI/SettingsForm.cs` | WinForms settings panel (sliders, target list) |

## Configurable parameters (settings panel)

- **Sensitivity** — raw delta multiplier, `0.5x` … `3.0x`.
- **Friction** — per-frame inertia decay, `0.80` … `0.98` (higher = longer glide).
- **Steps per event** — how many smaller messages each scroll is broken into,
  `4` … `20` (higher = smoother / finer).
- **Target windows** — add/remove by process name (e.g. `RadiAnt`) or window
  title fragment (e.g. `Weasis`).
- **Apply only to target windows / globally** — checkbox.

> Tuning tip: total emitted scroll is roughly conserved when
> `Steps per event ≈ 1 / (1 - Friction)`. e.g. Friction `0.90` → ~10 steps.

## Behavior

- Runs as a **system-tray icon** with no main window.
- **Double-click** the tray icon to open settings; **right-click** for the menu.
- **Quick on/off** via the tray menu's *Enabled* item.
- Settings persist to `%AppData%\SmoothScroll\settings.json`.
- Single-instance (a second launch exits immediately).

## Build & run

Requires the .NET 8 SDK on Windows.

```sh
dotnet build -c Release
dotnet run --project SmoothScroll
```

The published binary is `SmoothScroll.exe`.

## Permissions / elevation note

- The `WH_MOUSE_LL` low-level hook does **not** require administrator rights, and
  it does not inject a DLL into other processes — the callback runs inside
  SmoothScroll and is serviced by its message loop. So SmoothScroll normally runs
  as a standard user (`asInvoker` in `app.manifest`).
- **Exception — UIPI / integrity levels:** a non-elevated process cannot post
  messages to (or hook input destined for) a window owned by a process running at
  a *higher* integrity level. If a target viewer is started **"Run as
  administrator"**, SmoothScroll must also run elevated for its `PostMessage`
  injection to be delivered. To force elevation, change the manifest's
  `requestedExecutionLevel` from `asInvoker` to `requireAdministrator`.

## Known limitation — Java Swing (Weasis)

Java's `MouseWheelEvent.getWheelRotation()` is an **integer** number of wheel
clicks (`nativeDelta / 120`). Swing components that read the integer rotation
will treat any posted delta smaller than `120` as zero and not scroll. If Weasis
does not respond to fine sub-click steps, increase **Sensitivity** and/or reduce
**Steps per event** so each emitted message is at least one full wheel click
(≥ 120).
