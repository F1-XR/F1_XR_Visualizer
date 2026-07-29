# AIBridge — F1 튜토리얼 AI ↔ Unity 브릿지 (클라이언트)

F1_XR_AI(에이전트 서버 :8001)와 WebSocket으로 통신해, 음성 발화를 보내고
명령·자막·답변음성을 받아 실행하는 **Unity 클라이언트**입니다.

- 계약(프로토콜) 원본: `F1_XR_AI/app/ws/protocol.py` + `F1_XR_AI/docs/UNITY_BRIDGE.md`
- ⚠️ 원본 필드명이 바뀌면 `Protocol/AgentMessages.cs`도 **동시에** 갱신.

---

## 브랜치
- 이 작업은 `ai-voice-bridge` 브랜치에서 진행 → 테스트 후 `develop`으로 머지.

## 패키지 설치 + 활성화 (3단계)
런타임 스크립트(Net/Voice/Commands/AgentBridge)는 `#if AIBRIDGE_READY`로 감싸져 있어,
아래 3단계를 마치기 전엔 **비활성(빈 파일)** 이라 컴파일이 안 깨진다. 3단계 후 한 번에 켜진다.

**1) NativeWebSocket 설치** (WebSocket 클라이언트)
- Unity 상단 메뉴 **Window → Package Manager**
- 좌상단 **`+` → Add package from git URL…**
- 입력: `https://github.com/endel/NativeWebSocket.git#upm` → Add

**2) Newtonsoft Json.NET 설치** (동적 args 파싱)
- 같은 Package Manager에서 **`+` → Add package by name…**
- 입력: `com.unity.nuget.newtonsoft-json` → Add
  (또는 좌상단 드롭다운을 **Unity Registry**로 바꾸고 "Newtonsoft Json" 검색 → Install)

**3) 스크립팅 심볼 AIBRIDGE_READY 추가** (가드 해제 = 코드 켜기)
- **Edit → Project Settings → Player**
- **Other Settings → Script Compilation → Scripting Define Symbols**
- `AIBRIDGE_READY` 추가하고 **Apply** (Android/Quest 플랫폼 탭에서도 동일하게)

→ 3단계까지 하면 AIBridge 전체가 컴파일된다. 순서가 중요: **패키지 먼저(1,2) → 심볼(3)**.
   (심볼만 켜고 패키지가 없으면 그때 컴파일 에러가 난다.)

---

## 폴더 구조와 역할
```
AIBridge/
├─ Protocol/AgentMessages.cs      메시지 DTO (protocol.py와 1:1)
├─ Net/
│   ├─ AgentWebSocketClient.cs    :8001/ws 연결·JSON 송수신·재연결
│   └─ AgentConnectionConfig.cs   서버 URL 등 설정(ScriptableObject)
├─ Voice/
│   ├─ MicRecorder.cs             마이크 캡처 → wav → audio_utterance 전송
│   ├─ WavUtil.cs                 AudioClip ↔ wav(PCM16) 변환
│   └─ TtsAudioPlayer.cs          tts_audio(base64 wav) → AudioSource 재생
├─ Commands/
│   ├─ AgentCommandDispatcher.cs  command.name → 해당 Handler 호출
│   └─ Handlers/                  명령별 실행부(기존 시스템에 연결)
└─ AgentBridge.cs                 진입점: 연결·수신 라우팅·컴포넌트 배선
```

## 수신 메시지 처리 순서
한 발화에 대해 서버는 보통: `transcript?` → `command*` → `assistant_text` → `tts_audio?`
- 먼저 `type`만 읽어 분기(Envelope) → 타입별로 다시 파싱.
- `command`는 `name`을 보고 Dispatcher가 Handler로 넘김.

## 기존 시스템 연결점 (팀원 확인 필요 — 실제 메서드명 매핑)
이 레포에 이미 있는 리플레이/차량 시스템에 명령을 연결한다:

| 명령 | 연결 후보(기존 스크립트) | 비고 |
|---|---|---|
| `loadSession {session_key}` | `RestAPI/Replay/Startup/ReplayLoad`, `SessionSelect/UI/ReplayLoadUI`, `ReplayManifestPoller` | 그 세션 리플레이 로드 |
| `highlightDriver {driver_number}` | `RestAPI/Replay/Car/ReplayCarView`, `ReplayCarInteractable`, `DriverRoster` | 번호로 차 찾아 강조 UI |
| `controlReplay {action,value}` | `RestAPI/UI/Replay/ReplayBar`, 리플레이 재생부 | play/pause/speed/seek |

> 원칙: 브릿지는 **UI 버튼이 부르는 기존 함수를 그대로 호출**한다(중복 구현 금지).

## controlReplay.value 주의
- `speed` → 숫자(0.5, 2.0)
- `seek` → **숫자(상대시간) 또는 ISO 절대시각 문자열**(jump_to_event 발) → Handler에서 형 판별 후 분기.

## 하이라이트 수명(합의됨)
- 새 `highlightDriver`가 오면 **이전 강조 대체**, `loadSession`이면 **자동 해제**,
  `controlReplay`(재생/정지/속도)는 **유지**. 별도 해제 명령은 두지 않음(추후 필요 시 clearHighlight 추가).

## 구현 순서 추천
1. Net(WS) + 텍스트 왕복(utterance→assistant_text) — 연결 검증
2. Commands(highlightDriver 하나) — 명령이 화면에 먹는지
3. Voice(TtsAudioPlayer) — 답변음성 재생
4. Voice(MicRecorder) — 마이크 입력(권한·포맷 까다로움, 마지막)

## 서버 검증 도구
Unity 붙이기 전, 서버가 정상인지: `F1_XR_AI/scripts/ws_client_test.py`로 왕복 확인.
