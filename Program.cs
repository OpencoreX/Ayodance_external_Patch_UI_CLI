using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            PrintBanner();

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

            (int? pid, string? processName, Dictionary<string, bool> toggles) = ParseArgs(args);
            pid ??= ResolveProcessId(processName, interactive: false);
            if (pid is null)
            {
                Console.WriteLine("ERROR: PID invalid or process not found. Use <PID> or --process <name>.");
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
                WriteAccent($"[LINK ONLINE] PID {pid.Value}  HANDLE 0x{mem.Handle.ToInt64():X}");
                Console.WriteLine();

                if (toggles.Count == 0)
                {
                    Console.WriteLine("No feature args given -> applying ALL patches + Max Grade.");
                    ApplyMany(patcher, Features, enable: true);
                    RunGrade(mem);
                }
                else if (toggles.TryGetValue("all", out bool allState))
                {
                    ApplyMany(patcher, Features, allState);
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
            Console.Write($"  {feature.Name,-22} ");
            PatchReport report = patcher.Apply(feature, enable);
            PrintReport(report, enable);
        }

        private static void ApplyMany(GamePatcher patcher, IReadOnlyList<PatchFeature> features, bool enable)
        {
            WriteAccent($"[FAST SCAN] {features.Count} modules / readable regions / single memory pass");
            IReadOnlyList<PatchReport> reports = patcher.ApplyMany(features, enable, ShowScanProgress);
            Console.WriteLine();
            foreach (PatchReport report in reports)
            {
                Console.Write($"  {report.Feature.Name,-22} ");
                PrintReport(report, enable);
            }
        }

        private static void ShowScanProgress(int completed, int total)
        {
            if (Console.IsOutputRedirected)
            {
                return;
            }

            int percent = total == 0 ? 100 : completed * 100 / total;
            int filled = percent / 5;
            string bar = new string('█', filled).PadRight(20, '░');
            Console.Write($"\r  SCAN [{bar}] {percent,3}%  regions {completed}/{total}");
        }

        private static void PrintReport(PatchReport report, bool enable)
        {
            string status = report.Failed > 0
                ? $"FAILED ({report.Failed})"
                : enable
                    ? report.Applied == 0 && report.AlreadyDone == 0 ? "NOT FOUND" : "ONLINE"
                    : report.Restored == 0 && report.AlreadyDone == 0 ? "NOT FOUND" : "RESTORED";
            string detail = enable
                ? $"{report.Applied} applied / {report.AlreadyDone} active"
                : $"{report.Restored} restored / {report.AlreadyDone} original";
            WriteStatus($"{status,-10} {detail}", report.Failed > 0 ? ConsoleColor.Red : ConsoleColor.Green);
        }

        private static void InteractiveRun()
        {
            int? selectedPid = ResolveProcessId("Ld9BoxHeadless.exe", interactive: true);
            if (selectedPid is null)
            {
                return;
            }
            int pid = selectedPid.Value;

            try
            {
                using var mem = new MemoryReader(pid);
                var patcher = new GamePatcher(mem);
                WriteAccent($"[LINK ONLINE] PID {pid}  HANDLE 0x{mem.Handle.ToInt64():X}");
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

                List<PatchFeature> enableFeatures = enableSet
                    .Where(idx => idx < Features.Count)
                    .Select(idx => Features[idx])
                    .ToList();
                if (enableFeatures.Count > 0)
                {
                    ApplyMany(patcher, enableFeatures, enable: true);
                }

                foreach (int idx in enableSet)
                {
                    if (idx == Features.Count)
                    {
                        RunGrade(mem);
                    }
                }

                List<PatchFeature> disableFeatures = disableSet
                    .Where(idx => idx < Features.Count)
                    .Select(idx => Features[idx])
                    .ToList();
                if (disableFeatures.Count > 0)
                {
                    ApplyMany(patcher, disableFeatures, enable: false);
                }

                foreach (int idx in disableSet)
                {
                    if (idx == Features.Count)
                    {
                        continue;
                    }
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

            List<nint> candidates = scanner.Scan(ShowGradeProgress);
            Console.WriteLine();
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

        private static void ShowGradeProgress(int completed, int total)
        {
            if (Console.IsOutputRedirected)
            {
                return;
            }

            int percent = total == 0 ? 100 : completed * 100 / total;
            int filled = percent / 5;
            string bar = new string('█', filled).PadRight(20, '░');
            Console.Write($"\r[Max Grade] SCAN [{bar}] {percent,3}%  regions {completed}/{total}");
        }

        private static PatchFeature? GetFeatureByKey(string key) =>
            Features.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

        private static (int? pid, string? processName, Dictionary<string, bool> toggles) ParseArgs(string[] args)
        {
            int? pid = null;
            string? processName = null;
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
                    if (key is "process" or "name")
                    {
                        processName = value;
                    }
                    else
                    {
                        toggles[key] = ParseBool(value);
                    }
                }
                else if (int.TryParse(token, out int p))
                {
                    pid = p;
                }
            }

            return (pid, processName, toggles);
        }

        private static int? ResolveProcessId(string? requestedName, bool interactive)
        {
            string processName = requestedName ?? string.Empty;

            processName = Path.GetFileNameWithoutExtension(processName.Trim());
            if (processName.Length == 0)
            {
                WriteStatus("ERROR: process name is empty.", ConsoleColor.Red);
                return null;
            }

            List<(int Id, string Name)> matches = Process.GetProcessesByName(processName)
                .Select(process =>
                {
                    using (process)
                    {
                        return (process.Id, process.ProcessName);
                    }
                })
                .OrderBy(match => match.Id)
                .ToList();

            if (matches.Count == 0)
            {
                WriteStatus($"ERROR: process '{processName}.exe' not found.", ConsoleColor.Red);
                return null;
            }

            if (matches.Count == 1)
            {
                return matches[0].Id;
            }

            if (!interactive)
            {
                WriteStatus($"ERROR: found {matches.Count} processes named '{processName}.exe'; specify a PID.", ConsoleColor.Red);
                foreach ((int id, string name) in matches)
                {
                    Console.WriteLine($"  PID {id}  {name}.exe");
                }
                return null;
            }

            WriteAccent($"[TARGETS] {matches.Count} matching processes");
            for (int i = 0; i < matches.Count; i++)
            {
                Console.WriteLine($"  [{i + 1}] PID {matches[i].Id}  {matches[i].Name}.exe");
            }

            while (true)
            {
                Console.Write("Select target number or PID: ");
                string? input = Console.ReadLine()?.Trim();
                if (int.TryParse(input, out int selection))
                {
                    if (selection >= 1 && selection <= matches.Count)
                    {
                        return matches[selection - 1].Id;
                    }

                    if (matches.Any(match => match.Id == selection))
                    {
                        return selection;
                    }
                }
                WriteStatus("Invalid selection.", ConsoleColor.Red);
            }
        }

        private static bool ParseBool(string value) =>
            value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase);

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:  AyodanceID.exe <PID> [--feature on|off ...]");
            Console.WriteLine("        AyodanceID.exe --process Ld9BoxHeadless.exe [--feature on|off ...]");
            Console.WriteLine();
            Console.WriteLine("No feature args  = apply ALL patches + Max Grade (on).");
            Console.WriteLine("--all on|off     = apply/restore every patch at once (on also runs Max Grade).");
            Console.WriteLine("--process NAME  = resolve PID by process name (--name is an alias).");
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

        private static void PrintBanner()
        {
            WriteAccent("╔══════════════════════════════════════════════════════════╗");
            WriteAccent("║  AYODANCE // MEMORY PATCH CONSOLE       v2.0 FAST SCAN  ║");
            WriteAccent("║  SIGNAL: READY     MODE: SAFE RESTORE     CORE: ONLINE  ║");
            WriteAccent("╚══════════════════════════════════════════════════════════╝");
        }

        private static void WriteAccent(string text)
        {
            WriteStatus(text, ConsoleColor.Cyan);
        }

        private static void WriteStatus(string text, ConsoleColor color)
        {
            if (Console.IsOutputRedirected)
            {
                Console.WriteLine(text);
                return;
            }

            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = previous;
        }
    }
}
