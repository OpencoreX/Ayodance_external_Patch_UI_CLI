using System;
using System.Collections.Generic;
using System.Linq;

namespace AyodanceID
{
    internal static class Program
    {
        private static readonly List<PatchFeature> Features = new()
        {
            new PatchFeature(
                "lockperfect",
                "Lock Perfect",
                "Force perfect timing (MOV R9,#5 -> #1)",
                "05 90 A0 E3 00 00 58 E3 67 00 00 1A 28 50 9A E5",
                "01 90 A0 E3 00 00 58 E3 67 00 00 1A 28 50 9A E5"),

            new PatchFeature(
                "unlockep",
                "Unlock EP",
                "Force EP unlock function to return 0",
                "F0 4F 2D E9 04 D0 4D E2 28 63 9F E5 00 40 A0 E1",
                "00 00 A0 E3 1E FF 2F E1 00 F0 20 E3 00 F0 20 E3"),

            new PatchFeature(
                "ismission",
                "IsMissionComplete",
                "Force mission-complete check to return true",
                "70 40 2D E9 B8 51 9F E5 01 40 A0 E1 05 50 8F E0",
                "01 00 A0 E3 1E FF 2F E1 00 F0 20 E3 00 F0 20 E3"),

            new PatchFeature(
                "beatup",
                "Auto Perfect Beatup",
                "MusicStation_Prop_BeatupTouchTempo$$FadeoutNote (MOV R6,#5 -> #1)",
                "05 60 A0 E3 1C 60 85 E5 00 00 57 E3 01 00 00 1A",
                "01 60 A0 E3 1C 60 85 E5 00 00 57 E3 01 00 00 1A"),

            new PatchFeature(
                "bubble",
                "Auto Bubble",
                "MusicStation_Bubble_Note$$Update (branch past timing code)",
                "F0 41 2D E9 06 8B 2D ED 08 D0 4D E2 00 50 A0 E1",
                "4A FF FF EA 06 8B 2D ED 08 D0 4D E2 00 50 A0 E1"),

            new PatchFeature(
                "autokey",
                "Autokey",
                "MusicStation_Prop_NoteBoard$$Show (force key path, all matches)",
                "D5 00 D4 E5 00 00 50 E3 0D 00 00 0A 10 40 9A E5",
                "D5 00 D4 E5 00 00 50 E3 00 F0 20 E3 10 40 9A E5"),
        };

