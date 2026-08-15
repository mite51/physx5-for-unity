# Cross-platform determinism: can a phone stay in sync with a PC?

Findings from investigating why an Android build (Galaxy Note 10+) would not stay in sync
with a Windows build in the previous UNDPWR iteration, and what it would take to change
that.

## Short answer

The phone's hardware is not the problem. The Snapdragon 855 / Exynos 9825 is ARMv8.2-A
and fully IEEE-754 compliant for the basic arithmetic operations, so the instinct that it
*should* work is sound in principle.

It did not work because **PhysX compiles fundamentally different arithmetic on the two
platforms**, not because of an FPU mode or a rounding setting. The divergence is roughly
four orders of magnitude larger than float noise, and it appears from the very first step.

It is fixable in principle, and less expensively than that makes it sound, because the
largest cause can be removed with a single build flag. But nothing here comes with a
guarantee, and it is a build-configuration project rather than a framework change.

There is also a prerequisite problem: **this PhysX 5 distribution has no Android build
support at all**, so there is porting work before determinism even becomes the question.

---

## What was found

### 1. The two platforms do not run the same code

PhysX decides whether to use SIMD like this:

```
// physx/include/foundation/PxVecMath.h
#if !defined(PX_SIMD_DISABLED)
#if PX_INTEL_FAMILY && (!defined(__EMSCRIPTEN__) || defined(__SSE2__))
	#define COMPILE_VECTOR_INTRINSICS 1
#elif PX_SWITCH
	#define COMPILE_VECTOR_INTRINSICS 1
#else
	#define COMPILE_VECTOR_INTRINSICS 0
#endif
#else
	#define COMPILE_VECTOR_INTRINSICS 0
#endif
```

On Android/ARM64 that evaluates to `0`. The NEON headers under
`foundation/unix/neon/` exist but are effectively dead code — only Intel and Nintendo
Switch enable vector intrinsics, and everything else falls through to the scalar
implementation in `PxVecMathAoSScalar.h`.

So a Windows build runs SSE and an Android build runs plain scalar C++. Three different
arithmetic backends are reachable in total:

| platform | backend |
| --- | --- |
| Windows / Linux x86-64 | SSE2 (`PxVecMathSSE.h`) |
| Nintendo Switch | NEON (`PxUnixNeonInlineAoS.h`) |
| Android, and every other ARM target | scalar (`PxVecMathAoSScalarInline.h`) |

Any two of those disagree, and they disagree differently in each pairing.

### 2. The disagreement is large, not last-bit

This would be survivable if the backends produced the same numbers. They do not, and the
gap is nowhere near rounding noise. The approximate reciprocal operations are the clearest
example.

x86 uses the hardware approximation instructions directly:

```
// physx/include/foundation/PxVecMathSSE.h
PX_FORCE_INLINE Vec4V V4RecipFast(const Vec4V a) { return _mm_rcp_ps(a); }
PX_FORCE_INLINE Vec4V V4RsqrtFast(const Vec4V a) { return _mm_rsqrt_ps(a); }
```

The scalar path computes them exactly:

```
// physx/include/foundation/PxVecMathAoSScalarInline.h
PX_FORCE_INLINE Vec4V V4RecipFast(const Vec4V a)
{
	return Vec4V(1.0f / a.x, 1.0f / a.y, 1.0f / a.z, 1.0f / a.w);
}
PX_FORCE_INLINE Vec4V V4RsqrtFast(const Vec4V a)
{
	return Vec4V(PxRecipSqrt(a.x), PxRecipSqrt(a.y), PxRecipSqrt(a.z), PxRecipSqrt(a.w));
}
```

`_mm_rcp_ps` is a twelve-bit table lookup whose relative error is specified only as being
within 1.5 × 2⁻¹², about **3.7e-4**. `1.0f / x` is correctly rounded to within half an ulp,
about 6e-8. A constraint solver touches reciprocals constantly — inverse mass, inverse
inertia, effective-mass denominators, normalisations — so the two platforms compute
visibly different numbers immediately.

For scale, put that next to the numbers this framework already measures. The replay error
from PhysX's unexposed warm-start contact impulses, which is the irreducible noise floor
we designed the rollback around, is **1.8e-06 m** over thirty ticks. The reciprocal
approximation is four orders of magnitude coarser than that, per operation. This is why the
previous Android build did not drift apart gradually: it was never on the same trajectory
at all.

