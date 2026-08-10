using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AyodanceID
{
    public readonly struct MemoryRegion
    {
        public nint BaseAddress { get; }
        public long RegionSize { get; }
        public uint Protect { get; }
        public uint Type { get; }

        public MemoryRegion(nint baseAddress, long regionSize, uint protect, uint type)
        {
            BaseAddress = baseAddress;
            RegionSize = regionSize;
            Protect = protect;
            Type = type;
        }
    }

    public sealed class MemoryReader : IDisposable
    {
        public const uint MEM_COMMIT = 0x1000;
        public const uint MEM_PRIVATE = 0x20000;
        public const uint MEM_IMAGE = 0x1000000;
        public const uint MEM_MAPPED = 0x40000;

        public const uint PAGE_NOACCESS = 0x01;
        public const uint PAGE_READWRITE = 0x04;
        public const uint PAGE_WRITECOPY = 0x08;
        public const uint PAGE_EXECUTE_READWRITE = 0x40;
        public const uint PAGE_GUARD = 0x100;

        public const uint PROCESS_VM_READ = 0x0010;
        public const uint PROCESS_VM_WRITE = 0x0020;
        public const uint PROCESS_VM_OPERATION = 0x0008;
        public const uint PROCESS_QUERY_INFORMATION = 0x0400;

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public nint BaseAddress;
            public nint AllocationBase;
            public uint AllocationProtect;
            public nint RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(nint hObject);

        [DllImport("kernel32.dll", EntryPoint = "ReadProcessMemory", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadProcessMemory(nint hProcess, nint lpBaseAddress, byte[] lpBuffer, nint nSize, out nint lpNumberOfBytesRead);

        [DllImport("kernel32.dll", EntryPoint = "WriteProcessMemory", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteProcessMemory(nint hProcess, nint lpBaseAddress, byte[] lpBuffer, nint nSize, out nint lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", EntryPoint = "VirtualQueryEx", SetLastError = true)]
        private static extern nint VirtualQueryEx(nint hProcess, nint lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualProtectEx(nint hProcess, nint lpAddress, nint dwSize, uint flNewProtect, out uint lpflOldProtect);

        public nint Handle { get; }
        public int ProcessId { get; }

        public MemoryReader(int processId)
        {
            ProcessId = processId;
            Handle = OpenProcess(PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION, false, processId);
            if (Handle == nint.Zero)
            {
                throw new InvalidOperationException(
                    $"OpenProcess failed for PID {processId} (Win32 error {Marshal.GetLastWin32Error()}). " +
                    "Run as Administrator and check the PID is correct.");
            }
        }

        public bool ReadBytes(nint address, byte[] buffer, out int bytesRead)
        {
            nint read = 0;
            bool ok = ReadProcessMemory(Handle, address, buffer, (nint)buffer.Length, out read);
            bytesRead = (int)read;
            return ok;
        }

        public bool ReadBytes(nint address, byte[] buffer, nint size, out int bytesRead)
        {
            nint read = 0;
            bool ok = ReadProcessMemory(Handle, address, buffer, size, out read);
            bytesRead = (int)read;
            return ok;
        }

        public bool WriteBytes(nint address, byte[] data)
        {
            nint written = 0;
            return WriteProcessMemory(Handle, address, data, (nint)data.Length, out written);
        }

        /// <summary>
        /// Write bytes even into read-only/execute-only code pages by temporarily
        /// forcing PAGE_EXECUTE_READWRITE, then restoring the original protection.
        /// </summary>
        public bool ProtectedWrite(nint address, byte[] data)
        {
            if (data.Length == 0)
            {
                return false;
            }

            if (!VirtualProtectEx(Handle, address, (nint)data.Length, PAGE_EXECUTE_READWRITE, out uint old))
            {
                return WriteBytes(address, data);
            }

            bool ok = WriteBytes(address, data);
            VirtualProtectEx(Handle, address, (nint)data.Length, old, out _);
            return ok;
        }

        /// <summary>Committed writable regions (used by the User-struct scanner).</summary>
        public IEnumerable<MemoryRegion> EnumerateRegions()
        {
            nint address = nint.Zero;
            uint mbiSize = (uint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();

            while (true)
            {
                if (VirtualQueryEx(Handle, address, out MEMORY_BASIC_INFORMATION mbi, mbiSize) == nint.Zero)
                {
                    break;
                }

                long size = (long)mbi.RegionSize;
                if (size <= 0)
                {
                    break;
                }

                bool committed = mbi.State == MEM_COMMIT;
                bool rwProtect = (mbi.Protect & 0x6E) != 0;
                bool noAccess = (mbi.Protect & PAGE_NOACCESS) != 0;
                bool guard = (mbi.Protect & PAGE_GUARD) != 0;

                if (committed && rwProtect && !noAccess && !guard)
                {
                    yield return new MemoryRegion(mbi.BaseAddress, size, mbi.Protect, mbi.Type);
                }

                long next = (long)mbi.BaseAddress + size;
                if (next <= (long)address)
                {
                    break;
                }
                address = (nint)next;
            }
        }

        /// <summary>
        /// All committed, readable regions (writable, read-only and executable).
        /// Used for AOB code-pattern scans.
        /// </summary>
        public IEnumerable<MemoryRegion> EnumerateReadableRegions()
        {
            nint address = nint.Zero;
            uint mbiSize = (uint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();

            while (true)
            {
                if (VirtualQueryEx(Handle, address, out MEMORY_BASIC_INFORMATION mbi, mbiSize) == nint.Zero)
                {
                    break;
                }

                long size = (long)mbi.RegionSize;
                if (size <= 0)
                {
                    break;
                }

                bool committed = mbi.State == MEM_COMMIT;
                bool noAccess = (mbi.Protect & PAGE_NOACCESS) != 0;
                bool guard = (mbi.Protect & PAGE_GUARD) != 0;

                if (committed && !noAccess && !guard)
                {
                    yield return new MemoryRegion(mbi.BaseAddress, size, mbi.Protect, mbi.Type);
                }

                long next = (long)mbi.BaseAddress + size;
                if (next <= (long)address)
                {
                    break;
                }
                address = (nint)next;
            }
        }

        public void Dispose()
        {
            if (Handle != nint.Zero)
            {
                CloseHandle(Handle);
            }
        }
    }
}
