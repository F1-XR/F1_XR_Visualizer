using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace F1XR.RestAPI.UI
{
    public partial class ReplayUI
    {
        private void StyleControls()
        {
            if (controlsStyled)
                return;

            controlsStyled = true;

            StyleControlPanel();
            StyleButton(playPauseButton);
            StyleDropdown(speedDropdown);
            StyleReplayBar();

            if (timeLabel != null)
            {
                timeLabel.fontSize = 28f;
                timeLabel.fontStyle = FontStyles.Bold;
                timeLabel.color = new Color(0.92f, 0.94f, 0.98f);
                timeLabel.alignment = TextAlignmentOptions.Center;
            }
        }

        private void StyleControlPanel()
        {
            Transform parent = playPauseButton != null
                ? playPauseButton.transform.parent
                : transform;

            if (parent == null)
                return;

            Image panel = parent.GetComponent<Image>();
            if (panel == null)
                return;

            panel.color = new Color(0.015f, 0.018f, 0.026f, 0.82f);
        }

        private static void StyleButton(Button button)
        {
            if (button == null)
                return;

            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color(0.09f, 0.1f, 0.13f, 0.95f);
                button.targetGraphic = image;
            }

            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.09f, 0.1f, 0.13f, 0.95f);
            colors.highlightedColor = new Color(0.16f, 0.18f, 0.24f, 0.98f);
            colors.pressedColor = new Color(0.62f, 0.04f, 0.07f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.05f, 0.055f, 0.07f, 0.65f);
            button.colors = colors;

            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label == null)
                return;

            label.fontSize = 20f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.92f, 0.94f, 0.98f);
            label.alignment = TextAlignmentOptions.Center;
        }

        private static void StyleDropdown(TMP_Dropdown dropdown)
        {
            if (dropdown == null)
                return;

            if (dropdown.targetGraphic is Image target)
                target.color = new Color(0.09f, 0.1f, 0.13f, 0.95f);

            ColorBlock colors = dropdown.colors;
            colors.normalColor = new Color(0.09f, 0.1f, 0.13f, 0.95f);
            colors.highlightedColor = new Color(0.16f, 0.18f, 0.24f, 0.98f);
            colors.pressedColor = new Color(0.62f, 0.04f, 0.07f, 1f);
            colors.selectedColor = colors.highlightedColor;
            dropdown.colors = colors;

            if (dropdown.captionText != null)
            {
                dropdown.captionText.fontSize = 16f;
                dropdown.captionText.fontStyle = FontStyles.Bold;
                dropdown.captionText.color = new Color(0.92f, 0.94f, 0.98f);
            }

            if (dropdown.itemText != null)
            {
                dropdown.itemText.fontSize = 15f;
                dropdown.itemText.color = new Color(0.92f, 0.94f, 0.98f);
            }

            StyleDropdownTemplate(dropdown);
        }

        private static void StyleDropdownTemplate(TMP_Dropdown dropdown)
        {
            if (dropdown.template == null)
                return;

            foreach (Image image in dropdown.template.GetComponentsInChildren<Image>(true))
            {
                image.color = image.gameObject.name.Contains("Checkmark")
                    ? new Color(0.92f, 0.94f, 0.98f, 1f)
                    : new Color(0.05f, 0.055f, 0.075f, 0.98f);
            }

            foreach (TMP_Text text in dropdown.template.GetComponentsInChildren<TMP_Text>(true))
            {
                text.fontSize = 15f;
                text.color = new Color(0.92f, 0.94f, 0.98f);
            }
        }

        private void StyleReplayBar()
        {
            if (bar == null)
                return;

            Image background = bar.barRect != null
                ? bar.barRect.GetComponent<Image>()
                : bar.GetComponent<Image>();

            if (background != null)
                background.color = new Color(0.11f, 0.12f, 0.15f, 0.92f);

            if (bar.loadedFill != null)
                bar.loadedFill.color = new Color(0.62f, 0.64f, 0.68f, 0.88f);

            if (bar.playedFill != null)
                bar.playedFill.color = new Color(0.95f, 0.08f, 0.08f, 0.96f);
        }
    }
}
