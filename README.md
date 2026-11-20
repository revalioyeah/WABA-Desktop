# Warung Auto Bag Assistant PC — v0.3.1.5 (Phase 3.1.5 Anchor-Based Shelf & Counter Fallback)

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

Outside the Phase 1 scope:
- Automatic order queue.
- Automatic item clicking.
- Fishing and other minigames.
- Automated tip-dialog handling.

The manual click is deliberately confirmation-gated. Phase 1 does not run unattended automation.
OCR and order-panel recognition are introduced separately in Phase 2 below.

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



## Phase 2 — Smart Order Recognition

Phase 2 adds a first, testable order-panel recognition path while keeping all Phase 1
window enumeration, visible capture, and manual click test services.

### Recognition pipeline

1. Capture the selected game client area on a background task.
2. Crop the order board using a configurable percentage-based ROI.
3. Upscale the crop 2x before recognition.
4. Run Windows built-in OCR using a language pack available in the current Windows profile.
5. Preserve each OCR word's bounding box and group words into visual rows.
6. Pair product labels and quantities by their visual row, even when Windows OCR splits them into separate text lines.
7. Parse OCR rows against the local product dictionary.
8. Keep unknown product names visible as unknown; they are not eligible for automatic input.
9. Optional change monitoring samples only the configured order-board ROI and requests OCR
   after a meaningful visual change. It does not run OCR on every preview frame.

### Initial ROI calibration

The default ROI values are a starting estimate only:

- X: 40%
- Y: 17%
- Width: 23%
- Height: 20%

Use `Scan panel pesanan`, inspect the crop preview, and adjust X/Y/W/H until the crop
contains the order note and its item rows only. These percentages scale with the captured
client area, so they also work when the game window is resized.

### Windows OCR language

The OCR adapter uses `Windows.Media.Ocr` and the language packs already installed in
Windows. It first tries the current user's OCR profile languages and then falls back to
English (United States) when that OCR language is installed.

If the OCR status says that no engine is available, install an OCR-capable language pack
from Windows Settings > Time & language > Language & region, then restart WABA.

The engine language is displayed in the UI. Indonesian product names use Latin characters,
but recognition quality must be validated against actual game screenshots; OCR results
remain review-only at this phase.

### Phase 2 boundaries

- No automatic item clicks are sent from OCR.
- No stock/rack coordinate is inferred from the order-panel OCR.
- Unknown labels and unreadable quantities are preserved for manual review.
- Automatic panel monitoring runs only while Live Preview is active.
- The change detector is a lightweight ROI pixel sampler, not a semantic game-state detector.
  It may require ROI calibration to avoid reacting to visual effects or missing a subtle update.

### Build

The Windows projects now target `net10.0-windows10.0.19041.0` so the app can call Windows
Runtime OCR APIs. The Domain project remains `net10.0`.

```powershell
dotnet restore .\WarungAutoBagAssistant.PC.sln
dotnet build .\WarungAutoBagAssistant.PC.sln -c Release
```


## Phase 2.1 — Quantity association fix

The OCR adapter now retains Windows OCR word bounding boxes. The parser groups words
into visual rows and reads the product name and quantity from the reconstructed row.
This addresses cases where Windows OCR returns a product label and its `0/1` quantity
as separate text lines even though they appear on the same row in the game panel.

The parser also compares short token windows against the product catalog so surrounding
OCR noise does not prevent a product-name match. Quantities that cannot be read remain
unknown (`? / ?`); the parser does not invent a quantity.

This is still a recognition-only update. It does not send game clicks or execute orders.


## Phase 2.2 — Long order-panel ROI

The default order-panel ROI height is now 45% (previously 20%). The shorter ROI could crop away lower rows when the game displayed a long order list, so OCR could only recognize the rows that remained inside the crop.

Recommended starting values: X=40, Y=17, W=23, H=45. Verify the crop preview includes the complete yellow order note, including its bottom row. If the bottom row remains clipped, increase H gradually (for example, to 48 or 50) while keeping unrelated shelf text out of the crop.

This change only adjusts the default scan region and documentation. It does not change live capture, OCR language selection, or mouse input behavior.


## Phase 3.1 — Smart Item Detection

