# WABA Desktop v0.3.1.5\n\n# WABA Desktop v0.3.1.5 — Anchor-Based Shelf & Counter Fallback

## Changes

1. **Gula uses neighbouring shelf anchors.**
   The fallback derives the fourth-column target from the detected Galon and LPG
   positions, and derives the row from Galon/LPG/Obat. It no longer relies only
   on a hard-coded percentage of the captured frame.
2. **Gula visual package signature.**
   When OCR cannot read the caption, a white/blue package signature can create
   a low-confidence candidate. The preview marker must still be visually checked.
3. **Counter positions use detected anchors.**
   E-Wallet, 10 GB, and Token listrik establish the spacing of the five counter
   slots. Missing 2 GB and 5 GB candidates are positioned from those anchors.
4. **Counter artwork colour fallback.**
   A yellow signature supports 2 GB and a blue signature supports 5 GB when the
   label OCR is incomplete.
5. **Counter coordinates are mapped to artwork centres.**
   Existing OCR detections for the five counter products are re-anchored to
   their artwork row, instead of using the text baseline as the click target.

## Safety and performance

- This patch does not send mouse input or automate gestures.
- Visual-signature matches are candidates, not final validation.
- Fallback OCR is only run for catalogue items that are still missing.
- The normal order-panel OCR path is unchanged.
- The standard shelf OCR path is unchanged except for the Gula fallback.

## Build

Local .NET compilation was not available in the patch-authoring environment.
Run GitHub Actions to verify compilation and the Windows x64 publish artifact.