        private static void Main(string[] args)
        {
            Console.WriteLine("AyodanceID - Ayodance patch tool");
            Console.WriteLine("===================================");

            if (args.Length == 0)
            {
                InteractiveRun();
                return;
            }

            if (args[0] == "--help" || args[0] == "-h" || args[0] == "/?")
            {
                PrintUsage();
                return;
            }

            (int? pid, Dictionary<string, bool> toggles) = ParseArgs(args);
            if (pid is null)
            {
                Console.WriteLine("ERROR: PID argument missing or invalid.");
                PrintUsage();
                return;
            }

            foreach (string key in toggles.Keys)
            {
                if (key != "all" && key != "grade" && GetFeatureByKey(key) is null)
                {
                    Console.WriteLine($"WARNING: unknown feature '{key}' (use --help).");
                }
            }

            try
            {
                using var mem = new MemoryReader(pid.Value);
                var patcher = new GamePatcher(mem);
                Console.WriteLine($"Connected to PID {pid.Value} (handle 0x{mem.Handle.ToInt64():X}).");
                Console.WriteLine();

                if (toggles.Count == 0)
                {
                    Console.WriteLine("No feature args given -> applying ALL patches + Max Grade.");
                    foreach (PatchFeature feature in Features)
                    {
                        Apply(patcher, feature, enable: true);
                    }
                    RunGrade(mem);
                }
                else if (toggles.TryGetValue("all", out bool allState))
                {
                    foreach (PatchFeature feature in Features)
                    {
                        Apply(patcher, feature, allState);
                    }
                    if (allState)
                    {
                        RunGrade(mem);
                    }
                }
                else
                {
                    foreach ((string key, bool enable) in toggles)
                    {
                        if (key == "grade")
                        {
                            if (enable)
                            {
                                RunGrade(mem);
                            }
                            continue;
                        }

                        PatchFeature? feature = GetFeatureByKey(key);
                        if (feature is not null)
                        {
                            Apply(patcher, feature, enable);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }
        }

        private static void Apply(GamePatcher patcher, PatchFeature feature, bool enable)
        {
            Console.Write($"[{feature.Name}] progressing....");
            PatchReport report = patcher.Apply(feature, enable);

            string status;
            if (enable)
            {
                status = report.Failed > 0
                    ? $"failed ({report.Failed})"
                    : report.Applied == 0 && report.AlreadyDone == 0
                        ? "not found"
                        : "done";
            }
            else
            {
                status = report.Failed > 0
                    ? $"failed ({report.Failed})"
                    : report.Restored == 0 && report.AlreadyDone == 0
                        ? "not found"
                        : "done";
            }
            Console.WriteLine($" > {status}");
        }

        private static void InteractiveRun()
        {
            Console.Write("Game PID: ");
            int pid;
            while (!int.TryParse(Console.ReadLine(), out pid))
            {
                Console.Write("Invalid PID, try again: ");
            }

            try
            {
                using var mem = new MemoryReader(pid);
                var patcher = new GamePatcher(mem);
                Console.WriteLine($"Connected to PID {pid} (handle 0x{mem.Handle.ToInt64():X}).");
                Console.WriteLine();

                for (int i = 0; i < Features.Count; i++)
                {
                    Console.WriteLine($"  {i + 1}. {Features[i].Name}");
                }
                Console.WriteLine($"  {Features.Count + 1}. Scan User struct + write Max Grade");
                Console.WriteLine();

                Console.Write("Features to ENABLE (comma list, Enter = all): ");
                string? enableInput = Console.ReadLine();
                Console.Write("Features to RESTORE to original (prefix with !, e.g. !1,3): ");
                string? disableInput = Console.ReadLine();

                List<int> enableSet = string.IsNullOrWhiteSpace(enableInput)
                    ? Enumerable.Range(0, Features.Count + 1).ToList()
                    : ParseSelection(enableInput, Features.Count + 1);
                List<int> disableSet = ParseSelection(disableInput, Features.Count + 1);

                foreach (int idx in enableSet)
                {
                    if (idx == Features.Count)
                    {
                        RunGrade(mem);
                        continue;
                    }
                    Apply(patcher, Features[idx], enable: true);
                }

                foreach (int idx in disableSet)
                {
                    if (idx == Features.Count)
                    {
                        continue;
                    }
                    Apply(patcher, Features[idx], enable: false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }
        }

        private static List<int> ParseSelection(string? input, int maxExclusive)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(input))
            {
                return result;
            }

            foreach (string raw in input.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string part = raw.Trim().TrimStart('!');
                if (int.TryParse(part, out int v) && v >= 1 && v <= maxExclusive && !result.Contains(v - 1))
                {
                    result.Add(v - 1);
                }
            }
            return result;
        }

        private static void RunGrade(MemoryReader mem)
        {
            Console.Write("[Max Grade] progressing....");
            var scanner = new UserStructScanner(mem);

            List<nint> candidates = scanner.Scan();
            if (candidates.Count == 0)
            {
                Console.WriteLine(" > not found");
                return;
            }

            int ok = 0;
            foreach (nint addr in candidates)
            {
                if (scanner.WriteMaxGrade(addr))
                {
                    ok++;
                }
            }
            Console.WriteLine($" > done ({ok}/{candidates.Count})");
        }

        private static PatchFeature? GetFeatureByKey(string key) =>
            Features.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

        private static (int? pid, Dictionary<string, bool> toggles) ParseArgs(string[] args)
        {
            int? pid = null;
            var toggles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length; i++)
            {
                string token = args[i];
                if (token.StartsWith("--", StringComparison.Ordinal))
                {
                    string key = token[2..].Trim().ToLowerInvariant();
                    if (key.Length == 0)
                    {
                        continue;
                    }

                    string value;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        value = args[++i];
                    }
                    else
                    {
                        value = "on";
                    }
                    toggles[key] = ParseBool(value);
                }
                else if (int.TryParse(token, out int p))
                {
                    pid = p;
                }
            }

            return (pid, toggles);
        }

        private static bool ParseBool(string value) =>
            value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase);

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:  AyodanceID.exe <PID> [--feature on|off ...]");
            Console.WriteLine();
            Console.WriteLine("No feature args  = apply ALL patches + Max Grade (on).");
            Console.WriteLine("--all on|off     = apply/restore every patch at once (on also runs Max Grade).");
            Console.WriteLine();
            Console.WriteLine("Features:");
            foreach (PatchFeature feature in Features)
            {
                Console.WriteLine($"  --{feature.Key,-12} on|off   {feature.Name} - {feature.Description}");
            }
            Console.WriteLine("  --grade        on        Scan User struct + write Max Grade (2610)");
            Console.WriteLine("  --help                   Show this help.");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  AyodanceID.exe 1234");
            Console.WriteLine("  AyodanceID.exe 1234 --lockperfect on --autokey off");
            Console.WriteLine("  AyodanceID.exe 1234 --all off");
        }
    }
}
