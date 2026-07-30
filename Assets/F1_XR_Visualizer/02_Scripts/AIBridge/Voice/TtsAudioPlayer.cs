// AIBridge/Voice/TtsAudioPlayer.cs
// 수신한 tts_audio(base64 wav)를 디코드해 재생.
// 필요 패키지 없음이나 런타임 배선의 일부라 AIBRIDGE_READY 로 함께 관리.
#if AIBRIDGE_READY
using UnityEngine;

namespace F1XR.AIBridge.Voice
{
    [RequireComponent(typeof(AudioSource))]
    public class TtsAudioPlayer : MonoBehaviour
    {
        AudioSource _src;
        void Awake() { _src = GetComponent<AudioSource>(); }

        /// <summary>base64 인코딩된 wav → AudioClip → 재생.
        /// interrupt=true(답변): 이전 음성 끊고 최신 재생.
        /// interrupt=false(능동 안내): 이미 재생 중이면 끼어들지 않고 건너뜀(답변 보호).</summary>
        public void Play(string base64Wav, bool interrupt = true)
        {
            if (string.IsNullOrEmpty(base64Wav)) return;
            if (!interrupt && _src.isPlaying) return;   // 능동 안내는 재생 중 답변을 안 끊음
            byte[] wav = System.Convert.FromBase64String(base64Wav);
            AudioClip clip = WavUtil.ToAudioClip(wav, "tts_reply");
            _src.Stop();
            _src.clip = clip;
            _src.Play();
        }
    }
}
#endif
