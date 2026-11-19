# Phase 3.1.4 — Targeted ROI OCR fallback

## Problem observed

The supplied 1767x992 game screenshot visibly contains the Gula bag on the left shelf and the 2 GB and 5 GB vouchers on the pulse counter, while the previous whole-region OCR run did not consistently include all three catalogue entries.

The issue is likely a combination of small caption text, OCR token grouping, and a fixed vertical-offset assumption when converting a caption into a thumbnail point.

## Implementation

### Gula

When `Gula` is absent after the ordinary `LEFT_SHELF` pass, a compact ROI is scanned at 4x. The ROI covers the far-right slot of row three and includes the black caption. A candidate is emitted only if the OCR text matches `Gula` or one of its explicit aliases. Its point is the estimated centre of the sugar package, scaled to the captured client dimensions.

### 2 GB and 5 GB

When either data-package product is missing, the detector scans only that product's slot ROI at 4x. The five-slot counter layout is represented as fractions of the existing `PULSE_COUNTER` crop. The expected data-package slot is not enough by itself: OCR text must match the expected product or a weak digit candidate must appear inside that slot crop.

Aliases include common OCR glyph substitutions:
- 2 GB: `2GB`, `2 GB`, `2G8`, `ZGB`.
- 5 GB: `5GB`, `5 GB`, `50B`, `5OB`, `S0B`, `5G8`.

A weak standalone digit is emitted with 68% confidence and remains an orange/red provisional marker. Strong text matches are capped at 94% because the marker coordinate is estimated from the slot geometry, not directly validated by a click.

### Coordinates

- Gula candidate: estimated product-thumbnail centre at 34.7% of frame width and 47.3% of frame height.
- Counter candidate X: the second and third of five equally spaced card slots inside `PULSE_COUNTER`.
- Counter candidate Y: 40% down the `PULSE_COUNTER` crop, corresponding to the voucher-artwork row in the supplied reference layout.

All points are client-area-relative and scale with the captured frame. The reference screenshot is 1767x992. Different game UI scale/layout may require recalibration.

## Performance boundary

The normal whole-area scan remains unchanged. Additional OCR is performed only for missing catalogue products and only when the user manually runs shelf detection. At most three small crops are added. Phase 2 order OCR is not changed.

## Validation steps

1. Build Release using GitHub Actions.
2. Run shelf detection on a clear game capture.
3. Confirm `Gula`, `2 GB`, and `5 GB` appear only when the targeted OCR crops return supporting text.
4. Confirm the total expected catalogue is 37 items.
5. Inspect the orange markers over the centre of each product thumbnail.
6. Repeat with a different game client size to confirm proportional scaling.
7. Do not enable automatic input based only on the text confidence score.

## Safety boundary

This phase remains detection and preview only. It does not send mouse clicks or execute orders.
