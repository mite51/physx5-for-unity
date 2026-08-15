# Archive

These are historical design and investigation documents, kept for provenance. They were
written while the framework was being built and record how its determinism guarantees were
established and how the rollback model arrived at its current shape. They are **not**
maintained as part of the user manual and may describe intermediate states that no longer
match the code.

Start from the [manual](../README.md) instead. Read these only when you want the reasoning
and measurements behind a design decision the manual states as fact.

| document | what it records |
| --- | --- |
| [DeterminismInvestigation.md](DeterminismInvestigation.md) | The bitwise-determinism investigation under rollback: what was measured, which hypotheses were tested and discarded, and the numbers behind the cold-step and PGS requirements. |
| [AdaptiveRollbackPlan.md](AdaptiveRollbackPlan.md) | The route from a fixed prediction horizon to the free-running clock and conditional rollback that ship today. Fully travelled; retained for the measurements in its later sections. |
| [CrossPlatformDeterminism.md](CrossPlatformDeterminism.md) | The full analysis of why peers must share a CPU architecture, what specifically breaks between ARM and x86 (and possibly Intel and AMD), what it would take to change, and how to test it cheaply. The operative summary is in [Limits and platforms](../LimitsAndPlatforms.md). |
