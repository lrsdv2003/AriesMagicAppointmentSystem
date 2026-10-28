-- Metadata-only manual preflight. Never executed against production by this task.
-- Use the intended database and an authorized read-only account. Do not paste credentials.
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
BEGIN
    SELECT N'MIGRATION_HISTORY_MISSING' AS Readiness;
    RETURN;
END;
SELECT expected.MigrationId,
       CASE WHEN actual.MigrationId IS NULL THEN N'MISSING_NO_GO' ELSE N'PRESENT' END AS Readiness
FROM (VALUES
        (N'20260906154742_InitiaCreate'),
        (N'20260907192521_AddProfilePicturePath'),
        (N'20260910132032_AddBookingServiceability'),
        (N'20260913142143_AddCommunicationCenter'),
        (N'20260913195500_RepairLegacyLastLoginAt'),
        (N'20260914041637_AddOcrAssistedVerification'),
        (N'20260917002455_AddEmailVerificationOtp'),
        (N'20260917033307_AddRescheduleRequestAuditTrail'),
        (N'20261021182539_AddTransactionalEmailOutbox'),
        (N'20261021183211_ProtectFinancialHistory'),
        (N'20261021220548_AddPostEventFeedback')
     ) AS expected(MigrationId)
LEFT JOIN dbo.__EFMigrationsHistory AS actual ON actual.MigrationId=expected.MigrationId
ORDER BY expected.MigrationId;
-- Extra history requires review; do not delete it or downgrade the database.
SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
-- Presence is necessary, not a full schema-drift/permission proof. Run normal
-- startup validation during an approved cutover; never auto-apply migrations.
