-- OPTIONAL OPERATOR READ-ONLY INVENTORY. Not executed by this assessment.
-- Use a reviewed read-only account against the intended database or an approved clone.
-- Returns aggregate classifications only: no names, account IDs, file paths or contents.
-- Missing/not-supplied values may be legitimate; these counts do not prove file existence.

WITH ProofReferences AS (
    SELECT N'Payment proof' AS ReferenceKind, ProofImagePath AS StoredPath FROM dbo.Payments
    UNION ALL
    SELECT N'Refund request proof', PaymentProofImagePath FROM dbo.RefundRequests
    UNION ALL
    SELECT N'Refund confirmation', RefundProofImagePath FROM dbo.RefundRequests
), Classified AS (
    SELECT ReferenceKind,
        CASE
            WHEN StoredPath IS NULL OR LEN(LTRIM(RTRIM(StoredPath))) = 0 THEN N'Not supplied'
            WHEN LOWER(StoredPath) LIKE N'/uploads/%' THEN N'Legacy web-root candidate'
            WHEN LOWER(StoredPath) LIKE N'payments/%'
              OR LOWER(StoredPath) LIKE N'refunds/%'
              OR LOWER(StoredPath) LIKE N'refund-confirmations/%' THEN N'Relative private-root candidate'
            ELSE N'Unmapped: private review required'
        END AS PathClass
    FROM ProofReferences
)
SELECT ReferenceKind, PathClass, COUNT_BIG(*) AS ReferenceCount
FROM Classified GROUP BY ReferenceKind, PathClass ORDER BY ReferenceKind, PathClass;

WITH ProfileReferences AS (
    SELECT CASE
        WHEN ProfilePicturePath IS NULL OR LEN(LTRIM(RTRIM(ProfilePicturePath))) = 0 THEN N'Not supplied'
        WHEN ProfilePicturePath LIKE N'/private-profiles/%' THEN N'Private App_Data profile candidate'
        WHEN ProfilePicturePath LIKE N'/uploads/profiles/%' THEN N'Legacy public profile candidate'
        ELSE N'Unmapped: private review required'
    END AS PathClass FROM dbo.AspNetUsers
)
SELECT PathClass, COUNT_BIG(*) AS ReferenceCount FROM ProfileReferences GROUP BY PathClass;

WITH MediaReferences AS (
    SELECT CASE WHEN ProcessingStatus = N'Ready' THEN N'Ready'
                WHEN ProcessingStatus = N'Quarantined' THEN N'Quarantined' ELSE N'Other' END AS StateClass,
           CASE WHEN LEN(StorageKey) = 68 AND RIGHT(StorageKey, 4) COLLATE Latin1_General_100_BIN2 = N'.jpg'
                     AND LEFT(StorageKey, 64) COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9a-f]%'
                THEN N'Expected key shape' ELSE N'Unmapped key shape' END AS KeyClass
    FROM dbo.FeedbackMedia
)
SELECT StateClass, KeyClass, COUNT_BIG(*) AS ReferenceCount
FROM MediaReferences GROUP BY StateClass, KeyClass ORDER BY StateClass, KeyClass;