Note also that the NEON path is a *third* answer. It uses an estimate instruction refined
by Newton–Raphson iterations:

```
// physx/include/foundation/unix/neon/PxUnixNeonInlineAoS.h
template <int n>
PX_FORCE_INLINE float32x4_t recipq_newton(const float32x4_t& in)
{
	float32x4_t recip = vrecpeq_f32(in);
	for(int i = 0; i < n; ++i)
		recip = vmulq_f32(recip, vrecpsq_f32(recip, in));
	return recip;
}
```

So forcing NEON on for Android would not fix anything. It would replace one disagreement
with a different one.

### 3. PhysX 5 has no Android build support in this distribution

The available build presets are:

```
physx/buildtools/presets/public/
    linux-aarch64-clang{,-cpu-only}.xml
    linux-aarch64-gcc{,-cpu-only}.xml
    linux-clang{,-cpu-only}.xml
    linux-gcc{,-cpu-only}.xml
    vc16win64{,-cpu-only}.xml
    vc17win64{,-cpu-only}.xml
```

and `physx/source/compiler/cmake/` contains only `linux/` and `windows/` platform
directories. PhysX 4 shipped Android presets; PhysX 5 as distributed here does not.

The `linux-aarch64-clang` preset is encouraging — the ARM64 code paths are maintained and
buildable — and Android is close enough to Linux that adapting it to the NDK toolchain is
plausible. But it is unsupported work that has to happen before any determinism testing
can start.

The plugin's own `CMakeLists.txt` has the same gap: it branches only on `Linux` and
`Windows`, and links `PhysX*_64` / `PhysX*_static_64` by name.

### 4. What remains after the SIMD split is removed

Three further causes, none of which is a runtime setting anything could flip from C#.

**Fused multiply-add contraction.** Clang on the NDK will fold `a * b + c` into a single
`FMLA` with one rounding step, while MSVC under `/fp:precise` emits a separate multiply and
add with two. Same source, different result. PhysX's `FScaleAdd` and friends are used
throughout the solver, so this is pervasive rather than incidental.

**Transcendental functions.** IEEE-754 specifies correct rounding for `+ - * /` and `sqrt`,
but says nothing about `sin`, `cos`, `acos`, `atan2`, `exp`, `log` or `pow`. Android's
bionic and the Windows UCRT differ in the last bits, and PhysX calls straight through to
whichever libc it was linked against:

```
// physx/include/foundation/PxMath.h
PX_FORCE_INLINE float PxAcos(float f)          { return ::acosf(PxClamp(f, -1.0f, 1.0f)); }
PX_FORCE_INLINE float PxAtan2(float x, float y) { return ::atan2f(x, y); }
PX_FORCE_INLINE float PxSin(float a)            { return intrinsics::sin(a); }
```

These are reached by joint limits, articulation drives and the vehicle tire model. The
useful part of this finding is that `PxMath.h` is a **single interception point** — every
transcendental in PhysX funnels through it.

**Denormal handling.** Flush-to-zero lives in `MXCSR` on x86 and `FPCR` on ARM. Nothing
guarantees Unity, PhysX or the platform runtime configures them the same way on both, and a
mismatch changes results near zero.

---

## What it would take to proceed

Ordered by leverage. Each step removes a known cause of divergence; none of them proves
the result.

### Step 1 — build the PC side with `PX_SIMD_DISABLED`

The single highest-value change. Defining `PX_SIMD_DISABLED` for the Windows PhysX build
forces `COMPILE_VECTOR_INTRINSICS` to `0`, so both platforms compile the identical scalar
C++ from `PxVecMathAoSScalarInline.h`. This deletes the entire approximate-reciprocal
problem in one flag, rather than attempting to make two different algorithms agree.

Unifying downward to scalar is the right direction. Unifying upward — enabling NEON on
Android — cannot work, because SSE and NEON approximate reciprocals with different
hardware tables and different refinement.

**Cost:** PhysX without SIMD is meaningfully slower, plausibly two to four times in the
solver. For a title shipping phone crossplay this may be acceptable, since the simulation
budget is already set by the phone and the PC would simply have less headroom to spare
than it otherwise would.

