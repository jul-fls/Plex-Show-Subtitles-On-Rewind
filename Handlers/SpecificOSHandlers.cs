using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RewindSubtitleDisplayerForPlex;

internal static partial class OS_Handlers
{
    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF; // -1

    internal partial class WindowsNativeMethods
    {
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AttachConsole(uint dwProcessId);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AllocConsole();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool FreeConsole();
    }

    public static void FreeConsoleIfNeeded()
    {
        if (OperatingSystem.IsWindows() && isConsoleAttached)
        {
            var result = WindowsNativeMethods.FreeConsole();
        }
    }

    public static bool isConsoleAttached = false;

    // Checks if background mode is supported and applys it. Returns true only if actually running in background mode.
    public static bool HandleBackgroundMode(bool runInBackgroundArg)
    {
        //DEBUG - Get os and current target framework
        //OperatingSystem os = Environment.OSVersion;
        //string? targetFramework = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName;

        // Windows
        if (OperatingSystem.IsWindows())
        {
            string osMessage = "Windows OS detected.";

            // Default is to NOT run in background mode, so allocate console
            if (runInBackgroundArg == false)
            {
                string bgMode = "Background mode is not enabled. Allocating console for the process.";
                // Attempt to attach to the parent process's console.
                bool attached;
                attached = WindowsNativeMethods.AttachConsole(ATTACH_PARENT_PROCESS);

                // If we are not attached to a console, we need to allocate one.
                if (!attached)
                {
                    string noParentConsole = "No parent console found. Attempting to allocate a new console for the process.";
                    // Allocate a new console for the process.
                    bool allocated = WindowsNativeMethods.AllocConsole();

                    // If allocation failed, we can use logging again because it doesn't matter anyway
                    if (!allocated)
                    {
                        LogDebug(osMessage);
                        LogDebug(bgMode);
                        LogDebug(noParentConsole);
                        LogError("Error: Failed to allocate console. Exiting.");
                    }
                    else
                    {
                        LogDebug(osMessage);
                        LogDebug(bgMode);
                        LogDebug(noParentConsole);
                        LogDebug("Console allocated successfully.");
                    }


                    return !allocated; // if we allocated a console, we are not in background mode.
                }
                else
                {
                    LogDebug(osMessage);
                    LogDebug(bgMode);
                    LogDebug("Attached to parent console successfully.");
                    isConsoleAttached = true;

                    // Use carriage return to clear the current line and write the full width of the console
                    Console.Write("\r");
                    Console.Write(new string(' ', Console.WindowWidth-1));
                    Console.Write("\r");

                    return false; // Successfully attached to the parent console, so we are not in background mode.
                }

            }
            else // To run in background mode we do nothing
            {
                LogDebug("Background mode is enabled. No console will be allocated.");
                return true;
            }
        }
        // Not windows
        else
        {
            if (runInBackgroundArg)
            {
                LogError("Error: Can only use \"background\" mode (without console window) on Windows.");
            }
            return false; // Not Windows, so return false always
        }
    }
}

