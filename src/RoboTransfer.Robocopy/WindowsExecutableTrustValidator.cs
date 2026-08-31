using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RoboTransfer.Core;

namespace RoboTransfer.Robocopy;

public sealed class WindowsExecutableTrustValidator : IExecutableTrustValidator
{
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    public Task<ExecutableTrustResult> ValidateAsync(string path, bool requireMicrosoftPublisher, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return Task.FromResult(new ExecutableTrustResult(ExecutableTrustStatus.Unavailable, null, null, null, "Authenticode trust validation is available only on Windows."));
        try
        {
            var canonical = Path.GetFullPath(path);
            var expected = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "robocopy.exe"));
            if (!string.Equals(canonical, expected, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetFileName(canonical), "robocopy.exe", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new ExecutableTrustResult(ExecutableTrustStatus.InvalidLocation, canonical, null, null, "Robocopy must be the canonical Windows System32 executable."));
            if (!File.Exists(canonical)) return Task.FromResult(new ExecutableTrustResult(ExecutableTrustStatus.Unavailable, canonical, null, null, "The expected Windows Robocopy executable does not exist."));
            var version = FileVersionInfo.GetVersionInfo(canonical).FileVersion;
            var trust = VerifyEmbeddedSignature(canonical);
            if (trust != 0) return Task.FromResult(new ExecutableTrustResult(ExecutableTrustStatus.NotTrusted, canonical, version, null, $"Windows Authenticode validation failed (0x{trust:X8})."));
            using var certificate = X509Certificate.CreateFromSignedFile(canonical);
            var publisher = certificate.Subject;
            if (requireMicrosoftPublisher && !publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new ExecutableTrustResult(ExecutableTrustStatus.InvalidIdentity, canonical, version, publisher, "The valid signer is not identified as Microsoft; strict policy fails closed."));
            return Task.FromResult(new ExecutableTrustResult(ExecutableTrustStatus.Trusted, canonical, version, publisher, "Windows validated the embedded Authenticode signature and the executable identity is authorized."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        { return Task.FromResult(new ExecutableTrustResult(ExecutableTrustStatus.Unavailable, null, null, null, "Executable trust validation was technically unavailable.")); }
    }
    private static uint VerifyEmbeddedSignature(string fileName)
    {
        var fileInfo = new WinTrustFileInfo(fileName); var data = new WinTrustData(fileInfo);
        try { return WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, ref data); }
        finally { data.StateAction = 2; _ = WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, ref data); data.Dispose(); fileInfo.Dispose(); }
    }
    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)] private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, ref WinTrustData data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WinTrustFileInfo : IDisposable
    {
        public uint StructSize; public IntPtr FilePath; public IntPtr FileHandle; public IntPtr KnownSubject;
        public WinTrustFileInfo(string path) { StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(); FilePath = Marshal.StringToCoTaskMemUni(path); FileHandle = IntPtr.Zero; KnownSubject = IntPtr.Zero; }
        public void Dispose() { if (FilePath != IntPtr.Zero) Marshal.FreeCoTaskMem(FilePath); FilePath = IntPtr.Zero; }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WinTrustData : IDisposable
    {
        public uint StructSize; public IntPtr PolicyCallbackData; public IntPtr SipClientData; public uint UiChoice; public uint RevocationChecks; public uint UnionChoice; public IntPtr FileInfo; public uint StateAction; public IntPtr StateData; public string? UrlReference; public uint ProviderFlags; public uint UiContext;
        public WinTrustData(WinTrustFileInfo file) { StructSize = (uint)Marshal.SizeOf<WinTrustData>(); PolicyCallbackData = IntPtr.Zero; SipClientData = IntPtr.Zero; UiChoice = 2; RevocationChecks = 0; UnionChoice = 1; FileInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>()); Marshal.StructureToPtr(file, FileInfo, false); StateAction = 1; StateData = IntPtr.Zero; UrlReference = null; ProviderFlags = 0x00000080; UiContext = 0; }
        public void Dispose() { if (FileInfo != IntPtr.Zero) Marshal.FreeCoTaskMem(FileInfo); FileInfo = IntPtr.Zero; }
    }
}
