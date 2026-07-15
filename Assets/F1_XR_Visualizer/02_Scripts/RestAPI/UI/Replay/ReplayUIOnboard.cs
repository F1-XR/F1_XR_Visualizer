using TMPro;
using UnityEngine;
using UnityEngine.UI;
using F1XR.RestAPI.Replay;

namespace F1XR.RestAPI.UI
{
    public partial class ReplayUI
    {
        private void EnsureDriverOnboardPanel()
        {
            if (driverOnboardRoot != null ||
                standingsRoot == null)
                return;

            driverOnboardRoot = new GameObject(
                "DriverOnboardPanel",
                typeof(RectTransform),
                typeof(Image))
                .GetComponent<RectTransform>();

            driverOnboardRoot.SetParent(
                standingsRoot.parent,
                false);

            driverOnboardRoot.anchorMin =
                standingsRoot.anchorMin;

            driverOnboardRoot.anchorMax =
                standingsRoot.anchorMax;

            driverOnboardRoot.pivot =
                standingsRoot.pivot;

            driverOnboardRoot.anchoredPosition =
                standingsRoot.anchoredPosition +
                new Vector2(
                    standingsSize.x + 8f,
                    -driverDetailSize.y - 8f);

            driverOnboardRoot.sizeDelta =
                driverOnboardSize;

            driverOnboardRoot.localRotation =
                standingsRoot.localRotation;

            driverOnboardRoot.localScale =
                Vector3.one;

            driverOnboardRoot.SetSiblingIndex(
                driverDetailRoot != null
                    ? driverDetailRoot.GetSiblingIndex() + 1
                    : standingsRoot.GetSiblingIndex() + 1);

            Image panel =
                driverOnboardRoot.GetComponent<Image>();

            panel.color =
                new Color(0.015f, 0.018f, 0.026f, 0.92f);

            driverOnboardTitle = CreateText(
                "Header",
                driverOnboardRoot,
                13,
                FontStyles.Bold,
                TextAlignmentOptions.Left);

            driverOnboardTitle.color =
                new Color(0.82f, 0.86f, 0.92f);

            driverOnboardTitle.text = "ONBOARD";

            SetRect(
                driverOnboardTitle.rectTransform,
                10f,
                -7f,
                driverOnboardSize.x - 42f,
                20f);

            Button close = new GameObject(
                "Close",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button))
                .GetComponent<Button>();

            close.transform.SetParent(
                driverOnboardRoot,
                false);

            SetRect(
                close.GetComponent<RectTransform>(),
                driverOnboardSize.x - 26f,
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

            Image frame = new GameObject(
                "ViewportFrame",
                typeof(RectTransform),
                typeof(Image))
                .GetComponent<Image>();

            frame.transform.SetParent(
                driverOnboardRoot,
                false);

            frame.color =
                new Color(0f, 0f, 0f, 0.65f);

            SetRect(
                frame.rectTransform,
                8f,
                -32f,
                driverOnboardSize.x - 16f,
                driverOnboardSize.y - 40f);

            driverOnboardImage = new GameObject(
                "Viewport",
                typeof(RectTransform),
                typeof(RawImage))
                .GetComponent<RawImage>();

            driverOnboardImage.transform.SetParent(
                frame.transform,
                false);

            driverOnboardImage.color = Color.white;
            driverOnboardImage.rectTransform.anchorMin =
                Vector2.zero;
            driverOnboardImage.rectTransform.anchorMax =
                Vector2.one;
            driverOnboardImage.rectTransform.offsetMin =
                new Vector2(2f, 2f);
            driverOnboardImage.rectTransform.offsetMax =
                new Vector2(-2f, -2f);

            EnsureDriverOnboardView();
            driverOnboardRoot.gameObject.SetActive(false);
        }

        private void EnsureDriverOnboardView()
        {
            if (driverOnboardView == null)
            {
                Transform parent =
                    player != null
                        ? player.transform
                        : transform;

                GameObject obj =
                    new GameObject("DriverOnboardView");

                obj.transform.SetParent(parent, false);

                driverOnboardView =
                    obj.AddComponent<DriverOnboardView>();
            }

            if (driverOnboardImage == null)
                return;

            driverOnboardView.SetOutput(
                driverOnboardImage,
                Mathf.RoundToInt(
                    driverOnboardTextureSize.x),
                Mathf.RoundToInt(
                    driverOnboardTextureSize.y));
        }

        private void RefreshDriverOnboard()
        {
            if (selectedDriverNumber <= 0 ||
                player == null)
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
            {
                driverOnboardTitle.text =
                    $"ONBOARD  " +
                    player.GetDriverLabel(
                        selectedDriverNumber);
            }

            if (player.TryGetCarTransform(
                    selectedDriverNumber,
                    out Transform carTransform))
            {
                driverOnboardView.Show(carTransform);
            }
            else
            {
                driverOnboardView.Hide();
            }
        }
    }
}