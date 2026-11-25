# Fails if the working tree contains environment-specific identifiers that must never be committed:
# tenant UPNs, real Fabric SQL endpoints, or GUIDs outside the documented allowlist.
# Run locally: pwsh scripts/check-sanitized.ps1   (also executed in CI)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent

# Generic leak classes (the repository must stay tenant-independent). Matching is case-sensitive:
# committed placeholders are upper-case (YOUR-WAREHOUSE-…) or bracketed (<your-…>), real identifiers are lower-case.
$patterns = @(
    @{ Name = 'Entra tenant UPN/domain';     Regex = '\.onmicrosoft\.com' },
    @{ Name = 'real Fabric SQL endpoint';    Regex = '[a-z0-9]\.datawarehouse\.fabric\.microsoft\.com' },
    @{ Name = 'real Azure OpenAI resource';  Regex = 'https://(?!<your|example)[a-z0-9-]+\.(cognitiveservices|openai)\.azure\.com' },
    @{ Name = 'GUID (possible resource id)'; Regex = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}' }
)

# Documented exceptions:
#  - docs/ENTRA_SETUP.md lists Microsoft's well-known permission ids (identical in every tenant)
#  - Northstar.Web.csproj contains a random UserSecretsId (a local secrets-store key, not a resource id)
$guidAllowlist = @(
    'docs/ENTRA_SETUP.md',
    'src/Northstar.Web/Northstar.Web.csproj'
)

$extensionsToSkip = '\.(png|jpg|jpeg|gif|ico|woff2?|pptx|xlsx)$'
$pathsToSkip = '[\\/](bin|obj|\.git|\.vs|TestResults|node_modules)[\\/]|check-sanitized\.ps1$'

# Scan tracked files only (local working copies may hold gitignored files such as fabric/governed-model-id.txt).
Push-Location $root
try { $tracked = git ls-files 2>$null } finally { Pop-Location }

$failures = @()
$files = if ($tracked) { $tracked | ForEach-Object { Join-Path $root $_ } | Where-Object { Test-Path $_ } | Get-Item }
         else { Get-ChildItem -Path $root -Recurse -File }

# Presentations and office documents never belong in this repository (personal metadata, speaker notes).
$files | Where-Object { $_.Name -match '\.(pptx?|pdf|docx?|key)$' } | ForEach-Object {
    $failures += "{0}: presentation/office document must not be tracked" -f ($_.FullName.Substring($root.Length + 1) -replace '\\', '/')
}

$files | Where-Object {
    $_.FullName -notmatch $pathsToSkip -and $_.Name -notmatch $extensionsToSkip
} | ForEach-Object {
    $relative = $_.FullName.Substring($root.Length + 1) -replace '\\', '/'
    $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
    if (-not $content) { return }
    foreach ($p in $patterns) {
        if ($p.Name -eq 'GUID (possible resource id)' -and $guidAllowlist -contains $relative) { continue }
        if ([System.Text.RegularExpressions.Regex]::IsMatch($content, $p.Regex)) {
            $failures += "{0}: {1}" -f $relative, $p.Name
        }
    }
}

# The committed TMDL must keep its placeholders (deploy-model substitutes real values in-memory only).
$expressions = Join-Path $root 'fabric/semantic-model/CWD-GovernedModel/definition/expressions.tmdl'
if ((Test-Path $expressions) -and (Get-Content $expressions -Raw) -notmatch 'YOUR-WAREHOUSE-SQL-ENDPOINT') {
    $failures += 'fabric/semantic-model/CWD-GovernedModel/definition/expressions.tmdl: placeholder was replaced - never commit a real SQL endpoint'
}

if ($failures.Count -gt 0) {
    Write-Host "Sanitization check FAILED - environment-specific content found:" -ForegroundColor Red
    $failures | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "Sanitization check passed: repository is tenant-independent." -ForegroundColor Green
exit 0
