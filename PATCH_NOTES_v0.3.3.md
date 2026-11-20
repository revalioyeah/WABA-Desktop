# Patch v0.3.3 — Area-Specific Marker Calibration

## Purpose

Correct the visible product-marker alignment reported on v0.3.2 without changing the
37-slot stable catalogue or the OCR recognition pipeline.

## Changes

- Keeps all 16 `LEFT_SHELF` coordinates unchanged.
- Applies `RIGHT_SHELF` horizontal offset: **X +24 reference-frame pixels** to all 16 slots.
- Applies `PULSE_COUNTER` vertical offset: **Y +18 reference-frame pixels** to all 5 slots.
- Applies offsets after reference coordinates are loaded/recalibrated and before coordinates
  are scaled to the captured client frame.
- Keeps persisted baseline coordinates unchanged; the offsets are independent calibration
  transforms and remain active even when an existing v0.3.2 local catalog file is present.
- Updates app version metadata and GitHub Actions artifact name to v0.3.3.

## Validation boundary

The offsets are initial calibration values based on the user's requested directions and the
previously proposed +24/+18 pixel adjustments. They are not click-validated. Review all
markers in the preview before using any coordinates for input. No automatic clicking or
unattended order execution is enabled.

## Expected reference-frame result

- Right shelf: X values increase by 24; Y values are unchanged.
- Pulse counter: Y values increase by 18; X values are unchanged.
- Left shelf: X and Y remain unchanged.

The offsets scale with the captured frame dimensions using the 1831×1028 reference frame.
