# Phase 2 Acceptance Checklist — Smart Order Recognition

## A. Build and OCR availability
- [ ] GitHub Actions `Build WABA Desktop` succeeds.
- [ ] WABA starts on Windows 11.
- [ ] UI reports the Windows OCR engine and language tag.
- [ ] If OCR is unavailable, the UI gives a clear language-pack message.

## B. ROI calibration
- [ ] Launch Warung Life Simulator and enter the shop.
- [ ] Select the game window and capture a frame.
- [ ] Start with X=40, Y=17, W=23, H=20 percent.
- [ ] Click `Scan panel pesanan`.
- [ ] Confirm the crop preview contains only the order note and rows.
- [ ] Adjust X/Y/W/H until the crop excludes shelves, characters, and subtitles.

## C. OCR and parsing
- [ ] Test an order with one item.
- [ ] Test an order with two different items.
- [ ] Test an order with three or more items.
- [ ] Test quantities such as 0/1, 0/2, and partially completed values.
- [ ] Compare raw OCR text and parsed product names with the game panel.
- [ ] Record false reads and unreadable quantities; do not treat them as confirmed.

## D. Change monitoring
- [ ] Enable `Pantau perubahan panel & scan otomatis`.
- [ ] Confirm live preview starts if it was stopped.
- [ ] Change to a different customer/order.
- [ ] Confirm OCR runs after the ROI changes.
- [ ] Confirm OCR is not run continuously on every preview frame.
- [ ] Disable the checkbox and confirm automatic scanning stops.

## E. Safety boundary
- [ ] Confirm OCR never sends mouse clicks.
- [ ] Confirm unknown product labels remain marked as unknown.
- [ ] Do not proceed to automatic execution until order recognition has been validated
      across multiple order types and quantities.


## F. Phase 2.1 quantity association regression
- [ ] Product names and quantities are paired by OCR word bounding boxes.
- [ ] Test a row where Windows OCR splits the label and quantity into separate lines.
- [ ] Verify 0/1, 0/2, and partially completed quantities.
- [ ] Verify adjacent rows never exchange quantities.
- [ ] Verify unreadable quantities remain unknown rather than being guessed.
