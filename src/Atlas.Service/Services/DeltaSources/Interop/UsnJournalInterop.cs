using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Atlas.Service.Services.DeltaSources.Interop;

/// <summary>
/// Raw Win32 P/Invoke declarations for NTFS USN change journal access.
/// Internal to the service project; consumed only by UsnJournalReader.
/// </summary>
internal static class UsnJournalInterop
{
    // ── IOCTL codes ─────────────────────────────────────────────────────
    internal const uint FSCTL_QUERY_USN_JOURNAL = 0x000900F4;
    internal const uint FSCTL_READ_USN_JOURNAL = 0x000900BB;

    // ── CreateFile constants ────────────────────────────────────────────
    internal const uint GENERIC_READ = 0x80000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    // ── GetFinalPathNameByHandle flags ──────────────────────────────────
    internal const uint FILE_NAME_NORMALIZED = 0x0;

    // ── USN_RECORD_V2 field offsets ─────────────────────────────────────
    //  0: RecordLength (uint, 4 bytes)
    //  4: MajorVersion (ushort, 2)
    //  6: MinorVersion (ushort, 2)
    //  8: FileReferenceNumber (ulong, 8)
    // 16: ParentFileReferenceNumber (ulong, 8)
    // 24: Usn (long, 8)
    // 32: TimeStamp (long, 8)
    // 40: Reason (uint, 4)
    // 44: SourceInfo (uint, 4)
    // 48: SecurityId (uint, 4)
    // 52: FileAttributes (uint, 4)
    // 56: FileNameLength (ushort, 2)
    // 58: FileNameOffset (ushort, 2)
    // 60+: FileName (variable, UTF-16)
    internal const int USN_RECORD_V2_FIXED_SIZE = 60;

    // ── Reason mask: capture all change types ───────────────────────────
    internal const uint USN_REASON_MASK_ALL = 0xFFFFFFFF;

    // ── P/Invoke: CreateFileW ───────────────────────────────────────────
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    // ── P/Invoke: DeviceIoControl (query journal — fixed-size output) ──
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        nint lpInBuffer,
        int nInBufferSize,
        out USN_JOURNAL_DATA_V1 lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        nint lpOverlapped);

    // ── P/Invoke: DeviceIoControl (read journal — byte[] output) ────────
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref READ_USN_JOURNAL_DATA_V0 lpInBuffer,
        int nInBufferSize,
        [Out] byte[] lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        nint lpOverlapped);

    // ── P/Invoke: OpenFileById ──────────────────────────────────────────
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeFileHandle OpenFileById(
        SafeFileHandle hVolumeHint,
        ref FILE_ID_DESCRIPTOR lpFileId,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwFlagsAndAttributes);

    // ── P/Invoke: GetFinalPathNameByHandleW ─────────────────────────────
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile,
        [Out] char[] lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    // ── Native structs ──────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct USN_JOURNAL_DATA_V1
    {
        public ulong UsnJournalID;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct READ_USN_JOURNAL_DATA_V0
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalID;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct FILE_ID_DESCRIPTOR
    {
        [FieldOffset(0)] public uint dwSize;
        [FieldOffset(4)] public uint Type; // 0 = FileIdType
        [FieldOffset(8)] public long FileId;
    }
}
