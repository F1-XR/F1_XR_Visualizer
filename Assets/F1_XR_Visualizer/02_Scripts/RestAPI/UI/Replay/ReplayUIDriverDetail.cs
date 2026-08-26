using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using F1XR.RestAPI.Api;

namespace F1XR.RestAPI.UI
{
    public partial class ReplayUI
    {
        private const float DriverPortraitWidth = 92f;
        private const float DriverPortraitHeight = 96.7f;
        private Image driverDetailPortrait;
        private readonly Dictionary<string, Sprite> driverPortraits =
            new(System.StringComparer.OrdinalIgnoreCase);
        private bool driverPortraitsLoaded;

        private void EnsureDriverDetailPanel()
        {
            if (driverDetailRoot != null)
            {
                RefreshDriverDetailLayout();
                return;
            }

            if (standingsRoot == null)
                return;

            driverDetailRoot = new GameObject(
                "DriverDetailPanel",
                typeof(RectTransform),
                typeof(Image))
                .GetComponent<RectTransform>();

            driverDetailRoot.SetParent(
                standingsRoot.parent,
                false);

            driverDetailRoot.anchorMin =
                standingsRoot.anchorMin;

            driverDetailRoot.anchorMax =
                standingsRoot.anchorMax;

            driverDetailRoot.pivot =
                standingsRoot.pivot;

            driverDetailRoot.anchoredPosition =
                standingsRoot.anchoredPosition +
                new Vector2(standingsSize.x + 8f, 0f);

            driverDetailRoot.sizeDelta =
                new Vector2(
                    driverDetailSize.x + DriverPortraitWidth,
                    driverDetailSize.y);

            driverDetailRoot.localRotation =
                standingsRoot.localRotation;

            driverDetailRoot.localScale =
                Vector3.one;

            driverDetailRoot.SetSiblingIndex(
                standingsRoot.GetSiblingIndex() + 1);

            Image panel =
                driverDetailRoot.GetComponent<Image>();

            panel.color =
                new Color(0.015f, 0.018f, 0.026f, 0.9f);

            driverDetailTeamBar = new GameObject(
                "TeamColor",
                typeof(RectTransform),
                typeof(Image))
                .GetComponent<Image>();

            driverDetailTeamBar.transform.SetParent(
                driverDetailRoot,
                false);

            SetRect(
                driverDetailTeamBar.rectTransform,
                0f,
                0f,
                5f,
                driverDetailSize.y);

            driverDetailName = CreateText(
                "DriverName",
                driverDetailRoot,
                18,
                FontStyles.Bold,
                TextAlignmentOptions.Left);

            SetRect(
                driverDetailName.rectTransform,
                14f,
                -10f,
                driverDetailSize.x - 26f,
                24f);

            driverDetailNumber = CreateText(
                "DriverNumber",
                driverDetailRoot,
                13,
                FontStyles.Bold,
                TextAlignmentOptions.Left);

            driverDetailNumber.color =
                new Color(0.82f, 0.86f, 0.92f);

            SetRect(
                driverDetailNumber.rectTransform,
                14f,
                -36f,
                driverDetailSize.x - 26f,
                18f);

            driverDetailTeam = CreateText(
                "Team",
                driverDetailRoot,
                12,
                FontStyles.Normal,
                TextAlignmentOptions.Left);

            driverDetailTeam.color =
                new Color(0.72f, 0.76f, 0.82f);

            SetRect(
                driverDetailTeam.rectTransform,
                14f,
                -58f,
                driverDetailSize.x - 26f,
                18f);

            driverDetailPosition = CreateText(
                "Position",
                driverDetailRoot,
                14,
                FontStyles.Bold,
                TextAlignmentOptions.Left);

            SetRect(
                driverDetailPosition.rectTransform,
                14f,
                -88f,
                driverDetailSize.x - 26f,
                20f);

            driverDetailTire = CreateText(
                "Tire",
                driverDetailRoot,
                14,
                FontStyles.Bold,
                TextAlignmentOptions.Left);

            SetRect(
                driverDetailTire.rectTransform,
                14f,
                -114f,
                driverDetailSize.x - 26f,
                20f);

            driverDetailPortrait = new GameObject(
                "DriverPortrait",
                typeof(RectTransform),
                typeof(Image))
                .GetComponent<Image>();

            driverDetailPortrait.transform.SetParent(
                driverDetailRoot,
                false);

            driverDetailPortrait.preserveAspect = false;
            driverDetailPortrait.raycastTarget = false;

            SetRect(
                driverDetailPortrait.rectTransform,
                driverDetailSize.x - 8f,
                -(driverDetailSize.y - DriverPortraitHeight),
                DriverPortraitWidth - 8f,
                DriverPortraitHeight);

            Button close = new GameObject(
                "Close",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button))
                .GetComponent<Button>();

            close.transform.SetParent(
                driverDetailRoot,
                false);

            SetRect(
                close.GetComponent<RectTransform>(),
                driverDetailSize.x + DriverPortraitWidth - 26f,
                -6f,
                20f,
                20f);

            close.targetGraphic =
                close.GetComponent<Image>();

            close.GetComponent<Image>().color =
                new Color(0.14f, 0.15f, 0.2f, 0.9f);

            close.onClick.AddListener(
                HideDriverDetail);

            TMP_Text closeText = CreateText(
                "Label",
                close.transform,
                12,
                FontStyles.Bold,
                TextAlignmentOptions.Center);

            closeText.text = "X";
            closeText.rectTransform.anchorMin =
                Vector2.zero;
            closeText.rectTransform.anchorMax =
                Vector2.one;
            closeText.rectTransform.offsetMin =
                Vector2.zero;
            closeText.rectTransform.offsetMax =
                Vector2.zero;

