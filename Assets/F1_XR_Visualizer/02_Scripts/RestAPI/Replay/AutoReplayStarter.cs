using System.Collections;
using UnityEngine;
using F1XR.RestAPI.Api;

namespace F1XR.RestAPI.Replay
{
    public class AutoReplayStarter : MonoBehaviour
    {
        public ApiClient api;
        public ChunkReplayPlayer player;

        public bool autoStart = true;
        public int preferredYear = 2024;
        public string preferredCircuitShortName = "";
        public string preferredSessionName = "Race";
        public int replayMinutes = 6;
        public int chunkMinutes = 2;
        public int overlapSeconds = 2;
        public bool skipWarmupLap = true;

        private void Awake()
        {
            if (api == null)
                api = GetComponent<ApiClient>();

            if (player == null)
                player = GetComponent<ChunkReplayPlayer>();

            if (player != null && api != null)
                player.api = api;
        }

        private void Start()
        {
            if (autoStart)
                StartCoroutine(LoadDefaultReplay());
        }

        public void Reload()
        {
            StopAllCoroutines();
            StartCoroutine(LoadDefaultReplay());
        }

        private IEnumerator LoadDefaultReplay()
        {
            if (api == null || player == null)
            {
                Debug.LogError("AutoReplayStarter requires ApiClient and ChunkReplayPlayer.");
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
                yield break;
            }

            SessionCatalogResponse sessions = null;
            yield return api.GetSessions(year, track.circuitKey, result => sessions = result, Debug.LogError);

            SessionOption session = PickSession(sessions);
            if (session == null)
            {
                Debug.LogError("No F1 session was returned from the REST API.");
                yield break;
            }

            CreateDatasetBody body = new CreateDatasetBody
            {
                sessionKey = session.sessionKey,
                chunkMinutes = chunkMinutes,
                overlapSeconds = overlapSeconds,
                initialChunks = 1,
                prefetchChunks = 0,
                requestedMinutes = Mathf.Max(1, replayMinutes),
                skipWarmupLap = skipWarmupLap
            };

            yield return api.CreateDataset(
                body,
                manifest =>
                {
                    Debug.Log($"RestAPI scene dataset created: {manifest.datasetId}");
                    player.LoadDataset(manifest, track, true);
                },
                error => Debug.LogError($"Create dataset failed: {error}")
            );
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
