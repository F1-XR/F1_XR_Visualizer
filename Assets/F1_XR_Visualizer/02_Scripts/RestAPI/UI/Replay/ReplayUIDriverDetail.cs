using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using F1XR.RestAPI.Api;

namespace F1XR.RestAPI.UI
{
    public partial class ReplayUI
    {
        private void EnsureDriverDetailPanel()
        {
            if (driverDetailRoot != null ||
                standingsRoot == null)
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
                driverDetailSize;

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
                driverDetailSize.x - 26f,
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

        private void ShowDriverDetail(int driverNumber)
        {
            if (driverNumber <= 0)
                return;

            selectedDriverNumber = driverNumber;

            if (player != null)
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
    }
}