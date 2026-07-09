using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Text;
using System.Windows.Media;
using DataConcentrator;

namespace ScadaGUI
{
    // Plays a repeating alarm tone for as long as at least one alarm is active
    // and unacknowledged; silent otherwise. The user can choose between several
    // distinct tones and set the volume (F1). Playback uses a WPF MediaPlayer
    // (which honours Volume, unlike SystemSounds); the tones are generated once
    // as .wav files next to the executable so no binary assets need shipping.
    //
    // All public methods are expected to be called from the UI thread (that is
    // where MainWindow drives them), which is also where MediaPlayer must live.
    public static class AlarmSoundService
    {
        private static readonly object syncRoot = new object();
        private const int SampleRate = 44100;

        // Ordered list of selectable sounds -> generated file path.
        private static readonly Dictionary<string, string> soundFiles = new Dictionary<string, string>();
        private static readonly string[] availableSounds = { "Beep", "Chirp", "Siren" };

        private static MediaPlayer player;
        private static bool initialized;
        private static bool useFallback;
        private static bool isPlaying;
        private static double volume = 0.8; // 0.0 - 1.0
        private static string soundName = "Beep";

        public static IReadOnlyList<string> AvailableSounds => availableSounds;

        // Applies both the selected sound and the volume (percent 0-100).
        public static void Configure(string sound, double volumePercent)
        {
            SetVolume(volumePercent);
            SetSound(sound);
        }

        public static void SetVolume(double volumePercent)
        {
            lock (syncRoot)
            {
                volume = Clamp(volumePercent / 100.0, 0.0, 1.0);
                EnsureInitialized();
                if (player != null)
                {
                    player.Volume = volume;
                }
            }
        }

        public static void SetSound(string sound)
        {
            lock (syncRoot)
            {
                if (string.IsNullOrWhiteSpace(sound) || Array.IndexOf(availableSounds, sound) < 0)
                {
                    return;
                }

                soundName = sound;
                EnsureInitialized();

                // Make the change audible immediately if an alarm is sounding.
                if (isPlaying && !useFallback)
                {
                    PlayCurrent();
                }
            }
        }

        public static void UpdateState(bool hasActiveUnacknowledgedAlarms)
        {
            lock (syncRoot)
            {
                EnsureInitialized();

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

            if (useFallback)
            {
                PlayFallback();
            }
            else
            {
                PlayCurrent();
            }
        }

        private static void Stop()
        {
            if (!isPlaying)
            {
                return;
            }

            isPlaying = false;

            if (!useFallback && player != null)
            {
                try
                {
                    player.Stop();
                }
                catch (Exception ex)
                {
                    SystemLogger.LogError("Alarm sound stop failed.", ex);
                }
            }
        }

        private static void PlayCurrent()
        {
            try
            {
                if (soundFiles.TryGetValue(soundName, out string path) && File.Exists(path))
                {
                    player.Open(new Uri(path));
                    player.Volume = volume;
                    player.Position = TimeSpan.Zero;
                    player.Play();
                }
            }
            catch (Exception ex)
            {
                SystemLogger.LogError("Alarm sound playback failed; falling back to system sound.", ex);
                useFallback = true;
                PlayFallback();
            }
        }

        private static void PlayFallback()
        {
            try
            {
                SystemSounds.Exclamation.Play();
            }
            catch (Exception ex)
            {
                SystemLogger.LogError("Fallback alarm sound playback failed.", ex);
            }
        }

        private static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;

            try
            {
                GenerateSoundFiles();
                player = new MediaPlayer { Volume = volume };
                // Loop the tone for as long as the alarm is active.
                player.MediaEnded += (sender, args) =>
                {
                    try
                    {
                        player.Position = TimeSpan.Zero;
                        player.Play();
                    }
                    catch
                    {
                        // ignore transient looping errors
                    }
                };
            }
            catch (Exception ex)
            {
                SystemLogger.LogError("Alarm sound initialization failed; using system sound.", ex);
                useFallback = true;
            }
        }

        private static void GenerateSoundFiles()
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds");
            Directory.CreateDirectory(folder);

            WriteIfMissing(folder, "Beep", GenerateBeep());
            WriteIfMissing(folder, "Chirp", GenerateChirp());
            WriteIfMissing(folder, "Siren", GenerateSiren());
        }

        private static void WriteIfMissing(string folder, string name, short[] samples)
        {
            string path = Path.Combine(folder, name + ".wav");
            soundFiles[name] = path;
            if (!File.Exists(path))
            {
                WriteWav(path, samples);
            }
        }

        // A high tone pulsed on/off a few times over ~1.2 s.
        private static short[] GenerateBeep()
        {
            int total = (int)(SampleRate * 1.2);
            var samples = new short[total];
            int pulse = (int)(SampleRate * 0.15);
            for (int i = 0; i < total; i++)
            {
                bool on = (i / pulse) % 2 == 0;
                samples[i] = on ? Tone(i, 880) : (short)0;
            }
            return samples;
        }

        // Two alternating tones (a "chirp").
        private static short[] GenerateChirp()
        {
            int total = (int)(SampleRate * 1.2);
            var samples = new short[total];
            int segment = (int)(SampleRate * 0.1);
            for (int i = 0; i < total; i++)
            {
                double freq = (i / segment) % 2 == 0 ? 660 : 990;
                samples[i] = Tone(i, freq);
            }
            return samples;
        }

        // A frequency sweep that repeats (a "siren").
        private static short[] GenerateSiren()
        {
            int total = (int)(SampleRate * 1.2);
            var samples = new short[total];
            double sweepSeconds = 0.6;
            int sweep = (int)(SampleRate * sweepSeconds);
            double phase = 0;
            for (int i = 0; i < total; i++)
            {
                double position = (double)(i % sweep) / sweep;
                double freq = 500 + 900 * position;
                phase += 2 * Math.PI * freq / SampleRate;
                samples[i] = (short)(0.5 * short.MaxValue * Math.Sin(phase));
            }
            return samples;
        }

        private static short Tone(int sampleIndex, double frequency)
        {
            double value = Math.Sin(2 * Math.PI * frequency * sampleIndex / SampleRate);
            return (short)(0.5 * short.MaxValue * value);
        }

        private static void WriteWav(string path, short[] samples)
        {
            int dataSize = samples.Length * 2;
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataSize);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);              // PCM header size
                writer.Write((short)1);        // PCM format
                writer.Write((short)1);        // mono
                writer.Write(SampleRate);
                writer.Write(SampleRate * 2);  // byte rate (mono, 16-bit)
                writer.Write((short)2);        // block align
                writer.Write((short)16);       // bits per sample
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataSize);
                foreach (var sample in samples)
                {
                    writer.Write(sample);
                }
            }
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
