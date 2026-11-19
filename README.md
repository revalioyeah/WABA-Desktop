# Warung Auto Bag Assistant PC — Phase 1

Windows desktop companion prototype for Warung Life Simulator running in Google Play Games on PC.

## Target hardware

- OS: Windows 11 64-bit
- CPU: AMD Ryzen 7 5800X
- RAM: 32 GB
- GPU: NVIDIA GTX 1660 Super
- Monitor: single monitor
- Intended game layout: side-by-side with the WABA dashboard

## Phase 1 scope

Implemented:
1. Enumerate visible top-level windows.
2. Select a target game window.
3. Capture the selected window's visible client area.
4. Display one-shot and live preview (4 FPS).
5. Manually test one left mouse click using client-relative coordinates.
6. Activity log and basic target status.

Not implemented yet:
- OCR and product recognition.
- Automatic order queue.
- Automatic item clicking.
- Fishing and other minigames.
- Automated tip-dialog handling.

The manual click is deliberately confirmation-gated. Phase 1 does not run unattended automation.

## Capture implementation note

Phase 1 uses `System.Drawing.Graphics.CopyFromScreen` to capture the selected window's visible client rectangle. This is a practical first integration test for a single-monitor, side-by-side layout.

Important:
- Keep the game window visible.
- Do not minimize it.
- Do not cover the game window with another window.
- The capture may include other desktop content if another window overlaps the game.
- If Google Play Games renders a black/blank capture, Phase 1 must be upgraded to a per-window Windows Graphics Capture adapter before moving to OCR.

The `DesktopCaptureService` is isolated so the capture backend can be replaced without changing the WPF UI.

## Requirements

- Visual Studio 2026 Community or newer with ".NET desktop development" workload, or
- .NET 10 SDK with Windows desktop targeting support.
- Windows 11.

## Build

Open `WarungAutoBagAssistant.PC.sln` in Visual Studio and build `Release`.

Or use PowerShell:

```powershell
dotnet --info
dotnet restore .\WarungAutoBagAssistant.PC.sln
dotnet build .\WarungAutoBagAssistant.PC.sln -c Release
```

Run:

```powershell
dotnet run --project .\src\WABA.Desktop\WABA.Desktop.csproj
```

## One-monitor layout

A single 1920x1080 monitor cannot show a full-size 1920x1080 game and a side dashboard simultaneously. Use a windowed game layout, for example:
- Game window: approximately 1080x608 (16:9 windowed area).
- WABA dashboard: approximately 800x820.

These are starting layout estimates. Use the actual client dimensions reported by WABA; game UI scaling and Google Play Games window constraints may differ.

The capture reports the actual client-area dimensions. Coordinate mapping must use those captured dimensions, not assume 1920x1080.

If the game needs to remain at a full 1920x1080 client size, use a second monitor or switch to a separate-dashboard workflow.

## Input safety

The "Test klik kiri" button sends one real mouse click to the selected game window. Always:
1. Capture the game.
2. Verify the intended point.
3. Enter client-relative X/Y.
4. Confirm the warning dialog.

If the click is not delivered, ensure the game and WABA run at the same Windows integrity level (both normally, not one as administrator and the other unelevated).

## Next phase

Phase 2 will add:
- ROI-based order-board detection.
- OCR engine and Indonesian product dictionary.
- Product catalog and coordinate calibration.
- Order change detection and queue sessions.

