# Starts the app in Connected Mode (Entra sign-in, Azure OpenAI, Fabric semantic model). Open https://localhost:7245
# Prerequisites: docs/ENTRA_SETUP.md (user-secrets AzureAd:ClientId/TenantId/ClientSecret), az login --tenant <tenant>
param([ValidateSet('AzureOpenAI','Deterministic')][string]$Parser = 'AzureOpenAI')
$ErrorActionPreference = 'Stop'
$env:Demo__Mode = 'Connected'
$env:Demo__IntentParser = $Parser
dotnet run --project "$PSScriptRoot\src\Northstar.Web" --urls "https://localhost:7245;http://localhost:5130"
