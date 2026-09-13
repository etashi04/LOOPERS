param([Parameter(Mandatory=$true)][string]$Payload,[Parameter(Mandatory=$true)][string]$Icon,[Parameter(Mandatory=$true)][string]$Logo,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$source=Join-Path $PSScriptRoot 'distribution/LOOPERS_Installer.cs'
$manifest=Join-Path $PSScriptRoot 'distribution/manifest.tsv'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$Output" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll "/win32icon:$Icon" "/resource:$Icon,InstallerIcon" "/resource:$Logo,InstallerLogo" "/resource:$Payload,payload.zip" "/resource:$manifest,manifest.tsv" $source
if($LASTEXITCODE -ne 0){throw '설치기 빌드 실패'}
