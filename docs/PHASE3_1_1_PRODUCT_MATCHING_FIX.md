# WABA Desktop v0.3.1.1 — Phase 3.1.1 Product Matching Fix

## Changes

- Added OCR aliases for compact counter labels: `E-Wallet`, `2 GB`, `5 GB`, and `10 GB`.
- Added common Windows OCR glyph-confusion aliases for `Gula`, `Galon`, `Teh celup`, and `Teh botol`.
- Kept the fuzzy-match threshold conservative; explicit aliases handle known substitutions.
- Increased the pulse-counter scan crop to 3x while keeping left/right shelves at 2x.
- Extended shelf and counter scan regions slightly to include lower captions.
- Calculate the estimated horizontal target from the complete caption bounds, including numeric tokens.
- Increased caption token grouping tolerance slightly and improved same-area duplicate suppression.
- Added per-area diagnostics and unmatched OCR captions to the visible detection output.

## Validation checklist

1. Confirm existing Phase 2 order OCR still reads the order panel.
2. Run shelf detection and check that `Gula` appears when visible.
3. Check pulse-counter captions: `E-Wallet`, `2 GB`, `5 GB`, `10 GB`, and `Token listrik`.
4. Confirm left and right shelf products still appear at their existing approximate positions.
5. Inspect all green markers; coordinate estimates are not click-safe until visually validated.
6. Compare scan duration with v0.3.0. The pulse crop is larger and scanned at 3x, so a moderate increase is possible.
7. Review `DIAGNOSTIK AREA` for OCR tokens that remain unmatched.

## Coordinate reference

The file `PHASE3_1_1_COORDINATE_BASELINE.csv` stores the 33 coordinates supplied from the previous scan at 1831x1028. These values are reference observations only; they are not assumed to be ground-truth product centers and are not loaded as click coordinates. They must be visually verified before being used for calibration.

The UI now calls the matcher value a **text match score**, not coordinate accuracy. A high text score only means the OCR caption resembles a catalogue name.

## Safety boundary

This update remains detection/preview only. It does not send mouse input and does not automate order execution.
