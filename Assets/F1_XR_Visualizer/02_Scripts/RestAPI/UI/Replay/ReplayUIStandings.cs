using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Replay;

namespace F1XR.RestAPI.UI
{
    public partial class ReplayUI
    {
        private void RefreshStandings(List<PositionSampleDto> positions)
        {
            EnsureStandings();

            if (standingsRoot == null)
                return;

            if (lapHeader != null)
                lapHeader.text = $"LAP  {FormatTime(player.CurrentTime)}";

            positionChangeTracker.BeginFrame(player.CurrentTime);

            visibleDrivers.Clear();

            if (positions != null)
            {
                for (int i = 0; i < positions.Count; i++)
                {
                    PositionSampleDto position = positions[i];
                    TireSampleDto tire = player.GetTire(position.driverNumber);
                    string compound = tire != null ? tire.compound : "";
                    PositionChange change = positionChangeTracker.Update(
                        position.driverNumber,
                        position.position,
                        positionChangeFlashSeconds);
                    StandingRow row = GetStandingRow(position.driverNumber);

                    if (row == null)
                        continue;

                    visibleDrivers.Add(position.driverNumber);
                    row.Root.SetActive(true);
                    row.SetSlot(i, standingsRowHeight, standingsMoveSpeed);
                    row.Set(
                        position.position,
                        player.GetDriverLabel(position.driverNumber),
                        player.GetDriverColor(position.driverNumber),
                        TireSymbol(compound),
                        TireColor(compound),
                        tire != null && tire.tireAge > 0 ? $"{tire.tireAge} L" : "",
                        change
                    );
                }
            }

            foreach (StandingRow row in standingRows)
            {
                if (row.DriverNumber == 0 || visibleDrivers.Contains(row.DriverNumber))
                    continue;

                row.Root.SetActive(false);
            }

            RefreshDriverDetail(positions);
        }

        private StandingRow GetStandingRow(int driverNumber)
        {
            if (rowsByDriver.TryGetValue(driverNumber, out StandingRow row))
                return row;

            foreach (StandingRow candidate in standingRows)
            {
                if (candidate.DriverNumber != 0)
                    continue;

                candidate.DriverNumber = driverNumber;
                rowsByDriver.Add(driverNumber, candidate);
                return candidate;
            }

            return null;
        }

        private void EnsureStandings()
        {
            if (standingsRoot != null || rankText == null)
                return;

            RectTransform source = rankText.rectTransform;
            rankText.gameObject.SetActive(false);
            standingsSize = new Vector2(Mathf.Max(standingsSize.x, 196f), standingsSize.y);

            standingsRoot = new GameObject("StandingsPanel", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            standingsRoot.SetParent(source.parent, false);
            standingsRoot.anchorMin = source.anchorMin;
            standingsRoot.anchorMax = source.anchorMax;
            standingsRoot.anchoredPosition = source.anchoredPosition;
            standingsRoot.sizeDelta = standingsSize;
            standingsRoot.pivot = source.pivot;
            standingsRoot.localRotation = source.localRotation;
            standingsRoot.localScale = Vector3.one;
            standingsRoot.SetSiblingIndex(source.GetSiblingIndex());

            Image panel = standingsRoot.GetComponent<Image>();
            panel.color = new Color(0.015f, 0.018f, 0.026f, 0.82f);

            lapHeader = CreateText("Header", standingsRoot, 18, FontStyles.Bold, TextAlignmentOptions.Center);
            lapHeader.color = new Color(0.82f, 0.86f, 0.92f);
            SetRect(lapHeader.rectTransform, 8f, -8f, standingsSize.x - 16f, 28f);

            tireHeader = CreateText("TireHeader", standingsRoot, 8, FontStyles.Bold, TextAlignmentOptions.Right);
            tireHeader.text = "CURRENT TYRE AGE (LAPS)";
            tireHeader.color = new Color(1f, 0.2f, 0.12f);
            SetRect(tireHeader.rectTransform, 8f, -36f, standingsSize.x - 16f, 12f);

            for (int i = 0; i < 20; i++)
            {
                StandingRow row = CreateStandingRow(standingsRoot, i, standingsSize.x - 16f, standingsRowHeight);
                row.Root.SetActive(false);
                standingRows.Add(row);
            }

            EnsureDriverDetailPanel();
        }

        private StandingRow CreateStandingRow(Transform parent, int index, float rowWidth, float rowHeight)
        {
            RectTransform root = new GameObject($"StandingRow_{index + 1}", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
            root.SetParent(parent, false);
            SetRect(root, 8f, RowY(index, rowHeight), rowWidth, rowHeight);

            Image background = root.GetComponent<Image>();
            background.color = index % 2 == 0
                ? new Color(0.08f, 0.09f, 0.12f, 0.92f)
                : new Color(0.04f, 0.045f, 0.065f, 0.92f);

            Outline outline = root.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.clear;
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.enabled = false;

            Button button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;

            HorizontalLayoutGroup layout = root.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(5, 6, 1, 1);
            layout.spacing = 5f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            root.GetComponent<LayoutElement>().preferredHeight = rowHeight;

            TMP_Text position = CreateText("Position", root, 14, FontStyles.Bold, TextAlignmentOptions.Center);
            position.gameObject.AddComponent<LayoutElement>().preferredWidth = 24f;

            Image teamBar = new GameObject("TeamColor", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
            teamBar.transform.SetParent(root, false);
            LayoutElement teamBarLayout = teamBar.GetComponent<LayoutElement>();
            teamBarLayout.preferredWidth = 3f;

            TMP_Text driver = CreateText("Driver", root, 15, FontStyles.Bold, TextAlignmentOptions.Left);
            driver.gameObject.AddComponent<LayoutElement>().preferredWidth = 56f;

            Image tireDot = new GameObject("TireDot", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
            tireDot.transform.SetParent(root, false);
            LayoutElement tireDotLayout = tireDot.GetComponent<LayoutElement>();
            tireDotLayout.preferredWidth = 14f;

            TMP_Text tire = CreateText("Tire", tireDot.transform, 12, FontStyles.Bold, TextAlignmentOptions.Center);
            RectTransform tireRect = tire.rectTransform;
            tireRect.anchorMin = Vector2.zero;
            tireRect.anchorMax = Vector2.one;
            tireRect.offsetMin = Vector2.zero;
            tireRect.offsetMax = Vector2.zero;
            tire.color = Color.black;

            TMP_Text age = CreateText("Age", root, 14, FontStyles.Normal, TextAlignmentOptions.Right);
            age.gameObject.AddComponent<LayoutElement>().preferredWidth = 32f;

            TMP_Text change = CreateText("Change", root, 12, FontStyles.Bold, TextAlignmentOptions.Center);
            change.gameObject.AddComponent<LayoutElement>().preferredWidth = 16f;

            StandingRow row = null;
            button.onClick.AddListener(() =>
            {
                if (row != null)
                    ShowDriverDetail(row.DriverNumber);
            });

            row = new StandingRow(root.gameObject, root, background, outline, background.color, position, teamBar, driver, tireDot, tire, age, change);
            return row;
        }

        private static float RowY(int slotIndex, float rowHeight)
        {
            return -50f - slotIndex * (rowHeight + 2f);
        }

        private static void SetRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private TMP_Text CreateText(string name, Transform parent, int size, FontStyles style, TextAlignmentOptions alignment)
        {
            TMP_Text text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            text.transform.SetParent(parent, false);
            text.font = rankText.font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
            return text;
        }

    }
}
