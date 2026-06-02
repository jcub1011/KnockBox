# Alpha Chain — Implementation Milestones

This folder holds the five implementation milestone specs for Alpha Chain. They build on one
another in order; the game design itself lives in [`../alpha-chain-gdd.md`](../alpha-chain-gdd.md)
(see its **§8 Implementation Deviations** for where the shipped game intentionally differs from
the GDD).

| # | Spec | What it delivers |
|---|------|------------------|
| 1 | [`01-foundation.md`](01-foundation.md) | FSM skeleton, plugin/module wiring, turn loop, settings record. |
| 2 | [`02-chain-gameplay.md`](02-chain-gameplay.md) | Chain/succession rule, shot clock, banned letter + Zero-Point Tax, scoring entry. |
| 3 | [`03-card-system.md`](03-card-system.md) | Modifier + Action cards, the `(L + ΣA) × ΠM` scoring pipeline, card play. |
| 4 | [`04-era-and-intermission.md`](04-era-and-intermission.md) | Era loop, the four-sub-phase Intermission (Deal → Expansion → Optimization → Sniper Ban), game-over results. |
| 5 | [`05-host-config-polish-tests.md`](05-host-config-polish-tests.md) | Host configuration UI + validation, final visuals, drag-reorder UX, release-grade test coverage. |

## Conventions

- Each milestone's "Acceptance gate" lists the build/test/analyzer bar it must clear.
- The architecture this plugin slots into is documented in
  [`../../../KnockBox/Specs/knockbox-platform-architecture.md`](../../../KnockBox/Specs/knockbox-platform-architecture.md).
