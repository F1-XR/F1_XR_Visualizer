# AGENTS.md

## Project

- Unity XR project for visualizing F1 replay data.
- Main stack: Unity, C#, XR Interaction Toolkit, uGUI, and REST APIs.
- Main assets: `Assets/F1_XR_Visualizer`.

## Always-On Rules

- Follow the user's explicit scope. Do not continue into another phase or add cleanup, refactoring, or polish unasked.
- Inspect only the files relevant to the request before editing.
- Prefer the smallest reversible change and preserve nearby style.
- Do not rewrite unrelated code, change existing data formats, or add dependencies without need.
- Edit files only when the user clearly asks. For guidance requests, give exact file names and paste-ready changes.
- Ask only when missing information could cause irreversible or materially wrong changes. Otherwise use the safest reasonable assumption and state it in the final report.; otherwise state reasonable assumptions.
- Never expose or commit secrets, credentials, tokens, or sensitive log values.
- Do not weaken authentication, authorization, validation, or error handling.
- Do not modify lock files, generated files, vendor code, migrations, deployment settings, or public API contracts unless explicitly required.
- Keep responses concise and preferably in Korean.
- State what was actually validated and what remains unverified.

## Conditional Rules

Read only the document that matches the current task. Do not load all detailed documents by default.

- Replay, OpenF1, spatial presentation, or MR placement: `.agents/replay-mr.md`
- C#, scenes, prefabs, serialization, packages, or platform settings: `.agents/unity-assets.md`
- Validation after implementation or Unity verification instructions: `.agents/validation.md`
- Notion document work: `.agents/notion.md`

When several categories genuinely apply, read only those matching documents.

## Execution Efficiency

* 필요한 구현과 검증은 생략하지 않되 반복 조사, 대량 출력, 동일한 재시도와 자동적인 범위 확장을 피한다.
* 먼저 핵심 결함, 변경하지 않을 영역, Editor 검증 범위, Quest 검증 범위를 구분한다.
* 예상하지 못한 다른 시스템 수정, 새로운 설계, 검증 도구 개발 또는 반복적인 도구 실패가 필요해지면 작업을 중단하고, 현재까지의 결과와 원인, 남은 위험, 다음 최소 작업을 보고한다. 사용자의 추가 요청 없이 범위를 확대하거나 다른 해결안을 계속 시도하지 않는다.
* 상세한 Unity 실행 및 검증 절차는 `.agents/validation.md`를 따른다.
