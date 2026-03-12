using System.Runtime.InteropServices;
using System.Text;
using Atlas.Service.Services.DeltaSources.Interop;
using Microsoft.Win32.SafeHandles;

namespace Atlas.Service.Services.DeltaSources;

/// <summary>
/// Metadata about a volume's USN change journal.
/// </summary>
public sealed class UsnJournalInfo
{
    public ulong JournalId { get; init; }
    public long FirstUsn { get; init; }
    public long NextUsn { get; init; }
}

/// <summary>
/// Result of reading USN journal changes for a volume, filtered to a root path.
/// </summary>
public sealed class UsnJournalReadResult
{
    public bool Success { get; init; }
    public string ErrorReason { get; init; } = string.Empty;
    public long NextUsn { get; init; }
    public IReadOnlyList<string> ChangedPaths { get; init; } = [];
    public int UnresolvedCount { get; init; }
    public bool Overflowed { get; init; }
}

/// <summary>
/// Abstraction over Win32 USN journal access. Allows mocking in tests.
/// </summary>
public interface IUsnJournalReader
{
    UsnJournalInfo? QueryJournal(string volumeRoot);

    UsnJournalReadResult ReadChanges(
        string volumeRoot,
        long startUsn,
        ulong journalId,
        int maxChangedPaths,
        string rootPathFilter);
}

/// <summary>
/// Reads the NTFS USN change journal via Win32 P/Invoke.
/// </summary>
public sealed class UsnJournalReader : IUsnJournalReader
{
    private const int ReadBufferSize = 64 * 1024;
    private const int MaxTotalRecords = 200_000;

    public UsnJournalInfo? QueryJournal(string volumeRoot)
    {
        var devicePath = GetVolumeDevicePath(volumeRoot);
        using var handle = UsnJournalInterop.CreateFileW(
            devicePath,
            UsnJournalInterop.GENERIC_READ,
            UsnJournalInterop.FILE_SHARE_READ | UsnJournalInterop.FILE_SHARE_WRITE,
            nint.Zero,
            UsnJournalInterop.OPEN_EXISTING,
            0,
            nint.Zero);

        if (handle.IsInvalid)
            return null;

        var success = UsnJournalInterop.DeviceIoControl(
            handle,
            UsnJournalInterop.FSCTL_QUERY_USN_JOURNAL,
            nint.Zero,
            0,
            out UsnJournalInterop.USN_JOURNAL_DATA_V1 journalData,
            Marshal.SizeOf<UsnJournalInterop.USN_JOURNAL_DATA_V1>(),
            out _,
            nint.Zero);

        if (!success)
            return null;

        return new UsnJournalInfo
        {
            JournalId = journalData.UsnJournalID,
            FirstUsn = journalData.FirstUsn,
            NextUsn = journalData.NextUsn
        };
    }

