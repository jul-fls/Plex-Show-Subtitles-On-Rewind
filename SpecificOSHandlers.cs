using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PlexShowSubtitlesOnRewind;

internal static partial class OS_Handlers
{
    internal partial class WindowsNativeMethods
    {
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool AllocConsole();
    }

    public static void InitializeConsole(string[] args)
    {
        //#if Windows
            WindowsNativeMethods.AllocConsole();
        //#endif
    }
}

