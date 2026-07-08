using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace F1XR.Editor
{
    public static class AudioLapExtractor
    {
        private const string SourcePath = "Assets/F1_XR_Visualizer/07_Sounds/Source/F1_1.mp3";
        private const string OutputFolder = "Assets/F1_XR_Visualizer/07_Sounds/Extracted";
        private const float SegmentSeconds = 3.0f;
        private const float WindowSeconds = 0.25f;
        private const float FadeSeconds = 0.06f;

        [MenuItem("F1XR/Audio/Extract Engine Loop Candidates")]
        public static void ExtractDefault()
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SourcePath);
            if (clip == null)
            {
                Debug.LogError($"Audio source not found: {SourcePath}");
                return;
            }

            if (!Directory.Exists(OutputFolder))
                Directory.CreateDirectory(OutputFolder);

            float[] samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0))
            {
                Debug.LogError($"Could not read audio samples: {SourcePath}");
                return;
            }

            List<Window> windows = Analyze(samples, clip.channels, clip.frequency);
            if (windows.Count == 0)
            {
                Debug.LogError("No analysis windows were generated.");
                return;
            }

            float[] rmsValues = Values(windows, item => item.rms);
            float[] zcrValues = Values(windows, item => item.zcr);
            float rmsLow = Percentile(rmsValues, 0.18f);
            float rmsMid = Percentile(rmsValues, 0.50f);
            float rmsHigh = Percentile(rmsValues, 0.72f);
            float zcrLow = Percentile(zcrValues, 0.28f);
            float zcrMid = Percentile(zcrValues, 0.55f);
            float zcrHigh = Percentile(zcrValues, 0.78f);
            float zcrMax = Percentile(zcrValues, 0.90f);

            Export("lap_idle_loop.wav", PickStable(windows, item => item.rms <= rmsLow && item.zcr <= zcrLow), samples, clip.channels, clip.frequency);
            Export("lap_low_load_loop.wav", PickStable(windows, item => item.rms >= rmsHigh && item.zcr <= zcrMid), samples, clip.channels, clip.frequency);
            Export("lap_mid_load_loop.wav", PickStable(windows, item => item.rms >= rmsHigh && item.zcr > zcrLow && item.zcr <= zcrHigh), samples, clip.channels, clip.frequency);
            Export("lap_high_load_loop.wav", PickStable(windows, item => item.rms >= rmsMid && item.zcr >= zcrHigh), samples, clip.channels, clip.frequency);
            Export("lap_coast_loop.wav", PickStable(windows, item => item.rms > rmsLow && item.rms <= rmsMid && item.zcr >= zcrMid), samples, clip.channels, clip.frequency);
            Export("lap_maxrpm_loop.wav", PickStable(windows, item => item.rms >= rmsHigh && item.zcr >= zcrMax), samples, clip.channels, clip.frequency);

            AssetDatabase.Refresh();
            Debug.Log($"Engine loop candidates exported to {OutputFolder}");
        }

        private static List<Window> Analyze(float[] samples, int channels, int sampleRate)
        {
            List<Window> windows = new();
            int windowFrames = Mathf.Max(1, Mathf.RoundToInt(WindowSeconds * sampleRate));
            int totalFrames = samples.Length / channels;

            for (int start = 0; start + windowFrames < totalFrames; start += windowFrames)
            {
                double sum = 0.0;
                int crossings = 0;
                float previous = 0f;
                bool hasPrevious = false;

                for (int frame = start; frame < start + windowFrames; frame++)
                {
                    float mono = Mono(samples, channels, frame);
                    sum += mono * mono;

                    if (hasPrevious && Mathf.Sign(previous) != Mathf.Sign(mono))
                        crossings++;

                    previous = mono;
                    hasPrevious = true;
                }

                windows.Add(new Window
                {
                    startFrame = start,
                    rms = Mathf.Sqrt((float)(sum / windowFrames)),
                    zcr = crossings / (float)windowFrames
                });
            }

            return windows;
        }

        private static Window PickStable(List<Window> windows, Predicate<Window> predicate)
        {
            Window best = default;
            float bestScore = float.MinValue;

            for (int i = 0; i < windows.Count; i++)
            {
                if (!predicate(windows[i]))
                    continue;

                float score = 0f;
                int count = 0;
                int from = Mathf.Max(0, i - 3);
                int to = Mathf.Min(windows.Count - 1, i + 3);

                for (int j = from; j <= to; j++)
                {
                    if (!predicate(windows[j]))
                        continue;

                    score += 1f - Mathf.Abs(windows[j].rms - windows[i].rms);
                    count++;
                }

                score += count * 0.8f;
                if (score > bestScore)
                {
                    best = windows[i];
                    bestScore = score;
                }
            }

            if (bestScore > float.MinValue)
                return best;

            return windows[Mathf.Clamp(windows.Count / 2, 0, windows.Count - 1)];
        }

        private static void Export(string fileName, Window window, float[] samples, int channels, int sampleRate)
        {
            int totalFrames = samples.Length / channels;
            int segmentFrames = Mathf.Min(Mathf.RoundToInt(SegmentSeconds * sampleRate), totalFrames);
            int startFrame = Mathf.Clamp(window.startFrame - segmentFrames / 2, 0, Mathf.Max(0, totalFrames - segmentFrames));
            float[] segment = new float[segmentFrames * channels];
            Array.Copy(samples, startFrame * channels, segment, 0, segment.Length);
            ApplyFade(segment, channels, sampleRate);

            string path = Path.Combine(OutputFolder, fileName);
            WriteWav(path, segment, channels, sampleRate);
            Debug.Log($"Exported {fileName}: start={startFrame / (float)sampleRate:0.00}s, rms={window.rms:0.000}, zcr={window.zcr:0.000}");
        }

        private static void ApplyFade(float[] samples, int channels, int sampleRate)
        {
            int fadeFrames = Mathf.Min(Mathf.RoundToInt(FadeSeconds * sampleRate), samples.Length / channels / 2);

            for (int frame = 0; frame < fadeFrames; frame++)
            {
                float fadeIn = frame / (float)fadeFrames;
                float fadeOut = 1f - fadeIn;
                int endFrame = samples.Length / channels - 1 - frame;

                for (int channel = 0; channel < channels; channel++)
                {
                    samples[frame * channels + channel] *= fadeIn;
                    samples[endFrame * channels + channel] *= fadeOut;
                }
            }
        }

        private static void WriteWav(string path, float[] samples, int channels, int sampleRate)
        {
            using BinaryWriter writer = new(File.Open(path, FileMode.Create));
            int dataLength = samples.Length * 2;

            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            foreach (float sample in samples)
            {
                short value = (short)Mathf.Clamp(Mathf.RoundToInt(sample * short.MaxValue), short.MinValue, short.MaxValue);
                writer.Write(value);
            }
        }

        private static float Mono(float[] samples, int channels, int frame)
        {
            float sum = 0f;
            for (int channel = 0; channel < channels; channel++)
                sum += samples[frame * channels + channel];

            return sum / channels;
        }

        private static float[] Values(List<Window> windows, Func<Window, float> selector)
        {
            float[] values = new float[windows.Count];
            for (int i = 0; i < windows.Count; i++)
                values[i] = selector(windows[i]);

            return values;
        }

        private static float Percentile(float[] values, float percentile)
        {
            Array.Sort(values);
            int index = Mathf.Clamp(Mathf.RoundToInt((values.Length - 1) * percentile), 0, values.Length - 1);
            return values[index];
        }

        private struct Window
        {
            public int startFrame;
            public float rms;
            public float zcr;
        }
    }
}
