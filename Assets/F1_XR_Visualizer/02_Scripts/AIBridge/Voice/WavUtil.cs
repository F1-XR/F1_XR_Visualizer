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

        /// <summary>WAV 바이트 → AudioClip.
        /// 헤더가 항상 44바이트라는 보장이 없어(서버 wav엔 fmt 확장·fact·LIST 등 추가 청크가 붙는다)
        /// 청크를 실제로 순회해 'fmt '와 'data'를 찾는다. 16-bit PCM뿐 아니라 IEEE float32,
        /// 24/32-bit PCM도 처리한다(MeloTTS/libsndfile 출력이 float거나 헤더가 44B가 아닐 때 대비).</summary>
        public static AudioClip ToAudioClip(byte[] wav, string name = "tts")
        {
            if (wav == null || wav.Length < 44)
                throw new ArgumentException("WAV 데이터가 너무 짧습니다");

            int fmtFormat = 1;    // 1=PCM, 3=IEEE float
            int channels = 1;
            int sampleRate = 44100;
            int bits = 16;
            int dataOffset = -1;
            int dataLen = 0;

            // 'RIFF'(4)+size(4)+'WAVE'(4) 다음(12)부터 청크 순회.
            int p = 12;
            while (p + 8 <= wav.Length)
            {
                string id = new string(new[] { (char)wav[p], (char)wav[p + 1], (char)wav[p + 2], (char)wav[p + 3] });
                int size = BitConverter.ToInt32(wav, p + 4);
                int body = p + 8;
                if (id == "fmt " && body + 16 <= wav.Length)
                {
                    fmtFormat = BitConverter.ToInt16(wav, body);
                    channels = BitConverter.ToInt16(wav, body + 2);
                    sampleRate = BitConverter.ToInt32(wav, body + 4);
                    bits = BitConverter.ToInt16(wav, body + 14);
                }
                else if (id == "data")
                {
                    dataOffset = body;
                    dataLen = size;
                    break;   // data를 찾으면 끝(뒤 청크는 무시)
                }
                p = body + size + (size & 1);   // 청크는 2바이트 정렬 → 홀수면 패딩 1B
            }

            if (dataOffset < 0)
                throw new ArgumentException("WAV에 data 청크가 없습니다");
            // 헤더의 data 크기가 실제 남은 바이트보다 크면 잘라 안전하게(부정확한 size 필드 방지)
            dataLen = Mathf.Min(dataLen, wav.Length - dataOffset);

            int bytesPerSample = Mathf.Max(1, bits / 8);
            int totalSamples = dataLen / bytesPerSample;   // 전 채널 합친 샘플 수
            float[] f = new float[totalSamples];

            if (fmtFormat == 3 && bits == 32)              // IEEE float32
                for (int i = 0; i < totalSamples; i++)
                    f[i] = BitConverter.ToSingle(wav, dataOffset + i * 4);
            else if (bits == 16)                            // PCM 16
                for (int i = 0; i < totalSamples; i++)
                    f[i] = BitConverter.ToInt16(wav, dataOffset + i * 2) / 32768f;
            else if (bits == 32)                            // PCM 32
                for (int i = 0; i < totalSamples; i++)
                    f[i] = BitConverter.ToInt32(wav, dataOffset + i * 4) / 2147483648f;
            else if (bits == 24)                            // PCM 24
                for (int i = 0; i < totalSamples; i++)
                {
                    int o = dataOffset + i * 3;
                    int v = (wav[o + 2] << 16) | (wav[o + 1] << 8) | wav[o];
                    if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);  // 부호 확장
                    f[i] = v / 8388608f;
                }
            else if (bits == 8)                             // PCM 8 (unsigned)
                for (int i = 0; i < totalSamples; i++)
                    f[i] = (wav[dataOffset + i] - 128) / 128f;
            else
                throw new ArgumentException($"지원하지 않는 WAV 형식: format={fmtFormat}, bits={bits}");

            channels = Mathf.Max(1, channels);
            int frames = Mathf.Max(1, totalSamples / channels);
            // SetData는 길이가 frames*channels와 정확히 맞아야 함 → 나머지 버림
            if (f.Length != frames * channels)
                Array.Resize(ref f, frames * channels);

            AudioClip clip = AudioClip.Create(name, frames, channels, Mathf.Max(1, sampleRate), false);
            clip.SetData(f, 0);
            return clip;
        }
    }
}
