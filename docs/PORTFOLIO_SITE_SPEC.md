# MY LITTLE GRAND PRIX — portfolio site specification

This specification translates `PORTFOLIO_PROJECT_ANALYSIS.md` into a public technical case study. The analysis document remains authoritative if the two ever disagree.

## 1. Product goal

Build a focused, credible case study for recruiters and technical peers that answers four questions quickly:

1. What experience was being built?
2. What systems actually exist in the repository?
3. Which engineering decisions are interesting and defensible?
4. What remains experimental, disconnected, or unverified?

This is not a marketing landing page and should not imply product completion. The tone is an engineering postmortem for an in-development prototype.

## 2. Audience and reading modes

- **30-second scan:** hero, one-sentence definition, status rail, three engineering challenges.
- **3-minute review:** pipeline, room presentation, core system ledger, architecture diagram.
- **Deep review:** evidence notes, contribution verification, limitations, source repository.

Primary language is Korean. Short English system labels keep the visual voice concise and make the page useful in an international portfolio review.

## 3. Core message hierarchy

### Project name

**MY LITTLE GRAND PRIX**

Internal repository: `F1_XR_Visualizer`

### Tagline

**THE RACE. IN YOUR SPACE.**

### One-sentence definition

> 외부 서비스가 준비한 과거 F1 데이터셋을 하나의 시간축에서 다중 차량 리플레이로 재구성하고, 선택한 이벤트를 실제 방의 Hero 지점에 MR 디오라마로 다시 배치하는 Unity XR 프로젝트.

### Truth boundary shown near the top

> 저장소 정적 감사 기준의 개발 중 기술 사례입니다. Quest 실기기 성능과 백엔드의 OpenF1 데이터 출처는 아직 공개 검증되지 않았습니다.

## 4. Information architecture

### 00 — Hero

- MY LITTLE GRAND PRIX title.
- Tagline and one-sentence definition.
- Status rail: `In development`, `Unity 6000.4.11f1`, `Meta Quest target`, `Repository-audited copy`.
- CTAs: “시스템 보기”, “소스 보기”.

### 01 — Project thesis

- Problem: flat replay interfaces show the race but do not use the viewer's room.
- Approach: keep the race clock/source state separate from a reversible room presentation.
- Why MR: the room influences wall selection, focus, scale, and orientation.
- Explicit correction: Entry/Exit establish layout context, but the current default cars are Hero-centered rather than guaranteed to cross both walls.

### 02 — Runtime pipeline

Six ordered steps:

1. Companion REST dataset request.
2. Manifest polling and ready-chunk loading.
3. Time-sorted/deduplicated normalized source copy and separately stabilized playback samples.
4. Shared source-time multi-driver replay.
5. Entry/Exit and Hero setup from AR planes, using a containing-floor boundary for automatic setup when available and manual wall selection otherwise.
6. Event-local two-car stage rigidly fitted around Hero.

Footnote: Unity does not call OpenF1 directly in this repository. Use “OpenF1-derived” only after the backend provenance is confirmed.

### 03 — Demo evidence

- One large 16:9 hero demo slot.
- Four smaller slots: room detection, Hero/layout setup, vehicle replay, overtake presentation.
- Missing media stays visibly labeled; no fake screenshot, copyrighted broadcast frame, or generated image presented as product proof.
- Link to `portfolio/public/assets/README.md` in source documentation, not on the public page.

### 04 — Core system ledger

Editorial rows rather than generic cards. Every row carries a status:

- Dataset streaming — verified implementation.
- Shared replay clock — verified implementation.
- Room layout — verified implementation.
- Event-local two-car diorama — verified implementation.
- Logical / visual transform split — verified implementation.
- Overtake / pit reconstructions — experimental.
- Curated collision forensics — experimental and working-tree review required.
- Entry/Hero/Exit path preview — disconnected prototype.
- Room-shell MR→VR — alternate-scene experiment.

### 05 — Architecture

Responsive inline SVG with these exact conceptual layers:

