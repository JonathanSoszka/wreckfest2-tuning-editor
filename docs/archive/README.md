# Archive — historical documents

Kept for provenance. **None of these are current.** If anything here contradicts
[`docs/format.md`](../format.md) or the root [`README.md`](../../README.md), those win.

| File | What it was | Why archived |
|---|---|---|
| `PLAN.md` | The original project spec | Predates the format being solved. Its goal statement (exceed the game's limits) and save path were both wrong, and Milestone 0.2 concluded header `0x10` was "cosmetic" — the single most expensive error in the project. |
| `PLAN_runtime.md` | Live-memory editing plan | Abandoned approach. Never isolated the authoritative value; unnecessary once save writing worked. |
| `PROGRESS.md` | Running changelog + milestone table | Milestones predate the solve. The dated changelog is still useful as a narrative of how the format was cracked. |
| `HANDOFF.md` | Investigation record | Written mid-project; §3–5 describe the record hash as unreproducible and memory editing as the way forward. Both superseded. |
| `CALIBRATION.md` | Procedure that mapped `paramIndex` → parameter names | Completed; results live in `docs/PARAM_MAP.md`. Two of its premises were wrong (values are physical, not percentages; `aux` is only verified for the gearbox). Re-usable if new adjustable parts appear. |

The reason these are worth keeping: several dead ends here (Steam Cloud delivery, live-memory
editing, the hash hunt) were expensive, and the record stops someone re-running them.
