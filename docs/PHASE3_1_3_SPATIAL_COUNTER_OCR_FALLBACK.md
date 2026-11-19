# WABA Desktop v0.3.1.3 — Spatial Counter OCR Fallback

## Latest user-reported run

The latest run reports 34 detections: 15 on `LEFT_SHELF`, 16 on `RIGHT_SHELF`, and 3 on `PULSE_COUNTER`. The detected counter anchors are `E-Wallet` at (268, 910), `10 GB` at (586, 910), and `Token listrik` at (692, 909). `2 GB` and `5 GB` are still missing. Raw unmatched counter fragments include `2`, `50B`, `106B`, `11`, and `ld-ooo`. `Gula` remains missing on the left shelf.

## Changes

- Counter unmatched fragments now include approximate OCR center coordinates in the diagnostic output.
- A provisional `2 GB` candidate is added only when a standalone `2` or `Z` fragment appears near the expected slot between E-Wallet and 10 GB.
- A provisional `5 GB` candidate is added only when a `50B`/`5OB`/`S0B`-like fragment appears near the expected 5 GB slot.
- Expected slot spacing is derived from the three detected anchors: E-Wallet, 10 GB, and Token listrik. It is not a hard-coded coordinate table.
- Provisional candidates have low confidence (68% / 72%) and are rendered as non-green markers. They are not validated click targets.
- Added `gua` as a conservative OCR alias for `Gula`; no coordinate is synthesized when no corresponding label fragment is found.
- Updated the coordinate baseline with the latest 34 user-reported observations. Scores and positions are still user-reported OCR observations, not click-validated ground truth.

## Expected result

The counter should report 3 to 5 products depending on whether the two raw fragments are spatially consistent with their expected slots. If no matching fragment appears in the slot, the product remains listed as missing. The left shelf may still report `Gula` missing; capture a close-up of the third row (LPG 3 kg and the adjacent sugar product) to diagnose that label.

## Safety boundary

This remains preview-only. No mouse click or order execution is added. The 2 GB and 5 GB fallback points require visual verification and must not be treated as validated coordinates.
