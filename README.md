# Trackpad Window Control

Move any window by dragging with **3 fingers** on your Windows Precision Touchpad — the same behaviour as the macOS accessibility 3-finger drag.

## Requirements

- Windows 10 / 11 x64
- A **Windows Precision Touchpad** (built-in laptop touchpads from 2016+ are almost always WPT)
- .NET 8 SDK (to build) or the pre-built `.exe` (no runtime needed with self-contained build)

## How to use

1. Run `TrackpadWindowControl.exe`.  
   A small icon appears in the system tray (notification area).

2. Place **3 fingers** on the trackpad and **hold** for ~80 ms, then slide.  
   The window under your cursor will follow.  Lift any finger to release.

3. Right-click the tray icon for:
   - **Enable / Disable** – temporarily pause the gesture without exiting
   - **Start with Windows** – toggle auto-start via the registry Run key
   - **About** – version info and tips
   - **Exit** – quit the application

## Tips for best results

Windows reserves 3-finger swipes for system gestures (Alt-Tab, show desktop, etc.).
A quick 3-finger *swipe* will still trigger those.  The app distinguishes a **drag**
(hold ≥ 80 ms then move) from a swipe, so normal Windows gestures continue to work.

For *maximum* reliability (no accidental system-gesture interference), disable the
conflicting built-in gestures:

> **Settings → Bluetooth & devices → Touchpad**  
> **Three-finger gestures → Swipes** → set to **Nothing**

You do **not** need to disable taps or three-finger tap (Cortana/Search).

## Building from source

```
build.bat
```

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).  
The output is a single, self-contained `.exe` in `publish\`.

Alternatively:

```
dotnet run
```

…runs directly without publishing.

## How it works

The app registers for **Raw Input** (HID) from the precision touchpad with
`RIDEV_INPUTSINK`, so it receives touchpad contact data globally without a
visible window.  On each report it:

1. Parses contact X/Y positions using the `HidP_*` API (no hard-coded offsets —
   works with any WPT-compliant device).
2. Detects when ≥ 3 contacts are simultaneously active for ≥ 80 ms.
3. Tracks the **centroid** (average position) of the active contacts.
4. Translates centroid movement into `SetWindowPos` calls on the top-level window
   that was under the mouse cursor when the gesture started.

## Adjusting sensitivity

Edit `WindowMover.cs` and change the `Sensitivity` field (default `1.5`).  
Higher = faster window movement.  Rebuild with `build.bat`.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Nothing happens when I drag 3 fingers | Check that you have a WPT touchpad (not an older Synaptics/ELAN driver in legacy mode). See Device Manager → Human Interface Devices. |
| Windows gestures (Alt-Tab) still fire | Set **Three-finger gestures → Swipes** to **Nothing** in Touchpad settings. |
| Window moves too fast / slow | Adjust `Sensitivity` in `WindowMover.cs` and rebuild. |
| App starts but tray icon is missing | Check the hidden icons overflow area in the taskbar. |
| Only works sometimes | Ensure no other application (e.g. Logi Options, TouchpadBlocker) is consuming raw HID input exclusively. |
