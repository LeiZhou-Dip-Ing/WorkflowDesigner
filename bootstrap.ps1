$ErrorActionPreference = 'Stop'

Write-Host "Checking .NET SDK..."
dotnet --version | Out-Host

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    Write-Warning "GitHub CLI was not found. Install it or configure NuGet credentials manually."
}
else {
    Write-Host "Checking GitHub CLI authentication..."
    gh auth status | Out-Host
}

$sourceName = 'github-workflow'
$sourceUrl = 'https://nuget.pkg.github.com/LeiZhou-Dip-Ing/index.json'
$sources = dotnet nuget list source
if ($sources -notmatch [regex]::Escape($sourceName)) {
    Write-Warning "NuGet source '$sourceName' is not configured at user level."
    Write-Host "Use a GitHub credential with read:packages and store it in the user NuGet config, never in this repository."
    Write-Host "Example:"
    Write-Host "dotnet nuget add source $sourceUrl --name $sourceName --username <github-user> --password <read-packages-token> --store-password-in-clear-text"
}

Write-Host "Restoring WorkflowDesigner..."
dotnet restore WorkflowDesigner.sln

Write-Host "Building WorkflowDesigner..."
dotnet build WorkflowDesigner.sln -m:1