Phase 3.1 adds Windows OCR label detection for the left shelf, right shelf, and pulse counter. OCR captions are clustered by both vertical alignment and horizontal gaps so multiple products on the same shelf row can be detected independently. It displays estimated product points and confidence for manual verification only. It does not send clicks or execute orders. See `docs/PHASE3_1_SMART_ITEM_DETECTION.md`.


## Phase 3.1.1 — Product Matching & Coordinate Estimate Fix

This update preserves the Phase 2 order OCR and improves shelf-label matching for the current product catalogue. It adds explicit OCR aliases for common compact labels (`E-Wallet`, `2GB`, `5GB`, `10GB`) and common glyph confusions (`Gula`, `Galon`, `Teh celup`, `Teh botol`). The pulse-counter crop is scanned at 3x while the two main shelves remain at 2x to improve small-label recall without increasing the cost of every region.

Target X now uses the full caption bounds, including numeric tokens, so labels such as `2 GB` do not anchor only on `GB`. The scan regions were slightly extended to include lower shelf captions. Area diagnostics and unmatched OCR labels are now included in the detection output.

**Safety boundary remains unchanged:** detections are preview-only estimates. This version does not send clicks or execute orders. Verify every marker before any future interaction phase.


## Phase 3.1.2 — Counter OCR & Missing Catalogue Diagnostics

This update targets the latest 33-product scan where the expected 37-product layout was missing `Gula`, `E-Wallet`, `2 GB`, and `5 GB`. It adds explicit aliases for the observed `E-WaUet` OCR fragment, counter-specific `2/Z` and `5/S` aliases, 4x OCR scaling only for the small pulse-counter crop, and wider token grouping only in that region. It also reports expected catalogue entries that were not found, without inventing their coordinates. The two main shelf crops remain at 2x. The latest user-reported coordinate observations are recorded in `docs/PHASE3_1_1_COORDINATE_BASELINE.csv`; those are text-match observations, not click-validated ground truth.

`Tisu` remains in the order vocabulary but is not in the 37-item coordinate baseline because no current coordinate was supplied. Calibrate it separately if it is visible in the current game layout. This phase remains preview-only and does not send mouse input. See `docs/PHASE3_1_2_COUNTER_OCR_AND_CATALOG_DIAGNOSTICS.md`.


## Phase 3.1.3 — Spatial Counter OCR Fallback

This update uses the latest user-reported counter anchors (`E-Wallet`, `10 GB`, and `Token listrik`) to estimate the spacing of the five counter-card slots. It only proposes `2 GB` or `5 GB` when a corresponding raw OCR fragment is found close to the expected slot. These candidates receive deliberately lower confidence values and are shown with non-green markers; they are not click-ready. Unmatched counter fragments now include their approximate OCR center coordinates in diagnostics. `50B`/`5OB`-like fragments are considered for `5 GB` only when they occur at the expected 5 GB slot.

`Gula` remains unresolved because the latest OCR diagnostics contain no recognizable text fragment for that label. No coordinate is synthesized for it. Capture a close-up of the left-shelf third row (LPG 3 kg and the adjacent sugar product) if it remains missing.

This phase remains preview-only. It does not send mouse input. The 34 user-reported product coordinates are text/position observations and have not been validated by actual clicks.


## Phase 3.1.4 — Targeted ROI fallback for Gula and pulse data cards

This update keeps the normal 2x shelf OCR and 4x counter OCR passes unchanged. Only when a catalogue entry is still missing, it performs a small additional 4x OCR crop:

- `Gula`: a dedicated caption ROI at the far-right slot of row three on `LEFT_SHELF`.
- `2 GB` and `5 GB`: separate slot ROIs within `PULSE_COUNTER`, using the five-card counter layout.

Counter OCR aliases now include observed glyph substitutions such as `50B`/`5OB` for `5 GB` and `2G8` for `2 GB`. A weak standalone digit can produce only a low-confidence provisional candidate. Exact/fuzzy text matches are capped below the green-marker threshold because the target point is derived from the slot geometry and still requires visual verification.

The targeted passes run only for missing products, not on every frame and not as part of Phase 2 order OCR. At most three small extra OCR crops are processed in one shelf scan. No automatic click or order execution is introduced.

For the supplied 1767x992 reference screenshot, the targeted candidate positions are scaled from client-area proportions. They are preview estimates, not click-validated coordinates. Check every marker in the live WABA preview before any future input phase.
