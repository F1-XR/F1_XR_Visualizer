# Validation Rules

Read this file only after making an implementation change or when asking the user to verify Unity behavior.

## Proportional Validation

- Match validation to the changed behavior.
- Do not launch Unity or enter Play Mode for documentation, analysis, or read-only tasks.
- For C# changes, check compilation when Unity is available.
- Use Play Mode once in the relevant scene only when runtime behavior changed.
- Repeat Play Mode only after another change or when new evidence justifies it.
- Require a Quest headset check only for behavior that cannot be validated adequately in the Editor.
- Review only relevant new Console errors and warnings; do not dump the full log.
- Never claim a check was performed when it was not.

## Completion Report

Report only applicable items:

- files changed and why
- hierarchy or serialized-reference changes
- commands or checks actually run
- relevant errors or warnings
- remaining manual Editor or headset checks
- known limitations or postponed work

## User Verification Checklist

When user verification is needed, give a short checklist containing only:

- exact scene and whether Play Mode must restart
- relevant Hierarchy path, component, and field
- user action sequence
- expected success result
- specific Console message to watch for, if useful

Ask for a screenshot only when it materially helps the next diagnosis.

For engine-audio work, additionally check only the relevant audio source clip, volume, pitch, loop, mute, spatial blend, playback gating, distance limits, and active-car limits.

## Efficient Investigation and Validation

### Investigation

* 관련 파일, 타입, 메서드와 필요한 줄 범위만 확인한다.
* 전체 파일, 전체 저장소 diff, 전체 Console 로그를 출력하지 않는다.
* 이미 확인한 정보는 전제가 바뀌지 않는 한 반복 조사하지 않는다.
* 변경 전에 가능한 원인을 좁히고 여러 해결안을 동시에 누적하지 않는다.
* API 확인은 실제 변경에서 사용할 API와 관련 패키지 범위로 제한한다.

### Unity execution order

런타임 변경은 원칙적으로 다음 순서로 처리한다.

1. Play Mode를 종료한다.
2. 관련 코드와 에셋을 수정한다.
3. Unity 자동 컴파일과 자동 리로드를 기다린다.
4. 서버, 데이터와 필수 참조의 준비 상태를 확인한다.
5. 한 Play 세션에서 가능한 한 Open → 핵심 진단 → Pause/Resume 또는 Seek → Close를 통합 검증한다.
6. Play Mode를 종료한 뒤 변경 파일과 의도하지 않은 자동 변경을 확인한다.

명확한 필요가 없는 한 강제 `AssetDatabase.Refresh`, `ForceUpdate`, 강제 컴파일 또는 동일한 MCP 명령 재시도를 사용하지 않는다.

### Additional validation

Play Mode 실행 횟수를 고정 상한으로 사용하지 않는다.

추가 실행이나 수정이 허용되는 경우:

* 첫 검증에서 수정 범위와 직접 관련된 작은 결함이 발견됨
* 원인이 명확하고 기존 해결 방향을 바꾸지 않음
* 추가 실행으로 성공 여부를 바로 판별할 수 있음

자동으로 범위를 확대하지 않는 경우:

* 예상하지 못한 다른 서브시스템을 수정해야 함
* 첫 원인 가설이 틀려 설계를 다시 해야 함
* Quest 전용 동작을 검증하기 위해 Editor용 임시 시스템을 만들어야 함
* 새로운 대규모 진단 코드나 검증 도구가 필요함
* 같은 타임아웃, 도메인 리로드 또는 도구 실패가 반복됨
* 수정 대상보다 검증 환경을 고치는 작업이 더 커짐

이 경우 현재까지 완료한 변경, 실패 원인, 검증된 항목, 미검증 위험과 다음 최소 작업을 보고한다.

### Editor and Quest boundary

Editor에서는 다음을 우선 검증한다.

* 컴파일
* 데이터와 Transform 권위
* 생성 객체 수와 계층
* 경로와 진행도 일치
* Pause, Resume, Seek의 상태 일관성
* Close cleanup
* 관련된 신규 오류와 경고

다음 항목은 Quest에서만 판단한다.

* 실제 공간에서의 크기 체감
* 벽과 차량의 공간적 관계
* 시야와 가독성
* MR 앵커 및 실제 방 스캔 동작
* 사용자 위치에서 보이는 연출 완성도

Quest 전용 항목을 증명하기 위해 Editor에 가짜 공간 배치나 임시 MR 시스템을 추가하지 않는다.

### Git and generated changes

검증 후 다음 변경이 의도하지 않게 포함되지 않았는지 확인한다.

* TMP 동적 폰트 아틀라스
* 관련 없는 씬 또는 프리팹 재직렬화
* 자동 생성 파일
* 캐시 또는 로그
* 작업 범위 밖 설정과 패키지 파일

최종 보고는 원인, 실제 변경 파일, 수행한 검증, Quest 확인 사항과 미검증 위험만 포함한다.
