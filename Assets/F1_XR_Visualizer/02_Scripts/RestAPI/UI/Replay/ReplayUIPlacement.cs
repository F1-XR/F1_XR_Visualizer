using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace F1XR.RestAPI.UI
{
    public partial class ReplayUI
    {
        RectTransform placementControls;
        TMP_Text placementStatus;
        Button placementAutomaticButton;
        Button placementFreeButton;
        Button placementPrimaryButton;
        Button placementSecondaryButton;
        Button placementResetButton;
        float lastPlacementActionTime = float.NegativeInfinity;

        void EnsurePlacementControls()
        {
            if (placementControls != null)
                return;

            placementControls = new GameObject(
                "TrackPlacementControls",
                typeof(RectTransform),
                typeof(Image))
                .GetComponent<RectTransform>();
            placementControls.SetParent(transform, false);
            placementControls.anchorMin = new Vector2(0.5f, 1f);
            placementControls.anchorMax = new Vector2(0.5f, 1f);
            placementControls.pivot = new Vector2(0.5f, 1f);
            placementControls.anchoredPosition = new Vector2(0f, 96f);
            placementControls.sizeDelta = new Vector2(300f, 136f);
            placementControls.GetComponent<Image>().color = new Color(0.015f, 0.018f, 0.026f, 0.92f);

            placementStatus = CreateText(
                "Status",
                placementControls,
                15,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            SetRect(placementStatus.rectTransform, 8f, -8f, 284f, 28f);

            placementAutomaticButton = CreatePlacementButton(
                "TableAutomatic",
                8f,
                -44f,
                137f,
                OnPlacementAutomatic);
            placementFreeButton = CreatePlacementButton(
                "Free",
                155f,
                -44f,
                137f,
                OnPlacementFree);
            placementPrimaryButton = CreatePlacementButton("Primary", 8f, -88f, 88f, OnPlacementPrimary);
            placementSecondaryButton = CreatePlacementButton("Secondary", 106f, -88f, 88f, OnPlacementSecondary);
            placementResetButton = CreatePlacementButton("Reset", 204f, -88f, 88f, OnPlacementReset);
            RefreshPlacementControls();
        }

        Button CreatePlacementButton(
            string name,
            float x,
            float y,
            float width,
            UnityEngine.Events.UnityAction action)
        {
            Button button = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button))
                .GetComponent<Button>();
            button.transform.SetParent(placementControls, false);
            SetRect(button.GetComponent<RectTransform>(), x, y, width, 38f);
            button.targetGraphic = button.GetComponent<Image>();
            button.onClick.AddListener(action);

            TMP_Text label = CreateText(
                "Label",
                button.transform,
                15,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            StyleButton(button);
            return button;
        }

        void RefreshPlacementControls()
        {
            if (placementControls == null || player == null)
                return;

            bool placed = player.IsTrackPlaced;
            bool edit = player.IsTrackEditMode;
            bool automatic = player.IsAutomaticTrackPlacement;

            SetModeButton(placementAutomaticButton, "TABLE AUTO", automatic, !placed);
            SetModeButton(placementFreeButton, "FREE", !automatic, !placed);

            if (!placed)
            {
                if (!player.IsTrackPlacementActive)
                {
                    placementStatus.text = "Placement paused";
                    SetButton(placementPrimaryButton, "Resume", true);
                }
                else if (player.HasValidTrackSurface)
                {
                    placementStatus.text = automatic
                        ? "Table ready — centered and fitted automatically"
                        : "Free mode — place at the pointed position";
                    SetButton(placementPrimaryButton, "Confirm", true);
                }
                else
                {
                    placementStatus.text = "Point at a table";
                    SetButton(placementPrimaryButton, "Confirm", false);
                }

                SetButton(placementSecondaryButton, "Cancel", player.IsTrackPlacementActive);
                SetButton(placementResetButton, "Reset", false);
                return;
            }

            placementStatus.text = edit
                ? "EDIT MODE — grab to move/rotate, use two hands to scale"
                : "Track ready — vehicle and replay controls are active";
            SetButton(placementPrimaryButton, edit ? "Done" : "Edit", true);
            SetButton(placementSecondaryButton, "Undo", edit && player.CanUndoTrackManipulation);
            SetButton(placementResetButton, "Reset", true);
        }

        static void SetButton(Button button, string label, bool interactable)
        {
            if (button == null)
                return;

            button.interactable = interactable;
            TMP_Text text = button.GetComponentInChildren<TMP_Text>();
            if (text != null)
                text.text = label;
        }

        static void SetModeButton(Button button, string label, bool selected, bool interactable)
        {
            SetButton(button, label, interactable);
            if (button == null)
                return;

            ColorBlock colors = button.colors;
            colors.normalColor = selected
                ? new Color(0.62f, 0.04f, 0.07f, 1f)
                : new Color(0.09f, 0.1f, 0.13f, 0.95f);
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
        }

        bool CanRunPlacementAction()
        {
            if (Time.unscaledTime - lastPlacementActionTime < 0.2f)
                return false;

            lastPlacementActionTime = Time.unscaledTime;
            return true;
        }

        void OnPlacementPrimary()
        {
            if (player == null || !CanRunPlacementAction())
                return;

            if (player.IsTrackPlaced)
                player.ToggleTrackEditMode();
            else if (player.IsTrackPlacementActive)
                player.ConfirmTrackPlacement();
            else
                player.BeginTrackPlacement();
        }

        void OnPlacementAutomatic()
        {
            if (player == null || player.IsTrackPlaced || !CanRunPlacementAction())
                return;

            player.SetAutomaticTrackPlacement(true);
            RefreshPlacementControls();
        }

        void OnPlacementFree()
        {
            if (player == null || player.IsTrackPlaced || !CanRunPlacementAction())
                return;

            player.SetAutomaticTrackPlacement(false);
            RefreshPlacementControls();
        }

        void OnPlacementSecondary()
        {
            if (player == null || !CanRunPlacementAction())
                return;

            if (player.IsTrackPlaced)
                player.UndoTrackManipulation();
            else
                player.CancelTrackPlacement();
        }

        void OnPlacementReset()
        {
            if (player == null || !CanRunPlacementAction())
                return;

            player.ResetTrackPlacement();
        }
    }
}