```text
External F1 dataset service
  → catalog / dataset / manifest / chunks
  → source + processed sample stores
  → shared ReplayTimeline
  → main replay cars
  → EventPopoutReplay
  → event-local source path + two drivers
  → Hero-centered RoomDiorama presentation

AR planes
  → WallDiscovery
  → Entry / Exit / Hero layout
  → frozen room frames
  → RoomDiorama scale / orientation context
```

Do not draw `ShowcasePathPreview` as the vehicle mapper or depict a connected dual-wall portal.

### 06 — Three engineering challenges

1. Chunked data with one coherent race clock.
2. Stable layout over changing plane IDs.
3. Reversible spatial presentation without mutating replay truth.

Each challenge uses Problem → Decision → Trade-off copy, including limitations.

### 07 — Entry / Hero / Exit

- Entry: inward orientation from the first wall.
- Exit: outward orientation from a different wall.
- Hero: stored spatial pose and viewing-focus reference for the current default diorama; do not imply an `ARAnchor` component.
- Honest implementation note: the disconnected preview explores an Entry→Hero→Exit route, but the production RoomDiorama currently preserves source-track shape around Hero instead.

### 08 — Engineering constants, not benchmarks

- One shared replay timebase.
- Two distinct walls required for a valid standard layout.
- First two distinct event drivers mapped into the general room stage.
- Two-second serialized wall reacquisition grace period.

All four are labeled “implementation constants.” Do not use the inactive three-car LOD budget, 61 preview points, or any FPS claim as a headline metric.

### 09 — Technology and status

- Unity `6000.4.11f1`
- C#
- URP `17.4.0`
- OpenXR `1.16.1`
- Meta OpenXR `2.5.1`
- XR Interaction Toolkit `3.4.1`
- AR Foundation `6.5.0` (transitive)
- REST / JSON
- Android ARM64 / IL2CPP
- Vite static case-study site

Status note: Quest 3 is a configured target; a reproducible device validation record is still planned.

### 10 — My contribution

This section must remain visible. Because the audit cannot infer personal ownership from a multi-contributor repository, display:

> **기여 범위 확인 필요.** 이 저장소에는 여러 기여자가 있습니다. 공개 전에 본인의 역할, 담당 시스템, 팀 규모, 작업 기간과 근거 링크를 입력하세요.

Do not fill this with a guessed role. Do not say “I built” elsewhere on the page until this panel is verified.

### 11 — Evidence and next work

Compact disclosure rows:

- Verified by code + default scene wiring.
- Experimental / alternate scene.
- Not validated on device.
- Public claim exclusions.

Finish with the three highest-impact improvements from the analysis document.

### 12 — CTA and disclaimer

- Source repository link.
- Main portfolio home link.
- Back-to-top link.
- Independent technical study disclaimer.

## 5. Visual direction

### Character

- Dark editorial layout inspired by motorsport timing sheets and engineering notebooks.
- Near-black background, warm off-white type, one restrained red signal color.
- Condensed system heading stack; no network font dependency.
- Thin borders, registration lines, large project typography, asymmetric track-like diagonals.
- Square edges; no glassmorphism, neon bloom, faux telemetry dashboard, or excessive card grid.

### Color tokens

| Token | Value | Use |
| --- | --- | --- |
| Paper | `#080A0C` | Primary background |
| Raised paper | `#0F1215` | Media and diagram fields |
| Ink | `#F1EEE7` | Primary text |
| Soft ink | `#AAA8A2` | Body copy |
| Line | `#292E32` | Dividers |
| Signal | `#E5322D` | CTA, index, racing accent |
| Verified | `#9FC4A7` | Status only |
| Experimental | `#D6B36D` | Status only |
| Planned | `#8CA5C0` | Status only |

### Layout

- Maximum content width: 1240 px.
- Desktop sections use a sticky index column and main editorial column.
- Tablet collapses the index to an inline label.
- Mobile is one column, preserves reading order, and avoids hidden information.

### Motion

- Optional small opacity/translate reveals only.
- Content is visible without JavaScript.
- `prefers-reduced-motion` removes animation and smooth scrolling.

