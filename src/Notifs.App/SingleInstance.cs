using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Notifs.App;

/// <summary>Claims this user's data profile before any database or scheduler is opened.</summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activation;
    private readonly MemoryMappedFile ownerProcess;
    private readonly MemoryMappedViewAccessor ownerId;
    private RegisteredWaitHandle? listener;
    public bool IsOwner { get; }

    public SingleInstance(string dataDirectory)
    {
        var profile = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory)).ToUpperInvariant();
        var identity = WindowsIdentity.GetCurrent().User!.Value + ":" + Process.GetCurrentProcess().SessionId + ":" + profile;
        var name = "Local\\Notifs-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        activation = new(false, EventResetMode.AutoReset, name + "-activate");
        mutex = new(false, name);
        try { IsOwner = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsOwner = true; }
        ownerProcess = MemoryMappedFile.CreateOrOpen(name + "-owner", sizeof(int));
        ownerId = ownerProcess.CreateViewAccessor();
        if (IsOwner) ownerId.Write(0, Environment.ProcessId);
    }

    public void ActivateOwner()
    {
        // A user-launched secondary can transfer its foreground permission before waking the owner.
        var processId = 0;
        SpinWait.SpinUntil(() => (processId = ownerId.ReadInt32(0)) != 0, TimeSpan.FromSeconds(5));
        if (processId != 0) AllowSetForegroundWindow(processId);
        activation.Set();
    }
    public void Listen(Action activate)
    {
        if (!IsOwner || listener is not null) throw new InvalidOperationException("Only the owning process can listen once.");
        listener = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => activate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }
    public void Dispose()
    {
        listener?.Unregister(null);
        ownerId.Dispose(); ownerProcess.Dispose();
        if (IsOwner) mutex.ReleaseMutex();
        mutex.Dispose(); activation.Dispose();
    }
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
}
