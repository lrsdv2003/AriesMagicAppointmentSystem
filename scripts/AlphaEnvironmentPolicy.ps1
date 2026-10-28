function Test-AlphaEnvironmentPolicy($Environment,$BranchPolicies) {
    if(!$Environment -or $Environment.name -ine 'alpha'){throw 'Environment alpha is missing or unavailable.'}
    $reviewRules=@($Environment.protection_rules | Where-Object {$_.type -eq 'required_reviewers' -and @($_.reviewers | Where-Object {$null -ne $_}).Count -gt 0})
    if(!$reviewRules.Count){throw 'Actual required-reviewer protection is required; a variable assertion is insufficient. Keep Actions inventory-only on unsupported private plans.'}
    if($Environment.can_admins_bypass -ne $false){throw 'Disable administrator bypass for the alpha Environment before transfer.'}
    if(!$Environment.deployment_branch_policy.custom_branch_policies){throw 'Select the main branch explicitly in alpha deployment branch policies.'}
    $policies=@($BranchPolicies.branch_policies | Where-Object {$null -ne $_})
    if($policies.Count -ne 1 -or $policies[0].name -cne 'main' -or $policies[0].type -cne 'branch'){throw 'The alpha Environment must permit only the main branch, with no wildcard/tag policy.'}
}
