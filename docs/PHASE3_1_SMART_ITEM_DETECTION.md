# WABA Desktop v0.3.0 — Phase 3.1 Smart Item Detection

## Scope

This phase adds label-based visual detection for products shown in the game shelves. It scans three configurable-by-source percentage regions: left shelf, right shelf, and pulse counter. Windows OCR reads product captions, a conservative fuzzy matcher maps captions to the canonical order-product catalogue, and the detector estimates a product point above the caption.

## Safety boundary

- This phase is detection/preview only.
- It does not call `MouseInputService` and does not send mouse clicks.
- Coordinates are estimates, not verified interaction points.
- Verify green markers visually before any future interaction phase.
- OCR can confuse similar labels or read unrelated text; confidence is shown for review.

## Default scan areas

| Area | X | Y | Width | Height |
|---|---:|---:|---:|---:|
| LEFT_SHELF | 4% | 13% | 35% | 58% |
| RIGHT_SHELF | 60% | 13% | 37% | 58% |
| PULSE_COUNTER | 7% | 75% | 34% | 23% |

These are starting regions for the standard landscape game layout. If the game UI or client aspect ratio differs, adjust `DefaultRegions` in `ShelfItemDetector.cs` and verify the crop/markers.

## Estimated point

The detector places the estimated point horizontally at the center of the recognized product caption and vertically above it by 2.7% of frame height (clamped to 12–42 pixels). This is a calibration heuristic, not a learned object detector. The green overlay must be visually checked.

## Test procedure

1. Keep Google Play Games visible and unobstructed.
2. Start live preview and capture a frame.
3. Click `Deteksi lokasi barang pada rak`.
4. Confirm that the OCR candidates match visible product captions.
5. Confirm each green marker is on the product thumbnail, not the caption, price, customer, or order board.
6. Record false positives, missed products, and coordinate offsets before Phase 3.2.

## Known limitations

- Product captions must be visible and legible to Windows OCR.
- The default shelf regions match the current standard layout; unlocks, menus, dialogs, or resolution/layout changes may require region calibration.
- Similar product names and duplicate shelf appearances can produce multiple candidates.
- This phase does not yet match the current order list to shelf detections and does not execute input.
