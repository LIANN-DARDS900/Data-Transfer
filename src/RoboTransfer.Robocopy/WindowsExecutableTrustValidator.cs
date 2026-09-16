using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RoboTransfer.Core;

namespace RoboTransfer.Robocopy;

public sealed class WindowsExecutableTrustValidator : IExecutableTrustValidator
{
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public async Task<ExecutableTrustResult> ValidateAsync(string path, bool requireMicrosoftPublisher, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return new ExecutableTrustResult(ExecutableTrustStatus.Unavailable, null, null, null, "Authenticode trust validation is available only on Windows.");

        try
        {
            var canonical = Path.GetFullPath(path);
            var expected = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "robocopy.exe"));
            if (!string.Equals(canonical, expected, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(canonical), "robocopy.exe", StringComparison.OrdinalIgnoreCase))
            {
                return new ExecutableTrustResult(
                    ExecutableTrustStatus.InvalidLocation,
                    canonical,
                    null,
                    null,
                    "Robocopy must be the canonical Windows System32 executable.");
            }

            if (!File.Exists(canonical))
                return new ExecutableTrustResult(ExecutableTrustStatus.Unavailable, canonical, null, null, "The expected Windows Robocopy executable does not exist.");

            var version = FileVersionInfo.GetVersionInfo(canonical).FileVersion;
            var trust = VerifyEmbeddedSignature(canonical);

            if (trust == 0)
            {
                using var certificate = LoadAuthenticodeSigner(canonical);
                return Authorize(canonical, version, certificate.Subject, requireMicrosoftPublisher,
                    "Windows validated the embedded Authenticode signature and the executable identity is authorized.");
            }

            // Some Windows system binaries are catalog-signed instead of carrying an embedded
            // Authenticode signer. Get-AuthenticodeSignature is Windows' supported user-facing
            // validation path for both embedded and catalog signatures. Use it only as a read-only
            // fallback for the canonical System32 Robocopy binary; any non-Valid result still fails closed.
            var fallback = await VerifyWindowsAuthenticodeAsync(canonical, cancellationToken);
            if (!fallback.Available)
            {
                return new ExecutableTrustResult(
                    ExecutableTrustStatus.Unavailable,
                    canonical,
                    version,
                    null,
                    $"Windows Authenticode validation was technically unavailable after WinVerifyTrust failed (0x{trust:X8}). {fallback.Detail}");
            }

            if (!fallback.Valid)
            {
                return new ExecutableTrustResult(
                    ExecutableTrustStatus.NotTrusted,
                    canonical,
                    version,
                    fallback.Publisher,
                    $"Windows Authenticode validation failed (WinVerifyTrust 0x{trust:X8}; {fallback.Detail}).");
            }

            return Authorize(canonical, version, fallback.Publisher, requireMicrosoftPublisher,
                "Windows validated the Authenticode signature (including catalog signing when applicable) and the executable identity is authorized.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or Win32Exception)
        {
            return new ExecutableTrustResult(ExecutableTrustStatus.Unavailable, null, null, null, "Executable trust validation was technically unavailable.");
        }
    }

    private static ExecutableTrustResult Authorize(string canonical, string? version, string? publisher, bool requireMicrosoftPublisher, string successDetail)
    {
        if (requireMicrosoftPublisher && (string.IsNullOrWhiteSpace(publisher) || !publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)))
        {
            return new ExecutableTrustResult(
                ExecutableTrustStatus.InvalidIdentity,
                canonical,
                version,
                publisher,
                "The valid signer is not identified as Microsoft; strict policy fails closed.");
        }

        return new ExecutableTrustResult(
            ExecutableTrustStatus.Trusted,
            canonical,
            version,
            publisher,
            successDetail);
    }

    private static async Task<WindowsAuthenticodeResult> VerifyWindowsAuthenticodeAsync(string path, CancellationToken cancellationToken)
    {
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        if (!File.Exists(powershell))
            return new WindowsAuthenticodeResult(false, false, null, "Windows PowerShell is unavailable.");

        const string command = "$s = Get-AuthenticodeSignature -LiteralPath $env:ROBOTRANSFER_TRUST_PATH; " +
                               "[Console]::Out.WriteLine([string]$s.Status); " +
                               "if ($null -ne $s.SignerCertificate) { [Console]::Out.WriteLine([string]$s.SignerCertificate.Subject) } else { [Console]::Out.WriteLine('') }";

        var startInfo = new ProcessStartInfo
        {
            FileName = powershell,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        startInfo.Environment["ROBOTRANSFER_TRUST_PATH"] = path;

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            return new WindowsAuthenticodeResult(false, false, null, "Windows PowerShell could not be started.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            return new WindowsAuthenticodeResult(false, false, null, string.IsNullOrWhiteSpace(error) ? $"Windows PowerShell exited with code {process.ExitCode}." : error.Trim());

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            return new WindowsAuthenticodeResult(false, false, null, "Windows returned no Authenticode status.");

        var status = lines[0];
        var publisher = lines.Length > 1 ? lines[1] : null;
        return new WindowsAuthenticodeResult(true, string.Equals(status, "Valid", StringComparison.OrdinalIgnoreCase), publisher, $"Get-AuthenticodeSignature status: {status}");
    }

    private static X509Certificate2 LoadAuthenticodeSigner(string path)
    {
        if (X509Certificate2.GetCertContentType(path) != X509ContentType.Authenticode)
            throw new CryptographicException("The executable does not contain an Authenticode signature.");

        // .NET 9/10 intentionally obsoletes the legacy certificate-loading APIs, but
        // X509CertificateLoader currently has no equivalent for extracting an Authenticode
        // signer from a PE file. Keep this suppression limited to the supported workaround.
#pragma warning disable SYSLIB0057
        return new X509Certificate2(path);
#pragma warning restore SYSLIB0057
    }

    private static uint VerifyEmbeddedSignature(string fileName)
    {
        var fileInfo = new WinTrustFileInfo(fileName);
        var data = new WinTrustData(fileInfo);
        try
        {
            return WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, ref data);
        }
        finally
        {
            data.StateAction = 2;
            _ = WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, ref data);
            data.Dispose();
            fileInfo.Dispose();
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo : IDisposable
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;

        public WinTrustFileInfo(string path)
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            FilePath = Marshal.StringToCoTaskMemUni(path);
            FileHandle = IntPtr.Zero;
            KnownSubject = IntPtr.Zero;
        }

        public void Dispose()
        {
            if (FilePath != IntPtr.Zero)
                Marshal.FreeCoTaskMem(FilePath);
            FilePath = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData : IDisposable
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public string? UrlReference;
        public uint ProviderFlags;
        public uint UiContext;

        public WinTrustData(WinTrustFileInfo file)
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustData>();
            PolicyCallbackData = IntPtr.Zero;
            SipClientData = IntPtr.Zero;
            UiChoice = 2;
            RevocationChecks = 0;
            UnionChoice = 1;
            FileInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(file, FileInfo, false);
            StateAction = 1;
            StateData = IntPtr.Zero;
            UrlReference = null;
            ProviderFlags = 0x00000080;
            UiContext = 0;
        }

        public void Dispose()
        {
            if (FileInfo != IntPtr.Zero)
                Marshal.FreeCoTaskMem(FileInfo);
            FileInfo = IntPtr.Zero;
        }
    }

    private sealed record WindowsAuthenticodeResult(bool Available, bool Valid, string? Publisher, string Detail);
}
