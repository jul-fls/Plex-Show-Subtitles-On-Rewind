using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RewindSubtitleDisplayerForPlex;


internal class SubtitlesHotkeyMonitor
{
    private string PlaybackID;
    private string MachineID;
    private static List<SubtitlesHotkeyMonitor> _allHotkeyMonitors = [];

    // -------------------------------------
    private int msOfLastPause = 0;
    private int msOfLastPlay = 0;
    private int currentTime = 0;

    private Action lastAction = Action.None;

    private int clickTimeThreshold = 500; // 500 ms threshold for double click detection
    private HotKeyMode hotKeyMode = HotKeyMode.TripleClick;

    public SubtitlesHotkeyMonitor(string playbackID, string machineID)
    {
        PlaybackID = playbackID;
        MachineID = machineID;
        _allHotkeyMonitors.Add(this);
    }

    // This method is called when the play/pause key is pressed.
    public void OnPlayPauseKeyPress(Action action)
    {
        // Need to monitor for double presses of the play/pause key.
        // For Double Click: Pause -> Play          (Just need to know current time and last pause time)
        // For Triple Click: Pause -> Play -> Pause (Need to know current time, last play time, and last pause time)

        // For the purposes of the hotkey, we'll treat a buffering action the same as playing.
        //  But a playing action immediately following buffering will be ignored

        // Ignore buffering action immediately after playing
        if (action == Action.Play && lastAction == Action.Buffering)
        {
            return;
        }

        // -------------------------------------------------------
        currentTime = Environment.TickCount;
        int pauseTimeDiff = currentTime - msOfLastPause;
        int playTimeDiff = currentTime - msOfLastPlay;

        // If the action is Play or Buffering, update the last play time
        if (action == Action.Play || action == Action.Buffering)
        {
            msOfLastPlay = currentTime;
            lastAction = action;
        }
        else if (action == Action.Pause) // If the action is Pause, update the last pause time
        {
            msOfLastPause = currentTime;
            lastAction = action;
        }

        if (hotKeyMode == HotKeyMode.DoubleClick)
        {
            if ((action == Action.Play || action == Action.Buffering) && pauseTimeDiff < clickTimeThreshold)
            {
                // Double click detected
                OnDoubleClick();

            }
            else if (action == Action.Pause && playTimeDiff < clickTimeThreshold)
            {
                // Double click detected
                OnDoubleClick();
            }
        }

        if (hotKeyMode == HotKeyMode.TripleClick)
        {
            if ((action == Action.Play || action == Action.Buffering)   // Current click - Play
                && pauseTimeDiff < clickTimeThreshold                   // Last click - Pause
                && playTimeDiff < clickTimeThreshold)                   // Preceding click - Play
            {
                // Triple click detected
                OnTripleClick();
            }
            else if (action == Action.Pause                     // Current click - Pause
                && playTimeDiff < clickTimeThreshold            // Last click - Play
                && pauseTimeDiff < (clickTimeThreshold * 2))    // Preceding click - Pause -- Allow twice as much time since it's a triple click
            {
                // Triple click detected
                OnTripleClick();
            }
        }

    } // ----------------- End of OnPlayPauseKeyPress -----------------

    public void OnDoubleClick()
    {
        // Handle double click action here
        // For example, you might want to skip forward or backward in the video
        LogDebugExtra("Double click action triggered.", Yellow);
    }
    public void OnTripleClick()
    {
        // Handle triple click action here
        // For example, you might want to skip forward or backward in the video
        LogDebugExtra("Triple click action triggered.", Yellow);
    }

    public static void ForwardActionToMonitorByID(string? machineID, Action action)
    {
        if (string.IsNullOrEmpty(machineID))
        {
            LogWarning("Machine ID is null or empty. Cannot forward action.");
            return;
        }

        // Find the monitor with the specified playbackID
        SubtitlesHotkeyMonitor? monitor = _allHotkeyMonitors.FirstOrDefault(m => m.MachineID == machineID);
        if (monitor != null)
        {
            // Call the OnPlayPauseKeyPress method on the found monitor
            monitor.OnPlayPauseKeyPress(action);
        }
        else
        {
            LogWarning($"No monitor found for playbackID trying to forward event to proper hotkey monitor: {machineID}");
        }
    }

    //  ------------------------------------------
    public enum Action
    {
        Play,
        Pause,
        Buffering,
        None
    }

    public enum HotKeyMode
    {
        DoubleClick,
        TripleClick
    }
}
