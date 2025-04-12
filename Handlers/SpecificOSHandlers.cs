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

        // Note: The #if Windows check might only work after publishing, not during development.
        #if WINDOWS
            if (runInBackgroundArg == false)
            {
                WindowsNativeMethods.AllocConsole();
                return false;
            }
            else
            {
                return true;
            }
        #else
            if (runInBackgroundArg && !OperatingSystem.IsWindows())
            {
                LogError("Error: Can only use \"background\" mode (without console window) on Windows.");
            }
            else if (runInBackgroundArg && OperatingSystem.IsWindows())
            {
                // The background mode won't work in development mode. If it wasn't caught by the pre-processor directive, it didn't work so print a message
                LogError("Error: Background mode is not supported in development mode. Please run the published version.");
            }

            return false; // Not Windows, so return false always
        #endif
    }
}

