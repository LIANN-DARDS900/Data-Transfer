[CmdletBinding()]
param([ValidateSet('FrameworkDependent','SelfContained')][string]$Profile='FrameworkDependent',[string]$VersionSuffix='rc.1',[string]$SourceRevisionId='local')
$ErrorActionPreference='Stop'; $root=Split-Path $PSScriptRoot -Parent; Set-Location $root
& dotnet restore RoboTransfer.sln -p:VersionSuffix=$VersionSuffix -p:SourceRevisionId=$SourceRevisionId
& dotnet build RoboTransfer.sln -c Release --no-restore -p:VersionSuffix=$VersionSuffix -p:SourceRevisionId=$SourceRevisionId
& dotnet test RoboTransfer.sln -c Release --no-build
$selfContained=$Profile -eq 'SelfContained'; $name=if($selfContained){'self-contained'}else{'framework-dependent'}
& dotnet publish src/RoboTransfer.App/RoboTransfer.App.csproj -c Release -r win-x64 --no-restore --self-contained:$($selfContained.ToString().ToLowerInvariant()) -o "artifacts/publish/$name" -p:VersionSuffix=$VersionSuffix -p:SourceRevisionId=$SourceRevisionId
if(!$selfContained){ & dotnet build installer/RoboTransfer.Installer/RoboTransfer.Installer.wixproj -c Release -p:PublishDir="$root/artifacts/publish/framework-dependent" -p:VersionPrefix=0.4.0 }
