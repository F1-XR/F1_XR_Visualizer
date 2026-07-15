using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.UI
{
    internal sealed class PositionChangeTracker
    {
        private readonly Dictionary<int, int> lastPositions = new();
        private readonly Dictionary<int, PositionChange> changes = new();

        private float lastTime = -1f;

        public void BeginFrame(float currentTime)
        {
            if (lastTime >= 0f &&
                Mathf.Abs(currentTime - lastTime) > 2f)
            {
                lastPositions.Clear();
                changes.Clear();
            }

            lastTime = currentTime;
        }

        public PositionChange Update(
            int driverNumber,
            int currentPosition,
            float flashSeconds)
        {
            PositionChange change = null;

            if (changes.TryGetValue(
                    driverNumber,
                    out PositionChange activeChange))
            {
                if (Time.time <= activeChange.endTime)
                    change = activeChange;
                else
                    changes.Remove(driverNumber);
            }

            if (lastPositions.TryGetValue(
                    driverNumber,
                    out int previousPosition) &&
                previousPosition != currentPosition)
            {
                change = new PositionChange
                {
                    improved =
                        currentPosition < previousPosition,
                    places =
                        Mathf.Abs(
                            previousPosition -
                            currentPosition),
                    endTime =
                        Time.time + flashSeconds
                };

                changes[driverNumber] = change;
            }

            lastPositions[driverNumber] =
                currentPosition;

            return change;
        }
    }

    internal sealed class PositionChange
    {
        public bool improved;
        public int places;
        public float endTime;
    }
}