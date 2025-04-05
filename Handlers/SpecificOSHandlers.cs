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

    public static void HandleBackgroundArg(bool runInBackgroundArg)
    {
        //DEBUG - Get os and current target framework
        //OperatingSystem os = Environment.OSVersion;
        //string? targetFramework = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName;

        #if WINDOWS
            if (runInBackgroundArg == false)
            {
                WindowsNativeMethods.AllocConsole();
            }
        #else
            if (runInBackgroundArg)
            {
                LogError("Error: Can only use \"background\" mode (without console window) on Windows.");
            }
        #endif
    }
}

