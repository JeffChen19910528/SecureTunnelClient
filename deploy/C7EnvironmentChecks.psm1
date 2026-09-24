#
# C7EnvironmentChecks.psm1
#
# Pure(ish), reusable classification/redaction helpers shared by
# deploy/check-c7-environment.ps1 and deploy/collect-c7-evidence.ps1, and
# covered by real Pester tests (deploy/tests/C7EnvironmentChecks.Tests.ps1).
# Kept separate from the scripts that use them specifically so the
# classification/redaction LOGIC (as opposed to the real system probes
# that call it) can be unit-tested deterministically, without touching
# the real OS, per Phase C7-ENV Part H.
#
# No function here performs a destructive or state-changing operation -
# every exported function is read-only / pure.
#

Set-StrictMode -Version Latest

<#
.SYNOPSIS
    Classifies a single readiness check into one of the five required
    status values: READY, NOT READY, BLOCKED, NOT APPLICABLE, NOT CHECKED.
.DESCRIPTION
    - NOT APPLICABLE: the check does not apply in this context (e.g. a
      WireGuard-specific check when WireGuard isn't part of this
      environment's scope at all).
    - BLOCKED: the check could not be attempted because a prerequisite
      (typically elevation) is missing - distinct from FAILED, which
      would mean the check ran and the product behaved incorrectly.
    - NOT CHECKED: the check was never attempted for a reason other than
      a known blocker (e.g. skipped by the caller).
    - READY / NOT READY: the check was actually attempted and its result
      is known.
#>
function Get-C7CheckStatus {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Applicable,

        [Parameter(Mandatory = $true)]
        [ValidateSet('NotAttempted', 'Blocked', 'Succeeded', 'Failed')]
        [string]$Outcome,

        [string]$BlockedReason
    )

    if (-not $Applicable) {
        return "NOT APPLICABLE"
    }

    switch ($Outcome) {
        'NotAttempted' { return "NOT CHECKED" }
        'Blocked' {
            if ([string]::IsNullOrWhiteSpace($BlockedReason)) {
                throw "BlockedReason is required when Outcome is 'Blocked'."
            }
            return "BLOCKED"
        }
        'Succeeded' { return "READY" }
        'Failed' { return "NOT READY" }
    }
}

<#
.SYNOPSIS
    Redacts WireGuard-key-shaped tokens and common secret-labeled values
    from a string before it is printed, logged, or written to a report.
.DESCRIPTION
    Mirrors the redaction approach already used in
    SecureTunnel.Client.Core.Diagnostics.DiagnosticResultFactory (a
    42-44-character base64-shaped token, the size of a WireGuard key) and
    additionally strips anything that looks like "password=...",
    "token=...", "secret=...", or "-Credential ..." so accidental
    inclusion in a probe/evidence script's own output is caught even if
    the value itself doesn't look like a WireGuard key.
#>
function Protect-C7SensitiveText {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [AllowEmptyString()]
        [string]$Text
    )

    process {
        $result = $Text
        $result = [regex]::Replace($result, '[A-Za-z0-9+/]{42,44}=', '***REDACTED-KEY***')
        $result = [regex]::Replace($result, '(?i)\b(password|passwd|token|secret|apikey|api_key)\b\s*[:=]\s*\S+', '$1=***REDACTED***')
        $result = [regex]::Replace($result, '(?i)-Credential\s+\S+', '-Credential ***REDACTED***')
        return $result
    }
}

<#
.SYNOPSIS
    True only if the current process token is actually elevated (not
    merely a member of BUILTIN\Administrators, which UAC can filter out
    of the effective token) - see docs/c4-acceptance-report.md and
    docs/c7-acceptance-report.md for why this distinction matters.
#>
function Test-C7ProcessElevated {
    [CmdletBinding()]
    [OutputType([bool])]
    param()

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

<#
.SYNOPSIS
    True if the current user's token carries ANY group membership in
    BUILTIN\Administrators, regardless of whether that membership is
    currently enabled (elevated) or filtered (deny-only, unelevated).
.DESCRIPTION
    Deliberately distinct from Test-C7ProcessElevated - group membership
    alone does NOT imply the current process can perform admin-only
    operations; the two must be reported separately, matching the
    "avoid confusing membership in Administrators with an elevated
    token" requirement.
#>
function Test-C7AdministratorGroupMember {
    [CmdletBinding()]
    [OutputType([bool])]
    param()

    # REAL DEFECT FOUND AND FIXED (Phase C7-ENV): the first implementation
    # checked [WindowsIdentity]::GetCurrent().Groups for the well-known
    # Administrators SID. That collection does NOT reliably include a
    # "deny only" group entry - and this project's own documented
    # scenario since Phase C4 (an Administrators-group member running an
    # unelevated, UAC-filtered token) is exactly a deny-only case. The
    # original implementation therefore returned False for the precise
    # situation it exists to detect - verified via `whoami /groups`
    # showing "BUILTIN\Administrators ... Group used for deny only" while
    # Test-C7AdministratorGroupMember returned False. Fixed by querying
    # local group membership directly (Get-LocalGroupMember), which
    # reflects account membership independent of the current token's
    # filtering, matching what `whoami /groups` reports.
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value

    try {
        $members = @(Get-LocalGroupMember -Group "Administrators" -ErrorAction Stop)
    }
    catch {
        # Get-LocalGroupMember can fail on a domain controller or a
        # locked-down environment where the LocalAccounts module isn't
        # available - callers must treat this as "could not determine",
        # not "not a member"; surfaced as a terminating error so the
        # caller's own Blocked/NotChecked classification applies rather
        # than silently reporting False.
        throw "Unable to query local Administrators group membership: $($_.Exception.Message)"
    }

    $matchingMembers = @($members | Where-Object { $_.SID.Value -eq $currentSid })
    return $matchingMembers.Count -gt 0
}

Export-ModuleMember -Function Get-C7CheckStatus, Protect-C7SensitiveText, Test-C7ProcessElevated, Test-C7AdministratorGroupMember
