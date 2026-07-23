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
        public int preStartSeconds = 0;
        public bool skipWarmupLap = true;
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
    public class RaceControlEventDto
    {
        public float startT;
        public float endT;
        public float t;
        public string date;
        public string category;
        public string flag;
        public string scope;
        public int sector;
        public string message;
    }

    [System.Serializable]
    public class ReplayEventDto
    {
        public string eventId;
        public string eventType;
        public float anchorTime;
        public float startTime;
        public float endTime;
        public int[] driverNumbers;
        public float progressStart = -1f;
        public float progressEnd = -1f;
        public float confidence = -1f;
        public string displayTitle;
        public string displayDescription;
        public string passingSide;
        public string sideSource;
        public float sideConfidence = -1f;
        public string motionProfile;
        public float overtakerShare = -1f;
        public float defenderShare = -1f;
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
        public float raceStartT;
        public float raceEndT;
        public RaceControlEventDto[] yellowFlags;
        public RaceControlEventDto[] redFlags;
        public ReplayEventDto[] events;

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
        public TireSampleDto[] tires;
    }

    [Serializable]
    public class LocationSample
    {
        public float t;
        public int driverNumber;
        public float x;
        public float y;
        public float z;
        public float rpm;
        public float throttle;
        public float speed;
        public int nGear;
        public int n_gear;
        public int brake;
        public int drs;
    }
    
    [Serializable]
    public class PositionSampleDto
    {
        public float t;
        public int driverNumber;
        public int position;
    }

    [Serializable]
    public class TireSampleDto
    {
        public float t;
        public int driverNumber;
        public string compound;
        public int tireAge;
    }
}

