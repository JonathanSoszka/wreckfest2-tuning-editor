# Canonical `paramIndex` → tuning parameter map

Derived from the CALIB calibration run (2026-07-22) on the Hurricane (car02), which had all six
adjustable parts fitted. Each mapping below was established by setting a slider to a distinctive
value in-game, saving the preset, and matching the stored value in `profile.sgfi`.

## Record format

Inside a preset's `atvc` node:

```
atvc [u32 kind=2] [u32 count]  then count × 12-byte records:
     [u32 paramIndex] [u32 aux] [f32 value]
```

- **`value` is the physical value in SI base units** — NOT normalized. The UI converts for display.
- **`aux` is the slider position** — the authoritative field. The stored value is *derived* from it:
  `value = min + aux × (max − min) / steps`, with min/max/**steps** from the part's `.ctms`. Verified
  on every record of every fixture save; see "The arithmetic — SOLVED" below. (An earlier claim that
  `aux` was a *percent* — `aux/100` — is **retracted**; the divisor is the schema's `steps`, and
  `aux` is meaningful for every parameter, not just the gearbox. Braking Pressure `aux=18` reads
  `50 + 18×5 = 140`, which is exactly the observed value.)
- **Only non-default values are stored.** Sliders left at their default are omitted entirely
  (confirmed: Rear Camber and Front Balancer were set/left at default and have no record).

## Unit conversions (file → UI)

| Quantity | Stored unit | UI shows | Factor |
|---|---|---|---|
| Ride height | metres | cm | ×100 |
| Springs | N/m | kN/m | ÷1000 |
| Dampers | N·s/m | kN·s/m | ÷1000 |
| Anti-roll bar | N/m | kN/m | ÷1000 |
| Gear ratios, balance, camber, toe | unitless / native | same | ×1 |

The game can switch metric/imperial, so **never trust the UI number as the stored value** — always
convert via the table above.

## The map

| idx | Slider | Stored (CALIB) | UI showed | Status |
|----:|--------|---------------:|----------:|--------|
| 0  | Braking Balance          | 0.1100  | 11    | confirmed |
| 1  | Braking Pressure         | 140.0   | 140   | confirmed |
| 2  | Front Balancer           | *(absent)* | off | inferred (default → not stored) |
| 14 | Differential – power     | 0.3500  | 35    | confirmed |
| 15 | Differential – coast     | 0.3000  | 30    | confirmed |
| 16 | Differential – preload   | 140.0   | 140   | confirmed |
| 20 | Front Bump (low speed)   | 560     | 0.6   | confirmed |
| 21 | Front Bump (high speed)  | 1196    | 1.2   | confirmed |
| 22 | Front Rebound (low)      | 1832    | 1.8   | confirmed |
| 23 | Front Rebound (high)     | 2468    | 2.5   | confirmed |
| 24 | Springs – Front          | 47680   | 47.7  | confirmed |
| 25 | Anti-roll bar – front    | 17500   | 17.5  | confirmed |
| 26 | Front Camber             | -1.5    | -1.5  | confirmed |
| 27 | Front Toe                | -0.05   | -0.05 | confirmed |
| 28 | Rear Bump (low speed)    | 3104    | 3.1   | confirmed |
| 29 | Rear Bump (high speed)   | 3740    | 3.7   | confirmed |
| 30 | Rear Rebound (low)       | 5012    | 5.0   | confirmed |
| 31 | Rear Rebound (high)      | 5648    | 5.6   | confirmed |
| 32 | Springs – Rear           | 43120   | 43.1  | confirmed |
| 33 | Anti-roll bar – rear     | 77500   | 77.5  | confirmed |
| 34 | Rear Camber              | *(absent)* | -1.0 | inferred (symmetry with 26/27; default → not stored) |
| 35 | Rear Toe                 | -0.35   | -0.35 | confirmed |
| 40 | Gearbox – final drive    | 2.2000  | 2.20  | confirmed |
| 41 | Gearbox – gear 1         | 2.3016  | 2.30  | confirmed |
| 42 | Gearbox – gear 2         | 2.6880  | 2.69  | confirmed |
| 43 | Gearbox – gear 3         | 2.9088  | 2.91  | confirmed |
| 44 | Gearbox – gear 4         | 3.1848  | 3.18  | confirmed |
| 45 | Gearbox – gear 5         | 4.1232  | 4.12  | confirmed |
| 46 | Gearbox – gear 6         | 4.8960  | 4.90  | confirmed |
| 53 | Ride Height – Front      | 0.2035  | 20.3  | confirmed |
| 54 | Ride Height – Rear       | 0.1980  | 19.8  | confirmed |
| 55 | Ackerman                 | 15.0    | 15%   | confirmed |
| 56 | Oversteer Bias           | 0.0     | 0     | probable (see caveat) |

## Observed but unmapped

### idx 3 & 4 — IDENTIFIED as the front differential (2026-07-23)

`wf2 calibrate` + co-occurrence analysis: idx 3 and 4 appear **only** on the FWD cars (Crusader/car05,
Phaser/car12 — both have `transmission/fwd_adjustable`), always together, and **never** with the RWD
differential trio 14/15/16. That is the front-differential signature. idx 4's observed 20–150 range
matches `differential_fwd_full.ctms` `5→150`. So:

- **idx 3 = Front Differential – power/lock** (0–1), the FWD analog of idx 14.
- **idx 4 = Front Differential – preload** (5–150), the FWD analog of idx 16.

The *category* is certain (drivetrain correlation); the exact power-vs-lock wording is not, so both are
marked `Confirmed = false` in `ParamMap.cs`.

### Still unidentified — 51, 52, 57, 58, 59

Ranges are known (see the empirical table below); the names are not. `PresetIo` carries them through
verbatim, so they import/export/duplicate correctly regardless.

| idx | Observed range | Cars seen on | Notes toward identification |
|----:|---|---|---|
| 51 | 696 – 800 | Crusader, Hurricane, Jackal, Nami, Rocket | common; sits between gearbox (40–46) and ride height (53) |
| 52 | 11.2 – 13.95 | Crusader, Hurricane, Nami, Rocket | fine steps (12.083, 12.917) — a computed ratio? |
| 57 | 0 only | Jackal, Phaser | always default so far — needs a preset that moves it |
| 58 | 0.5 only | Nami (1 preset) | co-occurs with 59 |
| 59 | 0.333, 1 | Nami, Phaser | thirds — plausibly a 3-position selector |

**To finish these:** open one of the listed cars in-game, and for the suspect slider set it to a
**distinctive** value, save the preset, and `wf2 calibrate` / `wf2 cars` before-and-after to see which
index moved. 51 is the easiest (common, wide range). 57/58/59 are rare — they need a preset that
actually exercises them.

## Where the defaults ("base state") live — found 2026-07-23

**Yes, the game ships each car's baseline setup**, and it is readable with our existing container
decoder. This matters because a preset stores only *non-default* values: an empty preset is not
"nothing", it is "all of these bases". Dump any of these with `wf2 bbag <file>`.

Under `data/vehicle/<car>/part/` there is a **property-file layer** beneath the `.upgr` parts — all
of them ordinary bbag containers (CRCs verify):

| File | Holds |
|---|---|
| `brakes/*.vbpr` | brake torque, **balance default**, **pressure default** |
| `chassis.vecs` | masses / CoG-ish chassis constants (e.g. `1050` ≈ kerb mass) |
| `suspension/geom.vesg` | suspension hardpoint geometry, in metres |
| `differential/*.vedi`, `tires/*.vtpr`, `steering.vest` | per-part physical properties |
| `suspension/<springs+dampers>.upgr` (the **non-adjustable** variant) | base spring rates, damper rates, static camber |

**Worked examples (car02 / Hurricane):**

- **Brakes** — `disc_stock.vbpr` / `disc_sport.vbpr` / `adjustable_disc.vbpr` differ in exactly one
  float: `0.6 / 0.5 / 0.55`. That is **Braking Balance's default** (idx 0), per brake part. The next
  float is `100` — **Braking Pressure's default** (idx 1), the midpoint of `brakes_full.ctms`'s
  `50 → 150`. Clean and unambiguous.
- **Suspension** — `race_springs_stiff_dampers.upgr` carries base spring rates **64000 N/m front /
  39500 N/m rear**, damper pairs (5800/2800, 8300/3700 front; 4200/2000, 6000/2700 rear), and static
  camber `-0.017453` rad = **exactly −1.0°**. Note the unit shift: the game file stores camber in
  **radians**, the save stores it in **degrees**.
- **Part → schema binding is now proven, not inferred.** Each adjustable part's `smtc` node names its
  schema outright — the adjustable suspension points at
  `data/vehicle/shared/part/tuning/suspension_full.ctms`. Adjustable variants are thin wrappers that
  *reference* their non-adjustable sibling, so bases chain through the part tree.

## The arithmetic — SOLVED 2026-07-23

**`aux` is the slider position, and the stored value is derived from it:**

```
value = min + aux × (max − min) / steps
```

`min`, `max` and **`steps`** all come from the part's `.ctms` record. `steps` is a **u32 sitting
immediately after `max`** (tag+0x14) that we had simply never decoded — it is the number of slider
increments, and it is the whole missing link.

**How it was established.** Fitting `value = A + B × aux` over every (car, paramIndex) pair in all
five saves came back **exactly linear in all 90+ cases, no exceptions**. Then the fitted constants
turned out to be exactly the schema:

| Parameter | fitted intercept | `.ctms` min | fitted step | `(max−min)/steps` |
|---|---|---|---|---|
| Braking Balance (0) | 0 | 0 | 0.01 | (1−0)/100 |
| Braking Pressure (1) | 50 | 50 | 5 | (150−50)/20 |
| Diff preload (16) | 5 | 5 | 5 | (150−5)/29 |
| Anti-roll bar (25/33) | 0 | 0 | 2500 | (100000−0)/40 |
| Camber (26/34) | −5 | −5 | 0.25 | (2−(−5))/28 |
| Toe (27/35) | −2 | −2 | 0.05 | (2−(−2))/80 |
| Gearbox final drive (40) | 2.2 | 2.2 | 0.1 | (6.1−2.2)/39 |
| Ackerman (55) | 0 | 0 | 5 | (100−0)/20 |

Spot-checks against real records: pressure `50+9×5 = 95` ✓, camber `−5+10×0.25 = −2.5` ✓, preload
`5+3×5 = 20` ✓, final drive `2.2+14×0.1 = 3.6` ✓, Ackerman `0+15×5 = 75` ✓.
`TuningArithmeticTests` asserts the law for **every** record of every known parameter in every fixture.

**RETRACTION — the old gearbox formula was wrong.** This document previously claimed
`value = min + aux/100 × (max−min)`, "verified exactly on all 7 gear params". For the final drive that
predicts `2.2 + 0.039×aux`, but the real law is `2.2 + 0.1×aux` (i.e. `/steps` = /39, not /100). The
old formula only ever coincided because `aux` was small. Use `/steps`.

**Consequences.** For `armt` (absolute) parameters the legal range and every legal value are now
computable straight from the `.ctms` — no empirical guessing. `aux` is authoritative: an editor should
set `aux` and *derive* the value, which guarantees an in-range, on-step value by construction.

**Still open — relative parameters.** `prmt` (springs, dampers) and `rrmt` (ride height) have min/max
expressed as an offset range (±60 %, −20…30) against the fitted part's base, so their *absolute*
bounds are car- and part-specific. The same law holds — each car's fit is exactly linear — but deriving
`A`/`B` from the base file is unsolved: Hurricane front springs fit `A=11200, B=4560` (so with
`steps=20`, range 11200…102400), which is neither ±60 % of the part's base spring rate (64000) nor of
the rear's (39500). Note front and rear share identical `A`/`B` despite different base rates, so it is
not a simple per-axle scaling. Until that is cracked, take the effective bounds for these from the
car's own fit (`min = A`, `max = A + B × steps`), or the empirical ranges below.

**Caveat:** some `.ctms` records carry `steps = 0` (e.g. 8 of `differential_full`'s 12 params). Those
are schemas for parts none of our cars fit; `TuningParameter.StepSize` returns 0 rather than dividing
by zero. Treat a zero-step parameter as "unknown", not "single-valued".

## Empirical stored-unit ranges (for M5 range validation)

`wf2 calibrate <save> [more saves…]` aggregates every stored value per index across all presets. Run
across the live save + all fixtures (241 presets), these are the **observed** min/max in **stored SI
units** — the only ranges usable for validation, because the `.ctms` min/max are per-parameter
offset/percentage scales with car-specific bases (springs store 17120–84160 N/m while
`suspension_full.ctms` shows `-60→60`, so `.ctms` cannot be compared to a stored value directly).

These are a **lower bound** on the true legal range — they cover what players actually set, which for a
warn-only validation is the honest bound to use. A full in-game min/max sweep (one preset with every
slider maxed, one minned) would tighten them to the true extremes. Regenerate anytime with
`wf2 calibrate`. **Watch for the `float.MinValue` (~-3.4e38) "unset/inherit" sentinel** some records
carry — `calibrate` skips it; anything reading raw records must too.

## Caveats

- **idx 2 vs idx 56 — distinctness now corroborated.** The old worry was that both read `0.0`. The
  241-preset aggregate shows idx 2 populated across 0.45–1 (38 records) and idx 56 across 0–1 (89
  records): they are clearly two separate, real parameters. The *labels* (which is Front Balancer,
  which is Oversteer Bias) are still inferred, not slider-confirmed.
- **Value 140 appears twice** (idx 1 Braking Pressure, idx 16 Differential preload) because both were
  set to 140. They were separated by part grouping, not by value. A re-run should use distinct numbers.
- Indices 3–13, 17–19, 36–39, 47–52 were not exercised — they belong to parts the Hurricane doesn't
  have fitted (e.g. FWD differential, 5-speed gearbox, half-suspension) or are unused.
