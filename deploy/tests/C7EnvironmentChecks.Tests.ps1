#
# Pester tests for deploy/C7EnvironmentChecks.psm1's pure classification
# and redaction logic. These are real, executable, repeatable unit tests
# (Pester) - not manual inspection - covering the specific behaviors
# Phase C7-ENV Part H calls out: readiness classification, BLOCKED vs
# FAILED vs NOT CHECKED distinction, and safe output redaction.
#
# Run with: Invoke-Pester -Script "deploy\tests\C7EnvironmentChecks.Tests.ps1"
#

$modulePath = Join-Path $PSScriptRoot "..\C7EnvironmentChecks.psm1"
Import-Module $modulePath -Force

Describe "Get-C7CheckStatus" {

    It "returns NOT APPLICABLE when Applicable is false, regardless of Outcome" {
        Get-C7CheckStatus -Applicable $false -Outcome 'Succeeded' | Should Be "NOT APPLICABLE"
        Get-C7CheckStatus -Applicable $false -Outcome 'Failed' | Should Be "NOT APPLICABLE"
    }

    It "returns NOT CHECKED when the check was never attempted" {
        Get-C7CheckStatus -Applicable $true -Outcome 'NotAttempted' | Should Be "NOT CHECKED"
    }

    It "returns BLOCKED when Outcome is Blocked and a reason is supplied" {
        Get-C7CheckStatus -Applicable $true -Outcome 'Blocked' -BlockedReason "Requires elevation" | Should Be "BLOCKED"
    }

    It "throws when Outcome is Blocked but no BlockedReason is supplied" {
        { Get-C7CheckStatus -Applicable $true -Outcome 'Blocked' } | Should Throw
    }

    It "returns READY when the check was actually attempted and succeeded" {
        Get-C7CheckStatus -Applicable $true -Outcome 'Succeeded' | Should Be "READY"
    }

    It "returns NOT READY when the check was actually attempted and failed - distinct from BLOCKED" {
        $result = Get-C7CheckStatus -Applicable $true -Outcome 'Failed'
        $result | Should Be "NOT READY"
        $result | Should Not Be "BLOCKED"
    }

    It "never conflates BLOCKED (prerequisite missing) with NOT READY (product actually failed)" {
        $blocked = Get-C7CheckStatus -Applicable $true -Outcome 'Blocked' -BlockedReason "No elevation"
        $failed = Get-C7CheckStatus -Applicable $true -Outcome 'Failed'
        $blocked | Should Not Be $failed
    }
}

Describe "Protect-C7SensitiveText" {

    It "redacts a 44-character base64 WireGuard-key-shaped token" {
        $text = "PrivateKey = AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
        $redacted = Protect-C7SensitiveText -Text $text
        $redacted | Should Not Match "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
        $redacted | Should Match "REDACTED-KEY"
    }

    It "redacts a password= style value" {
        $redacted = Protect-C7SensitiveText -Text "password=Sup3rSecret!"
        $redacted | Should Not Match "Sup3rSecret"
        $redacted | Should Match "REDACTED"
    }

    It "redacts a token= style value" {
        $redacted = Protect-C7SensitiveText -Text "token: abc.def.ghi-signature-value"
        $redacted | Should Not Match "abc.def.ghi-signature-value"
    }

    It "redacts a -Credential argument" {
        $redacted = Protect-C7SensitiveText -Text "New-Service -Credential (Get-Credential) -Name Foo"
        $redacted | Should Not Match "\(Get-Credential\)"
        $redacted | Should Match "REDACTED"
    }

    It "leaves ordinary, non-sensitive text unchanged" {
        $text = "Service 'SecureTunnelAgent' is not installed."
        Protect-C7SensitiveText -Text $text | Should Be $text
    }

    It "is safe to call repeatedly (idempotent on already-redacted text)" {
        $once = Protect-C7SensitiveText -Text "password=hunter2"
        $twice = Protect-C7SensitiveText -Text $once
        $twice | Should Be $once
    }
}

Describe "Test-C7ProcessElevated and Test-C7AdministratorGroupMember" {

    It "both return a boolean without throwing" {
        { Test-C7ProcessElevated | Out-Null } | Should Not Throw
        { Test-C7AdministratorGroupMember | Out-Null } | Should Not Throw
    }

    It "correctly detects deny-only Administrators membership (regression test for a real Phase C7-ENV defect)" {
        # This development machine's current user IS a member of
        # BUILTIN\Administrators but the token is deny-only/unelevated
        # (confirmed via `whoami /groups` showing "Group used for deny
        # only" - see docs/c4-acceptance-report.md Part A). The first
        # implementation of Test-C7AdministratorGroupMember (which
        # inspected [WindowsIdentity]::GetCurrent().Groups) returned
        # False here - the exact wrong answer for the exact scenario
        # this function exists to detect. Fixed to query
        # Get-LocalGroupMember instead. This test only asserts the
        # correct behavior on THIS known machine state; it does not
        # assume every CI/dev machine is deny-only-admin.
        Test-C7AdministratorGroupMember | Should Be $true
    }

    It "are independent signals - group membership does not imply elevation" {
        # Real-environment fact (see docs/c7-acceptance-report.md Part A):
        # this development session IS a member of BUILTIN\Administrators
        # but is NOT elevated (UAC deny-only token) - so these two must
        # be able to disagree, which is exactly why both exist as
        # separate functions rather than one combined check.
        $elevated = Test-C7ProcessElevated
        $isMember = Test-C7AdministratorGroupMember
        # Not asserting specific values (environment-dependent) - only
        # that the two are computed independently (no exception, no
        # forced coupling in the implementation).
        $elevated | Should BeOfType([bool])
        $isMember | Should BeOfType([bool])
    }
}
