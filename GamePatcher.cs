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
        private const int BufferSize = 64 * 1024 * 1024;
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

        private static void ScanPattern(byte[] buffer, int length, nint baseAddress, byte[] pattern, List<nint> hits)
        {
            if (pattern.Length == 0 || pattern.Length > length)
            {
                return;
            }

            ReadOnlySpan<byte> data = buffer.AsSpan(0, length);
            int searchOffset = 0;
            while (searchOffset <= length - pattern.Length)
            {
                int relativeOffset = data[searchOffset..].IndexOf(pattern[0]);
                if (relativeOffset < 0)
                {
                    break;
                }

                int matchOffset = searchOffset + relativeOffset;
                if (Matches(buffer, matchOffset, pattern))
                {
                    hits.Add((nint)((long)baseAddress + matchOffset));
                }
                searchOffset = matchOffset + 1;
            }
        }

        /// <summary>
        /// enable=true  -> find original bytes, write the patched bytes.
        /// enable=false -> find patched bytes, restore the original bytes.
        /// </summary>
        public PatchReport Apply(PatchFeature feature, bool enable)
        {
            return ApplyMany(new[] { feature }, enable)[0];
        }

        /// <summary>
        /// Applies multiple features after one pass over the target process memory.
        /// This avoids reading every region once per feature when enabling the full set.
        /// </summary>
        public IReadOnlyList<PatchReport> ApplyMany(
            IReadOnlyList<PatchFeature> features,
            bool enable,
            Action<int, int>? progress = null)
        {
            if (features is null || features.Count == 0)
            {
                return Array.Empty<PatchReport>();
            }

            var hits = new Dictionary<PatchFeature, (List<nint> Original, List<nint> Patched)>();
            foreach (PatchFeature feature in features)
            {
                hits[feature] = (new List<nint>(), new List<nint>());
            }

            byte[] buffer = new byte[BufferSize];
            int maxPatternLength = features.Max(feature => Math.Max(feature.Original.Length, feature.Patched.Length));
            List<MemoryRegion> regions = _mem.EnumerateReadableRegions().ToList();

            for (int regionIndex = 0; regionIndex < regions.Count; regionIndex++)
            {
                MemoryRegion region = regions[regionIndex];
                progress?.Invoke(regionIndex, regions.Count);

                long offset = 0;
                while (offset < region.RegionSize)
                {
                    int chunk = (int)Math.Min(buffer.Length, region.RegionSize - offset);
                    nint chunkBase = (nint)((long)region.BaseAddress + offset);

                    if (!_mem.ReadBytes(chunkBase, buffer, chunk, out int bytesRead) || bytesRead <= 0)
                    {
                        break;
                    }

                    foreach (PatchFeature feature in features)
                    {
                        var featureHits = hits[feature];
                        ScanPattern(buffer, bytesRead, chunkBase, feature.Original, featureHits.Original);
                        ScanPattern(buffer, bytesRead, chunkBase, feature.Patched, featureHits.Patched);
                    }

                    if (bytesRead < chunk)
                    {
                        break;
                    }

                    long advance = bytesRead - (maxPatternLength - 1);
                    offset += Math.Max(advance, 1);
                }
            }
            progress?.Invoke(regions.Count, regions.Count);

            var reports = new List<PatchReport>(features.Count);
            foreach (PatchFeature feature in features)
            {
                var report = new PatchReport { Feature = feature };
                (List<nint> originalHits, List<nint> patchedHits) = hits[feature];

                if (enable)
                {
                    report.AlreadyDone = patchedHits.Count;
                    WriteMatches(report, originalHits, feature.Patched, apply: true);
                }
                else
                {
                    report.AlreadyDone = originalHits.Count;
                    WriteMatches(report, patchedHits, feature.Original, apply: false);
                }

                reports.Add(report);
            }

            return reports;
        }

        private void WriteMatches(PatchReport report, IEnumerable<nint> addresses, byte[] data, bool apply)
        {
            foreach (nint address in addresses)
            {
                if (_mem.ProtectedWrite(address, data))
                {
                    if (apply)
                    {
                        report.Applied++;
                    }
                    else
                    {
                        report.Restored++;
                    }
                    report.Addresses.Add(address);
                }
                else
                {
                    report.Failed++;
                }
            }
        }
    }
}
