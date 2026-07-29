// AIBridge/Protocol/AgentMessages.cs
// F1_XR_AI 의 app/ws/protocol.py 와 필드명을 1:1로 맞춘 메시지 DTO.
// ⚠️ protocol.py 를 바꾸면 이 파일도 반드시 같이 바꾼다. (공유 계약)
//
// 이 파일은 외부 패키지 의존이 없어 단독으로 컴파일된다.
// 실제 송수신(직렬화)은 Net/AgentWebSocketClient 에서 처리한다.
//   - 전송(Unity→AI): Newtonsoft(JsonConvert) + NullValueHandling.Ignore.
//     (JsonUtility 는 null 문자열을 ""로, 미설정 int를 0으로 내보내 protocol의 int|null·string|null 과
//      어긋난다. session_key 를 int? 로 두고 null 은 생략해 서버가 기본 세션으로 폴백하게 한다.)
//   - 수신(AI→Unity): 먼저 Envelope 로 type 만 읽고 타입별로 재파싱.
//     command.args 는 name 마다 형이 달라 Newtonsoft(Json.NET) 사용을 권장.

using System;

namespace F1XR.AIBridge.Protocol
{
    /// <summary>메시지 type 상수 (protocol.py 의 type 필드와 일치).</summary>
    public static class MsgType
    {
        // Unity → AI
        public const string Utterance = "utterance";
        public const string AudioUtterance = "audio_utterance";
        // AI → Unity
        public const string Transcript = "transcript";
        public const string Command = "command";
        public const string AssistantText = "assistant_text";
        public const string TtsAudio = "tts_audio";
    }

    // ───────── Unity → AI (전송용) ─────────

    /// <summary>텍스트 발화(디버그·키보드 입력).</summary>
    [Serializable]
    public class UtteranceMsg
    {
        public string type = MsgType.Utterance;
        public string text;
        public int? session_key;    // 지금 보는 경기 ID (매 발화에 넣기). null이면 서버 기본 세션
        public string at_time;      // 리플레이 현재 시각(ISO), 없으면 null
    }

    /// <summary>음성 발화 — base64 인코딩한 wav(헤더 포함).</summary>
    [Serializable]
    public class AudioUtteranceMsg
    {
        public string type = MsgType.AudioUtterance;
        public string data;         // base64 wav
        public int? session_key;    // null이면 서버 기본 세션
        public string at_time;
    }

    // ───────── AI → Unity (수신용) ─────────

    /// <summary>수신 1차 파싱용 — type(+명령이면 name)만 읽는다.</summary>
    [Serializable]
    public class Envelope
    {
        public string type;
        public string name;         // type=="command" 일 때만 채워짐
    }

    [Serializable]
    public class TranscriptMsg { public string type; public string text; }

    [Serializable]
    public class AssistantTextMsg { public string type; public string text; }

    [Serializable]
    public class TtsAudioMsg { public string type; public string format; public string data; } // data=base64 wav

    // ── command 의 args (name 별 전용 클래스) ──
    // 참고: JsonUtility 는 동적 dict·다형(value가 숫자/문자열) 처리가 약하다.
    //       Dispatcher 에서 Newtonsoft(JObject)로 파싱하는 것을 권장하며,
    //       아래 클래스들은 형 계약을 명시하는 참조용이다.
    [Serializable] public class LoadSessionArgs { public int session_key; }
    [Serializable] public class HighlightDriverArgs { public int driver_number; }

    /// <summary>
    /// controlReplay 인자. action ∈ {play, pause, speed, seek}.
    /// value: speed→숫자, seek→숫자(상대시간) 또는 ISO 절대시각 문자열(jump_to_event 발).
    /// 다형이라 원본 문자열로 받아 Handler 에서 형을 판별한다.
    /// </summary>
    [Serializable] public class ControlReplayArgs { public string action; public string value; }
}
