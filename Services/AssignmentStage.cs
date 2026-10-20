using System;
using System.Collections.Generic;
using System.Linq;

namespace VaccineAPI.Services
{
    // The one place that turns an assignment's pinned doses into a stage the doctor sees
    // (PA_TRACKING_MERGE_SPEC.md). Pure and database-free so it can be unit tested.
    //   1 Assigned   not every pinned dose is given yet
    //   2 Given      every pinned dose given, no invoice yet
    //   3 Invoiced   a pinned dose is stamped to an invoice, payment not complete
    //   4 Paid       every billable pinned dose has its payment recorded, not confirmed by the doctor
    //   5 Confirmed  the doctor confirmed cash / closed the assignment
    // The PA handover is a lump sum per PA, not per assignment, so it is not a stage here.
    public static class AssignmentStage
    {
        public class DoseFacts
        {
            public bool IsDone { get; set; }
            public decimal Amount { get; set; }
            public bool IsPaymentCollected { get; set; }
            public long? InvoiceSubmissionId { get; set; }
            public DateTime? DoneAt { get; set; }
            public DateTime? InvoiceSubmittedAt { get; set; }
        }

        public class Result
        {
            public int Stage { get; set; }
            public string Label { get; set; } = "";
            public DateTime Since { get; set; }
        }

        private static readonly string[] Labels = { "", "Assigned", "Given", "Invoiced", "Paid", "Confirmed" };

        // "since" is the best timestamp the records hold for entering the stage: assignment time,
        // last give time, invoice submit time. There is no payment timestamp on a dose, so Paid
        // reuses the invoice time.
        public static Result Compute(DateTime assignedAt, bool cashConfirmed, DateTime? cashConfirmedAt, IList<DoseFacts> doses)
        {
            if (cashConfirmed)
                return new Result { Stage = 5, Label = Labels[5], Since = cashConfirmedAt ?? assignedAt };

            var allGiven = doses.Count > 0 && doses.All(d => d.IsDone);
            if (!allGiven)
                return new Result { Stage = 1, Label = Labels[1], Since = assignedAt };

            var givenAt = doses.Where(d => d.DoneAt.HasValue).Select(d => d.DoneAt!.Value).DefaultIfEmpty(assignedAt).Max();
            var invoiced = doses.Where(d => d.InvoiceSubmissionId.HasValue).ToList();
            if (invoiced.Count == 0)
                return new Result { Stage = 2, Label = Labels[2], Since = givenAt };

            var invoiceAt = invoiced.Where(d => d.InvoiceSubmittedAt.HasValue).Select(d => d.InvoiceSubmittedAt!.Value).DefaultIfEmpty(givenAt).Max();
            var billable = doses.Where(d => d.Amount > 0).ToList();
            var paid = billable.Count > 0 && billable.All(d => d.IsPaymentCollected);
            return paid
                ? new Result { Stage = 4, Label = Labels[4], Since = invoiceAt }
                : new Result { Stage = 3, Label = Labels[3], Since = invoiceAt };
        }

        public static int AgeDays(DateTime since, DateTime nowUtc)
        {
            var d = (int)Math.Floor((nowUtc - since).TotalDays);
            return d < 0 ? 0 : d;
        }
    }
}
