-- Read-only. Run against a restored copy first. Never delete records to pass this gate.
SET NOCOUNT ON;
SELECT
    (SELECT COUNT_BIG(*) FROM dbo.Payments WHERE Amount <= 0) AS InvalidPaymentAmounts,
    (SELECT COUNT_BIG(*) FROM dbo.RefundRequests WHERE Amount <= 0 OR (ApprovedAmount IS NOT NULL AND (ApprovedAmount <= 0 OR ApprovedAmount > Amount))) AS InvalidRefundAmounts,
    (SELECT COUNT_BIG(*) FROM dbo.RefundRequests r JOIN dbo.Payments p ON p.Id=r.OriginalPaymentId WHERE r.BookingId<>p.BookingId) AS MismatchedRefundPayments;
IF EXISTS (SELECT 1 FROM dbo.Payments WHERE Amount<=0)
    OR EXISTS (SELECT 1 FROM dbo.RefundRequests WHERE Amount<=0 OR (ApprovedAmount IS NOT NULL AND (ApprovedAmount<=0 OR ApprovedAmount>Amount)))
    OR EXISTS (SELECT 1 FROM dbo.RefundRequests r JOIN dbo.Payments p ON p.Id=r.OriginalPaymentId WHERE r.BookingId<>p.BookingId)
    THROW 51001, 'Financial preflight failed. Review historical records through an approved correction procedure before migration.', 1;
