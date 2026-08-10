using System;
using System.Collections.Generic;

namespace AyodanceID
{
    public sealed class PatchReport
    {
        public PatchFeature Feature { get; init; } = null!;
        public int Applied { get; set; }
        public int AlreadyDone { get; set; }
        public int Restored { get; set; }
        public int Failed { get; set; }
        public List<nint> Addresses { get; } = new();
    }

    /// <summary>
    /// Scans the target process for the feature patterns and applies or restores
    /// the patch at every match (AOB patches hit every address, not just one).
    /// </summary>
    public sealed class GamePatcher
    {
        private const int BufferSize = 4 * 1024 * 1024;
        private readonly MemoryReader _mem;

        public GamePatcher(MemoryReader mem)
        {
            _mem = mem ?? throw new ArgumentNullException(nameof(mem));
        }

        /// <summary>
        /// Single pass over all committed readable regions, returning every
        /// location that currently holds the original bytes or the patched bytes.
        /// </summary>
        public (List<nint> OriginalHits, List<nint> PatchedHits) FindPatterns(byte[] original, byte[] patched)
        {
            var originalHits = new List<nint>();
            var patchedHits = new List<nint>();
            byte[] buffer = new byte[BufferSize];

            foreach (MemoryRegion region in _mem.EnumerateReadableRegions())
            {
                long offset = 0;
                while (offset < region.RegionSize)
                {
                    int chunk = (int)Math.Min(buffer.Length, region.RegionSize - offset);
                    nint chunkBase = (nint)((long)region.BaseAddress + offset);

                    int bytesRead = 0;
                    if (!_mem.ReadBytes(chunkBase, buffer, chunk, out bytesRead) || bytesRead <= 0)
                    {
                        break;
                    }

                    ScanChunk(buffer, bytesRead, chunkBase, original, patched, originalHits, patchedHits);

                    if (bytesRead < chunk)
                    {
                        break;
                    }

                    long advance = bytesRead - (original.Length - 1);
                    if (advance <= 0)
                    {
                        advance = 1;
                    }
                    offset += advance;
                }
            }

            return (originalHits, patchedHits);
        }

        private static void ScanChunk(byte[] buffer, int length, nint baseAddr, byte[] a, byte[] b, List<nint> aHits, List<nint> bHits)
        {
            for (int i = 0; i + a.Length <= length; i++)
            {
                if (Matches(buffer, i, a))
                {
                    aHits.Add((nint)((long)baseAddr + i));
                }
                else if (Matches(buffer, i, b))
                {
                    bHits.Add((nint)((long)baseAddr + i));
                }
            }
        }

        private static bool Matches(byte[] buffer, int offset, byte[] pattern)
        {
            for (int i = 0; i < pattern.Length; i++)
            {
                if (buffer[offset + i] != pattern[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// enable=true  -> find original bytes, write the patched bytes.
        /// enable=false -> find patched bytes, restore the original bytes.
        /// </summary>
        public PatchReport Apply(PatchFeature feature, bool enable)
        {
            var report = new PatchReport { Feature = feature };
            (List<nint> originalHits, List<nint> patchedHits) = FindPatterns(feature.Original, feature.Patched);

            if (enable)
            {
                report.AlreadyDone = patchedHits.Count;
                foreach (nint addr in originalHits)
                {
                    if (_mem.ProtectedWrite(addr, feature.Patched))
                    {
                        report.Applied++;
                        report.Addresses.Add(addr);
                    }
                    else
                    {
                        report.Failed++;
                    }
                }
            }
            else
            {
                report.AlreadyDone = originalHits.Count;
                foreach (nint addr in patchedHits)
                {
                    if (_mem.ProtectedWrite(addr, feature.Original))
                    {
                        report.Restored++;
                        report.Addresses.Add(addr);
                    }
                    else
                    {
                        report.Failed++;
                    }
                }
            }

            return report;
        }
    }
}
