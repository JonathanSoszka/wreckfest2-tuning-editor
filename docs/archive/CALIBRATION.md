# Tuning `paramIndex` Calibration Procedure

> ## Completed 2026-07-22 — results in `docs/PARAM_MAP.md`
> Kept as the reusable procedure. **Two premises in the original write-up were wrong:**
> 1. It assumed sliders are set as *percentages*. They are not — the UI shows **physical values**
>    (gear ratios, mm, kN/m) and the file stores SI units. Pick unique *values*, not unique percents.
> 2. It assumed `aux` is always the normalized percent. Verified only for the gearbox.
>
> Still-valid core idea: set every slider to a **distinct** value, save once, and match values back
> to indices. Re-run this if new adjustable parts appear or to resolve `idx 56` (see PARAM_MAP).

**Goal:** build the permanent map `paramIndex → tuning parameter name` (e.g. `1 = Brake balance`),
which is the last missing piece before presets can be exported/imported with real labels.

**Status of what's already known** (see `docs/HANDOFF.md`):
- Tuning values are stored **normalized 0..1** plus a redundant integer percent (`percent = value*100`).
  29/29 records confirm this.
- `paramIndex` is a **global, stable ID** — not a per-car offset. Confirmed: `paramIndex 1` = brake
  balance on both RoadSlayer (brakes are its only adjustable part) and Hurricane (0.19 = the 19% we set).
- Unmapped IDs already present in the save: `10, 11, 12, 13, 14, 16, 18, 19, 20, 21, 25, 26, 28, 30, 31`.

---

## Why one session is enough

Each tuning entry stores its value as a distinct percentage. If **every slider is set to a different,
unusual number**, then a single saved preset lets us match value → index unambiguously:
see `73` in the data, and whichever slider we set to 73% is that `paramIndex`. No guessing, no
one-at-a-time iteration.

## Prerequisites

- Use a car with the **most adjustable parts equipped** so we cover the largest ID space in one pass.
  The **Hurricane** currently has 6 adjustable parts = **36 declared parameters**:
  gearbox 6-speed (7), front anti-roll bar (2), rear anti-roll bar (2), suspension full (18),
  RWD differential (4), adjustable brakes (3).
- The game closed when capturing files (so nothing is mid-write).

## Procedure

1. **Baseline capture.** Before touching anything, tell me — I copy `profile.sgfi` as
   `calib_before.sgfi`. Everything is diffed against this, so pre-existing values can't confuse us.
2. **Create a fresh preset** on the Hurricane named exactly **`CALIB`**. Using a new preset (rather
   than editing an existing one) keeps the calibration values isolated.
3. **Set every slider to its assigned number** from the table below, going part by part. Record the
   slider's on-screen label next to the number you actually achieved.
   - If a slider won't accept the exact number (steps/notches), **write down what it snapped to** —
     any unique value works, it does not have to be the suggested one.
   - If a slider doesn't exist for your parts, leave it blank and note that.
4. **Save the preset** with the explicit Save button (not exit-and-discard — that has bitten us before).
5. **Tell me it's saved.** I capture `calib_after.sgfi`, diff against baseline, and emit the ID map.

## Assigned values

All chosen to be unique and to avoid percentages already present in your save
(`19, 40, 51, 52, 53, 55, 57, 58, 59, 60, 62, 65, 66, 70, 100`). Any unique substitute is fine.

| # | Part group | Slider (fill in the on-screen label) | Set to | Actually got |
|---|---|---|---|---|
| 1 | Brakes |Braking Balance | 11 | |
| 2 | Brakes |Braking Pressure| 140 | |
| 3 | Brakes |Front Balancer | off | |
| 4 | Gearbox | final drive | 2.20 | |
| 5 | Gearbox | gear 1 | 2.30 | |
| 6 | Gearbox | gear 2 | 2.69 | |
| 7 | Gearbox | gear 3 | 2.91 | |
| 8 | Gearbox | gear 4 | 3.18 | |
| 9 | Gearbox | gear 5 | 4.12 | |
| 10 | Gearbox | gear 6 | 4.90 | |
| 11 | Anti-roll bar front | front | 17.5 | |
| 12 | Anti-roll bar front | | | |
| 13 | Anti-roll bar rear | rear | 77.5 | |
| 14 | Anti-roll bar rear | | | |
| 15 | Differential | power | 35 | |
| 16 | Differential | coast | 30 | |
| 17 | Differential | preload | 140 | |
| 18 | Differential | | | |
| 19 | Suspension |Springs - Front | 47.7 | |
| 20 | Suspension |Springs - Rear | 43.1 | |
| 21 | Suspension |Ride Height - Front | 20.3 | |
| 22 | Suspension |Ride Height - Rear | 19.8 | |
| 23 | Suspension |Front Bump (low speed) | 0.6 | |
| 24 | Suspension |Front Bump (high speed) | 1.2 | |
| 25 | Suspension |Front Rebound (low speed) | 1.8 | |
| 26 | Suspension |Front Rebound (high speed) | 2.5 | |
| 27 | Suspension |Rear Bump (low speed) | 3.1 | |
| 28 | Suspension |Rear Bump (low speed) | 3.7 | |
| 29 | Suspension |Rear Rebound (low speed) | 5.0 | |
| 30 | Suspension |Rear Rebound (low speed) | 5.6 | |
| 31 | Suspension |Front Camber |-1.5| |
| 32 | Suspension |Rear Camber| |-1.0|
| 33 | Suspension |Front Toe |-0.05| |
| 34 | Suspension |Rear Toe |-0.35| |
| 35 | Suspension |Ackerman |15%| |
| 36 | Suspension |Oversteer Bias |0 | |

**Partial passes are fine.** Even doing only the brakes + gearbox rows (10 sliders) permanently
names 10 IDs. The suspension block is the biggest win but also the most tedious — it can be a
second pass.

## What I produce from it

- `paramIndex → { name, part, .ctms parameter, min, max }` written into the repo as the canonical map.
- The JSON export upgraded from `"paramIndex": 13` to `"Rear damper rebound": 60%` with the physical
  value computed as `min + value*(max-min)`.
- A validator that rejects out-of-range imports (the corrected project goal explicitly excludes
  exceeding in-game limits).

## Gotchas learned the hard way

- **Use the explicit Save button.** Exiting and choosing "discard" silently invalidated several
  earlier experiments.
- **Don't trust a value that appears only once.** If a number we assigned shows up in an unexpected
  place, treat it as a collision and re-run that row with a different number.
- **Verify against a known anchor.** `paramIndex 1` must come back as brake balance; if it doesn't,
  the mapping run is suspect and should be discarded.
