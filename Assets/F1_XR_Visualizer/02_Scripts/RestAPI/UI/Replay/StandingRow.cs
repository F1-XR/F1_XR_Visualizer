using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace F1XR.RestAPI.UI
{
    internal sealed class StandingRow
    {
        public GameObject Root { get; }
        public int DriverNumber { get; set; }

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

        public StandingRow(
            GameObject root,
            RectTransform rect,
            Image background,
            Outline outline,
            Color defaultBackground,
            TMP_Text position,
            Image teamBar,
            TMP_Text driver,
            Image tireDot,
            TMP_Text tire,
            TMP_Text age,
            TMP_Text change)
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
                outline.effectColor =
                    WithAlpha(teamColor, 0.78f);
            }

            if (teamBarLayout != null)
            {
                teamBarLayout.preferredWidth =
                    selected ? 5f : 3f;
            }

            background.color = selected
                ? SelectionBackground(teamColor)
                : defaultBackground;

            teamBar.color = teamColor;
        }

        public void SetSlot(
            int slotIndex,
            float rowHeight,
            float moveSpeed)
        {
            Vector2 target = new(
                8f,
                RowY(slotIndex, rowHeight));

            if (!hasSlot)
            {
                rect.anchoredPosition = target;
                hasSlot = true;
                return;
            }

            float t =
                1f - Mathf.Exp(
                    -moveSpeed * Time.deltaTime);

            rect.anchoredPosition =
                Vector2.Lerp(
                    rect.anchoredPosition,
                    target,
                    t);
        }

        public void Set(
            int rank,
            string driverLabel,
            Color teamColor,
            string tireSymbol,
            Color tireColor,
            string tireAge,
            PositionChange positionChange)
        {
            position.text = rank.ToString();
            driver.text = driverLabel;
            this.teamColor = teamColor;
            teamBar.color = teamColor;

            if (outline != null)
            {
                outline.effectColor =
                    WithAlpha(teamColor, 0.78f);
            }

            tireDot.color = tireColor;
            tire.text = string.IsNullOrWhiteSpace(tireSymbol)
                ? "-"
                : tireSymbol;
            age.text = tireAge;

            if (positionChange != null)
            {
                bool improved = positionChange.improved;

                background.color = improved
                    ? new Color(0.02f, 0.28f, 0.13f, 0.96f)
                    : new Color(0.42f, 0.04f, 0.06f, 0.96f);

                change.text = improved
                    ? $"+{positionChange.places}"
                    : $"-{positionChange.places}";

                change.color = improved
                    ? new Color(0.25f, 1f, 0.45f)
                    : new Color(1f, 0.22f, 0.18f);

                return;
            }

            change.text = "";

            background.color = selected
                ? SelectionBackground(teamColor)
                : rank == 1
                    ? new Color(0.18f, 0.02f, 0.035f, 0.95f)
                    : defaultBackground;
        }

        private static float RowY(
            int slotIndex,
            float rowHeight)
        {
            return -50f -
                slotIndex * (rowHeight + 2f);
        }

        private static Color SelectionBackground(
            Color color)
        {
            return new Color(
                Mathf.Lerp(0.025f, color.r, 0.24f),
                Mathf.Lerp(0.03f, color.g, 0.24f),
                Mathf.Lerp(0.04f, color.b, 0.24f),
                0.98f);
        }

        private static Color WithAlpha(
            Color color,
            float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}