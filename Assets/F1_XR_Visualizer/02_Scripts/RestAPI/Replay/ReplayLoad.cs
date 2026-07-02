using F1XR.RestAPI.Api;

namespace F1XR.RestAPI.Replay
{
    public static class ReplayLoad
    {
        public static DatasetManifestDto Manifest;

        public static void Clear()
        {
            Manifest = null;
        }
    }
}