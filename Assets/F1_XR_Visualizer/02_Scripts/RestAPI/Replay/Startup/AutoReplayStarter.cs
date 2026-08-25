using System;
using System.Collections;
using UnityEngine;
using F1XR.RestAPI.Api;

namespace F1XR.RestAPI.Replay
{
    public class AutoReplayStarter : MonoBehaviour
    {
        private const int EventReplayInitialMinutes = 6;

        public ApiClient api;
        public ReplayPlayer player;

        public bool autoStart = true;
        public int preferredYear = 2024;
        public string preferredCircuitShortName = "";
        public string preferredSessionName = "Race";
        public int replayMinutes = 6;
        public int chunkMinutes = 2;
        public int overlapSeconds = 2;
        public bool skipWarmupLap = true;

        [Header("Cached Dataset Fast Start")]
        public bool useCachedDatasetFastStart;
        public string cachedDatasetId = "";
        public int cachedCircuitKey;

        [Header("Development Event Replay")]
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool useEventReplayTestSession = true;
#else
        public bool useEventReplayTestSession;
#endif

        private void Awake()
        {
            if (useEventReplayTestSession)
                ApplyEventReplayTestSettings();

            if (api == null)
                api = GetComponent<ApiClient>();

            if (player == null)
                player = GetComponent<ReplayPlayer>();

            if (player != null && api != null)
                player.api = api;
        }

        private void Start()
        {
            bool selectedReplayPending = ReplayLoad.Manifest != null;
            bool replayAlreadyLoaded = player != null && player.HasDataset;
            if (autoStart && !selectedReplayPending && !replayAlreadyLoaded)
                StartCoroutine(LoadDefaultReplay());
        }

        public void Reload()
        {
            StopAllCoroutines();
            StartCoroutine(LoadDefaultReplay());
        }

        public void ReloadEventReplayTestSession(
            Action<bool> onComplete)
        {
            ApplyEventReplayTestSettings();
            ReplayLoad.Clear();
            StopAllCoroutines();
            StartCoroutine(LoadDefaultReplay(onComplete));
        }

        private void ApplyEventReplayTestSettings()
        {
            useEventReplayTestSession = true;
            preferredYear = 2024;
            preferredCircuitShortName = "Suzuka";
            preferredSessionName = "Race";
            replayMinutes = 60;
            skipWarmupLap = false;
            useCachedDatasetFastStart = false;
            cachedDatasetId = "";
            cachedCircuitKey = 46;
        }

        private IEnumerator LoadDefaultReplay(
            Action<bool> onComplete = null)
        {
            if (api == null || player == null)
            {
                Debug.LogError("ReplayAutoLoader requires ApiClient and ReplayPlayer.");
                onComplete?.Invoke(false);
                yield break;
            }

            bool loadedCachedDataset = false;
            if (useCachedDatasetFastStart &&
                !string.IsNullOrWhiteSpace(cachedDatasetId))
            {
                yield return api.GetManifest(
                    cachedDatasetId,
                    manifest =>
                    {
                        if (!IsReady(manifest))
                        {
                            Debug.LogWarning(
                                $"Cached dataset is not ready: {cachedDatasetId}");
                            return;
                        }

                        TrackOption cachedTrack = new()
                        {
                            circuitKey = cachedCircuitKey,
                            circuitShortName = preferredCircuitShortName,
                            meetingName = preferredCircuitShortName
                        };

                        Debug.Log(
                            $"RestAPI scene cached dataset loaded: {manifest.datasetId}");
                        player.LoadDataset(manifest, cachedTrack, true);
                        loadedCachedDataset = true;
                    },
                    error => Debug.LogWarning(
                        $"Cached dataset load failed. Falling back to catalog lookup: {error}")
                );
            }

            if (loadedCachedDataset)
            {
                onComplete?.Invoke(true);
                yield break;
            }

            YearsResponse years = null;
            yield return api.GetYears(result => years = result, Debug.LogError);

            int year = PickYear(years);

            TrackCatalogResponse tracks = null;
            yield return api.GetTracks(year, result => tracks = result, Debug.LogError);

            TrackOption track = PickTrack(tracks);
            if (track == null)
            {
                Debug.LogError("No F1 track was returned from the REST API.");
                onComplete?.Invoke(false);
                yield break;
            }

            SessionCatalogResponse sessions = null;
            yield return api.GetSessions(year, track.circuitKey, result => sessions = result, Debug.LogError);

            SessionOption session = PickSession(sessions);
            if (session == null)
            {
                Debug.LogError("No F1 session was returned from the REST API.");
                onComplete?.Invoke(false);
                yield break;
            }

            int initialChunks = useEventReplayTestSession
                ? Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        (float)Mathf.Min(
                            Mathf.Max(1, replayMinutes),
                            EventReplayInitialMinutes) /
                        Mathf.Max(1, chunkMinutes)))
                : 1;
            CreateDatasetBody body = new CreateDatasetBody
            {
                sessionKey = session.sessionKey,
                chunkMinutes = chunkMinutes,
                overlapSeconds = overlapSeconds,
                initialChunks = initialChunks,
                prefetchChunks = 0,
                requestedMinutes = Mathf.Max(1, replayMinutes),
                preStartSeconds = 0,
                skipWarmupLap = skipWarmupLap
            };

            bool loadedDataset = false;
            yield return api.CreateDataset(
                body,
                manifest =>
                {
                    if (manifest == null ||
                        string.Equals(
                            manifest.status,
                            "failed",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    Debug.Log($"RestAPI scene dataset created: {manifest.datasetId}");
                    player.LoadDataset(manifest, track, true);
                    loadedDataset = true;
                },
                error => Debug.LogError($"Create dataset failed: {error}")
            );
            onComplete?.Invoke(loadedDataset);
        }

        private static bool IsReady(DatasetManifestDto manifest)
        {
            if (manifest == null ||
                manifest.status != "complete" ||
                manifest.chunks == null)
            {
                return false;
            }

            foreach (ChunkInfoDto chunk in manifest.chunks)
            {
                if (chunk.status == "ready" && chunk.sampleCount > 0)
                    return true;
            }

            return false;
        }

        private int PickYear(YearsResponse years)
        {
            if (years == null || years.years == null || years.years.Length == 0)
                return preferredYear;

            foreach (int year in years.years)
            {
                if (year == preferredYear)
                    return year;
            }

            return years.years[0];
        }

        private TrackOption PickTrack(TrackCatalogResponse tracks)
        {
            if (tracks == null || tracks.tracks == null || tracks.tracks.Length == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(preferredCircuitShortName))
            {
                foreach (TrackOption track in tracks.tracks)
                {
                    if (track.circuitShortName == preferredCircuitShortName)
                        return track;
                }
            }

            return tracks.tracks[0];
        }

        private SessionOption PickSession(SessionCatalogResponse sessions)
        {
            if (sessions == null || sessions.sessions == null || sessions.sessions.Length == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(preferredSessionName))
            {
                foreach (SessionOption session in sessions.sessions)
                {
                    if (session.sessionName == preferredSessionName || session.sessionType == preferredSessionName)
                        return session;
                }
            }

            return sessions.sessions[0];
        }
    }
}
