using System;

namespace AyodanceID
{
    /// <summary>
    /// An AOB-based ARM code patch. The patcher finds every occurrence of
    /// <see cref="Original"/> in the target process memory and replaces it with
    /// <see cref="Patched"/>. Restoring swaps the bytes back.
    /// </summary>
    public sealed class PatchFeature
    {
        public string Key { get; }
        public string Name { get; }
        public string Description { get; }
        public byte[] Original { get; }
        public byte[] Patched { get; }

        public PatchFeature(string key, string name, string description, string originalHex, string patchedHex)
        {
            Key = key;
            Name = name;
            Description = description;
            Original = ParseHex(originalHex);
            Patched = ParseHex(patchedHex);
            if (Original.Length == 0 || Original.Length != Patched.Length)
            {
                throw new ArgumentException($"Feature '{name}': original and patched patterns must be non-empty and equal length.");
            }
        }

        /// <summary>Parse a whitespace-separated hex string like "05 90 A0 E3".</summary>
        public static byte[] ParseHex(string hex)
        {
            string[] parts = hex.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                bytes[i] = Convert.ToByte(parts[i], 16);
            }
            return bytes;
        }
    }
}
