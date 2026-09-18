#!/usr/bin/env python3
"""
Read-only prod DB scan for stuck PAAssignment rows — same query shape used to
scope the 2026-09-18 ConfirmInvoice sync-gap incident (see project memory:
project_pa_payment_flow.md, project_reconciliation_duplicate_row_fix.md).

Run manually whenever you want a fresh count. No writes, no scheduling — just
prints the current scope so a growing backlog gets noticed within days instead
of months (the pattern every prior manual cleanup in this project's history
has hit: fixed once, silently reaccumulates, only caught by the next unrelated
incident weeks or months later).

Usage: python3 stale_assignment_scan.py
Requires: pip3 install --user pymysql
"""
import pymysql

HOST = "13.126.212.217"
PORT = 3306
USER = "vaccine_user"
PASSWORD = "V@cc1n3@P1!2024"
DB = "vaccineapi"


def main():
    conn = pymysql.connect(
        host=HOST, port=PORT, user=USER, password=PASSWORD,
        database=DB, cursorclass=pymysql.cursors.DictCursor,
    )
    cur = conn.cursor()

    print("=" * 70)
    print("1. Sync-gap rows — invoice Confirmed, assignment never synced")
    print("   (the exact 2026-09-18 bug class; ConfirmInvoice's self-heal")
    print("   path now closes these automatically on next doctor action,")
    print("   so a nonzero count here means the self-heal hasn't fired yet")
    print("   or a new gap has been introduced)")
    print("=" * 70)
    cur.execute("""
        SELECT COUNT(*) as cnt, COALESCE(SUM(inv.TotalAmount), 0) as total_amt
        FROM paassignments pa
        JOIN invoicesubmissions inv ON pa.InvoiceSubmissionId = inv.Id
        WHERE inv.IsConfirmedByDoctor = 1
          AND pa.IsCashConfirmedByDoctor = 0
          AND pa.IsCancelled = 0
    """)
    print(cur.fetchone())

    cur.execute("""
        SELECT p.Name as PaName, COUNT(*) as cnt, COALESCE(SUM(inv.TotalAmount), 0) as total_amt,
               MIN(pa.AssignedAt) as oldest, MAX(pa.AssignedAt) as newest
        FROM paassignments pa
        JOIN invoicesubmissions inv ON pa.InvoiceSubmissionId = inv.Id
        LEFT JOIN personalassistant p ON pa.PersonalAssistantId = p.Id
        WHERE inv.IsConfirmedByDoctor = 1
          AND pa.IsCashConfirmedByDoctor = 0
          AND pa.IsCancelled = 0
        GROUP BY p.Name
        ORDER BY cnt DESC
    """)
    for row in cur.fetchall():
        print(row)

    print()
    print("=" * 70)
    print("2. Orphan-FK rows — no invoice link at all, 7+ days stale")
    print("   (never-linked assignments; distinct from the sync-gap class)")
    print("=" * 70)
    cur.execute("""
        SELECT COUNT(*) as cnt
        FROM paassignments
        WHERE InvoiceSubmissionId IS NULL
          AND IsCancelled = 0
          AND IsCashConfirmedByDoctor = 0
          AND AssignedAt < DATE_SUB(NOW(), INTERVAL 7 DAY)
    """)
    print(cur.fetchone())

    print()
    print("=" * 70)
    print("3. Ungive/Edit amendments approved but assignment never closed")
    print("   (door #3 from the 2026-09-18 fix — should be zero going")
    print("   forward now that InvoiceAmendmentController.Approve closes")
    print("   the assignment on an approved Ungive; a nonzero count means")
    print("   either a pre-fix historical row or a new gap)")
    print("=" * 70)
    cur.execute("""
        SELECT COUNT(*) as cnt
        FROM invoiceamendments am
        JOIN invoicesubmissions inv ON am.InvoiceSubmissionId = inv.Id
        JOIN paassignments pa ON pa.InvoiceSubmissionId = inv.Id
        WHERE am.AmendmentType = 'Ungive'
          AND am.IsApprovedByDoctor = 1
          AND inv.InvoiceStatus = 'Cancelled'
          AND pa.IsCashConfirmedByDoctor = 0
          AND pa.IsCancelled = 0
    """)
    print(cur.fetchone())

    conn.close()


if __name__ == "__main__":
    main()
