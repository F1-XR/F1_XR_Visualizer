using System;

namespace F1XR.RestAPI.Api
{
    [Serializable]
    public class YearsResponse
    {
        public int[] years;
    }

    [Serializable]
    public class TrackCatalogResponse
    {
        public int year;
        public TrackOption[] tracks;
    }

    [Serializable]
    public class TrackOption
    {
        public int circuitKey;
        public string circuitShortName;
        public string location;
        public string countryName;
        public string meetingName;
    }

    [Serializable]
    public class SessionCatalogResponse
    {
        public int year;
        public int circuitKey;
        public SessionOption[] sessions;
    }

    [Serializable]
    public class SessionOption
    {
        public int sessionKey;
        public int meetingKey;
        public int circuitKey;
        public string circuitShortName;
        public string location;
        public string countryName;
        public string meetingName;
        public string sessionName;
        public string sessionType;
        public string dateStart;
        public string dateEnd;
        public int year;
    }

    [System.Serializable]
    public class CreateDatasetBody
    {
        public int sessionKey;
        public int chunkMinutes = 2;
        public int overlapSeconds = 2;
        public int initialChunks = 1;
        public int prefetchChunks = 0;
        public int requestedMinutes = 6;
    }

    [System.Serializable]
    public class DriverInfoDto
    {
        public int driverNumber;
        public string nameAcronym;
        public string fullName;
        public string teamName;
        public string teamColour;
    }
    
    [System.Serializable]
    public class DatasetManifestDto
    {
        public string datasetId;
        public string status;
        public string error;

        public int year;
        public string circuit;
        public int sessionKey;
        public int meetingKey;
        public string sessionName;

        public int chunkMinutes;
        public int overlapSeconds;
        public float durationSeconds;
        public float requestedDurationSeconds;
        public float readyUntilT;
        public int playbackStartChunkIndex;
        public float playbackStartT;

        public ChunkInfoDto[] chunks;
        public DriverInfoDto[] drivers;
    }

    [System.Serializable]
    public class ChunkInfoDto
    {
        public int index;
        public float startT;
        public float endT;
        public string status;
        public int sampleCount;
        public string error;
    }

    [Serializable]
    public class ReplayChunkDto
    {
        public string datasetId;
        public int chunkIndex;
        public float startT;
        public float endT;
        public int overlapSeconds;
        public LocationSample[] samples;
        public PositionSampleDto[] positions;
    }

    [Serializable]
    public class LocationSample
    {
        public float t;
        public int driverNumber;
        public float x;
        public float y;
        public float z;
    }
    
    [Serializable]
    public class PositionSampleDto
    {
        public float t;
        public int driverNumber;
        public int position;
    }
}