## 6. Accessibility requirements

- Semantic landmarks: `header`, `nav`, `main`, labeled `section`, `footer`.
- One `h1`; sequential `h2`/`h3` hierarchy.
- Skip link and visible focus outlines.
- Minimum 44 px practical hit targets for CTAs.
- No color-only status: each status includes text.
- SVG uses a title/description and readable fallback caption.
- Placeholder media includes a text description and never relies on decorative lines to convey meaning.
- Respect reduced motion.
- No autoplay audio; future demo video must be muted, inline, controlled, and captioned if it contains meaningful speech.

## 7. Performance requirements

- Static single-page output; no runtime framework.
- No remote fonts, analytics, trackers, embeds, or third-party scripts.
- One CSS file and a minimal JavaScript file.
- Product media must be WebP/AVIF or compressed H.264 and use explicit dimensions.
- Do not deploy the 102 MB aerial source image.
- Target a sub-100 KB initial authored HTML/CSS/JS bundle before media.
- Keep source maps disabled in production.

## 8. SEO and metadata

### Title

`MY LITTLE GRAND PRIX — Unity MR F1 Replay Case Study`

### Description

`과거 F1 데이터셋을 동기화된 다중 차량 리플레이와 방 크기 MR 디오라마로 재구성한 Unity/OpenXR 기술 사례.`

### Required metadata

- canonical URL injected from `VITE_SITE_URL`;
- Open Graph title, description, type, URL, locale;
- Twitter summary-large-image metadata;
- theme color;
- JSON-LD `CreativeWork` with `isAccessibleForFree`, `inLanguage`, and technology keywords;
- `og-image.webp` reference documented as a missing replacement asset until a rights-safe image is supplied.

If the OG image is absent at publication time, remove image metadata rather than publish a broken URL.

## 9. Deployment architecture

The audited source snapshot lives in this Unity repository's isolated `portfolio/` Vite project, but the only deployed source lives in the personal `YunDonggeurami/Yundonggeurami.github.io` repository under `f1/`. The Unity project is not built or modified by the web workflow.

The personal repository's existing Pages workflow:

1. Runs on personal-repository `main` or manual dispatch.
2. Uses Node 24 and lockfile-based installs for both the root portfolio and `f1/` child.
3. Lints and builds the root portfolio at `/`.
4. Builds the MLGP child with `VITE_BASE_PATH=/f1/` and `VITE_SITE_URL=https://yundonggeurami.github.io/f1/`, then runs its static verifier.
5. Copies `f1/dist` into root `dist/f1` and confirms both entry pages exist.
6. Uploads and deploys one combined artifact to the protected `github-pages` environment.

No Pages workflow belongs in the team Unity repository. The final case-study URL is `https://yundonggeurami.github.io/f1/`.

## 10. Main portfolio integration

The existing `https://yundonggeurami.github.io/` site has a planned “Main Project” slot. Replace that slot with a compact project entry:

- Title: `MY LITTLE GRAND PRIX`
- Type: `Unity / Meta Quest 3 / Mixed Reality`
- Summary: `과거 F1 리플레이를 실제 방의 MR 디오라마로 재구성한 공간 컴퓨팅 프로젝트.`
- Status: `Technical case study · In development`
- Link: `/f1/` in the same personal Pages site
- Thumbnail: `demo-poster.webp` only after a real, rights-cleared capture exists

The team repository intentionally has no portfolio deployment workflow. See `docs/MAIN_PORTFOLIO_INTEGRATION.md` for the single-artifact personal-repository integration.

## 11. Publication gate

Before presenting the page as application material:

- replace or intentionally retain every media slot;
- verify the contribution panel;
- confirm the backend/OpenF1 provenance or keep the current generic dataset wording;
- run the Editor tests and save results if “tested” is claimed;
- complete a documented Quest 3 device pass before claiming device validation;
- resolve tracked personal paths, cloud IDs, telemetry GUID, release identity, and media rights;
- remove the stale missing Build Settings scene;
- perform the public-claim verification checklist in the analysis document again against the release commit.
