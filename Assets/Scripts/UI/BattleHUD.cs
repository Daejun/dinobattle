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

        [Header("Gauntlet")]
        [Tooltip("Mode toggle, shown during placement only.")]
        [SerializeField] private GameObject modePanel;
        [SerializeField] private Button versusModeButton;
        [SerializeField] private Button gauntletModeButton;

        [Tooltip("The two team bars and their counts. Hidden in gauntlet mode, where neither can " +
                 "mean anything — see HandleModeChanged.")]
        [SerializeField] private GameObject versusReadouts;

        [Tooltip("Tier and budget readout, shown while a climb is running.")]
        [SerializeField] private GameObject gauntletPanel;
        [SerializeField] private Text tierLabel;
        [SerializeField] private Button sendWaveButton;

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

        [Tooltip("Filled Images showing each team's remaining share of its starting health. Optional, " +
                 "like every other reference here — the HUD assembles from whatever is wired up.")]
        [SerializeField] private Image redHealthFill;
        [SerializeField] private Image blueHealthFill;

        [Tooltip("Narrowest a team bar is allowed to get while that team still has anything alive. " +
                 "Without a floor the bar disappears precisely when the match is at its most " +
                 "interesting.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float minimumTeamFill = 0.08f;

        private Color redBaseColor;
        private Color blueBaseColor;

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

            HookButton(versusModeButton, () => battleManager.SetMode(GameMode.Versus));
            HookButton(gauntletModeButton, () => battleManager.SetMode(GameMode.Gauntlet));
            HookButton(sendWaveButton, () => battleManager.SendGauntletWave());

            battleManager.ModeChanged += HandleModeChanged;

            BuildRosterButtons();
            HandlePhaseChanged(battleManager.Phase);
            HandleModeChanged(battleManager.Mode);
        }

        private void OnDisable()
        {
            if (battleManager == null) return;

            battleManager.PhaseChanged -= HandlePhaseChanged;
            battleManager.BattleEnded -= HandleBattleEnded;
            battleManager.UnitCountChanged -= RefreshCounts;
            battleManager.ModeChanged -= HandleModeChanged;
        }

        private void Update()
        {
            if (battleManager == null) return;
            if (battleManager.Phase == BattlePhase.Placement) RefreshPlacementLabels();
            else RefreshTeamHealth();

            if (battleManager.Mode == GameMode.Gauntlet) RefreshGauntlet();
        }

        // ---------------------------------------------------------------- gauntlet

        private void HandleModeChanged(GameMode mode)
        {
            bool gauntlet = mode == GameMode.Gauntlet;

            // The mode bar belongs to setup only — switching arenas mid-climb would deactivate the
            // ground the creatures are standing on.
            SetActive(modePanel, battleManager.Phase == BattlePhase.Placement);
            SetActive(gauntletPanel, gauntlet && battleManager.Phase != BattlePhase.Placement);

            // The send button is not parented to the gauntlet strip — it sits over the arena so it
            // can be big enough to hit — so leaving the mode has to take it down explicitly.
            if (!gauntlet && sendWaveButton != null) SetActive(sendWaveButton.gameObject, false);

            // Versus-only controls. A boss battle arranges two armies on the round arena, which is
            // not the board, and the fight-bar replay restarts a match rather than a run.
            SetActive(bossButton != null ? bossButton.gameObject : null, !gauntlet);

            // The team bars and counts come off entirely in gauntlet mode.
            //
            // Reported: "총 체력이랑 공룡 수가 전혀 안맞음 그냥 빼는것도?" — and they were right, both
            // numbers were meaningless rather than merely odd. The health bars divide by a starting
            // total recorded in the versus start path, which a climb never runs, so there is no
            // denominator. And "blue" is not an army here, it is whichever single tier happens to be
            // awake, so its count jumps from zero to five and back as the wave climbs.
            //
            // Neither is worth repairing, because neither question applies: a run is not two sides
            // grinding each other down, it is how far up you got and what you have left to spend,
            // which is exactly what the gauntlet strip already says.
            SetActive(versusReadouts, !gauntlet);
            SetActive(redCountLabel != null ? redCountLabel.gameObject : null, !gauntlet);
            SetActive(blueCountLabel != null ? blueCountLabel.gameObject : null, !gauntlet);

            HighlightMode(versusModeButton, !gauntlet);
            HighlightMode(gauntletModeButton, gauntlet);
        }

        /// <summary>Selected mode reads as pressed, so the toggle shows its own state.</summary>
        private static void HighlightMode(Button button, bool selected)
        {
            if (button == null) return;

            var image = button.GetComponent<Image>();
            if (image == null) return;

            image.color = selected ? new Color(0.30f, 0.52f, 0.34f, 0.95f)
                                   : new Color(0.14f, 0.16f, 0.20f, 0.80f);
        }

        private void RefreshGauntlet()
        {
            var run = battleManager.Gauntlet;
            if (run == null) return;

            // The tier number is painted on the deck now, so it is not repeated here — the board says
            // where you are, and the strip only has to say what you have left. What remains is the
            // survivor count, which nothing in the world can show.
            if (tierLabel != null)
            {
                tierLabel.text = run.State == GauntletState.Cleared
                    ? "클리어!"
                    : $"<color=#7ad07a>{battleManager.AliveCount(Team.Red)}</color> 마리";
            }

            // Only offer another wave when there is nobody left to send it after, and only when it
            // can actually be paid for. An always-live button would let the player stack waves and
            // the climb would stop being a climb.
            //
            // Hidden rather than greyed out. It sits over the arena, and a permanent dead button
            // covering the fight is the same complaint that shrank these panels in the first place.
            if (sendWaveButton != null)
            {
                bool offer = run.CanSendWave && battleManager.Phase == BattlePhase.Fighting;

                SetActive(sendWaveButton.gameObject, offer);
                sendWaveButton.interactable = offer;
            }
        }

        // ---------------------------------------------------------------- phase handling

        private void HandlePhaseChanged(BattlePhase phase)
        {
            SetActive(placementPanel, phase == BattlePhase.Placement);
            SetActive(fightingPanel, phase == BattlePhase.Fighting);
            SetActive(resultPanel, phase == BattlePhase.Finished);

            bool gauntlet = battleManager.Mode == GameMode.Gauntlet;
            SetActive(modePanel, phase == BattlePhase.Placement);
            SetActive(gauntletPanel, gauntlet && phase == BattlePhase.Fighting);

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

            // Ask the manager, not the loadout. A gauntlet places one side only, so the loadout's own
            // "both teams have someone" test can never pass there and the button never lit.
            if (startButton != null) startButton.interactable = battleManager.CanStartBattle;
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

        /// <summary>
        /// Drive the two team health bars.
        ///
        /// Separate from <see cref="RefreshCounts"/> and driven from Update rather than the death
        /// event, because health falls continuously while the counts only change when something dies
        /// — that is the whole reason for showing it.
        /// </summary>
        private void RefreshTeamHealth()
        {
            if (battleManager == null) return;

            ApplyTeamHealth(redHealthFill, ref redBaseColor, battleManager.TeamHealthFraction(Team.Red));
            ApplyTeamHealth(blueHealthFill, ref blueBaseColor, battleManager.TeamHealthFraction(Team.Blue));
        }

        /// <summary>
        /// Drive one team bar so that a team on its last legs is MORE visible, not less.
        ///
        /// Mapping the fraction straight onto fillAmount made the bar vanish exactly when it mattered
        /// most: a team on 3% drew a three-percent-wide sliver, which reads as an empty trough rather
        /// than as a team about to lose. That is backwards — the closer a side is to dying, the more
        /// the player wants to see it.
        ///
        /// Two changes. The width stops shrinking at a floor while anything is still alive, so there
        /// is always a bar to look at. And the colour brightens as it drops, so the floor does not
        /// just become a permanent stub that means nothing: a full bar sits at its team colour, a
        /// nearly-dead one glows.
        ///
        /// Zero is still zero. A wiped-out team gets an empty trough, because at that point the
        /// information is that they are gone.
        /// </summary>
        private void ApplyTeamHealth(Image fill, ref Color baseColor, float fraction)
        {
            if (fill == null) return;

            // Captured on first use rather than in Awake: the scene builder sets these colours, and
            // reading them here keeps the team hues defined in exactly one place.
            if (baseColor.a <= 0f) baseColor = fill.color;

            fill.fillAmount = fraction <= 0f
                ? 0f
                : Mathf.Max(fraction, minimumTeamFill);

            // Ramp hardest over the last quarter, where the sliver problem actually bit.
            float urgency = 1f - Mathf.Clamp01(fraction / 0.25f);
            fill.color = Color.Lerp(baseColor, Color.Lerp(baseColor, Color.white, 0.55f), urgency);
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
