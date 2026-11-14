using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KillerMCP.Notify;

internal static class WindowsHelloConsent
{
    internal static async Task<bool> VerifyAsync(IntPtr owner, string message, CancellationToken cancellationToken = default)
    {
        IntPtr className = IntPtr.Zero, prompt = IntPtr.Zero;
        object? factory = null, operation = null;
        IAsyncInfo? info = null;
        try
        {
            const string runtimeClass = "Windows.Security.Credentials.UI.UserConsentVerifier";
            Marshal.ThrowExceptionForHR(WindowsCreateString(runtimeClass, runtimeClass.Length, out className));
            var factoryId = typeof(IUserConsentVerifierInterop).GUID;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, ref factoryId, out factory));
            Marshal.ThrowExceptionForHR(WindowsCreateString(message, message.Length, out prompt));
            var operationId = typeof(IVerificationOperation).GUID;
            ((IUserConsentVerifierInterop)factory).RequestVerificationForWindowAsync(owner, prompt, ref operationId, out operation);
            info = (IAsyncInfo)operation;
            var clock = Stopwatch.StartNew();
            while (info.Status == 0 && clock.Elapsed < TimeSpan.FromMinutes(2) && !cancellationToken.IsCancellationRequested) await Task.Delay(100);
            if (info.Status == 0)
            {
                info.Cancel();
                var cancellation = Stopwatch.StartNew();
                while (info.Status == 0 && cancellation.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(50);
                return false;
            }
            if (cancellationToken.IsCancellationRequested) return false;
            return info.Status == 1 && ((IVerificationOperation)operation).GetResults() == 0;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (info is not null) try { if (info.Status != 0) info.Close(); } catch (COMException) { }
            if (operation is not null && Marshal.IsComObject(operation)) Marshal.ReleaseComObject(operation);
            if (factory is not null && Marshal.IsComObject(factory)) Marshal.ReleaseComObject(factory);
            if (prompt != IntPtr.Zero) WindowsDeleteString(prompt);
            if (className != IntPtr.Zero) WindowsDeleteString(className);
        }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string value, int length, out IntPtr result);
    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr name, ref Guid id, [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    [ComImport, Guid("39E050C3-4E74-441A-8DC0-B81104DF949C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUserConsentVerifierInterop
    {
        void GetIids(out uint count, out IntPtr ids);
        void GetRuntimeClassName(out IntPtr name);
        void GetTrustLevel(out int level);
        void RequestVerificationForWindowAsync(IntPtr owner, IntPtr message, ref Guid id, [MarshalAs(UnmanagedType.IUnknown)] out object operation);
    }

    [ComImport, Guid("fd596ffd-2318-558f-9dbe-d21df43764a5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVerificationOperation
    {
        void GetIids(out uint count, out IntPtr ids);
        void GetRuntimeClassName(out IntPtr name);
        void GetTrustLevel(out int level);
        void SetCompleted(IntPtr handler);
        void GetCompleted(out IntPtr handler);
        int GetResults();
    }

    [ComImport, Guid("00000036-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAsyncInfo
    {
        void GetIids(out uint count, out IntPtr ids);
        void GetRuntimeClassName(out IntPtr name);
        void GetTrustLevel(out int level);
        uint Id { get; }
        int Status { get; }
        int ErrorCode { get; }
        void Cancel();
        void Close();
    }
}
