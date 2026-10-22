[CmdletBinding(SupportsShouldProcess, ConfirmImpact='High')]
param(
    [Parameter(Mandatory)][string]$Subscription,
    [Parameter(Mandatory)][string]$ResourceGroup,
    [Parameter(Mandatory)][string]$Server,
    [Parameter(Mandatory)][string]$SourceDatabase,
    [Parameter(Mandatory)][ValidatePattern('^aries-restore-[a-z0-9-]+$')][string]$TargetDatabase,
    [Parameter(Mandatory)][DateTimeOffset]$RestoreTimeUtc,
    [switch]$Execute
)
$ErrorActionPreference='Stop'
if ($SourceDatabase.Equals($TargetDatabase,[StringComparison]::OrdinalIgnoreCase)) { throw 'Restore target must differ from source.' }
if ($RestoreTimeUtc -gt [DateTimeOffset]::UtcNow) { throw 'Restore point must be in the past.' }
$arguments=@('sql','db','restore','--subscription',$Subscription,'--resource-group',$ResourceGroup,'--server',$Server,
    '--name',$SourceDatabase,'--dest-name',$TargetDatabase,'--time',$RestoreTimeUtc.ToUniversalTime().ToString('o'),'--only-show-errors','--output','none')
[pscustomobject]@{Mode= $(if($Execute){'Awaiting confirmation'}else{'Plan only'});Source=$SourceDatabase;Target=$TargetDatabase;RestorePointUtc=$RestoreTimeUtc.ToUniversalTime().ToString('o');ProductionOverwritten=$false}
if (!$Execute) { return }
if ($PSCmdlet.ShouldProcess("$Server/$TargetDatabase",'Create a separately billed SQL database from the approved point-in-time backup')) {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    & az @arguments
    if($LASTEXITCODE -ne 0){throw 'Azure SQL restore failed; inspect the provider operation without exposing settings.'}
    $watch.Stop()
    [pscustomobject]@{Target=$TargetDatabase;RestoreOperationSeconds=$watch.Elapsed.TotalSeconds;IntegrityAndApplicationChecks='Still required';Cleanup='Requires separate approval; no automatic deletion'}
}
