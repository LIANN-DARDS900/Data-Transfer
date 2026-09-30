[CmdletBinding(SupportsShouldProcess)] param([Parameter(Mandatory)][string]$Root,[ValidateSet('10k-small','100k-small','1gb','10gb','50gb','mixed')][string]$Dataset)
$ErrorActionPreference='Stop'; $rootPath=[IO.Path]::GetFullPath($Root); $marker=Join-Path $rootPath '.robotransfer-qualification-owned'
if(Test-Path $rootPath){ if(!(Test-Path $marker)){throw 'Existing root is not marked as qualification-owned.'} } else { New-Item -ItemType Directory $rootPath|Out-Null; New-Item -ItemType File $marker|Out-Null }
$count=switch($Dataset){'10k-small'{10000};'100k-small'{100000};'mixed'{10000};default{0}}
if($count){1..$count|ForEach-Object{ $dir=Join-Path $rootPath ('files/{0:D3}' -f ($_ % 100)); [IO.Directory]::CreateDirectory($dir)|Out-Null; [IO.File]::WriteAllText((Join-Path $dir ("item-{0:D7}.dat" -f $_)),('x'*512))}}
$size=switch($Dataset){'1gb'{1GB};'10gb'{10GB};'50gb'{50GB};'mixed'{1GB};default{0}}
if($size){$stream=[IO.File]::Open((Join-Path $rootPath "$Dataset.bin"),'CreateNew','Write','None'); try{$stream.SetLength($size)}finally{$stream.Dispose()}}
[pscustomobject]@{Dataset=$Dataset;Root=$rootPath;Files=(Get-ChildItem $rootPath -File -Recurse).Count;Bytes=(Get-ChildItem $rootPath -File -Recurse|Measure-Object Length -Sum).Sum;CreatedAt=(Get-Date).ToUniversalTime().ToString('o')}|ConvertTo-Json
