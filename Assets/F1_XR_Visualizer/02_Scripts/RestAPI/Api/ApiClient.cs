using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace F1XR.RestAPI.Api
{
    public class ApiClient : MonoBehaviour
    {
        public string baseUrl = "http://192.168.0.18:8000";

        public IEnumerator GetYears(Action<YearsResponse> onSuccess, Action<string> onError = null)
        {
            yield return GetJson("/catalog/years", onSuccess, onError);
        }

        public IEnumerator GetTracks(int year, Action<TrackCatalogResponse> onSuccess, Action<string> onError = null)
        {
            yield return GetJson($"/catalog/tracks?year={year}", onSuccess, onError);
        }

        public IEnumerator GetSessions(int year, int circuitKey, Action<SessionCatalogResponse> onSuccess, Action<string> onError = null)
        {
            yield return GetJson($"/catalog/sessions?year={year}&circuit_key={circuitKey}", onSuccess, onError);
        }

        public IEnumerator CreateDataset(CreateDatasetBody body, Action<DatasetManifestDto> onSuccess, Action<string> onError = null)
        {
            yield return PostJson("/datasets", JsonUtility.ToJson(body), onSuccess, onError);
        }

        public IEnumerator GetManifest(string datasetId, Action<DatasetManifestDto> onSuccess, Action<string> onError = null)
        {
            yield return GetJson($"/datasets/{datasetId}/manifest", onSuccess, onError);
        }

        public IEnumerator GetChunk(string datasetId, int chunkIndex, Action<ReplayChunkDto> onSuccess, Action<string> onError = null)
        {
            yield return GetJson($"/datasets/{datasetId}/chunks/{chunkIndex}", onSuccess, onError);
        }

        private IEnumerator GetJson<T>(string path, Action<T> onSuccess, Action<string> onError)
        {
            using UnityWebRequest request = UnityWebRequest.Get(baseUrl + path);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(request.error + "\n" + request.downloadHandler.text);
                yield break;
            }

            onSuccess?.Invoke(JsonUtility.FromJson<T>(request.downloadHandler.text));
        }

        private IEnumerator PostJson<T>(string path, string json, Action<T> onSuccess, Action<string> onError)
        {
            using UnityWebRequest request = new UnityWebRequest(baseUrl + path, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(request.error + "\n" + request.downloadHandler.text);
                yield break;
            }

            onSuccess?.Invoke(JsonUtility.FromJson<T>(request.downloadHandler.text));
        }
    }
}
