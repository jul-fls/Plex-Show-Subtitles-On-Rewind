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

    internal partial class WindowsNativeMethods
    {
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool AllocConsole();
    }

    // Checks if background mode is supported and applys it. Returns true only if actually running in background mode.
    public static bool HandleBackgroundMode(bool runInBackgroundArg)
    {
        //DEBUG - Get os and current target framework
        //OperatingSystem os = Environment.OSVersion;
        //string? targetFramework = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName;

        // Windows
        if (OperatingSystem.IsWindows())
            // Default is to NOT run in background mode, so allocate console
            if (runInBackgroundArg == false)
            {
                WindowsNativeMethods.AllocConsole();
                return false;
            }
            else // To run in background mode we do nothing
            {
                return true;
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

