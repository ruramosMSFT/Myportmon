using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PortMonitor.Services;

/// <summary>Result of a single prerequisite check.</summary>
/// <param name="Name">Human-readable name of the prerequisite.</param>
/// <param name="IsOk">True when the requirement is satisfied.</param>
/// <param name="Message">Status detail shown to the user.</param>
/// <param name="CanAutoFix">True when this tool can attempt an automated install/fix.</param>
/// <param name="WingetId">winget package ID used for installation, if applicable.</param>
public record PrerequisiteResult(
    string  Name,
    bool    IsOk,
    string  Message,
    bool    CanAutoFix  = false,
    string? WingetId    = null
);

/// <summary>
/// Checks that all runtime prerequisites are present on the current machine
/// and offers to install missing components via winget with user consent.
/// </summary>
public static class PrerequisiteChecker
{
    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Runs all prerequisite checks and returns a result per item.
    /// </summary>
    public static IReadOnlyList<PrerequisiteResult> CheckAll() =>
    [
        CheckWindowsVersion(),
        CheckDotNet8Runtime(),
        CheckAdminElevation(),
        CheckWinget(),
    ];

    /// <summary>
    /// Installs all fixable items in the supplied list using winget.
    /// Reports progress through the optional <paramref name="progress"/> callback.
    /// </summary>
    /// <param name="results">Results from <see cref="CheckAll"/> to process.</param>
    /// <param name="progress">Optional callback invoked with a status message for each step.</param>
    public static void InstallMissing(
        IReadOnlyList<PrerequisiteResult> results,
        Action<string>? progress = null)
    {
        bool wingetAvailable = results.Any(r => r.Name == "winget" && r.IsOk);
        if (!wingetAvailable)
        {
            progress?.Invoke("winget is not available — cannot auto-install.");
            return;
        }

        foreach (var item in results.Where(r => !r.IsOk && r.CanAutoFix))
        {
            progress?.Invoke($"Installing {item.Name}...");
            Install(item, progress);
        }
    }

    /// <summary>
    /// Presents a full interactive prerequisite report, prompts the user to
    /// install any missing component that supports auto-fix, and waits for
    /// confirmation before returning.
    /// </summary>
    /// <returns>True if all requirements are satisfied after the check (fixes included).</returns>
    public static bool RunInteractive()
    {
        Console.Clear();
        PrintBanner();

        var results = CheckAll();
        bool wingetAvailable = results.Any(r => r.Name == "winget" && r.IsOk);

        PrintResults(results, wingetAvailable);

        bool allOk = results.All(r => r.IsOk);

        // ── Offer to fix anything that can be auto-fixed ─────────────────────
        var fixable = results
            .Where(r => !r.IsOk && r.CanAutoFix && wingetAvailable)
            .ToList();

        if (fixable.Count > 0)
        {
            Console.WriteLine();
            WriteColored("  The following missing components can be installed automatically:", ConsoleColor.Yellow);
            foreach (var f in fixable)
                WriteColored($"    • {f.Name}", ConsoleColor.Yellow);

            Console.WriteLine();
            Console.Write("  Install now? [Y/N]: ");
            var key = Console.ReadKey(false).KeyChar;
            Console.WriteLine();

            if (char.ToUpperInvariant(key) == 'Y')
            {
                foreach (var f in fixable)
                    Install(f);

                Console.WriteLine();
                WriteColored("  Re-checking after installation...", ConsoleColor.Cyan);
                Console.WriteLine();
                var recheck = CheckAll();
                PrintResults(recheck, wingetAvailable);
                allOk = recheck.All(r => r.IsOk);
            }
        }

        Console.WriteLine();

        if (allOk)
        {
            WriteColored("  ✓ All prerequisites satisfied.", ConsoleColor.Green);
        }
        else
        {
            WriteColored("  ⚠ Some prerequisites are not met. The application may not work correctly.", ConsoleColor.Yellow);
        }

        Console.WriteLine();
        Console.WriteLine("  Press any key to continue...");
        Console.ReadKey(true);

        return allOk;
    }

    /// <summary>
    /// Performs a silent startup check. Returns a list of failed results.
    /// Does not install anything.
    /// </summary>
    public static IReadOnlyList<PrerequisiteResult> SilentCheck() =>
        CheckAll().Where(r => !r.IsOk).ToList();

    // ── Individual checks ────────────────────────────────────────────────────

    /// <summary>Verifies the OS is Windows 10 (build 10240) or later.</summary>
    private static PrerequisiteResult CheckWindowsVersion()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new("Windows 10/11", false, "Not running on Windows.");

