using System.Collections.Generic;
using DinoBattle.Core;
using DinoBattle.Data;
using DinoBattle.Placement;
using DinoBattle.Units;
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
        [SerializeField] private AutoPlacer autoPlacer;
        [SerializeField] private Button autoFillButton;
        [SerializeField] private Button mirrorButton;
        [SerializeField] private Button startButton;

        [Tooltip("Sets up one boss against a pack, instead of two even teams.")]
        [SerializeField] private Button bossButton;
        [SerializeField] private Button undoButton;
        [SerializeField] private Button teamToggleButton;
        [SerializeField] private Text teamLabel;
        [SerializeField] private Text budgetLabel;

        [Header("Roster list")]
        [Tooltip("Parent the roster buttons are generated under. One button per creature definition.")]
        [SerializeField] private Transform rosterContainer;
        [SerializeField] private Button rosterButtonTemplate;

        [Header("Fight controls")]
        [Tooltip("Speed control. Left in place but no longer built into the scene — matches resolve " +
                 "in well under a minute, and a button that fast-forwards the thing you came to watch " +
                 "earns little. Wire it back up in BattleSceneBuilder if that changes.")]
        [SerializeField] private Button speedButton;
        [SerializeField] private Text speedLabel;

        [Tooltip("Mid-fight: run the same match again from the start.")]
        [SerializeField] private Button fightReplayButton;

        [Tooltip("Mid-fight: stop and go back to setup.")]
        [SerializeField] private Button fightQuitButton;
        [SerializeField] private Text redCountLabel;
        [SerializeField] private Text blueCountLabel;

        [Header("Result")]
        [SerializeField] private Text winnerLabel;

        [Tooltip("Line under the winner: survivors and how much health they finished on. Optional, " +
                 "like every other reference here.")]
        [SerializeField] private Text resultSummaryLabel;
        [SerializeField] private Button rematchButton;

        [Tooltip("Runs the same two armies again, rather than returning to setup.")]
        [SerializeField] private Button replayButton;

        /// <summary>Buttons this HUD instantiated, so a rebuild can clean up exactly what it created.</summary>
        private readonly List<Button> spawnedRosterButtons = new();

        /// <summary>Definition behind each spawned button, index-aligned with the list above.</summary>
        private readonly List<CreatureDefinition> rosterOrder = new();

        private static readonly Color SelectedTint = new(0.35f, 0.62f, 0.42f, 0.98f);
        private static readonly Color UnselectedTint = new(0.20f, 0.24f, 0.32f, 0.95f);

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

            HookButton(autoFillButton, () => autoPlacer?.FillBothTeams());
            HookButton(mirrorButton, () => autoPlacer?.MirrorMatch());
            HookButton(startButton, () => battleManager.StartBattle());
            HookButton(undoButton, () => placement?.UndoLast());
            HookButton(teamToggleButton, () => { placement?.ToggleActiveTeam(); RefreshPlacementLabels(); });
            HookButton(speedButton, () => { battleManager.CycleSpeed(); RefreshSpeedLabel(); });
            HookButton(rematchButton, () => battleManager.EnterPlacement());
            HookButton(replayButton, () => battleManager.Replay());
            HookButton(bossButton, () => autoPlacer?.BossBattle());
            HookButton(fightReplayButton, () => battleManager.Replay());
            HookButton(fightQuitButton, () => battleManager.EnterPlacement());

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
            if (winnerLabel != null)
            {
                winnerLabel.text = winner == Team.Neutral ? "무승부" : $"{TeamName(winner)} 승리";
            }

            if (resultSummaryLabel == null) return;

            // Who is left, and in what shape. The bare result told a player nothing about whether the
            // match was a rout or came down to one wounded survivor, which is most of what makes a
            // spectator match worth watching to the end.
            int survivors = UnitRegistry.AliveCount(winner);

            if (winner == Team.Neutral || survivors == 0)
            {
                resultSummaryLabel.text = "생존자 없음";
                return;
            }

            float remaining = 0f;
            float capacity = 0f;

            foreach (var unit in UnitRegistry.AliveOf(winner))
            {
                if (unit == null || unit.IsDead || unit.Health == null) continue;

                remaining += unit.Health.Current;
                capacity += unit.Health.Max;
            }

            int percent = capacity > 0f ? Mathf.RoundToInt(remaining / capacity * 100f) : 0;

            resultSummaryLabel.text = $"{survivors}마리 생존 · 체력 {percent}%";
        }

        // ---------------------------------------------------------------- roster

        private void BuildRosterButtons()
        {
            if (rosterContainer == null || rosterButtonTemplate == null) return;
            if (battleManager?.Roster == null) return;

            // Rebuild from scratch; the roster can change between scene loads.
            //
            // Track what we spawned rather than walking rosterContainer's children. Destroy() is
            // deferred to the end of the frame, so a child-enumeration cleanup still sees the old
            // buttons if this runs twice before then -- which happened, and left two full sets of
            // buttons live. Detaching immediately makes the cleanup take effect right away.
            foreach (var stale in spawnedRosterButtons)
            {
                if (stale == null) continue;
                stale.transform.SetParent(null, false);
                Destroy(stale.gameObject);
            }
            spawnedRosterButtons.Clear();
            rosterOrder.Clear();

            rosterButtonTemplate.gameObject.SetActive(false);

            foreach (var definition in battleManager.Roster.Creatures)
            {
                if (definition == null) continue;

                var button = Instantiate(rosterButtonTemplate, rosterContainer);
                button.gameObject.SetActive(true);
                button.name = $"Btn_{definition.name}";
                spawnedRosterButtons.Add(button);
                rosterOrder.Add(definition);
                if (button.targetGraphic is Image swatch) swatch.color = UnselectedTint;

                var label = button.GetComponentInChildren<Text>();
                if (label != null) label.text = $"{definition.displayName}\n{definition.cost}";

                // Optional: the generated template has no "Icon" child yet, so this is inert until
                // real creature art lands and BattleSceneBuilder.CreateButton gains one. Guarded
                // rather than assumed so adding the child is the only change needed.
                var image = button.transform.Find("Icon")?.GetComponent<Image>();
                if (image != null && definition.icon != null) image.sprite = definition.icon;

                var captured = definition;
                button.onClick.AddListener(() =>
                {
                    placement?.Select(captured);
                    HighlightSelected(captured);
                });
            }
        }

        /// <summary>
        /// Tint the chosen roster entry and dim the rest.
        ///
        /// Without this, tapping a creature changed nothing on screen — the selection was recorded but
        /// invisible, so the button read as broken and there was no way to tell what would be placed.
        /// </summary>
        private void HighlightSelected(CreatureDefinition chosen)
        {
            for (int i = 0; i < spawnedRosterButtons.Count; i++)
            {
                var button = spawnedRosterButtons[i];
                if (button == null) continue;

                bool isChosen = i < rosterOrder.Count && rosterOrder[i] == chosen;
                if (button.targetGraphic is Image image) image.color = isChosen ? SelectedTint : UnselectedTint;
            }
        }

        // ---------------------------------------------------------------- label refresh

        private void RefreshPlacementLabels()
        {
            if (placement == null || battleManager == null) return;

            Team team = placement.ActiveTeam;

            if (teamLabel != null) teamLabel.text = TeamName(team);
            if (budgetLabel != null)
            {
                budgetLabel.text = $"{battleManager.Loadout.RemainingFor(team)} / {battleManager.Loadout.BudgetPerTeam}";
            }

            if (startButton != null) startButton.interactable = battleManager.Loadout.IsReadyToFight;
        }

        private void RefreshSpeedLabel()
        {
            if (speedLabel == null || battleManager == null) return;
            speedLabel.text = $"{battleManager.SimulationSpeed:0.##}배속";
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

        /// <summary>
        /// Team names as the player sees them. The enum is English because the code is; the screen
        /// is Korean because the players are, and the two do not have to be the same string.
        /// </summary>
        private static string TeamName(Team team) => team switch
        {
            Team.Red => "빨강",
            Team.Blue => "파랑",
            _ => "중립"
        };

        private static void HookButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }
    }
}
