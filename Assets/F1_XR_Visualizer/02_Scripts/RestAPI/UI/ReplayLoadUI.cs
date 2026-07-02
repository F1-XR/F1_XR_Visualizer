using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Replay;

namespace F1XR.RestAPI.UI
{
    public class ReplayLoadUI : MonoBehaviour
    {
        public ApiClient api;

        public TMP_Dropdown yearDropdown;
        public TMP_Dropdown trackDropdown;
        public TMP_Dropdown sessionDropdown;
        public Button playButton;
        public TMP_Text statusText;

        public string replaySceneName = "RestAPI";
        public int replayMinutes = 6;
        public int chunkMinutes = 2;
        public int overlapSeconds = 2;
        public float manifestPollSeconds = 0.5f;

        private int[] years;
        private TrackOption[] tracks;
        private SessionOption[] sessions;
        private bool loading;

        private void Start()
        {
            playButton.onClick.AddListener(Play);
            yearDropdown.onValueChanged.AddListener(_ => StartCoroutine(LoadTracks()));
            trackDropdown.onValueChanged.AddListener(_ => StartCoroutine(LoadSessions()));

            StartCoroutine(LoadYears());
        }

        private IEnumerator LoadYears()
        {
            SetLoading("Loading years...");

            YearsResponse response = null;
            yield return api.GetYears(result => response = result, Debug.LogError);

            years = response?.years;
            SetOptions(yearDropdown, YearLabels(years));

            yield return LoadTracks();
        }

        private IEnumerator LoadTracks()
        {
            if (years == null || years.Length == 0)
                yield break;

            SetLoading("Loading tracks...");

            TrackCatalogResponse response = null;
            yield return api.GetTracks(SelectedYear(), result => response = result, Debug.LogError);

            tracks = response?.tracks;
            SetOptions(trackDropdown, TrackLabels(tracks));

            yield return LoadSessions();
        }

        private IEnumerator LoadSessions()
        {
            if (tracks == null || tracks.Length == 0)
                yield break;

            SetLoading("Loading sessions...");

            SessionCatalogResponse response = null;
            yield return api.GetSessions(SelectedYear(), SelectedTrack().circuitKey, result => response = result, Debug.LogError);

            sessions = response?.sessions;
            SetOptions(sessionDropdown, SessionLabels(sessions));

            SetReady();
        }

        private void Play()
        {
            if (!loading)
                StartCoroutine(LoadReplay());
        }

        private IEnumerator LoadReplay()
        {
            if (SelectedSession() == null)
                yield break;

            SetLoading("Loading replay...");

            DatasetManifestDto manifest = null;

            CreateDatasetBody body = new CreateDatasetBody
            {
                sessionKey = SelectedSession().sessionKey,
                chunkMinutes = chunkMinutes,
                overlapSeconds = overlapSeconds,
                initialChunks = 1,
                prefetchChunks = 0,
                requestedMinutes = Mathf.Max(1, replayMinutes)
            };

            yield return api.CreateDataset(body, result => manifest = result, Debug.LogError);

            while (manifest != null && !IsReady(manifest))
            {
                yield return new WaitForSeconds(manifestPollSeconds);
                yield return api.GetManifest(manifest.datasetId, result => manifest = result, Debug.LogError);
            }

            if (manifest == null || manifest.status == "failed")
            {
                SetReady();
                yield break;
            }

            ReplayLoad.Manifest = manifest;
            SceneManager.LoadScene(replaySceneName);
        }

        private bool IsReady(DatasetManifestDto manifest)
        {
            if (manifest.chunks == null)
                return false;

            foreach (ChunkInfoDto chunk in manifest.chunks)
            {
                if (chunk.status == "ready" && chunk.sampleCount > 0)
                    return true;
            }

            return manifest.status == "complete";
        }

        private int SelectedYear()
        {
            return years[yearDropdown.value];
        }

        private TrackOption SelectedTrack()
        {
            return tracks[trackDropdown.value];
        }

        private SessionOption SelectedSession()
        {
            if (sessions == null || sessions.Length == 0)
                return null;

            return sessions[sessionDropdown.value];
        }

        private void SetOptions(TMP_Dropdown dropdown, List<string> labels)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(labels);
            dropdown.value = 0;
            dropdown.RefreshShownValue();
        }

        private List<string> YearLabels(int[] values)
        {
            List<string> labels = new();

            if (values == null)
                return labels;

            foreach (int value in values)
                labels.Add(value.ToString());

            return labels;
        }

        private List<string> TrackLabels(TrackOption[] values)
        {
            List<string> labels = new();

            if (values == null)
                return labels;

            foreach (TrackOption value in values)
                labels.Add($"{value.circuitShortName} - {value.meetingName}");

            return labels;
        }

        private List<string> SessionLabels(SessionOption[] values)
        {
            List<string> labels = new();

            if (values == null)
                return labels;

            foreach (SessionOption value in values)
                labels.Add(value.sessionName);

            return labels;
        }

        private void SetLoading(string text)
        {
            loading = true;
            playButton.interactable = false;

            if (statusText != null)
                statusText.text = text;
        }

        private void SetReady()
        {
            loading = false;
            playButton.interactable = sessions != null && sessions.Length > 0;

            if (statusText != null)
                statusText.text = "play";
        }
    }
}