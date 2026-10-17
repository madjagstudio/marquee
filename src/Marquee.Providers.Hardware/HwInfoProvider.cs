using System.IO.MemoryMappedFiles;
using Marquee.Core.Sensors;

namespace Marquee.Providers.Hardware;

/// The alternative hardware source: reads HWiNFO's shared memory while HWiNFO is running with
/// Settings → Shared Memory Support turned on. Needs no driver of Marquee's own.
public sealed class HwInfoProvider : ISensorProvider
{
    const string MutexName = @"Global\HWiNFO_SM2_MUTEX";

    public string Name => "Hardware sensors (HWiNFO)";
    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public void Poll(SensorStore store)
    {
        var keys = HardwareKeyMap.Map(HwInfoSharedMemory.ToHardware(HwInfoSharedMemory.Parse(ReadSharedMemory())));
        foreach (var (key, value) in keys)
            store.Set(key, value, LibreHardwareMonitorProvider.Ttl);
    }

    public void Dispose() { }

    /// HWiNFO holds its mutex while it rewrites the block; take it briefly so a read never sees half an update.
    static byte[] ReadSharedMemory()
    {
        using var mutex = Mutex.TryOpenExisting(MutexName, out var existing) ? existing : null;
        var locked = false;
        try
        {
            try
            {
                locked = mutex?.WaitOne(100) == true;
            }
            catch (AbandonedMutexException)
            {
                locked = true;
            }

            using var map = MemoryMappedFile.OpenExisting(HwInfoSharedMemory.MapName, MemoryMappedFileRights.Read);
            using var view = map.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            var memory = new byte[view.Length];
            view.ReadExactly(memory);
            return memory;
        }
        catch (FileNotFoundException)
        {
            throw new InvalidOperationException(
                "HWiNFO's shared memory isn't available. Start HWiNFO (sensors-only mode is enough) and turn on Settings → Shared Memory Support.");
        }
        finally
        {
            if (locked) mutex!.ReleaseMutex();
        }
    }
}
