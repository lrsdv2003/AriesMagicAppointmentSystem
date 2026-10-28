-- OPTIONAL OPERATOR READ-ONLY METADATA QUERY. Never run automatically on production.
-- Run with an approved SELECT-only account against the confirmed application database.
-- The default output contains no raw filename, customer details, image or credential.
-- Classification is advisory; the running .NET resolver is authoritative.
DECLARE @PaymentId int = 3;

SELECT target.PaymentId AS RequestedPaymentId,
    CASE WHEN p.Id IS NULL THEN N'Row absent' ELSE N'Row present' END AS RowState,
    p.BookingId,
    CASE WHEN p.Id IS NULL THEN N'Not applicable' WHEN b.Id IS NULL THEN N'Booking row absent' ELSE N'Booking row present' END AS BookingRowState,
    CASE
        WHEN p.Id IS NULL THEN N'No reference: row absent'
        WHEN p.ProofImagePath IS NULL THEN N'Unexpected NULL: inspect privately'
        WHEN DATALENGTH(p.ProofImagePath) = 0 THEN N'Empty: resolver error candidate'
        WHEN LEN(LTRIM(RTRIM(p.ProofImagePath))) = 0 THEN N'Whitespace: invalid reference candidate'
        WHEN LEFT(p.ProofImagePath, 9) COLLATE Latin1_General_100_CI_AS = N'/uploads/'
            THEN N'Legacy: effective WebRootPath + uploads/...'
        WHEN p.ProofImagePath COLLATE Latin1_General_100_CI_AS LIKE N'payments/%'
            THEN N'Private: private base + financial-proofs + payments/...'
        ELSE N'Other format: private resolver review required'
    END AS ReferenceClass,
    DATALENGTH(p.ProofImagePath) AS StoredReferenceBytes,
    CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max), p.ProofImagePath)), 2) AS ReferenceSha256
FROM (VALUES (@PaymentId)) AS target(PaymentId)
LEFT JOIN dbo.Payments AS p ON p.Id = target.PaymentId
LEFT JOIN dbo.Bookings AS b ON b.Id = p.BookingId;

-- ONLY in the authorized operator's private session, if exact mapping is needed:
-- SELECT Id, BookingId, ProofImagePath FROM dbo.Payments WHERE Id = @PaymentId;
-- Do not paste the raw reference or image into a public ticket, log or diagnostic URL.
