# Phase 2.2 — ROI Height Fix

## Symptom
Long order boards were only partially recognized because the default ROI height was 20% of the captured client frame. The OCR engine cannot recognize rows that fall outside the cropped image.

## Change
- Default ROI: X=40%, Y=17%, W=23%, H=45%.
- The UI hint now tells the operator to verify the last visible item row in the crop preview.
- The Phase 2 checklist and README use the same recommended calibration.

## Test
1. Launch the game and start live preview.
2. Confirm ROI fields show X=40, Y=17, W=23, H=45.
3. Click `Scan panel pesanan`.
4. Verify the crop contains the complete yellow order board from header to the last row.
5. Compare every item and quantity in the crop against the parsed result.
6. Test again with a short order and a long order (6–8 rows).

If the last row is clipped, raise H in small increments (48, then 50) and scan again. Do not use OCR output as execution input until every visible row and quantity has been checked.

This patch changes the default ROI only; it does not alter capture or mouse-input code.
