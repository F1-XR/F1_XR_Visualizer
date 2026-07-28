// AIBridge/ReplayTimeMap.cs
// 리플레이 상대초(float) ↔ 절대시각(ISO) 변환.
//
// 원리:
//   매니페스트의 race_control 이벤트(RaceControlEventDto)는 t(상대초)와 date(절대 ISO)를
//   '둘 다' 가진다. 이 한 쌍으로 "리플레이 t=0의 절대 epoch(baseEpoch)"를 구하면,
//   임의의 시각을 양방향으로 변환할 수 있다.
//     baseEpoch = date_epoch - t
//     상대→절대: epoch = baseEpoch + relative
//     절대→상대: relative = target_epoch - baseEpoch
//
// 용도:
//   - jump_to_event: AI가 준 ISO 절대시각 → 상대초 → ReplayPlayer.Seek
//   - at_time:      현재 상대초(CurrentTime) → ISO 절대시각 → 발화에 첨부(스포일러 방지)
#if AIBRIDGE_READY
using System;
using System.Globalization;
using F1XR.RestAPI.Replay;   // ReplayPlayer
using F1XR.RestAPI.Api;      // RaceControlEventDto

namespace F1XR.AIBridge
{
    public static class ReplayTimeMap
    {
        // ISO 문자열 → Unix epoch(초). OpenF1 date 형식(…+00:00, 마이크로초) 파싱.
        static bool TryParseIso(string iso, out double epochSeconds)
        {
            epochSeconds = 0;
            if (string.IsNullOrEmpty(iso)) return false;
            if (DateTimeOffset.TryParse(
                    iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset dto))
            {
                epochSeconds = dto.ToUnixTimeMilliseconds() / 1000.0;
                return true;
            }
            return false;
        }

        // flag 배열에서 (t, date) 앵커 하나를 찾아 baseEpoch(=t=0의 절대초) 계산
        static bool TryAnchor(RaceControlEventDto[] events, out double baseEpoch)
        {
            baseEpoch = 0;
            if (events == null) return false;
            foreach (RaceControlEventDto e in events)
            {
                if (e != null && TryParseIso(e.date, out double epoch))
                {
                    baseEpoch = epoch - e.t;
                    return true;
                }
            }
            return false;
        }

        static bool TryGetBaseEpoch(ReplayPlayer player, out double baseEpoch)
        {
            baseEpoch = 0;
            if (player == null || !player.HasDataset) return false;
            // ① 서버가 준 baseDate(정확) 우선
            DatasetManifestDto manifest = player.Manifest;
            if (manifest != null && TryParseIso(manifest.baseDate, out baseEpoch))
                return true;
            // ② 폴백: 깃발(t+date) 앵커로 추정 (구버전 서버·baseDate 없을 때)
            return TryAnchor(player.YellowFlags, out baseEpoch)
                || TryAnchor(player.RedFlags, out baseEpoch);
        }

        /// <summary>ISO 절대시각 → 리플레이 상대초. 앵커 없거나 파싱 실패 시 false.</summary>
        public static bool IsoToRelative(ReplayPlayer player, string iso, out float relative)
        {
            relative = 0f;
            if (!TryParseIso(iso, out double target)) return false;
            if (!TryGetBaseEpoch(player, out double baseEpoch)) return false;
            relative = (float)(target - baseEpoch);
            return true;
        }

        /// <summary>리플레이 상대초 → ISO 절대시각(UTC, OpenF1 호환 형식). 앵커 없으면 null.</summary>
        public static string RelativeToIso(ReplayPlayer player, float relative)
        {
            if (!TryGetBaseEpoch(player, out double baseEpoch)) return null;
            DateTimeOffset utc = DateTimeOffset
                .FromUnixTimeMilliseconds((long)((baseEpoch + relative) * 1000.0))
                .ToUniversalTime();
            // OpenF1 date 와 문자열 비교 가능하도록 …+00:00 형식으로
            return utc.ToString("yyyy-MM-ddTHH:mm:ss.ffffff", CultureInfo.InvariantCulture) + "+00:00";
        }
    }
}
#endif
