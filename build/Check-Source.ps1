$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $files = @(git ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate tracked source.' }
    $patterns = @(
        '(?i)[a-z]:[\\/]Users[\\/](?!Public[\\/]|Default[\\/])[^\\/\s"<>]{2,40}',
        '(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})',
        '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
        '(?:AKIA|ASIA)[A-Z0-9]{16}',
        'https?://[^/\s:@]+:[^/\s@]+@'
    )
    foreach ($file in $files) {
        if ($file -match '(?i)(^|/)(work|artifacts|captures|bin|obj)/|(^|/)(settings\.json|\.env)(\.|$)|\.(pfx|pdb|etl|csv|log|key)$') {
            throw "Generated, local or sensitive file is tracked: $file"
        }
        $content = [IO.File]::ReadAllText((Join-Path $root $file))
        foreach ($pattern in $patterns) {
            if ($content -match $pattern) { throw "Potential personal path or credential in $file. Inspect locally; do not print the value." }
        }
    }
    Write-Host "Source guard passed for $($files.Count) tracked files. This pattern check complements manual review and GitHub secret scanning."
} finally { Pop-Location }
