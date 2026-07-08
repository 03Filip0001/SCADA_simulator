using System;
using System.Media;
using System.Threading;
using DataConcentrator;

namespace ScadaGUI
{
    // Plays a repeating alert tone for as long as at least one alarm is
    // active and unacknowledged; silent otherwise. Calling UpdateState is
    // idempotent, so it can be invoked freely without restarting the sound
    // or spawning overlapping playback.
    public static class AlarmSoundService
    {
        private static readonly object syncRoot = new object();
        private static readonly TimeSpan RepeatInterval = TimeSpan.FromSeconds(1);

        private static Timer repeatTimer;
        private static bool isPlaying;

        public static void UpdateState(bool hasActiveUnacknowledgedAlarms)
        {
            lock (syncRoot)
            {
                if (hasActiveUnacknowledgedAlarms)
                {
                    Start();
                }
                else
                {
                    Stop();
                }
            }
        }

        private static void Start()
        {
            if (isPlaying)
            {
                return;
            }

            isPlaying = true;
            PlaySoundSafely();
            repeatTimer = new Timer(_ => PlaySoundSafely(), null, RepeatInterval, RepeatInterval);
        }

        private static void Stop()
        {
            if (!isPlaying)
            {
                return;
            }

            isPlaying = false;
            repeatTimer?.Dispose();
            repeatTimer = null;
        }

        private static void PlaySoundSafely()
        {
            try
            {
                SystemSounds.Exclamation.Play();
            }
            catch (Exception ex)
            {
                SystemLogger.LogError("Alarm sound playback failed.", ex);
            }
        }
    }
}
