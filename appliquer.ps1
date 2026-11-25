# Usage (PowerShell) : depuis le dossier dezippe  ->  .\appliquer.ps1 -Depot "D:\gtaa\GTAALL-tool"
param([Parameter(Mandatory=$true)][string]$Depot)
$ErrorActionPreference = "Stop"
if (-not (Test-Path "$Depot\GtaAllTool.sln")) { throw "GtaAllTool.sln introuvable dans $Depot" }
# 1. Fichiers deplaces vers les nouveaux projets : on retire les anciens exemplaires (sinon doublons a la compilation)
$obsolete = @(
 "src\GtaAll.Core\Formats\Rpf9Exporter.cs","src\GtaAll.Core\Formats\Rpf9Header.cs","src\GtaAll.Core\Formats\Rpf9Index.cs",
 "src\GtaAll.Core\Formats\Rpf9Indexer.cs","src\GtaAll.Core\Formats\ZlibInflater.cs","src\GtaAll.Core\Formats\EntryKind.cs",
 "src\GtaAll.Core\Search\EntryFilter.cs","src\GtaAll.Core\Formats\TtpfInspector.cs")
foreach ($f in $obsolete) { $p = Join-Path $Depot $f; if (Test-Path $p) { Remove-Item $p -Force; Write-Host "supprime  $f" } }
# 2. Copie des fichiers (sans toucher a sous-tool, bin, obj, .git)
$src = Join-Path $PSScriptRoot "GTAALL-tool"
Get-ChildItem $src -Recurse -File | Where-Object { $_.Name -ne "appliquer.ps1" } | ForEach-Object {
  $rel = $_.FullName.Substring($src.Length + 1); $dst = Join-Path $Depot $rel
  New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null; Copy-Item $_.FullName $dst -Force }
Write-Host "Termine. Lance : dotnet build GtaAllTool.sln  puis  dotnet run --project tests/GtaAll.Core.SmokeTests"
