-- READ-ONLY reconciliation report (safe to run on production; it only SELECTs).
-- Stored stock vs ledger-derived stock vs BrandAmount, per clinic / brand / batch, and every anomaly class.
-- Equivalent to GET /api/InventoryAudit once the new build is deployed.

-- A) Per batch: stored vs ledger (drift = stored - ledger)
SELECT COALESCE(s.ClinicId, b.ClinicId) AS ClinicId, s.BrandId, s.Id AS StockId, s.BatchLot, s.Expiry,
       s.Quantity AS Stored, COALESCE(SUM(t.QuantityDelta),0) AS LedgerDerived,
       s.Quantity - COALESCE(SUM(t.QuantityDelta),0) AS Drift, s.IsClosed, s.OriginalQuantity
  FROM stocks s
  LEFT JOIN bills b ON b.Id = s.BillId
  LEFT JOIN inventorytransactions t ON t.StockId = s.Id
 GROUP BY s.Id, s.ClinicId, b.ClinicId, s.BrandId, s.BatchLot, s.Expiry, s.Quantity, s.IsClosed, s.OriginalQuantity
HAVING Drift <> 0
 ORDER BY ClinicId, s.BrandId, s.Id;

-- B) Per brand & clinic: BrandAmount vs sum of batches vs sum of ledger
SELECT ba.ClinicId, ba.BrandId, ba.DoctorId, ba.Quantity AS BrandAmount,
       (SELECT COALESCE(SUM(s.Quantity),0) FROM stocks s LEFT JOIN bills b ON b.Id = s.BillId
         WHERE s.BrandId = ba.BrandId AND COALESCE(s.ClinicId, b.ClinicId) = ba.ClinicId) AS BatchSum,
       (SELECT COALESCE(SUM(t.QuantityDelta),0) FROM inventorytransactions t
         WHERE t.BrandId = ba.BrandId AND t.ClinicId = ba.ClinicId) AS LedgerSum
  FROM brandamounts ba
HAVING BrandAmount <> BatchSum OR LedgerSum <> BatchSum
 ORDER BY ba.ClinicId, ba.BrandId;

-- C) Anomaly classes
SELECT 'NEGATIVE_BATCH' AS Code, Id AS StockId, Quantity FROM stocks WHERE Quantity < 0;
SELECT 'CLOSED_WITH_STOCK' AS Code, Id AS StockId, Quantity FROM stocks WHERE IsClosed = 1 AND Quantity > 0;
SELECT 'ORPHAN_BATCH' AS Code, s.Id AS StockId FROM stocks s LEFT JOIN bills b ON b.Id = s.BillId WHERE s.ClinicId IS NULL AND b.Id IS NULL;
SELECT 'STOCK_WITHOUT_LEDGER' AS Code, s.Id AS StockId, s.Quantity FROM stocks s
  WHERE s.Quantity > 0 AND NOT EXISTS (SELECT 1 FROM inventorytransactions t WHERE t.StockId = s.Id);
SELECT 'LEDGER_WITHOUT_STOCK' AS Code, t.Id AS LedgerId, t.StockId FROM inventorytransactions t
  WHERE t.StockId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM stocks s WHERE s.Id = t.StockId);
SELECT 'LEGACY_UNBATCHED_LEDGER' AS Code, ClinicId, BrandId, COUNT(*) AS Rows_, SUM(QuantityDelta) AS Units
  FROM inventorytransactions WHERE StockId IS NULL AND QuantityDelta <> 0 GROUP BY ClinicId, BrandId;
SELECT 'BRANDAMOUNT_NEGATIVE' AS Code, Id, Quantity FROM brandamounts WHERE Quantity < 0;
SELECT 'DUPLICATE_BRANDAMOUNT' AS Code, BrandId, DoctorId, ClinicId, COUNT(*) AS copies FROM brandamounts GROUP BY BrandId, DoctorId, ClinicId HAVING COUNT(*) > 1;
-- Transfers with only one side in the ledger (SourceType: TransferOut=7, TransferIn=8)
SELECT 'TRANSFER_UNPAIRED' AS Code, x.SourceId FROM (
   SELECT SourceId, SUM(SourceType = 7) AS outs, SUM(SourceType = 8) AS ins
     FROM inventorytransactions WHERE SourceType IN (7, 8) GROUP BY SourceId) x
 WHERE x.outs <> x.ins;
