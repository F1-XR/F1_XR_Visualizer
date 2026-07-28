# Replay and MR Rules

Read this file only when the task changes replay flow, OpenF1 data use, spatial presentation, or mixed-reality placement.

## Architecture

- Inspect the relevant active path before creating a new system.
- Extend the existing replay and OpenF1 flow. Do not create parallel managers, duplicate pipelines, or duplicate overtake processing.
- Split code by concrete role: API access and DTOs, replay flow and samples, UI state and input, and small pure utilities.
- Treat legacy, duplicate, experimental, or inactive code as unavailable unless the user explicitly asks to use it.
- Reuse one presentation layer across table, floor, and wall modes when practical.

## Replay Data

- Keep logical replay data separate from presentation-only transforms.
- OpenF1 positions, timing, event order, and logical car state remain authoritative.
- Never write room placement, scale, cinematic offsets, portals, or path mapping back into logical replay data.
- Ensure presentation offsets do not accumulate after restart, seek, reuse, or cleanup.
- Reuse existing replay timing instead of introducing an unrelated timer.
- Preserve existing replay behavior as a fallback unless removal is explicitly requested.
- Do not invent telemetry. Describe reconstructed lateral overtake movement as presentation reconstruction.

## Mixed Reality

- Verify installed package versions before using version-specific APIs. Do not assume MRUK is installed.
- Use real detected scene anchors for walls and tables rather than fixed world positions.
- Manual selection and tunable offsets may be used, but keep them relative to detected anchors.
- Do not implement arbitrary-room automatic placement unless explicitly requested.
- Do not scan the whole room every frame.
- Subscribe and unsubscribe from room lifecycle events safely.

## Data and Backend

- Do not change the Python/FastAPI backend unless explicitly requested.
- Do not change OpenF1 requests, caches, DTOs, schemas, JSON, or dataset formats unless explicitly requested.

## Before modifying replay presentation

리플레이 또는 MR 경로를 수정하기 전에 다음 권위를 먼저 확인한다.

* 차량의 longitudinal progress와 replay time을 결정하는 시스템
* 차량의 position, rotation과 scale을 최종으로 쓰는 시스템
* source path와 차량 진행도가 사용하는 replay window

같은 Transform 또는 진행도를 여러 시스템이 동시에 갱신하지 않도록 하며, source path와 차량 진행도는 동일한 시간 구간을 기준으로 해야 한다.
