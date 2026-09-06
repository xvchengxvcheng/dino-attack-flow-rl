using System.Globalization;
using LlamAcademy.Dinos.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.UI
{
    [DisallowMultipleComponent]
    public sealed class GameComparisonPresenter : MonoBehaviour
    {
        private VisualElement Panel;
        private Label Status;
        private Label HumanResult;
        private Label AiResult;

        public void Configure(VisualElement root)
        {
            Panel = root?.Q("comparison-panel");
            Status = root?.Q<Label>("comparison-status");
            HumanResult = root?.Q<Label>("human-comparison-result");
            AiResult = root?.Q<Label>("ai-comparison-result");
            Hide();
        }

        public void PresentComparison(GameResultComparison comparison)
        {
            if (!IsConfigured)
            {
                return;
            }

            Status.text = "Same layout · Player vs AI";
            HumanResult.text = Format("Player", comparison.Human);
            AiResult.text = Format("AI", comparison.InferenceAi);
            Panel.style.display = DisplayStyle.Flex;
        }

        public void PresentRejected(GameResultPairingFailure failure)
        {
            if (!IsConfigured)
            {
                return;
            }

            HumanResult.text = string.Empty;
            AiResult.text = string.Empty;
            Status.text = failure switch
            {
                GameResultPairingFailure.LayoutSignatureMismatch =>
                    "Cannot compare as the same layout: layout signature mismatch.",
                GameResultPairingFailure.SceneMismatch =>
                    "Cannot compare as the same layout: scene mismatch.",
                GameResultPairingFailure.MissingPlayerResult =>
                    "Cannot compare: the player result is unavailable.",
                _ => "Cannot compare these results.",
            };
            Panel.style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            if (Panel != null)
            {
                Panel.style.display = DisplayStyle.None;
            }
            if (Status != null)
            {
                Status.text = string.Empty;
            }
            if (HumanResult != null)
            {
                HumanResult.text = string.Empty;
            }
            if (AiResult != null)
            {
                AiResult.text = string.Empty;
            }
        }

        private bool IsConfigured =>
            Panel != null && Status != null && HumanResult != null && AiResult != null;

        private static string Format(string heading, GameResultSnapshot snapshot) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}\n{1} · {2:0.00}s\nMeat {3} · Reward {4:0.00}",
                heading,
                snapshot.Won ? "Victory" : "Defeat",
                snapshot.ElapsedSeconds,
                snapshot.FinalMeat,
                snapshot.TotalReward);
    }
}
