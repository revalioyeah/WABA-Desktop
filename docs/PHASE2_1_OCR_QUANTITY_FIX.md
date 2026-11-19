# Phase 2.1 — OCR quantity association fix

## Root cause addressed

The previous scanner passed only `OcrLine.Text` to the parser. Windows OCR can return
the product label and the quantity as separate OCR lines. Because the parser expected
both parts in the same string, product names could be recognized while quantities
remained `? / ?`.

## Changes

- Preserve each recognized OCR word and its bounding rectangle.
- Reconstruct visual rows using the words' vertical center positions.
- Parse product names and quantity patterns from each reconstructed row.
- Accept common OCR substitutions for digits: `O` to `0`, `I`/`l` to `1`.
- Match product names using token windows, tolerating surrounding OCR noise.
- Keep unreadable quantities unknown instead of guessing.
- Show OCR word tokens and their bounding-box coordinates in the result panel to make further diagnosis possible.
- Keep the existing capture, live preview, manual click test, and review-only boundary.

## Validation on the user's current screenshot

The screenshot shows three order rows. The crop visually suggests:
- Minyak goreng — 0/1
- Air mineral — 0/1
- Kopi sachet — 0/2

These are visual observations from the screenshot, not a confirmed OCR test result.
After installing the new build, compare the parsed output against the live order panel.

## Test matrix

1. One item with `0/1`.
2. Three items with quantities `0/1`, `0/1`, and `0/2`.
3. Four or more items.
4. Partially completed order such as `1/2`.
5. OCR digit substitutions such as `O/1` or `0/I`.
6. Confirm product names and quantities do not shift between adjacent rows.
7. Confirm unreadable quantities remain `? / ?`.

## Safety boundary

OCR output remains review-only. This patch does not send clicks, infer rack coordinates,
or execute orders automatically.