            driverDetailRoot.gameObject.SetActive(false);
            EnsureDriverOnboardPanel();
        }

        private void RefreshDriverDetailLayout()
        {
            driverDetailRoot.sizeDelta = new Vector2(
                driverDetailSize.x + DriverPortraitWidth,
                driverDetailSize.y);

            if (driverDetailPortrait != null)
            {
                SetRect(
                    driverDetailPortrait.rectTransform,
                    driverDetailSize.x - 8f,
                    -(driverDetailSize.y - DriverPortraitHeight),
                    DriverPortraitWidth - 8f,
                    DriverPortraitHeight);
            }

            Transform close = driverDetailRoot.Find("Close");
            if (close != null)
            {
                SetRect(
                    close.GetComponent<RectTransform>(),
                    driverDetailSize.x + DriverPortraitWidth - 26f,
                    -6f,
                    20f,
                    20f);
            }
        }

        private void ShowDriverDetail(int driverNumber)
        {
            ShowDriverDetail(driverNumber, true);
        }

        private void ShowDriverDetail(int driverNumber, bool notifyPlayer)
        {
            if (driverNumber <= 0)
                return;

            selectedDriverNumber = driverNumber;

            if (notifyPlayer && player != null && player.SelectedDriverNumber != driverNumber)
                player.SetSelectedDriver(driverNumber);

            EnsureDriverDetailPanel();

            if (driverDetailRoot != null)
                driverDetailRoot.gameObject.SetActive(true);

            RefreshDriverDetail(
                player != null
                    ? player.GetPositions()
                    : null);

            RefreshDriverOnboard();
        }

        private void HideDriverDetail()
        {
            HideDriverDetail(true);
        }

        private void HideDriverDetail(bool notifyPlayer)
        {
            selectedDriverNumber = 0;

            if (notifyPlayer && player != null && player.SelectedDriverNumber != 0)
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

        private void RefreshDriverDetail(
            List<PositionSampleDto> positions)
        {
            if (selectedDriverNumber <= 0 ||
                player == null ||
                driverDetailRoot == null)
                return;

            DriverInfoDto driver =
                player.GetDriverInfo(
                    selectedDriverNumber);

            TireSampleDto tire =
                player.GetTire(
                    selectedDriverNumber);

            PositionSampleDto position =
                FindPosition(
                    positions,
                    selectedDriverNumber);

            driverDetailTeamBar.color =
                player.GetDriverColor(
                    selectedDriverNumber);

            driverDetailName.text =
                DriverName(
                    driver,
                    selectedDriverNumber);

            driverDetailNumber.text =
                $"#{selectedDriverNumber}  " +
                player.GetDriverLabel(
                    selectedDriverNumber);

            driverDetailTeam.text =
                driver != null &&
                !string.IsNullOrWhiteSpace(driver.teamName)
                    ? driver.teamName
                    : "Team -";

            driverDetailPosition.text =
                position != null
                    ? $"P{position.position}"
                    : "P-";

            if (tire != null)
            {
                string compound =
                    string.IsNullOrWhiteSpace(tire.compound)
                        ? "-"
                        : tire.compound.ToUpperInvariant();

                string age =
                    tire.tireAge > 0
                        ? $"{tire.tireAge} L"
                        : "-";

                driverDetailTire.text =
                    $"{compound}  {age}";

                driverDetailTire.color =
                    TireColor(tire.compound);
            }
            else
            {
                driverDetailTire.text = "TYRE -";
                driverDetailTire.color =
                    new Color(0.72f, 0.76f, 0.82f);
            }

            RefreshDriverPortrait(driver);

            foreach (StandingRow row in standingRows)
            {
                row.SetSelected(
                    row.DriverNumber ==
                    selectedDriverNumber);
            }

            RefreshDriverOnboard();
        }

        private static PositionSampleDto FindPosition(
            List<PositionSampleDto> positions,
            int driverNumber)
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

        private static string DriverName(
            DriverInfoDto driver,
            int driverNumber)
        {
            if (driver == null)
                return $"Driver #{driverNumber}";

            if (!string.IsNullOrWhiteSpace(driver.fullName))
                return driver.fullName;

            if (!string.IsNullOrWhiteSpace(driver.nameAcronym))
                return driver.nameAcronym;

            return $"Driver #{driverNumber}";
        }

        private void RefreshDriverPortrait(DriverInfoDto driver)
        {
            if (driverDetailPortrait == null)
                return;

            string portraitName = driver?.fullName?.Replace(" ", "_");
            driverDetailPortrait.sprite =
                string.IsNullOrWhiteSpace(portraitName)
                    ? null
                    : GetDriverPortrait(portraitName);
            driverDetailPortrait.enabled =
                driverDetailPortrait.sprite != null;
        }

        private Sprite GetDriverPortrait(string portraitName)
        {
            LoadDriverPortraits();
            return driverPortraits.TryGetValue(
                portraitName,
                out Sprite portrait)
                ? portrait
                : null;
        }

        private void LoadDriverPortraits()
        {
            if (driverPortraitsLoaded)
                return;

            driverPortraitsLoaded = true;

            foreach (Texture2D texture in Resources.LoadAll<Texture2D>(
                "DriverPortraits_2026"))
            {
                float torsoStart = texture.height * 0.60f;

                driverPortraits[texture.name] = Sprite.Create(
                    texture,
                    new Rect(
                        0f,
                        torsoStart,
                        texture.width,
                        texture.height - torsoStart),
                    new Vector2(0.5f, 0.5f));
            }
        }
    }
}
