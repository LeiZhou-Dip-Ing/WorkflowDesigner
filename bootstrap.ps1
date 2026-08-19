param(
    [switch]$ConfigurePackages,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$sourceName = 'github-workflow'
$sourceUrl = 'https://nuget.pkg.github.com/LeiZhou-Dip-Ing/index.json'

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Write-Host "[1/4] Checking .NET SDK..."
$sdkVersion = dotnet --version
if ($LASTEXITCODE -ne 0 -or [version]($sdkVersion.Split('-')[0]) -lt [version]'8.0') {
    throw '.NET SDK 8.0 or newer is required.'
}
Write-Host "      Found .NET SDK $sdkVersion"

Write-Host "[2/4] Checking private GitHub Packages access..."
$sources = dotnet nuget list source --format detailed | Out-String
$hasSource = $sources -match [regex]::Escape($sourceName) -and $sources -match [regex]::Escape($sourceUrl)

if (-not $hasSource -and $ConfigurePackages) {
    $githubUser = Read-Host 'GitHub username'
    $secureToken = Read-Host 'GitHub PAT (classic) with read:packages; input is hidden' -AsSecureString
    $credential = [System.Net.NetworkCredential]::new('', $secureToken).Password
    try {
        Invoke-DotNet nuget add source $sourceUrl `
            --name $sourceName `
            --username $githubUser `
            --password $credential `
            --store-password-in-clear-text
    }
    finally {
        $credential = $null
        $secureToken.Dispose()
    }
    $hasSource = $true
}

if (-not $hasSource) {
    Write-Host ''
    Write-Host 'Private packages are not configured yet.' -ForegroundColor Yellow
    Write-Host '1. Ask the repository owner to grant your GitHub account Read access to every Workflow package.'
    Write-Host '2. Create a Personal Access Token (classic) with read:packages.'
    Write-Host '3. Run this script again with:'
    Write-Host '   .\bootstrap.ps1 -ConfigurePackages' -ForegroundColor Cyan
    Write-Host ''
    Write-Host 'The token is stored only in your Windows user-level NuGet configuration and is never written to this repository.'
    exit 2
}

Write-Host "[3/4] Restoring WorkflowDesigner and WorkflowCore 2.0 packages..."
try {
    Invoke-DotNet restore WorkflowDesigner.sln
}
catch {
    Write-Host ''
    Write-Host 'Package restore failed. Check these items:' -ForegroundColor Red
    Write-Host '- The token is a PAT (classic), has read:packages, and has not expired.'
    Write-Host '- Your GitHub account accepted the WorkflowDesigner invitation.'
    Write-Host '- The owner granted that same account package-level Read access.'
    Write-Host '- If your organization uses SSO, the token is authorized for that organization.'
    Write-Host '- Remove an old credential and run: .\bootstrap.ps1 -ConfigurePackages'
    Write-Host ''
    throw
}

if (-not $SkipBuild) {
    Write-Host "[4/4] Building the solution..."
    Invoke-DotNet build WorkflowDesigner.sln --no-restore -m:1
}
else {
    Write-Host "[4/4] Build skipped."
}

Write-Host ''
Write-Host 'Setup complete.' -ForegroundColor Green
Write-Host 'Terminal 1: dotnet run --project src\WorkflowRuntime.WindowsService'
Write-Host 'Terminal 2: dotnet run --project src\WorkflowDesigner'
Write-Host 'Runtime check: http://localhost:5197/swagger'