        var ver = Environment.OSVersion.Version;
        bool ok = ver.Major > 10 || (ver.Major == 10 && ver.Build >= 10240);
        string msg = ok
            ? $"Windows build {ver.Build} ({ver})"
            : $"Windows build {ver.Build} — Windows 10 (build 10240) or later required.";
        return new("Windows 10/11", ok, msg);
    }

    /// <summary>
    /// Checks that a .NET 8 runtime is available by running <c>dotnet --list-runtimes</c>.
    /// Skipped when the current process is already a .NET 8 self-contained app.
    /// </summary>
    private static PrerequisiteResult CheckDotNet8Runtime()
    {
        // Self-contained publish bundles .NET — always satisfied.
        var frameworkDesc = RuntimeInformation.FrameworkDescription;
        if (frameworkDesc.StartsWith(".NET 8", StringComparison.OrdinalIgnoreCase))
            return new(".NET 8 Runtime", true, $"Bundled — {frameworkDesc}");

        // Framework-dependent: probe dotnet CLI.
        try
        {
            var psi = new ProcessStartInfo("dotnet", "--list-runtimes")
            {
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var proc = Process.Start(psi);
            string output  = proc?.StandardOutput.ReadToEnd() ?? string.Empty;
            proc?.WaitForExit();

            bool found = output.Contains("Microsoft.NETCore.App 8.", StringComparison.OrdinalIgnoreCase)
                      || output.Contains("Microsoft.AspNetCore.App 8.", StringComparison.OrdinalIgnoreCase);

            return found
                ? new(".NET 8 Runtime", true,  "Installed.")
                : new(".NET 8 Runtime", false, ".NET 8 runtime not found.", CanAutoFix: true,
                      WingetId: "Microsoft.DotNet.Runtime.8");
        }
        catch
        {
            return new(".NET 8 Runtime", false, "Could not query dotnet CLI.", CanAutoFix: true,
                       WingetId: "Microsoft.DotNet.Runtime.8");
        }
    }

    /// <summary>Checks whether the current process is running with Administrator privileges.</summary>
    private static PrerequisiteResult CheckAdminElevation()
    {
        try
        {
            bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);

            return isAdmin
                ? new("Administrator",  true,  "Running as Administrator.")
                : new("Administrator",  false, "Not elevated — some process names will show as [N/A]. " +
                                               "Right-click the exe and choose 'Run as administrator'.",
                      CanAutoFix: false);
        }
        catch
        {
            return new("Administrator", false, "Could not determine elevation status.", CanAutoFix: false);
        }
    }

    /// <summary>Checks whether winget is available (needed for auto-installation of other components).</summary>
    private static PrerequisiteResult CheckWinget()
    {
        try
        {
            var psi = new ProcessStartInfo("winget", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var proc = Process.Start(psi);
            string ver     = proc?.StandardOutput.ReadToEnd().Trim() ?? string.Empty;
            proc?.WaitForExit();

            bool ok = proc?.ExitCode == 0 && ver.Length > 0;
            return ok
                ? new("winget", true,  $"Available ({ver}).")
                : new("winget", false, "winget not found — automatic installation unavailable.");
        }
        catch
        {
            return new("winget", false, "winget not found — automatic installation unavailable.");
        }
    }

    // ── Installation ─────────────────────────────────────────────────────────

    /// <summary>Attempts to install a component via winget.</summary>
    private static void Install(PrerequisiteResult item, Action<string>? progress = null)
    {
        if (item.WingetId is null) return;

        void Report(string msg) { progress?.Invoke(msg); Console.WriteLine(msg); }

        Report($"  → Installing {item.Name} via winget...");

        try
        {
            var psi = new ProcessStartInfo(
                "winget",
                $"install --id {item.WingetId} --silent --accept-package-agreements --accept-source-agreements")
            {
                UseShellExecute = false,
                CreateNoWindow  = false
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit();

            bool success = proc?.ExitCode == 0;
            if (success)
                Report($"  ✓ {item.Name} installed successfully.");
            else
                Report($"  ✗ Installation of {item.Name} failed (exit code {proc?.ExitCode}). Please install manually.");
        }
        catch (Exception ex)
        {
            Report($"  ✗ Could not launch winget: {ex.Message}");
        }
    }

    // ── Rendering helpers ─────────────────────────────────────────────────────

    private static void PrintBanner()
    {
        WriteColored("╔══════════════════════════════════════════════════╗", ConsoleColor.Cyan);
        WriteColored("║        PortMonitor — Prerequisite Check          ║", ConsoleColor.Cyan);
        WriteColored("╚══════════════════════════════════════════════════╝", ConsoleColor.Cyan);
        Console.WriteLine();
    }

    private static void PrintResults(IReadOnlyList<PrerequisiteResult> results, bool wingetAvailable)
    {
        foreach (var r in results)
        {
            // Skip the winget row when it is only an enabler — shown once at the bottom if needed.
            ConsoleColor color = r.IsOk ? ConsoleColor.Green : ConsoleColor.Red;
            string       icon  = r.IsOk ? "✓" : "✗";

            // Admin is a soft requirement — yellow, not red.
            if (!r.IsOk && r.Name == "Administrator")
                color = ConsoleColor.Yellow;

            WriteColored($"  [{icon}] {r.Name,-22} {r.Message}", color);

            if (!r.IsOk && r.CanAutoFix && !wingetAvailable)
            {
                WriteColored($"      ↳ Install manually: winget install {r.WingetId}", ConsoleColor.DarkGray);
            }
        }
    }

    private static void WriteColored(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