### Step 2 — pin the floating-point contract in both compilers

| toolchain | flags |
| --- | --- |
| MSVC | `/fp:precise` (do not use `/fp:fast`); avoid `/arch:AVX2`, which permits FMA formation |
| Clang / NDK | `-ffp-model=precise`, `-ffp-contract=off`, `-fno-fast-math`; never `-Ofast` |

`-ffp-contract=off` is the important one. The default permits FMA contraction and that is
where clang and MSVC part company.

### Step 3 — ship one transcendental implementation

Replace the libc calls in `PxMath.h` with a portable implementation compiled from the same
source on both platforms — a fixed snapshot of something like musl's libm, or a small
correctly-rounded set covering only the functions PhysX actually uses. Because everything
routes through `PxMath.h`, this is a contained change rather than an audit of the whole
codebase.

### Step 4 — pin denormal mode explicitly

Set flush-to-zero and denormals-are-zero off on both platforms at startup, and assert the
state rather than assuming it. Unity and PhysX both touch these, and the defaults are not
promised to match.

### Step 5 — verify empirically, and keep verifying

None of the above is a proof. It is a set of changes that removes the known causes, after
which the only real answer is measurement — and re-measurement after every NDK bump, MSVC
update or PhysX upgrade, because each of those can silently reintroduce a difference.

---

## How to measure it cheaply

The machinery already exists. `tests/PxwUndpwrTests.cpp` in the native plugin is a
self-contained executable that builds a scene, simulates it and prints state hashes plus
per-entity hashes. No Unity, no netcode, no rendering.

1. Run it on the PC. Keep the output as a reference trace.
2. Cross-compile it for `arm64-v8a` with the NDK and run it on the device over `adb`.
3. Diff the hashes.

The per-entity hash output names the first body that diverges, which usually identifies the
cause. Run it twice: once with stock settings to confirm the diagnosis above, and again with
`PX_SIMD_DISABLED` on both sides to see how much of the gap that one flag closes. That
second number decides whether the remaining work is worth starting.

A useful intermediate check that avoids the Android port entirely: build for
`linux-aarch64-clang` and run it under an ARM64 Linux VM or container. Same architecture,
same scalar backend, none of the Android toolchain work — enough to answer "does
architecture unification actually work" before committing to a port.

## One thing worth testing regardless

`_mm_rcp_ps` and `_mm_rsqrt_ps` are specified only by an error bound; the exact values are
implementation-defined, and Intel and AMD have historically returned different results for
them. If PC-to-PC testing so far has all been on one CPU vendor, mixed-vendor play may have
the same class of problem waiting, on desktop, today.

This is cheap to rule out: run `PxwUndpwrTests` on one Intel and one AMD machine and diff
the hashes. If they differ, `PX_SIMD_DISABLED` fixes that too, which would change the
cost/benefit of Step 1 considerably.

---

## Recommendation

**Do the vendor test first.** One Intel machine, one AMD machine, existing test executable,
an afternoon at most. It is the cheapest experiment available and it affects a shipping
configuration right now rather than a hypothetical one.

**Then do the aarch64 Linux test.** It answers the architecture question without the Android
port, and it tells you whether `PX_SIMD_DISABLED` genuinely unifies the two before any
significant investment.

**Treat Android crossplay as a separate project**, not as a configuration option. The PhysX
Android port alone is substantial, and it sits underneath four more layers of work that each
need their own verification.

**Have a fallback ready.** Architecture-segregated matchmaking — PC with PC, mobile with
mobile — is what most shipped titles quietly do, and it costs nothing to design for now.
Games that genuinely achieve cross-architecture lockstep almost always use fixed-point
arithmetic or a software float library, paired with far simpler physics than a full PhysX
solver.

## The encouraging part

If bit-exactness across architectures were achieved, **the UNDPWR framework needs no
architectural change whatsoever**. The free-running prediction, the synchronised rebuild
and the confirmed-tick hashing all work unmodified. Nothing in the netcode design assumes a
single architecture; it assumes that the same operation on the same bytes produces the same
result, and that assumption is exactly what the work above would be restoring.

This is a problem that lives entirely underneath the framework, in the build configuration
of PhysX itself.
