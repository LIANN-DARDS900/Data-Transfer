#requires -Version 5.1
<# Read-only endpoint collection. Writes only its local report directory.
   No share creation, credentials, policy changes, firewall changes or telemetry.
   Peer probing is opt-in and restricted to exactly one explicit host, TCP 445.
#>
[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.\-]{0,252}$')][string]$Peer,
    [string]$OutputDirectory = (Join-Path $env:TEMP 'RoboTransfer-Diagnostics'),
    [ValidateRange(1,10)][int]$TimeoutSeconds = 3
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Run this collector on Windows.' }
$report = [ordered]@{
    SchemaVersion = 1
    CollectedUtc = [DateTime]::UtcNow.ToString('o')
    Machine = $env:COMPUTERNAME
    Notice = 'Contains internal network and security metadata. Review locally before sharing. No upload is performed.'
    Sections = [ordered]@{}
}
function Read-Section([string]$Name, [scriptblock]$Read) {
    try { $value = @(& $Read); $report.Sections[$Name] = @{ Status='COLLECTED'; Data=$value } }
    catch { $report.Sections[$Name] = @{ Status='UNAVAILABLE'; ErrorType=$_.Exception.GetType().Name; ErrorId=$_.FullyQualifiedErrorId } }
}
Read-Section 'System' { Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,OSArchitecture }
Read-Section 'Adapters' { Get-NetAdapter -IncludeHidden | Select-Object Name,InterfaceDescription,ifIndex,Status,LinkSpeed,MediaType,PhysicalMediaType }
Read-Section 'AdapterStatistics' { Get-NetAdapterStatistics | Select-Object Name,ReceivedBytes,SentBytes,ReceivedPacketErrors,OutboundPacketErrors,ReceivedDiscardedPackets,OutboundDiscardedPackets }
Read-Section 'Addresses' { Get-NetIPAddress | Select-Object InterfaceAlias,InterfaceIndex,AddressFamily,IPAddress,PrefixLength,AddressState,PrefixOrigin }
Read-Section 'Profiles' { Get-NetConnectionProfile | Select-Object Name,InterfaceAlias,InterfaceIndex,NetworkCategory,IPv4Connectivity,IPv6Connectivity }
Read-Section 'Routes' { Get-NetRoute | Select-Object InterfaceIndex,DestinationPrefix,NextHop,RouteMetric,State }
Read-Section 'Dns' { Get-DnsClientServerAddress | Select-Object InterfaceAlias,AddressFamily,ServerAddresses }
Read-Section 'Neighbours' { Get-NetNeighbor | Select-Object InterfaceIndex,IPAddress,State }
Read-Section 'FirewallProfiles' { Get-NetFirewallProfile -PolicyStore ActiveStore | Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction,AllowLocalFirewallRules,LogBlocked,LogAllowed,LogFileName }
Read-Section 'Firewall445Candidates' {
    # Includes broad Any-port rules, not just rules with SMB in an English display name.
    Get-NetFirewallRule -PolicyStore ActiveStore -Enabled True | ForEach-Object {
        $rule = $_
        foreach ($filter in @($rule | Get-NetFirewallPortFilter)) {
            if (([string]$filter.Protocol -in @('TCP','6','Any','256')) -and
                ((@($filter.LocalPort) -contains '445') -or (@($filter.RemotePort) -contains '445') -or
                 (@($filter.LocalPort) -contains 'Any') -or (@($filter.RemotePort) -contains 'Any'))) {
                [pscustomobject]@{ Name=$rule.Name; Direction=[string]$rule.Direction; Action=[string]$rule.Action; Profile=[string]$rule.Profile; SourceType=[string]$rule.PolicyStoreSourceType; Source=$rule.PolicyStoreSource; LocalPort=@($filter.LocalPort); RemotePort=@($filter.RemotePort) }
            }
        }
    }
}
Read-Section 'SmbClient' { Get-SmbClientConfiguration | Select-Object EnableSecuritySignature,RequireSecuritySignature,EnableInsecureGuestLogons,RequireEncryption,BlockNTLM }
Read-Section 'SmbServer' { Get-SmbServerConfiguration | Select-Object EnableSMB1Protocol,EnableSMB2Protocol,EnableSecuritySignature,RequireSecuritySignature,EncryptData,RejectUnencryptedAccess }
Read-Section 'SmbConnections' { Get-SmbConnection | Select-Object ServerName,ShareName,Dialect,Signed,Encrypted,NumOpens }
Read-Section 'SmbShareConfiguration' { Get-SmbShare | Select-Object Name,EncryptData,FolderEnumerationMode,ShareState,Special }
Read-Section 'Services' { Get-Service -Name LanmanServer,LanmanWorkstation,MpsSvc,BFE,WinDefend -ErrorAction Stop | Select-Object Name,Status,StartType }
Read-Section 'Listeners445' { Get-NetTCPConnection -State Listen -LocalPort 445 | Select-Object LocalAddress,LocalPort,OwningProcess }
Read-Section 'DefenderStatus' { Get-MpComputerStatus | Select-Object AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,BehaviorMonitorEnabled,IsTamperProtected,AntivirusSignatureLastUpdated }
Read-Section 'RegisteredAntivirus' { Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntivirusProduct | Select-Object displayName,productState }
Read-Section 'PolicyRegistry' {
    foreach ($path in @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation','HKLM:\SOFTWARE\Policies\Microsoft\Windows\LanmanServer','HKLM:\SYSTEM\CurrentControlSet\Control\Lsa')) {
        if (Test-Path -LiteralPath $path) {
            $v = Get-ItemProperty -LiteralPath $path
            [pscustomobject]@{ Path=$path; AllowInsecureGuestAuth=$v.AllowInsecureGuestAuth; LmCompatibilityLevel=$v.LmCompatibilityLevel; RestrictAnonymous=$v.RestrictAnonymous }
        }
    }
}
foreach ($log in @('Microsoft-Windows-SMBClient/Connectivity','Microsoft-Windows-SMBClient/Security','Microsoft-Windows-SMBServer/Operational','Microsoft-Windows-CodeIntegrity/Operational','Microsoft-Windows-AppLocker/EXE and DLL','Microsoft-Windows-Windows Defender/Operational')) {
    $logName = $log
    Read-Section $logName { Get-WinEvent -FilterHashtable @{LogName=$logName; StartTime=(Get-Date).AddHours(-4)} -MaxEvents 30 | Select-Object TimeCreated,Id,LevelDisplayName,ProviderName }
}
if ($Peer) {
    Read-Section 'PeerTcp445' {
        $client = New-Object Net.Sockets.TcpClient
        $clock = [Diagnostics.Stopwatch]::StartNew()
        try {
            $task = $client.ConnectAsync($Peer,445)
            try {
                if (-not $task.Wait($TimeoutSeconds*1000)) { $state = 'TIMEOUT' }
                elseif ($client.Connected) { $state = 'CONNECTED' }
                else { $state = 'FAILED' }
            } catch { $state = 'FAILED: ' + $_.Exception.GetBaseException().Message }
            [pscustomobject]@{ Host=$Peer; Port=445; Result=$state; ElapsedMs=$clock.ElapsedMilliseconds; Interpretation='Connectivity evidence only. Does not establish SMB authentication, share access or the cause of blocking.' }
        } finally { $client.Dispose() }
    }
}
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)) | Out-Null
$file = Join-Path $OutputDirectory ('RoboTransfer-{0}-{1}.json' -f $env:COMPUTERNAME,[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $file -Encoding UTF8
Write-Output $file
