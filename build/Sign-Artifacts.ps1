[CmdletBinding(SupportsShouldProcess)] param([Parameter(Mandatory)][string]$CertificateThumbprint,[Parameter(Mandatory)][string]$TimestampUrl,[Parameter(Mandatory)][string[]]$Paths)
$ErrorActionPreference='Stop'
if($TimestampUrl -notmatch '^https://'){ throw 'RFC 3161 timestamp URL must use HTTPS.' }
foreach($path in $Paths){ if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Artifact not found: $path"}; if($PSCmdlet.ShouldProcess($path,'Authenticode sign')){ & signtool sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $path; if($LASTEXITCODE){throw "Signing failed: $path"}; & signtool verify /pa /all /v $path; if($LASTEXITCODE){throw "Signature validation failed: $path"} } }
