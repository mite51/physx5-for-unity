# UNDPWR documentation

| document | what it covers |
| --- | --- |
| [Architecture.md](Architecture.md) | How the framework is built: layers, identity, state and snapshots, the tick lifecycle, input flow, data ownership, join and resync, extension points, failure modes. **Read this first.** |
| [CrossPlatformDeterminism.md](CrossPlatformDeterminism.md) | Why peers must share a CPU architecture today, what specifically breaks between ARM and x86, what it would take to change, and how to test it cheaply. |

The [package README](../README.md) is the short version: the governing rule, the measurements
behind it, and a usage sketch.

## Active investigation

[DeterminismInvestigation.md](DeterminismInvestigation.md) is the live working document for
bitwise determinism under rollback: what has been measured, what changed, which conclusions
turned out to be wrong, and what is still open. `Architecture.md` has been brought back in
line with it; where the two disagree, the investigation is newer.

Read its §5 before proposing a theory. Five plausible ones have already been measured and
killed, and the fifth was very nearly killed while being correct.

## Where to start

**Using the framework** — package README, then Architecture §10 (where your game plugs in)
and §11 (presentation).

**Changing the framework** — Architecture §2 first. Most of the design is shaped by one
constraint that is not visible from the code, and changes that look harmless tend to violate
it.

**Diagnosing a desync** — Architecture §15 (failure-mode table), then §5.4 for per-entity
hashing.

**Considering mobile or console** — CrossPlatformDeterminism.md, including the desktop
Intel-versus-AMD test that is worth running regardless.

## Where the numbers come from

Every measurement quoted across these documents is produced by `tests/PxwRollbackRepro.cpp`,
`tests/PxwUndpwrTests.cpp` and `tests/PxwPoseRoundTripTests.cpp` in the native plugin
repository. They are characterisation tests as much as regression tests: they exist so the
limits stay documented rather than being rediscovered.

`PxwRollbackRepro` prints `yes`/`no` lines alongside its pass/fail ones. A `no` is a
deliberately recorded limit, not a broken test — the suite is green at 22 checks, 0
failures, and the `no` lines are the subject matter.
