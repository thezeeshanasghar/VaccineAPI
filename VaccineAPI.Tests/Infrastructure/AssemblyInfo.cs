// Tests toggle process-wide static flags (InventoryTransactionService.ExcludeExpiredFromFefo,
// StrictInvariants) so they must not run concurrently with each other.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
