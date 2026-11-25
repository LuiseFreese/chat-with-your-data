# Starts the app in Local Demo Mode (no cloud dependencies). Open http://localhost:5130
$ErrorActionPreference = 'Stop'
$env:Demo__Mode = 'LocalDemo'
dotnet run --project "$PSScriptRoot\src\Northstar.Web" --urls "http://localhost:5130"
