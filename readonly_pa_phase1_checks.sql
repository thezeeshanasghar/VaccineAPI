-- READ-ONLY. Safe to run on prod. PA accountability Phase 1 checks.

-- 1. Active invoices with no saved PDF: the public "Verification-Invoice-PDF" link now returns 404 for these.
SELECT COUNT(*) AS active_invoices_without_pdf
FROM invoices WHERE IsVoided = 0 AND (PdfPath IS NULL OR PdfPath = '');

-- 1b. Same, split by year prefix of the number (first 2 chars), to see if it is only old data.
SELECT LEFT(InvoiceId,2) AS yr, COUNT(*) AS n
FROM invoices WHERE IsVoided = 0 AND (PdfPath IS NULL OR PdfPath = '')
GROUP BY LEFT(InvoiceId,2) ORDER BY yr;

-- 2. Invoice numbers shared by more than one child (needed before a unique index; expected: 2 per the audit).
SELECT InvoiceId, COUNT(DISTINCT ChildId) AS children, COUNT(*) AS rows_, SUM(IsVoided=0) AS active_rows
FROM invoices GROUP BY InvoiceId HAVING children > 1;

-- 3. Same number on several ACTIVE rows is normal (one print covers several doses); same number on several children is not.
SELECT COUNT(*) AS numbers_with_active_rows_for_several_children FROM (
  SELECT InvoiceId FROM invoices WHERE IsVoided = 0 GROUP BY InvoiceId HAVING COUNT(DISTINCT ChildId) > 1) x;

-- 4. Fee rows: invoice numbers with more than one fee row (audit said 522).
SELECT COUNT(*) AS invoice_numbers_with_multiple_fee_rows FROM (
  SELECT InvoiceId FROM fee GROUP BY InvoiceId HAVING COUNT(*) > 1) x;
