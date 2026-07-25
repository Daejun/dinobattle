using DinoBattle.Core;
using DinoBattle.Placement;
using UnityEngine;
using UnityEngine.UI;

namespace DinoBattle.UI
{
    /// <summary>
    /// Wires the on-screen controls to the battle manager. Every field is optional so the HUD can be
    /// built up piece by piece — a missing button just means that control is not available yet.
    ///
    /// Uses the built-in UI package (uGUI). Swap Text for TMP_Text once TextMeshPro is imported.
    /// </summary>
    public class BattleHUD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BattleManager battleManager;
        [SerializeField] private PlacementController placement;

        [Header("Panels")]
        [SerializeField] private GameObject placementPanel;
        [SerializeField] private GameObject fightingPanel;
        [SerializeField] private GameObject resultPanel;

        [Header("Placement controls")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button undoButton;
        [SerializeField] private Button teamToggleButton;
        [SerializeField] private Text teamLabel;
        [SerializeField] private Text budgetLabel;

        [Header("Roster list")]
        [Tooltip("Parent the roster buttons are generated under. One button per creature definition.")]
        [SerializeField] private Transform rosterContainer;
        [SerializeField] private Button rosterButtonTemplate;

        [Header("Fight controls")]
        [SerializeField] private Button speedButton;
        [SerializeField] private Text speedLabel;
        [SerializeField] private Text redCountLabel;
        [SerializeField] private Text blueCountLabel;

        [Header("Result")]
        [SerializeField] private Text winnerLabel;
        [SerializeField] private Button rematchButton;

        private void Awake()
        {
            if (battleManager == null) battleManager = BattleManager.Instance;
            if (placement == null) placement = FindAnyObjectByType<PlacementController>();
        }

        private void OnEnable()
        {
            if (battleManager == null) return;

            battleManager.PhaseChanged += HandlePhaseChanged;
            battleManager.BattleEnded += HandleBattleEnded;
            battleManager.UnitCountChanged += RefreshCounts;

            HookButton(startButton, () => battleManager.StartBattle());
            HookButton(undoButton, () => placement?.UndoLast());
            HookButton(teamToggleButton, () => { placement?.ToggleActiveTeam(); RefreshPlacementLabels(); });
            HookButton(speedButton, () => { battleManager.CycleSpeed(); RefreshSpeedLabel(); });
            HookButton(rematchButton, () => battleManager.EnterPlacement());

            BuildRosterButtons();
            HandlePhaseChanged(battleManager.Phase);
        }

        private void OnDisable()
        {
            if (battleManager == null) return;

            battleManager.PhaseChanged -= HandlePhaseChanged;
            battleManager.BattleEnded -= HandleBattleEnded;
            battleManager.UnitCountChanged -= RefreshCounts;
        }

        private void Update()
        {
            if (battleManager == null) return;
            if (battleManager.Phase == BattlePhase.Placement) RefreshPlacementLabels();
        }

        // ---------------------------------------------------------------- phase handling

        private void HandlePhaseChanged(BattlePhase phase)
        {
            SetActive(placementPanel, phase == BattlePhase.Placement);
            SetActive(fightingPanel, phase == BattlePhase.Fighting);
            SetActive(resultPanel, phase == BattlePhase.Finished);

            RefreshPlacementLabels();
            RefreshSpeedLabel();
            RefreshCounts();
        }

        private void HandleBattleEnded(Team winner)
        {
            if (winnerLabel == null) return;

            winnerLabel.text = winner == Team.Neutral ? "DRAW" : $"{winner.ToString().ToUpperInvariant()} WINS";
        }

        // ---------------------------------------------------------------- roster

        private void BuildRosterButtons()
        {
            if (rosterContainer == null || rosterButtonTemplate == null) return;
            if (battleManager?.Roster == null) return;

            // Rebuild from scratch; the roster can change between scene loads.
            for (int i = rosterContainer.childCount - 1; i >= 0; i--)
            {
                var child = rosterContainer.GetChild(i).gameObject;
                if (child != rosterButtonTemplate.gameObject) Destroy(child);
            }

            rosterButtonTemplate.gameObject.SetActive(false);

            foreach (var definition in battleManager.Roster.Creatures)
            {
                if (definition == null) continue;

                var button = Instantiate(rosterButtonTemplate, rosterContainer);
                button.gameObject.SetActive(true);
                button.name = $"Btn_{definition.name}";

                var label = button.GetComponentInChildren<Text>();
                if (label != null) label.text = $"{definition.displayName}\n{definition.cost}";

                var image = button.transform.Find("Icon")?.GetComponent<Image>();
                if (image != null && definition.icon != null) image.sprite = definition.icon;

                var captured = definition;
                button.onClick.AddListener(() => placement?.Select(captured));
            }
        }

        // ---------------------------------------------------------------- label refresh

        private void RefreshPlacementLabels()
        {
            if (placement == null || battleManager == null) return;

            Team team = placement.ActiveTeam;

            if (teamLabel != null) teamLabel.text = team.ToString().ToUpperInvariant();
            if (budgetLabel != null)
            {
                budgetLabel.text = $"{battleManager.Loadout.RemainingFor(team)} / {battleManager.Loadout.BudgetPerTeam}";
            }

            if (startButton != null) startButton.interactable = battleManager.Loadout.IsReadyToFight;
        }

        private void RefreshSpeedLabel()
        {
            if (speedLabel == null || battleManager == null) return;
            speedLabel.text = $"x{battleManager.SimulationSpeed:0.##}";
        }

        private void RefreshCounts()
        {
            if (battleManager == null) return;

            if (redCountLabel != null) redCountLabel.text = battleManager.AliveCount(Team.Red).ToString();
            if (blueCountLabel != null) blueCountLabel.text = battleManager.AliveCount(Team.Blue).ToString();
        }

        // ---------------------------------------------------------------- helpers

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        private static void HookButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }
    }
}
