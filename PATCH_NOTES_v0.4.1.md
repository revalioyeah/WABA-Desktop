# WABA Desktop v0.4.1 — Phase 4.1 Guided Order Queue UI

## What changed

- Reworked section 06 into a guided two-step flow: build/review a plan, then run it.
- Added plain-language prerequisites for the stable 37-slot catalogue and order OCR.
- Replaced the previous plan-only button with **Baca pesanan & buat rencana**. It captures a current frame when needed, performs a fresh order-panel OCR scan, then builds the plan from that scan.
- Added clear status messages for missing game selection, missing catalogue scan, unlocked catalogue, missing marker confirmation, stale OCR, and invalid plans.
- Increased plan freshness window from 8 to 20 seconds so the user has time to review the plan before confirming execution.
- Restyled disabled buttons so they remain dark and readable.
- Applied dark styling to the Google Play Games window selector and its list items.
- Updated app version and GitHub Actions artifact name.

## Preserved

- Stable 37-slot catalogue and v0.3.3 coordinate calibration: RIGHT_SHELF X+24 px and PULSE_COUNTER Y+18 px.
- Shelf OCR and order OCR recognition algorithms.
- Order execution planner rules and 105 ms click interval.
- Maximum 20 clicks per plan.
- Manual confirmation dialog before real mouse clicks.

## Workflow

1. Select the game and start Live Preview.
2. Scan the shelf catalogue in section 05 and verify the markers. If catalogue is already locked, keep it locked and tick the marker confirmation checkbox. If recalibration is open, complete three scans and lock it.
3. Show the customer order.
4. Click **Baca pesanan & buat rencana** in section 06. The app captures the latest frame, runs OCR, and builds the plan.
5. Review all product names and quantities.
6. Click **Jalankan rencana** and approve the confirmation dialog only if the plan is correct.
7. Verify the bag and order panel in the game.

## Safety limits and known limitations

- Maximum 20 clicks per plan; 105 ms interval.
- A plan expires after 20 seconds.
- The app sends real mouse clicks; it does not yet verify that the game accepted each click.
- Customer dialogs, tip popups, and minigames are not automatically handled.
- Keep the game visible and supervise execution. Stop the queue if the screen changes unexpectedly.

## Validation status

XAML and project XML parsed successfully. UI event-handler references and XAML names were checked for missing handlers and duplicates. A local .NET publish could not be run because the authoring environment does not have the .NET SDK; GitHub Actions must complete a Windows x64 build before installation.
