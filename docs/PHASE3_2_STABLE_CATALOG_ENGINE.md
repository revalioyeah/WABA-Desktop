# WABA Desktop v0.3.2 — Stable Catalog Engine

## Root cause addressed

The previous desktop UI assigned `result.Items` directly to `_lastShelfDetections` after every OCR run. A transient OCR miss therefore removed products from the visible list even when the game shelf had not changed.

## Changes

1. Adds a fixed 37-slot reference catalogue using the latest user-reported coordinates.
2. Merges each OCR scan into the catalogue instead of replacing the catalogue with the current scan.
3. Marks products observed in the latest scan as `LIVE OCR`.
4. Retains catalogue entries missed by the latest OCR pass as `BASELINE RETAINED`.
5. Displays all 37 slots after a scan; current-scan OCR coverage is reported separately.
6. Adds explicit `Kunci katalog` and `Kalibrasi ulang` controls.
7. During recalibration, a coordinate can change only after at least 2 of the last 3 observations agree within a tight coordinate spread and remain within the configured deviation guard.
8. Saves lock state and calibrated reference coordinates to `%LOCALAPPDATA%\WABA Desktop\stable-catalog.json`.
9. Scales reference coordinates to the captured client frame. If the frame aspect ratio differs by more than 1.5%, recalibration is blocked and a warning is shown.
10. Updates the coordinate baseline CSV to include all 37 products and the latest user-reported coordinates.

## Deliberately unchanged

- `ShelfItemDetector` OCR/clustering/fallback behavior is not redesigned.
- Order-panel OCR and order-change detection are unchanged.
- No automatic mouse gestures or automated item collection are enabled.
- Catalogue coordinates are reference positions, not click-validated guarantees. The manual click-test remains a separate, explicit action.

## How to use

- Normal use: leave the catalogue locked. OCR misses will not remove entries or move coordinates.
- If the game layout or shelf arrangement changes: click `Kalibrasi ulang`, run three manual shelf scans with the game stationary, review the preview, then click `Kunci katalog`.
- If the frame aspect ratio differs from the reference layout, select the intended game window/resolution before recalibrating.

## Build validation

Run the GitHub Actions Windows x64 workflow. Local .NET compilation was not available in the patch-authoring environment.
