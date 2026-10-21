using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Marquee.Core.Files;

/// The Win32 and native calls behind SafeFile.
static class FileNative
{
    public const uint FILE_LIST_DIRECTORY = 0x0001;
    public const uint FILE_READ_ATTRIBUTES = 0x0080;
    public const uint DELETE = 0x00010000;
    public const uint GENERIC_READ = 0x80000000;
    public const uint FILE_SHARE_READ = 0x0001;
    public const uint FILE_SHARE_WRITE = 0x0002;
    public const uint FILE_SHARE_DELETE = 0x0004;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    public const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    public const int FileBasicInfo = 0;
    public const int FileDispositionInfo = 4;
    public const int FileDispositionInfoEx = 21;
    public const uint FILE_DISPOSITION_FLAG_DELETE = 0x1;
    public const uint FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE = 0x10;

    // FILE_INFORMATION_CLASS values for NtSetInformationFile.
    public const int FileRenameInformation = 10;
    public const int FileRenameInformationEx = 65;
    public const uint FILE_RENAME_REPLACE_IF_EXISTS = 0x1;
    public const uint FILE_RENAME_POSIX_SEMANTICS = 0x2;

    public const int ERROR_FILE_NOT_FOUND = 2;
    public const int ERROR_PATH_NOT_FOUND = 3;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_FILE_EXISTS = 80;
    public const int ERROR_ALREADY_EXISTS = 183;
    public const int ERROR_SHARING_VIOLATION = 32;
    public const int ERROR_NOT_SUPPORTED = 50;
    public const int ERROR_INVALID_PARAMETER = 87;
    public const int ERROR_INVALID_FUNCTION = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_BASIC_INFO
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    /// Packed to 4 because each FILETIME is two DWORDs, not an 8-byte-aligned 64-bit value.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    /// FILE_RENAME_INFORMATION without its FileName, which follows FileNameLength.
    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_RENAME_INFORMATION
    {
        public uint Flags; // ReplaceIfExists, a BOOLEAN, for FileRenameInformation
        public IntPtr RootDirectory;
        public uint FileNameLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_DISPOSITION_INFO
    {
        public byte DeleteFile; // a BOOLEAN
    }

    /// FILE_DISPOSITION_INFO_EX, Windows 10 1809 and later on NTFS.
    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_DISPOSITION_INFO_EX
    {
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_STATUS_BLOCK
    {
        public IntPtr Status;
        public UIntPtr Information;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW", ExactSpelling = true)]
    public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int fileInformationClass, out FILE_BASIC_INFO info,
        int bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetFileInformationByHandle(SafeFileHandle file, out BY_HANDLE_FILE_INFORMATION info);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetFileInformationByHandle(SafeFileHandle file, int fileInformationClass, ref FILE_DISPOSITION_INFO info,
        int bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetFileInformationByHandle(SafeFileHandle file, int fileInformationClass, ref FILE_DISPOSITION_INFO_EX info,
        int bufferSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetFinalPathNameByHandleW", ExactSpelling = true)]
    public static extern int GetFinalPathNameByHandle(SafeFileHandle file, [Out] char[] path, int pathLength, uint flags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetLongPathNameW", ExactSpelling = true)]
    public static extern int GetLongPathName(string shortPath, [Out] char[] longPath, int longPathLength);

    /// The native call, not kernel32's SetFileInformationByHandle, which refuses a rename relative to a folder handle.
    [DllImport("ntdll.dll")]
    public static extern int NtSetInformationFile(SafeFileHandle file, out IO_STATUS_BLOCK ioStatus, IntPtr info, int length,
        int fileInformationClass);

    [DllImport("ntdll.dll")]
    public static extern int RtlNtStatusToDosError(int status);
}
