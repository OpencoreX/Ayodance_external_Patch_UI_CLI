using System;
using System.Collections.Generic;
using System.Linq;

namespace AyodanceID
{
    /// <summary>
    /// Zero-config pre-scan: locates the game's NetData.User struct in memory
    /// by validating a fixed 192-byte window against field heuristics
    /// (level 1-200, stats 0-100000, aligned pointers, etc).
    /// </summary>
    public sealed class UserStructScanner
    {
        public const int WindowSize = 192;
        public const int Stride = 4;
        public const int GradeOffset = 64;      // User.grade
        public const int GradeMax = 2610;        // 0x0A32 -> 0A 32 00 00

        // pointer validity: 4-byte aligned, inside 0x01000000 .. 0xFFFFFF00
        private static bool IsPointer(uint value) =>
            (value & 3u) == 0u && value >= 16777216u && value <= 4294963200u;

        // pointer validity, additionally requires non-zero
        private static bool IsPointerNonZero(uint value) =>
            value != 0u && IsPointer(value);

        private readonly MemoryReader _mem;

        public UserStructScanner(MemoryReader mem)
        {
            _mem = mem ?? throw new ArgumentNullException(nameof(mem));
        }

        /// <summary>Matches the original heuristic A(byte[], int).</summary>
        public static bool Heuristic(byte[] data, int offset)
        {
            if (offset < 0 || offset + WindowSize > data.Length)
            {
                return false;
            }

            uint p0 = BitConverter.ToUInt32(data, offset + 0);
            uint p2 = BitConverter.ToUInt32(data, offset + 16);
            uint p3 = BitConverter.ToUInt32(data, offset + 36);
            long p4 = BitConverter.ToInt64(data, offset + 40);
            uint p5 = BitConverter.ToUInt32(data, offset + 48);
            uint p6 = BitConverter.ToUInt32(data, offset + 52);
            int  lvl = BitConverter.ToInt32(data, offset + 56);
            uint p8 = BitConverter.ToUInt32(data, offset + 60);
            int  stat1 = BitConverter.ToInt32(data, offset + 64);
            int  stat2 = BitConverter.ToInt32(data, offset + 68);
            int  stat3 = BitConverter.ToInt32(data, offset + 72);
            int  stat4 = BitConverter.ToInt32(data, offset + 76);
            int  stat5 = BitConverter.ToInt32(data, offset + 80);
            int  stat6 = BitConverter.ToInt32(data, offset + 84);
            int  stat7 = BitConverter.ToInt32(data, offset + 88);
            int  pct1 = BitConverter.ToInt32(data, offset + 92);
            int  pct2 = BitConverter.ToInt32(data, offset + 96);
            int  expNow = BitConverter.ToInt32(data, offset + 180);
            int  expTotal = BitConverter.ToInt32(data, offset + 184);
            uint last = BitConverter.ToUInt32(data, offset + 188);

            if (!IsPointer(p0)) return false;
            if (!IsPointer(p5)) return false;
            if (!IsPointer(p6)) return false;
            if (!IsPointer(last)) return false;
            if (p4 < 4294967296L || p4 >= 72057594037927936L) return false;
            if (!IsPointerNonZero(p2)) return false;
            if (!IsPointerNonZero(p3)) return false;
            if (!IsPointerNonZero(p8)) return false;
            if (p0 == p5 || p0 == p6 || p5 == p6) return false;
            if (lvl < 1 || lvl > 200) return false;
            if (stat1 < 0 || stat1 > 100000) return false;
            if (pct1 < 0 || pct1 > 100) return false;
            if (pct2 < 0 || pct2 > 100) return false;
            if (stat2 < 0 || stat2 > 100000) return false;
            if (stat3 < 0 || stat3 > 100000) return false;
            if (stat4 < 0 || stat4 > 100000) return false;
            if (stat5 < 0 || stat5 > 100000) return false;
            if (stat6 < 0 || stat6 > 100000) return false;
            if (stat7 < 0 || stat7 > 100000) return false;
            if (expNow < 0 || expTotal <= 0) return false;
            if (expNow > expTotal || expTotal > 100000000) return false;

            return true;
        }

        /// <summary>
        /// Pre-scan all MEM_PRIVATE + writable regions and return every
        /// candidate address that matches the User-struct heuristic.
        /// </summary>
        public List<nint> Scan()
        {
            var results = new List<nint>();
            byte[] buffer = new byte[64 * 1024 * 1024];
            long totalRead = 0;

            foreach (MemoryRegion region in _mem.EnumerateRegions())
            {
                if (region.Type != MemoryReader.MEM_PRIVATE) continue;
                if ((region.Protect & 0x4C) == 0) continue;

                long offset = 0;
                while (offset < region.RegionSize)
                {
                    int chunk = (int)Math.Min(buffer.Length, region.RegionSize - offset);
                    nint chunkBase = (nint)((long)region.BaseAddress + offset);

                    int bytesRead = 0;
                    if (_mem.ReadBytes(chunkBase, buffer, chunk, out bytesRead))
                    {
                        totalRead += bytesRead;

                        // align first probe to 4-byte boundary relative to window start
                        int first = (int)((long)Stride - ((long)chunkBase & (Stride - 1))) & (Stride - 1);
                        for (int i = first; i + WindowSize <= bytesRead; i += Stride)
                        {
                            if (Heuristic(buffer, i))
                            {
                                results.Add((nint)((long)chunkBase + i));
                            }
                        }
                    }

                    if (bytesRead < chunk)
                    {
                        break;
                    }

                    long advance = (bytesRead > WindowSize - 1)
                        ? (bytesRead - (WindowSize - 1))
                        : chunk;
                    advance = advance / Stride * Stride;
                    if (advance <= 0)
                    {
                        advance = Stride;
                    }
                    offset += advance;
                }
            }

            Console.WriteLine($"Scanned {totalRead / (1024 * 1024)} MB, {results.Count} candidate(s).");
            return results;
        }

        /// <summary>
        /// Validate a candidate still looks like a User struct and write
        /// MAX grade (2610) at struct_addr + GradeOffset.
        /// </summary>
        public bool WriteMaxGrade(nint structAddr)
        {
            byte[] window = new byte[WindowSize];
            if (!_mem.ReadBytes(structAddr, window, out _) || !Heuristic(window, 0))
            {
                return false;
            }

            nint gradeAddr = (nint)((long)structAddr + GradeOffset);
            byte[] value = new byte[4]
            {
                (byte)(GradeMax & 0xFF),
                (byte)((GradeMax >> 8) & 0xFF),
                0x00,
                0x00
            };
            return _mem.WriteBytes(gradeAddr, value);
        }
    }
}
