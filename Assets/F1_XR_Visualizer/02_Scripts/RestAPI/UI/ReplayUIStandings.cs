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

            if (lastStandingsTime >= 0f && Mathf.Abs(player.CurrentTime - lastStandingsTime) > 2f)
            {
                lastPositions.Clear();
                positionChanges.Clear();
            }

            lastStandingsTime = player.CurrentTime;

            HashSet<int> visibleDrivers = new();

            if (positions != null)
            {
                for (int i = 0; i < positions.Count; i++)
                {
                    PositionSampleDto position = positions[i];
                    TireSampleDto tire = player.GetTire(position.driverNumber);
                    string compound = tire != null ? tire.compound : "";
                    PositionChange change = UpdatePositionChange(position.driverNumber, position.position);
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

        private PositionChange UpdatePositionChange(int driverNumber, int currentPosition)
        {
            PositionChange change = null;

            if (positionChanges.TryGetValue(driverNumber, out PositionChange activeChange))
            {
                if (Time.time <= activeChange.endTime)
                    change = activeChange;
                else
                    positionChanges.Remove(driverNumber);
            }

            if (lastPositions.TryGetValue(driverNumber, out int previousPosition) &&
                previousPosition != currentPosition)
            {
                change = new PositionChange
                {
                    improved = currentPosition < previousPosition,
                    places = Mathf.Abs(previousPosition - currentPosition),
                    endTime = Time.time + positionChangeFlashSeconds,
                };

                positionChanges[driverNumber] = change;
            }

            lastPositions[driverNumber] = currentPosition;
            return change;
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

        private void EnsureDriverDetailPanel()
        {
            if (driverDetailRoot != null || standingsRoot == null)
                return;

            driverDetailRoot = new GameObject("DriverDetailPanel", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            driverDetailRoot.SetParent(standingsRoot.parent, false);
            driverDetailRoot.anchorMin = standingsRoot.anchorMin;
            driverDetailRoot.anchorMax = standingsRoot.anchorMax;
            driverDetailRoot.pivot = standingsRoot.pivot;
            driverDetailRoot.anchoredPosition = standingsRoot.anchoredPosition + new Vector2(standingsSize.x + 8f, 0f);
            driverDetailRoot.sizeDelta = driverDetailSize;
            driverDetailRoot.localRotation = standingsRoot.localRotation;
            driverDetailRoot.localScale = Vector3.one;
            driverDetailRoot.SetSiblingIndex(standingsRoot.GetSiblingIndex() + 1);

            Image panel = driverDetailRoot.GetComponent<Image>();
            panel.color = new Color(0.015f, 0.018f, 0.026f, 0.9f);

            driverDetailTeamBar = new GameObject("TeamColor", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            driverDetailTeamBar.transform.SetParent(driverDetailRoot, false);
            SetRect(driverDetailTeamBar.rectTransform, 0f, 0f, 5f, driverDetailSize.y);

            driverDetailName = CreateText("DriverName", driverDetailRoot, 18, FontStyles.Bold, TextAlignmentOptions.Left);
            SetRect(driverDetailName.rectTransform, 14f, -10f, driverDetailSize.x - 26f, 24f);

            driverDetailNumber = CreateText("DriverNumber", driverDetailRoot, 13, FontStyles.Bold, TextAlignmentOptions.Left);
            driverDetailNumber.color = new Color(0.82f, 0.86f, 0.92f);
            SetRect(driverDetailNumber.rectTransform, 14f, -36f, driverDetailSize.x - 26f, 18f);

            driverDetailTeam = CreateText("Team", driverDetailRoot, 12, FontStyles.Normal, TextAlignmentOptions.Left);
            driverDetailTeam.color = new Color(0.72f, 0.76f, 0.82f);
            SetRect(driverDetailTeam.rectTransform, 14f, -58f, driverDetailSize.x - 26f, 18f);

            driverDetailPosition = CreateText("Position", driverDetailRoot, 14, FontStyles.Bold, TextAlignmentOptions.Left);
            SetRect(driverDetailPosition.rectTransform, 14f, -88f, driverDetailSize.x - 26f, 20f);

            driverDetailTire = CreateText("Tire", driverDetailRoot, 14, FontStyles.Bold, TextAlignmentOptions.Left);
            SetRect(driverDetailTire.rectTransform, 14f, -114f, driverDetailSize.x - 26f, 20f);

            Button close = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            close.transform.SetParent(driverDetailRoot, false);
            SetRect(close.GetComponent<RectTransform>(), driverDetailSize.x - 26f, -6f, 20f, 20f);
            close.targetGraphic = close.GetComponent<Image>();
            close.GetComponent<Image>().color = new Color(0.14f, 0.15f, 0.2f, 0.9f);
            close.onClick.AddListener(HideDriverDetail);

            TMP_Text closeText = CreateText("Label", close.transform, 12, FontStyles.Bold, TextAlignmentOptions.Center);
            closeText.text = "X";
            closeText.rectTransform.anchorMin = Vector2.zero;
            closeText.rectTransform.anchorMax = Vector2.one;
            closeText.rectTransform.offsetMin = Vector2.zero;
            closeText.rectTransform.offsetMax = Vector2.zero;

            driverDetailRoot.gameObject.SetActive(false);
            EnsureDriverOnboardPanel();
        }

        private void EnsureDriverOnboardPanel()
        {
            if (driverOnboardRoot != null || standingsRoot == null)
                return;

            driverOnboardRoot = new GameObject("DriverOnboardPanel", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            driverOnboardRoot.SetParent(standingsRoot.parent, false);
            driverOnboardRoot.anchorMin = standingsRoot.anchorMin;
            driverOnboardRoot.anchorMax = standingsRoot.anchorMax;
            driverOnboardRoot.pivot = standingsRoot.pivot;
            driverOnboardRoot.anchoredPosition = standingsRoot.anchoredPosition + new Vector2(standingsSize.x + 8f, -driverDetailSize.y - 8f);
            driverOnboardRoot.sizeDelta = driverOnboardSize;
            driverOnboardRoot.localRotation = standingsRoot.localRotation;
            driverOnboardRoot.localScale = Vector3.one;
            driverOnboardRoot.SetSiblingIndex(driverDetailRoot != null ? driverDetailRoot.GetSiblingIndex() + 1 : standingsRoot.GetSiblingIndex() + 1);

            Image panel = driverOnboardRoot.GetComponent<Image>();
            panel.color = new Color(0.015f, 0.018f, 0.026f, 0.92f);

            driverOnboardTitle = CreateText("Header", driverOnboardRoot, 13, FontStyles.Bold, TextAlignmentOptions.Left);
            driverOnboardTitle.color = new Color(0.82f, 0.86f, 0.92f);
            driverOnboardTitle.text = "ONBOARD";
            SetRect(driverOnboardTitle.rectTransform, 10f, -7f, driverOnboardSize.x - 42f, 20f);

            Button close = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            close.transform.SetParent(driverOnboardRoot, false);
            SetRect(close.GetComponent<RectTransform>(), driverOnboardSize.x - 26f, -6f, 20f, 20f);
            close.targetGraphic = close.GetComponent<Image>();
            close.GetComponent<Image>().color = new Color(0.14f, 0.15f, 0.2f, 0.9f);
            close.onClick.AddListener(HideDriverDetail);

            TMP_Text closeText = CreateText("Label", close.transform, 12, FontStyles.Bold, TextAlignmentOptions.Center);
            closeText.text = "X";
            closeText.rectTransform.anchorMin = Vector2.zero;
            closeText.rectTransform.anchorMax = Vector2.one;
            closeText.rectTransform.offsetMin = Vector2.zero;
            closeText.rectTransform.offsetMax = Vector2.zero;

            Image frame = new GameObject("ViewportFrame", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            frame.transform.SetParent(driverOnboardRoot, false);
            frame.color = new Color(0f, 0f, 0f, 0.65f);
            SetRect(frame.rectTransform, 8f, -32f, driverOnboardSize.x - 16f, driverOnboardSize.y - 40f);

            driverOnboardImage = new GameObject("Viewport", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            driverOnboardImage.transform.SetParent(frame.transform, false);
            driverOnboardImage.color = Color.white;
            driverOnboardImage.rectTransform.anchorMin = Vector2.zero;
            driverOnboardImage.rectTransform.anchorMax = Vector2.one;
            driverOnboardImage.rectTransform.offsetMin = new Vector2(2f, 2f);
            driverOnboardImage.rectTransform.offsetMax = new Vector2(-2f, -2f);

            EnsureDriverOnboardView();
            driverOnboardRoot.gameObject.SetActive(false);
        }

        private void EnsureDriverOnboardView()
        {
            if (driverOnboardView == null)
            {
                Transform parent = player != null ? player.transform : transform;
                GameObject obj = new GameObject("DriverOnboardView");
                obj.transform.SetParent(parent, false);
                driverOnboardView = obj.AddComponent<DriverOnboardView>();
            }

            if (driverOnboardImage != null)
            {
                driverOnboardView.SetOutput(
                    driverOnboardImage,
                    Mathf.RoundToInt(driverOnboardTextureSize.x),
                    Mathf.RoundToInt(driverOnboardTextureSize.y)
                );
            }
        }

        private void ShowDriverDetail(int driverNumber)
        {
            if (driverNumber <= 0)
                return;

            selectedDriverNumber = driverNumber;
            if (player != null)
                player.SetSelectedDriver(selectedDriverNumber);
            EnsureDriverDetailPanel();

            if (driverDetailRoot != null)
                driverDetailRoot.gameObject.SetActive(true);

            RefreshDriverDetail(player != null ? player.GetPositions() : null);
            RefreshDriverOnboard();
        }

        private void HideDriverDetail()
        {
            selectedDriverNumber = 0;
            if (player != null)
                player.SetSelectedDriver(0);

            if (driverDetailRoot != null)
                driverDetailRoot.gameObject.SetActive(false);

            if (driverOnboardRoot != null)
                driverOnboardRoot.gameObject.SetActive(false);

            if (driverOnboardView != null)
                driverOnboardView.Hide();

            foreach (StandingRow row in standingRows)
                row.SetSelected(false);
        }

        private void RefreshDriverDetail(List<PositionSampleDto> positions)
        {
            if (selectedDriverNumber <= 0 || player == null || driverDetailRoot == null)
                return;

            DriverInfoDto driver = player.GetDriverInfo(selectedDriverNumber);
            TireSampleDto tire = player.GetTire(selectedDriverNumber);
            PositionSampleDto position = FindPosition(positions, selectedDriverNumber);

            driverDetailTeamBar.color = player.GetDriverColor(selectedDriverNumber);
            driverDetailName.text = DriverName(driver, selectedDriverNumber);
            driverDetailNumber.text = $"#{selectedDriverNumber}  {player.GetDriverLabel(selectedDriverNumber)}";
            driverDetailTeam.text = driver != null && !string.IsNullOrWhiteSpace(driver.teamName)
                ? driver.teamName
                : "Team -";
            driverDetailPosition.text = position != null
                ? $"P{position.position}"
                : "P-";

            if (tire != null)
            {
                string compound = string.IsNullOrWhiteSpace(tire.compound) ? "-" : tire.compound.ToUpperInvariant();
                string age = tire.tireAge > 0 ? $"{tire.tireAge} L" : "-";
                driverDetailTire.text = $"{compound}  {age}";
                driverDetailTire.color = TireColor(tire.compound);
            }
            else
            {
                driverDetailTire.text = "TYRE -";
                driverDetailTire.color = new Color(0.72f, 0.76f, 0.82f);
            }

            foreach (StandingRow row in standingRows)
                row.SetSelected(row.DriverNumber == selectedDriverNumber);

            RefreshDriverOnboard();
        }

        private void RefreshDriverOnboard()
        {
            if (selectedDriverNumber <= 0 || player == null)
            {
                if (driverOnboardView != null)
                    driverOnboardView.Hide();
                return;
            }

            EnsureDriverOnboardPanel();
            EnsureDriverOnboardView();

            if (driverOnboardRoot != null)
                driverOnboardRoot.gameObject.SetActive(true);

            if (driverOnboardTitle != null)
                driverOnboardTitle.text = $"ONBOARD  {player.GetDriverLabel(selectedDriverNumber)}";

            if (player.TryGetCarTransform(selectedDriverNumber, out Transform carTransform))
                driverOnboardView.Show(carTransform);
            else
                driverOnboardView.Hide();
        }

        private static PositionSampleDto FindPosition(List<PositionSampleDto> positions, int driverNumber)
        {
            if (positions == null)
                return null;

            foreach (PositionSampleDto position in positions)
            {
                if (position.driverNumber == driverNumber)
                    return position;
            }

            return null;
        }

        private static string DriverName(DriverInfoDto driver, int driverNumber)
        {
            if (driver == null)
                return $"Driver #{driverNumber}";

            if (!string.IsNullOrWhiteSpace(driver.fullName))
                return driver.fullName;

            if (!string.IsNullOrWhiteSpace(driver.nameAcronym))
                return driver.nameAcronym;

            return $"Driver #{driverNumber}";
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

        private static TMP_Text CreateText(string name, Transform parent, int size, FontStyles style, TextAlignmentOptions alignment)
        {
            TMP_Text text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            text.transform.SetParent(parent, false);
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
            return text;
        }

        private class StandingRow
        {
            public readonly GameObject Root;
            public int DriverNumber;

            private readonly RectTransform rect;
            private readonly Image background;
            private readonly Outline outline;
            private readonly Color defaultBackground;
            private readonly TMP_Text position;
            private readonly Image teamBar;
            private readonly LayoutElement teamBarLayout;
            private readonly TMP_Text driver;
            private readonly Image tireDot;
            private readonly TMP_Text tire;
            private readonly TMP_Text age;
            private readonly TMP_Text change;
            private bool hasSlot;
            private bool selected;
            private Color teamColor;

            public StandingRow(GameObject root, RectTransform rect, Image background, Outline outline, Color defaultBackground, TMP_Text position, Image teamBar, TMP_Text driver, Image tireDot, TMP_Text tire, TMP_Text age, TMP_Text change)
            {
                Root = root;
                this.rect = rect;
                this.background = background;
                this.outline = outline;
                this.defaultBackground = defaultBackground;
                this.position = position;
                this.teamBar = teamBar;
                teamBarLayout = teamBar.GetComponent<LayoutElement>();
                this.driver = driver;
                this.tireDot = tireDot;
                this.tire = tire;
                this.age = age;
                this.change = change;
            }

            public void SetSelected(bool value)
            {
                selected = value;

                if (outline != null)
                {
                    outline.enabled = selected;
                    outline.effectColor = WithAlpha(teamColor, 0.78f);
                }

                if (teamBarLayout != null)
                    teamBarLayout.preferredWidth = selected ? 5f : 3f;

                if (selected)
                {
                    background.color = SelectionBackground(teamColor);
                    teamBar.color = teamColor;
                }
                else
                {
                    background.color = defaultBackground;
                    teamBar.color = teamColor;
                }
            }

            public void SetSlot(int slotIndex, float rowHeight, float moveSpeed)
            {
                Vector2 target = new Vector2(8f, RowY(slotIndex, rowHeight));

                if (!hasSlot)
                {
                    rect.anchoredPosition = target;
                    hasSlot = true;
                    return;
                }

                float t = 1f - Mathf.Exp(-moveSpeed * Time.deltaTime);
                rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition, target, t);
            }

            public void Set(int rank, string driverLabel, Color teamColor, string tireSymbol, Color tireColor, string tireAge, PositionChange positionChange)
            {
                position.text = rank.ToString();
                driver.text = driverLabel;
                this.teamColor = teamColor;
                teamBar.color = teamColor;
                if (outline != null)
                    outline.effectColor = WithAlpha(teamColor, 0.78f);
                tireDot.color = tireColor;
                tire.text = string.IsNullOrWhiteSpace(tireSymbol) ? "-" : tireSymbol;
                age.text = tireAge;

                if (positionChange != null)
                {
                    bool improved = positionChange.improved;
                    background.color = improved
                        ? new Color(0.02f, 0.28f, 0.13f, 0.96f)
                        : new Color(0.42f, 0.04f, 0.06f, 0.96f);
                    change.text = improved ? $"+{positionChange.places}" : $"-{positionChange.places}";
                    change.color = improved ? new Color(0.25f, 1f, 0.45f) : new Color(1f, 0.22f, 0.18f);
                    return;
                }

                change.text = "";
                background.color = selected
                        ? SelectionBackground(teamColor)
                        : rank == 1
                            ? new Color(0.18f, 0.02f, 0.035f, 0.95f)
                            : defaultBackground;
            }

            private static Color SelectionBackground(Color color)
            {
                return new Color(
                    Mathf.Lerp(0.025f, color.r, 0.24f),
                    Mathf.Lerp(0.03f, color.g, 0.24f),
                    Mathf.Lerp(0.04f, color.b, 0.24f),
                    0.98f
                );
            }

            private static Color WithAlpha(Color color, float alpha)
            {
                color.a = alpha;
                return color;
            }
        }

        private class PositionChange
        {
            public bool improved;
            public int places;
            public float endTime;
        }
    }
}