    public UsnJournalReadResult ReadChanges(
        string volumeRoot,
        long startUsn,
        ulong journalId,
        int maxChangedPaths,
        string rootPathFilter)
    {
        var devicePath = GetVolumeDevicePath(volumeRoot);
        using var handle = UsnJournalInterop.CreateFileW(
            devicePath,
            UsnJournalInterop.GENERIC_READ,
            UsnJournalInterop.FILE_SHARE_READ | UsnJournalInterop.FILE_SHARE_WRITE,
            nint.Zero,
            UsnJournalInterop.OPEN_EXISTING,
            0,
            nint.Zero);

        if (handle.IsInvalid)
        {
            return new UsnJournalReadResult
            {
                Success = false,
                ErrorReason = $"Cannot open volume {devicePath}. Error: {Marshal.GetLastWin32Error()}"
            };
        }

        var readData = new UsnJournalInterop.READ_USN_JOURNAL_DATA_V0
        {
            StartUsn = startUsn,
            ReasonMask = UsnJournalInterop.USN_REASON_MASK_ALL,
            ReturnOnlyOnClose = 0,
            Timeout = 0,
            BytesToWaitFor = 0,
            UsnJournalID = journalId
        };

        var changedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buffer = new byte[ReadBufferSize];
        var totalRecords = 0;
        var unresolvedCount = 0;
        var overflowed = false;
        var parentPathCache = new Dictionary<ulong, string?>();

        // Normalize the root filter for prefix matching.
        var rootFilter = rootPathFilter.TrimEnd('\\') + "\\";

        while (true)
        {
            var ok = UsnJournalInterop.DeviceIoControl(
                handle,
                UsnJournalInterop.FSCTL_READ_USN_JOURNAL,
                ref readData,
                Marshal.SizeOf(readData),
                buffer,
                buffer.Length,
                out int bytesReturned,
                nint.Zero);

            if (!ok)
            {
                var error = Marshal.GetLastWin32Error();
                return new UsnJournalReadResult
                {
                    Success = false,
                    ErrorReason = $"FSCTL_READ_USN_JOURNAL failed. Win32 error: {error}"
                };
            }

            // First 8 bytes of the output buffer contain the next USN to read from.
            if (bytesReturned <= 8)
            {
                // No more records.
                readData.StartUsn = BitConverter.ToInt64(buffer, 0);
                break;
            }

            var nextUsn = BitConverter.ToInt64(buffer, 0);
            var offset = 8;

            while (offset < bytesReturned)
            {
                if (offset + UsnJournalInterop.USN_RECORD_V2_FIXED_SIZE > bytesReturned)
                    break;

                var recordLength = BitConverter.ToUInt32(buffer, offset);
                if (recordLength == 0 || offset + (int)recordLength > bytesReturned)
                    break;

                var parentRef = BitConverter.ToUInt64(buffer, offset + 16);
                var nameLength = BitConverter.ToUInt16(buffer, offset + 56);
                var nameOffset = BitConverter.ToUInt16(buffer, offset + 58);

                if (offset + nameOffset + nameLength <= bytesReturned && nameLength > 0)
                {
                    var fileName = Encoding.Unicode.GetString(
                        buffer, offset + nameOffset, nameLength);

                    var fullPath = ResolveFilePath(handle, parentRef, fileName, parentPathCache);
                    if (fullPath is not null)
                    {
                        // Check if the path is under the monitored root.
                        if (fullPath.StartsWith(rootFilter, StringComparison.OrdinalIgnoreCase)
                            || fullPath.Equals(rootPathFilter.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        {
                            changedPaths.Add(fullPath);
                        }
                    }
                    else
                    {
                        unresolvedCount++;
                    }
                }

                totalRecords++;

                if (changedPaths.Count > maxChangedPaths || totalRecords > MaxTotalRecords)
                {
                    overflowed = true;
                    break;
                }

                offset += (int)recordLength;
            }

            if (overflowed)
                break;

            readData.StartUsn = nextUsn;
        }

        return new UsnJournalReadResult
        {
            Success = true,
            NextUsn = readData.StartUsn,
            ChangedPaths = changedPaths.ToList(),
            UnresolvedCount = unresolvedCount,
            Overflowed = overflowed
        };
    }

    private static string? ResolveFilePath(
        SafeFileHandle volumeHandle,
        ulong parentFileRefNum,
        string fileName,
        Dictionary<ulong, string?> cache)
    {
        if (cache.TryGetValue(parentFileRefNum, out var cachedParent))
        {
            return cachedParent is not null ? Path.Combine(cachedParent, fileName) : null;
        }

        var parentPath = ResolveDirectoryPath(volumeHandle, parentFileRefNum);
        cache[parentFileRefNum] = parentPath;
        return parentPath is not null ? Path.Combine(parentPath, fileName) : null;
    }

    private static string? ResolveDirectoryPath(SafeFileHandle volumeHandle, ulong fileRefNum)
    {
        var descriptor = new UsnJournalInterop.FILE_ID_DESCRIPTOR
        {
            dwSize = (uint)Marshal.SizeOf<UsnJournalInterop.FILE_ID_DESCRIPTOR>(),
            Type = 0, // FileIdType
            FileId = (long)fileRefNum
        };

        using var dirHandle = UsnJournalInterop.OpenFileById(
            volumeHandle,
            ref descriptor,
            UsnJournalInterop.GENERIC_READ,
            UsnJournalInterop.FILE_SHARE_READ | UsnJournalInterop.FILE_SHARE_WRITE,
            nint.Zero,
            UsnJournalInterop.FILE_FLAG_BACKUP_SEMANTICS);

        if (dirHandle.IsInvalid)
            return null;

        var pathBuffer = new char[1024];
        var result = UsnJournalInterop.GetFinalPathNameByHandleW(
            dirHandle,
            pathBuffer,
            (uint)pathBuffer.Length,
            UsnJournalInterop.FILE_NAME_NORMALIZED);

        if (result == 0 || result >= pathBuffer.Length)
            return null;

        var path = new string(pathBuffer, 0, (int)result);

        // Strip the \\?\ prefix that GetFinalPathNameByHandle returns.
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            path = path[4..];

        return path;
    }

    private static string GetVolumeDevicePath(string volumeRoot)
    {
        var driveLetter = Path.GetPathRoot(volumeRoot)?.TrimEnd('\\');
        return $@"\\.\{driveLetter}";
    }
}
