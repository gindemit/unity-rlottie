# Both the editor and the Android player report NUnit results. Count the individual
# cases as well as checking the root counters, so truncated or empty runs fail.
function Read-PlayerTestResult {
    param(
        [Parameter(Mandatory)][xml] $Document,
        [string[]] $AssemblyNames
    )
    $run = $Document.DocumentElement
    if ($run.LocalName -notin 'test-run', 'test-suite') {
        throw 'Expected an NUnit test-run or test-suite document.'
    }
    $cases = $run.SelectNodes('.//test-case')
    if ($cases.Count -eq 0) { throw 'No individual test cases were reported.' }

    $counts = @{ Passed = 0; Failed = 0; Skipped = 0; Inconclusive = 0 }
    $failures = @()
    foreach ($case in $cases) {
        $state = $case.GetAttribute('result')
        if (-not $counts.ContainsKey($state)) { throw "Unexpected NUnit test-case result '$state'." }
        $counts[$state]++
        if ($state -in 'Failed', 'Inconclusive' -and $failures.Count -lt 5) {
            $failures += $case.GetAttribute('fullname')
        }
    }
    foreach ($attribute in @('total', 'passed', 'failed', 'skipped', 'inconclusive')) {
        $expected = if ($attribute -eq 'total') { $cases.Count } else { $counts[$attribute] }
        if ($run.GetAttribute($attribute) -cne [string]$expected) {
            throw "NUnit $attribute count differs from the individual cases (expected $expected)."
        }
    }
    foreach ($assemblyName in $AssemblyNames) {
        $matching = $run.SelectNodes("descendant-or-self::test-suite[@type='Assembly']") | Where-Object {
            ([IO.Path]::GetFileName($_.GetAttribute('name')) -replace '\.dll$') -eq ($assemblyName -replace '\.dll$')
        }
        if (-not $matching) {
            throw "NUnit result is missing the requested assembly '$assemblyName'."
        }
    }
    [pscustomobject][ordered]@{
        result = $run.GetAttribute('result')
        total = $cases.Count; passed = $counts.Passed; failed = $counts.Failed
        skipped = $counts.Skipped; inconclusive = $counts.Inconclusive
        failures = $failures
    }
}

function Test-PlayerHeartbeatTimeout {
    param([int] $ExitCode, [string] $LogFile)
    # ReturnCodes.RunError (3) is shared with compilation and launch errors.
    $ExitCode -eq 3 -and (Test-Path -LiteralPath $LogFile -PathType Leaf) -and
        (Select-String -LiteralPath $LogFile -Quiet -Pattern '^Test execution timed out\. No activity received from the player in \d+ seconds\.$')
}
