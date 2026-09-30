# Design review resolutions

Two independent reviews of `INVENTORY_ARCHITECTURE.md` (red team, domain expert) found defects in the design. This file records what changes. Where this file and the architecture doc disagree, this file wins.

| Finding | Resolution | Stage |
|---|---|---|
| C1 Case 3 acceptance fails if Bill/Transfer refuse while pending uses exist | Inbound documents never refuse or prompt by default. Pending uses are left pending. Claiming is an opt-in field (`ClaimUnbatchedUseIds`) on the request. Response reports the pending count so a later UI can ask. | 5 |
| C1b direct callers of `AdministerSync` must still project BrandAmount | The compatibility shims flush and project on their own. | 1 |
| C2 retry/rollback impossible when joined to a controller-owned transaction | `RunAtomic` retries only when it owns the transaction. When joined it never retries and never clears the tracker: it returns a failure and the controller's `using tx` rolls back. | 1 |
| C3 new code on old schema breaks gives | No push from this work. Release order is a hard gate: run additive SQL (phase A) first, then deploy. Documented in the SQL file header. | 9 |
| H1 writers missing from the single-writer list | Cascade deletes (Clinic, Brand, Doctor, User, Dose, Child) are rejected while stock or ledger rows exist. `TransferPatients`, `CorrectBatch`, PA FullReset are listed and routed. | 8 |
| H2 BrandAmount creators outside the projection | One `EnsureBrandAmount` used by all four creators. `DoctorScheduleController` duplicate insert fixed. Duplicate audit before any unique index. | 8 |
| H3 soft reverse leaks into reports | Deferred. Documents keep their current delete behaviour in this work. Ledger reversal rows still reference the original movement. Soft reverse needs a full reader audit first. | later |
| H4/H5 reversal from summed legacy rows | Receipt reversal is planned from live `Quantity` and `OriginalQuantity` (batch must be untouched). Consumption reversal takes the latest un-reversed movement of the source document by id. | 2 |
| H6 non-negative guard blocks repair | Guard only when the delta is negative and the result would be below zero. | 1 |
| H7 phase B SQL defects | Phase B (legacy data correction) is not part of this work. | later |
| H8 multi-line requests | Plan keeps a running per-batch overlay. No SQL `Quantity > 0` prefilter when tracked state can differ. | 1 |
| H9 VacDoc breakage | No new prompts by default (see C1). `SplitConsumed` is fixed, not disabled. | 4 |
| M4 nested transactions in bulk give | Remove the inner `BeginTransaction` at the bulk path when it is wrapped. | 6 |
| M5 price-0 BrandAmount auto-create | Not introduced. Existing transfer-in behaviour kept and flagged in the audit. | 8 |
| M8 global StrictMode | Strict absolute checks are on in tests only. Delta-form checks always on. | 1 |
| Domain: expiry | FEFO expiry exclusion sits behind config `Inventory:ExcludeExpiredFromFefo`, default OFF, until the owner fixes the cut-off convention. BrandAmount stays the physical total. | 7 |
| Domain: pending dose data | Declared lot/expiry, PDF fix, P&L note are tracked as required follow-ups for stage 5. | 5 |
