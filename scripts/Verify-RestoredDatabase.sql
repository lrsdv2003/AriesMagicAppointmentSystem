-- Read-only restore validation. Azure restore target names must use aries-restore-*.
SET NOCOUNT ON;
IF DB_NAME() NOT LIKE 'aries-restore-%'
    THROW 51002, 'Refusing restore verification outside the separately named review database.', 1;
DBCC CHECKDB WITH NO_INFOMSGS;
DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS;
SELECT 'Bookings' AS Component, COUNT_BIG(*) AS Records FROM dbo.Bookings
UNION ALL SELECT 'Payments',COUNT_BIG(*) FROM dbo.Payments
UNION ALL SELECT 'RefundRequests',COUNT_BIG(*) FROM dbo.RefundRequests
UNION ALL SELECT 'BookingTimelines',COUNT_BIG(*) FROM dbo.BookingTimelines
UNION ALL SELECT 'EmailOutboxMessages',COUNT_BIG(*) FROM dbo.EmailOutboxMessages;
SELECT Status, COUNT_BIG(*) AS Records, SUM(Amount) AS AmountTotal FROM dbo.Payments GROUP BY Status;
SELECT Status, COUNT_BIG(*) AS Records, SUM(COALESCE(ApprovedAmount,Amount)) AS AmountTotal FROM dbo.RefundRequests GROUP BY Status;
SELECT COUNT_BIG(*) AS PendingOutbox FROM dbo.EmailOutboxMessages WHERE DeliveredAtUtc IS NULL AND FailedAtUtc IS NULL;
SELECT COUNT_BIG(*) AS FailedOutbox FROM dbo.EmailOutboxMessages WHERE FailedAtUtc IS NOT NULL;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('dbo.Payments'),OBJECT_ID('dbo.RefundRequests')) AND referenced_object_id=OBJECT_ID('dbo.Bookings') AND delete_referential_action<>0)
    THROW 51003, 'Financial history cascade protection is missing.', 1;
