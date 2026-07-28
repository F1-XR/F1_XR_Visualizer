// AIBridge/Voice/WavUtil.cs
// AudioClip ↔ 16-bit PCM WAV 변환. 외부 패키지 의존 없음(항상 컴파일).
//  - FromAudioClip: 마이크 캡처 → wav 바이트(전송용). mono로 다운믹스.
//  - ToAudioClip:   수신한 wav 바이트 → AudioClip(재생용).
using System;
using UnityEngine;

namespace F1XR.AIBridge.Voice
{
    public static class WavUtil
    {
        /// <summary>AudioClip → 16-bit PCM mono WAV(헤더 포함). sampleCount&lt;0 이면 전체.</summary>
        public static byte[] FromAudioClip(AudioClip clip, int sampleCount = -1)
        {
            int channels = Mathf.Max(1, clip.channels);
            int frames = sampleCount >= 0 ? sampleCount : clip.samples;
            float[] data = new float[frames * channels];
            clip.GetData(data, 0);

            short[] pcm = new short[frames];
            for (int i = 0; i < frames; i++)
            {
                float s = 0f;
                for (int c = 0; c < channels; c++) s += data[i * channels + c];
                s /= channels;                                   // mono 다운믹스
                pcm[i] = (short)Mathf.Clamp(s * 32767f, -32768f, 32767f);
            }
            return Encode(pcm, clip.frequency, 1);
        }

        static byte[] Encode(short[] pcm, int sampleRate, int channels)
        {
            int blockAlign = channels * 2;
            int byteRate = sampleRate * blockAlign;
            int dataLen = pcm.Length * 2;
            byte[] wav = new byte[44 + dataLen];
            using (var ms = new System.IO.MemoryStream(wav))
            using (var w = new System.IO.BinaryWriter(ms))
            {
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + dataLen);
                w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' });
                w.Write(16); w.Write((short)1); w.Write((short)channels);
                w.Write(sampleRate); w.Write(byteRate);
                w.Write((short)blockAlign); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(dataLen);
                foreach (var s in pcm) w.Write(s);
            }
            return wav;
        }

        /// <summary>16-bit PCM WAV 바이트 → AudioClip.
        /// 주의: 표준 44바이트 헤더를 가정한다. 서버 wav에 추가 청크가 있으면
        /// 'data' 청크 오프셋 탐색으로 개선할 것(TODO).</summary>
        public static AudioClip ToAudioClip(byte[] wav, string name = "tts")
        {
            int channels = BitConverter.ToInt16(wav, 22);
            int sampleRate = BitConverter.ToInt32(wav, 24);
            int dataOffset = 44;
            int dataLen = BitConverter.ToInt32(wav, 40);
            int sampleCount = dataLen / 2;

            float[] f = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
                f[i] = BitConverter.ToInt16(wav, dataOffset + i * 2) / 32768f;

            int frames = Mathf.Max(1, sampleCount / Mathf.Max(1, channels));
            AudioClip clip = AudioClip.Create(name, frames, Mathf.Max(1, channels), sampleRate, false);
            clip.SetData(f, 0);
            return clip;
        }
    }
}
