# Block 13 — Cost accounting and hard project budgets

This document covers the cost/budget slice of Block 13. Vision QA, smart model routing, state curves, and multi-output reuse remain separate Block 13 work.

## Source of truth

Cost accounting reuses the existing persisted generation-job graph rather than introducing a parallel spend ledger.

Each cost-tracked job already carries:

- project ID
- optional scene ID
- job/generation type
- provider ID
- model ID
- estimated cost
- actual cost
- currency
- persisted lifecycle state

`ProjectCostService` aggregates those records into one project summary.

## Accounting semantics

The project summary exposes:

- actual cost
- reserved estimated cost
- projected cost = actual + unresolved reservations
- configured planning/estimated budget
- configured hard maximum budget
- remaining hard-cap amount
- count of provider jobs whose cost is still unknown
- per-generation rows
- per-scene totals
- per-provider/model totals

An actual cost replaces that job's estimate for accounting rather than being added on top of it.

An estimated provider cost remains reserved until `ActualCost` is explicitly resolved. This remains true when a job becomes `Cancelled`, `Rejected`, or `FailedPermanent`, because a terminal provider outcome does not by itself prove that the provider charged zero. An explicit `ActualCost = 0` releases the reservation.

Local/admin jobs with no provider and no estimated/actual cost do not create false unknown-spend warnings.

## Hard-cap enforcement

`MusicVideoProject.MaximumBudget` is treated as the hard spend ceiling. The current MVP budget currency is USD because project budget fields do not yet carry an independent currency.

When no hard maximum is configured, generation may proceed even when a provider does not expose an estimate.

When a hard maximum is configured:

- positive-cost work requires a known USD estimate
- existing cost-tracked jobs with neither estimate nor actual cost block additional positive spend
- projected spend after the new reservation must not exceed the hard maximum
- zero-cost local/mock/render jobs remain allowed

The system deliberately refuses unknown paid-provider cost under a hard cap instead of fabricating a number.

Real provider adapters can later supply provider/model-specific price estimators without changing the project accounting model.

## Atomic reservation boundary

A friendly preflight check runs in keyframe/video coordinators before a planned variant is registered.

The authoritative enforcement point is `BudgetAwareJobQueue`, the production `IJobQueue` implementation:

```text
request
  → optional coordinator preflight
  → BudgetAwareJobQueue
       → project budget gate
       → reload persisted project jobs
       → validate projected spend
       → persist the new job through JobService
       → release gate
```

A per-project `SemaphoreSlim` prevents two concurrent requests in the same backend process from both passing the same remaining-budget check before either reservation is persisted.

This is correct for the current modular-monolith deployment where one backend process owns enqueueing. A future multi-process deployment would require a database/transactional reservation primitive instead of relying on an in-process semaphore.

Persisted jobs remain the source of truth after restart; the semaphore is only a concurrency guard.

The generic `POST /api/jobs/` endpoint also uses `IJobQueue`, so supported API job creation cannot bypass the hard-cap boundary.

## API

```text
GET /api/projects/{projectId}/costs
```

The response includes project totals, generation rows, scene totals, and provider/model totals.

## Frontend

`ProjectCostPanel` is mounted in the project workspace and refreshes from persisted job changes via the existing SSE job stream.

Simple mode shows:

- actual spend
- reserved estimated spend
- projected spend
- remaining hard cap
- budget utilization / configured planning budget
- unknown-cost warning

Advanced/Custom additionally show:

- provider/model breakdown
- scene breakdown
- per-generation cost history

Saving project budget settings triggers an immediate refresh in addition to normal job-event refreshes.

## Current estimation support

Offline mock image/video generation is explicitly zero-cost. Local render work is also zero-cost.

Real image/video adapters are not implemented yet and there is no repository pricing catalog to derive a trustworthy estimate from. Therefore non-mock generation currently has unknown estimated cost. With a hard maximum configured, such work is intentionally rejected until a real adapter provides a reliable estimate.

## Validation status

Repository-side tests cover:

- actual vs reserved cost aggregation
- per-generation/provider/model/scene accounting
- terminal unresolved estimates remaining reserved
- explicit zero actual cost releasing a reservation
- hard-cap overspend rejection
- unknown-cost rejection under a hard cap
- unknown cost allowed when no hard maximum is configured
- zero-cost work under a hard cap
- concurrent reservations where only one request may fit the remaining budget

Frontend source tests cover the mounted cost panel, progressive disclosure, SSE refresh, typed cost client, and shared budget-aware enqueue wiring.

These tests, typecheck, browser behavior, and end-to-end provider billing are **not considered passed until executed**. See `TESTPLAN.md`.
