-- SELECT-only public-catalog verification. No customer, account or credential data.
-- Run manually only against an authorized source using a read-only account.
-- This task does not execute it against production. Do not infer inclusions from images.
SET NOCOUNT ON;

SELECT s.Id, s.Name, s.Price, s.DurationInHours, s.Description,
       (SELECT COUNT(*) FROM dbo.ServiceInclusions AS i WHERE i.ServiceId=s.Id) AS InclusionCount
FROM dbo.Services AS s
WHERE s.IsArchived=0
ORDER BY s.Price,s.Id;

SELECT i.ServiceId,i.Id,i.Name
FROM dbo.ServiceInclusions AS i
INNER JOIN dbo.Services AS s ON s.Id=i.ServiceId
WHERE s.IsArchived=0
ORDER BY s.Price,s.Id,i.Id;
