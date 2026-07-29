using System;
using System.Collections.Generic;
using DinoBattle.Data;
using DinoBattle.Units;
using UnityEngine;

namespace DinoBattle.Core
{
    /// <summary>
    /// Runs a gauntlet: sends the player's creatures up a board of tiers, wakes each tier's monsters
    /// as they arrive, and asks for another wave when the ones already sent are dead.
    ///
    /// Owns <see cref="GauntletState"/>. It does NOT add cases to <see cref="BattlePhase"/> — a run
    /// stays in <see cref="BattlePhase.Fighting"/> from the first wave to the boss, so the HUD, the
    /// music, the camera director and the victory dance all keep working with no knowledge that this
    /// mode exists.
    ///
    /// Like everything else here, other systems subscribe rather than poll.
    /// </summary>
    [DisallowMultipleComponent]
    public class GauntletDirector : MonoBehaviour
    {
        [SerializeField] private GauntletLadder ladder;
        [SerializeField] private GauntletArena arena;

        [Tooltip("How close the leading creature must get to a tier's objective before that tier's " +
                 "monsters wake up. Generous: waking them late is a wave walking into a standing " +
                 "ambush, which reads better than monsters popping on in plain sight.")]
        [SerializeField] private float tierTriggerRadius = 14f;

        [Tooltip("Seconds after a tier is cleared before the wave is ordered onward, so a kill is " +
                 "not immediately followed by everyone turning their back on it.")]
        [SerializeField] private float advanceDelay = 1.2f;

        [Tooltip("Seconds between reinforcements poured into a fight that is still going. Without " +
                 "one the send button is a tap-to-win, and forty creatures on a tier is past what " +
                 "Docs/performance.md measured the phone can hold.")]
        [SerializeField] private float reinforceCooldown = 6f;

        [Tooltip("Below this height a creature has left the board and is falling into the sea. " +
                 "Clear of the start platform's underside (-1) and the sea slab (-3).")]
        [SerializeField] private float fallRescueHeight = -8f;

        private CreatureSpawner spawner;
        private BattleManager battleManager;

        /// <summary>Monsters for every tier, spawned up front and switched on tier by tier.</summary>
        private readonly List<List<CreatureUnit>> tierMonsters = new();

        /// <summary>
        /// Where each monster was posted, so it can go back after it has seen off a wave.
        ///
        /// Reported: "전투중 사다리에 있는 적이 이긴 후에 막 달려다닐때가 있음." A fight scatters them
        /// across the platform — they chase, they get shoved, they end up wherever the last kill
        /// happened — and with nothing left to fight they simply stopped there. The next wave then
        /// arrived to find the tier's defenders milling around the ramp mouth instead of waiting in
        /// formation, which looks like the AI has lost the plot.
        /// </summary>
        private readonly Dictionary<CreatureUnit, Vector3> posts = new();

        private readonly List<CreatureUnit> wave = new();

        /// <summary>Scratch list for the arrivals of one reinforcement, reused so sending allocates nothing.</summary>
        private readonly List<CreatureUnit> arrivals = new();

        private float advanceTimer;

        /// <summary>Game time at which reinforcing a fight in progress is allowed again.</summary>
        private float reinforceReady;

        private float rescueTimer;

        public GauntletState State { get; private set; } = GauntletState.Ready;
        public int CurrentTier { get; private set; }
        public int TierCount => ladder != null ? ladder.TierCount : 0;
        public int WavesSent { get; private set; }

        public event Action<GauntletState> StateChanged;
        public event Action<int> TierChanged;

        /// <summary>
        /// True when the player may send creatures up the board.
        ///
        /// Two ways in. Everything sent is dead, which is unrestricted — that is the run's normal
        /// rhythm and gating it would only make the player wait to resume a climb they have already
        /// lost ground on. Or the fight is still going, which is on a cooldown.
        ///
        /// Reinforcing mid-fight was asked for directly ("싸우는 중에도 추가로 공룡을 투입할 수
        /// 있도록"), and it is the better shape for the mode: watching a wave lose slowly with a dead
        /// button on screen is the least interesting thing a spectator game can offer. The cooldown is
        /// what keeps it from becoming a tap-to-win, and it is not only a balance concern —
        /// Docs/performance.md measured about twenty-four concurrent creatures as this phone's
        /// ceiling, and an ungated button reaches that in a few seconds of tapping.
        /// </summary>
        public bool CanSendWave =>
            State is GauntletState.Ready or GauntletState.WaveWiped
            || (State is GauntletState.Advancing or GauntletState.Engaging
                && Time.time >= reinforceReady);

        /// <summary>
        /// Seconds until reinforcing is allowed, or zero when it already is. Drives the button label,
        /// so a greyed-out button says why rather than just refusing to be pressed.
        /// </summary>
        public float SecondsUntilSend =>
            CanSendWave ? 0f : Mathf.Max(0f, reinforceReady - Time.time);

        private void Awake()
        {
            spawner = GetComponent<CreatureSpawner>();
            battleManager = GetComponent<BattleManager>();
        }

        // ---------------------------------------------------------------- run lifecycle

        /// <summary>Tear down anything left over and set up a fresh climb.</summary>
        public void BeginRun()
        {
            if (ladder == null || arena == null)
            {
                Debug.LogError("[GauntletDirector] No ladder or arena assigned — rebuild the scene.");
                return;
            }

            ClearMonsters();
            wave.Clear();

            CurrentTier = 0;
            WavesSent = 0;

            // Both are run state, and both survived a run without this. A leftover advanceTimer sent
            // the fresh run's first tier straight past itself; a leftover reinforceReady meant the
            // first thing a new run did was refuse the button.
            advanceTimer = 0f;
            reinforceReady = 0f;

            PreSpawnAllTiers();

            SetState(GauntletState.Ready);
            TierChanged?.Invoke(CurrentTier);
        }

        public void EndRun()
        {
            ClearMonsters();
            wave.Clear();
            SetState(GauntletState.Ready);
        }

        /// <summary>
        /// Create every tier's monsters now, switched off.
        ///
        /// Two reasons, and the second is the one that matters. Instantiating mid-fight costs a frame
        /// hitch, which is avoidable. But more importantly, <see cref="CreatureUnit"/> registers with
        /// <see cref="UnitRegistry"/> in OnEnable — so an inactive tier is simply not in the registry,
        /// and <c>FindNearestEnemy</c> cannot see it. That is the whole tier-scoping problem solved
        /// without touching a line of targeting code.
        ///
        /// It has to be solved: aggroRange on every generated creature is 90, and the board is
        /// roughly 290 units long. Left visible, a creature on tier one would pick a target on tier
        /// seven and walk the whole way to it.
        /// </summary>
        private void PreSpawnAllTiers()
        {
            for (int i = 0; i < ladder.TierCount; i++)
            {
                var spec = ladder.Tier(i);
                var tier = arena.Tier(i);
                var monsters = new List<CreatureUnit>();
                tierMonsters.Add(monsters);

                if (spec == null || tier == null || spec.species.Count == 0) continue;

                var points = tier.SpawnPoints;
                for (int n = 0; n < spec.count; n++)
                {
                    // Round-robin rather than random, so a mixed tier is actually mixed instead of
                    // occasionally being nine of the same thing.
                    var definition = spec.species[n % spec.species.Count];
                    if (definition == null) continue;

                    Vector3 position = points.Count > 0
                        ? points[n % points.Count].position
                        : tier.ObjectivePosition;

                    var unit = spawner.Spawn(new PlacedCreature
                    {
                        Definition = definition,
                        Team = Team.Blue,
                        Position = position,
                        YawDegrees = 180f,
                    }, spec.healthScale, spec.damageScale);

                    if (unit == null) continue;

                    unit.Died += HandleMonsterDied;
                    monsters.Add(unit);
                    posts[unit] = position;

                    // Off, and therefore out of the registry, until this tier is reached.
                    unit.gameObject.SetActive(false);
                }
            }
        }

        private void ClearMonsters()
        {
            foreach (var tier in tierMonsters)
            {
                foreach (var unit in tier)
                {
                    if (unit == null) continue;

                    unit.Died -= HandleMonsterDied;

                    // Actually destroy them. Unsubscribing alone left a full board of monsters
                    // standing, and BeginRun runs on every start — so pressing 전투 시작 twice put
                    // a second set of fifty-eight on the tiers on top of the first. The camera
                    // frames whatever is alive, which is how a repeated press sent the shot
                    // somewhere nobody was.
                    //
                    // Deactivate before destroying: Destroy only lands at the end of the frame, and
                    // a monster that is still registered until then is one the next run's framing
                    // and targeting can both see.
                    unit.gameObject.SetActive(false);
                    Destroy(unit.gameObject);
                }
            }

            tierMonsters.Clear();

            // Keyed on units that are about to be destroyed, so it has to go with them. Left behind
            // it would grow by a full board's worth of dead references on every run.
            posts.Clear();
        }

        // ---------------------------------------------------------------- waves

        /// <summary>
        /// Send what the player has arranged. Unlimited — a run ends by reaching the boss, not by
        /// running out of anything.
        /// </summary>
        public bool SendWave()
        {
            if (!CanSendWave || battleManager == null) return false;

            // Reinforcing an ongoing fight is a different operation from starting a fresh attempt,
            // and the difference is what must NOT happen: the wave list is not cleared, and the
            // creatures already up the board are not re-ordered. Running the fresh-attempt path on a
            // live fight would hand every creature on the tier a march order to the objective, which
            // means breaking off mid-swing and walking away from whatever they were fighting.
            bool reinforcing = State is GauntletState.Advancing or GauntletState.Engaging;

            // Roll a fresh army for every wave.
            //
            // It used to re-send whatever the player arranged the first time, so "더 보내기" sent the
            // same five creatures over and over — the run had one decision in it and then repeated
            // that decision until the ladder ran out. Re-rolling makes each attempt its own throw of
            // the dice, which is the only variety a mode with no unit selection can offer.
            //
            // The first wave is excluded: that one is whatever the player set up and looked at on
            // the placement screen, and replacing it at the moment they press start would be a lie.
            var placer = FindAnyObjectByType<Placement.AutoPlacer>();
            if (WavesSent > 0 && placer != null) placer.FillGauntletWave();

            var loadout = battleManager.Loadout;
            if (loadout.CountFor(Team.Red) == 0) return false;

            // Waves are unlimited. The design argued for a run budget on the grounds that a mode you
            // cannot fail has no tension, and the owner's call was that the tension should come from
            // the ladder rather than from being cut off — a climb you can keep attempting is a
            // different, gentler game, and that is the one being built.
            //
            // What remains as the record of a run is how far up you got and how many waves it took.
            WavesSent++;

            if (!reinforcing) wave.Clear();

            arrivals.Clear();
            Vector3 start = ReinforcementPoint();

            int index = 0;
            foreach (var placement in loadout.Placements)
            {
                if (placement.Team != Team.Red) continue;

                // Spread the wave across the start platform rather than trusting wherever the player
                // tapped on the versus arena, which is a different piece of geometry entirely.
                var placed = placement;
                placed.Position = start + StartOffset(index++);

                var unit = spawner.Spawn(placed);
                if (unit == null) continue;

                unit.Died += HandleFighterDied;
                wave.Add(unit);
                arrivals.Add(unit);

                foreach (var brain in unit.GetComponentsInChildren<CreatureBrain>())
                    brain.CombatEnabled = true;
            }

            if (arrivals.Count == 0) return false;

            reinforceReady = Time.time + reinforceCooldown;

            if (!reinforcing)
            {
                OrderAdvance();
                return true;
            }

            // Only the arrivals get an order, and only as far as the tier being fought over. Once
            // they are in among it their own targeting takes over — MarchTarget is a fallback for
            // having nothing to fight, not a leash.
            var contested = arena.Tier(CurrentTier);
            if (contested != null)
            {
                foreach (var unit in arrivals)
                {
                    if (unit == null || unit.IsDead) continue;
                    foreach (var brain in unit.GetComponentsInChildren<CreatureBrain>())
                        brain.MarchTarget = contested.ObjectivePosition;
                }
            }

            return true;
        }

        /// <summary>
        /// Where a reinforcing wave comes in: the tier below the one being fought over.
        ///
        /// Reported: "추가 공룡 보낼때 너무 멀리서 보내지말고 바로 이전 층에서 보낼수있도록하자."
        /// Sending every wave from the foot of the board meant that losing on tier seven cost a walk
        /// past six cleared, empty platforms before anything happened — the punishment for dying was
        /// boredom rather than difficulty, and it got worse the better the player was doing.
        ///
        /// One tier back rather than the contested tier itself, so a wave still arrives from below
        /// and climbs into the fight. Spawning directly onto the tier under attack would drop
        /// reinforcements into the middle of the defenders with no approach at all.
        /// </summary>
        /// <summary>Where the next wave will come in. Exposed so the preview stands in the right place.</summary>
        public Vector3 WaveEntryPoint => ReinforcementPoint();

        private Vector3 ReinforcementPoint()
        {
            var previous = arena.Tier(CurrentTier - 1);
            if (previous != null) return previous.ObjectivePosition;

            return arena.StartPlatform != null ? arena.StartPlatform.position : Vector3.zero;
        }

        /// <summary>
        /// Where the n-th creature of a wave stands relative to the entry point.
        ///
        /// Six across rather than four, and the rows wrap. The entry point sits about a third of the
        /// way onto a 22-deep platform, so at four across a wave of twelve wanted three rows — nine
        /// units back, which is off the platform's near edge and onto the ramp below it. Now a wave
        /// is at most two rows deep however many are in it, and a third row of arrivals shares the
        /// first; they shove each other apart in a moment, which is what separation steering is for.
        /// </summary>
        private static Vector3 StartOffset(int index)
        {
            int row = (index / 6) % 2;
            int column = index % 6;
            return new Vector3((column - 2.5f) * 3f, 0f, -row * 3f);
        }

        private void HandleFighterDied(CreatureUnit unit)
        {
            if (unit != null) unit.Died -= HandleFighterDied;
            wave.Remove(unit);

            if (AnyAlive(wave)) return;

            // Send the defenders back to their posts. They have just chased a wave all over the
            // platform and would otherwise stand wherever the last one died — usually clustered at
            // the ramp mouth, which both looks like confusion and ambushes the next wave at the
            // exact moment it is most helpless.
            //
            // A march order, not a teleport: they walk back, and because MarchTarget is only a
            // fallback for having no target, the moment the next wave arrives they abandon it and
            // fight. Arrive stops them at the post rather than orbiting it.
            ReturnToPosts();

            // Everything the player sent is dead. Not a defeat unless they cannot afford to try
            // again — that judgement needs the loadout, which the HUD rebuilds, so it is made when
            // the button is pressed rather than guessed at here.
            SetState(GauntletState.WaveWiped);
        }

        private void HandleMonsterDied(CreatureUnit unit)
        {
            if (unit != null) unit.Died -= HandleMonsterDied;

            CheckTierCleared();
        }

        /// <summary>
        /// Has the tier being fought over run out of defenders?
        ///
        /// Polled from Update as well as raised from a death, and the polling is the point. This used
        /// to live entirely inside the death handler behind a <c>State != Engaging</c> guard, which
        /// made "this tier is cleared" a fact that had to be NOTICED at one exact instant or never at
        /// all — and there are ordinary ways to miss that instant. A wave and the last monster killing
        /// each other on the same frame is the common one: whichever Died fires first decides, and if
        /// the creature's does, the run goes to WaveWiped and the monster's death then hits the guard
        /// and returns. The tier is empty and nothing has recorded it.
        ///
        /// That is where the reported "4-5층부터 적이 안나타남" came from. The player sends the next
        /// wave, it climbs to a tier whose defenders are already dead, WakeTier counts the corpses as
        /// woken and declares Engaging — a state with no send button, waiting on deaths that have all
        /// already happened. No enemies, no button, no way out.
        ///
        /// A condition that is checked whenever it might be true cannot be missed. It costs one list
        /// walk over at most ten monsters per frame.
        /// </summary>
        private void CheckTierCleared()
        {
            if (State != GauntletState.Engaging) return;
            if (advanceTimer > 0f) return;
            if (CurrentTier >= tierMonsters.Count) return;
            if (AnyAlive(tierMonsters[CurrentTier])) return;

            if (IsBossTier(CurrentTier))
            {
                SetState(GauntletState.Cleared);
                return;
            }

            // Cleared. Give the survivors a beat before turning them around.
            advanceTimer = advanceDelay;
        }

        private void Update()
        {
            if (State is GauntletState.Cleared or GauntletState.Defeated) return;

            // Catch-all for a wave that is gone while the run still thinks it is under way.
            //
            // HandleFighterDied is the normal route to WaveWiped, and it depends on every death
            // raising Died. A creature destroyed some other way — despawned, or its GameObject torn
            // down — never raises it, and the run would sit in Advancing or Engaging with nothing
            // alive and no button to press. That is a soft-lock with no message, which is the worst
            // kind of bug to ship, so it is worth one list walk a frame to make it impossible rather
            // than merely unlikely.
            if (State is GauntletState.Advancing or GauntletState.Engaging && !AnyAlive(wave))
            {
                ReturnToPosts();
                SetState(GauntletState.WaveWiped);
                return;
            }

            RescueTheFallen();

            if (advanceTimer > 0f)
            {
                advanceTimer -= Time.deltaTime;
                if (advanceTimer <= 0f)
                {
                    CurrentTier++;
                    TierChanged?.Invoke(CurrentTier);
                    OrderAdvance();
                }

                return;
            }

            if (State == GauntletState.Advancing) CheckTierReached();

            // Cheap, and the difference between a run that recovers and a run that stops dead. See
            // CheckTierCleared for what the event-only version missed — with this line removed and
            // WakeTier counting corpses, GauntletRunProbe deadlocks on tier five.
            else if (State == GauntletState.Engaging) CheckTierCleared();
        }

        /// <summary>
        /// Put anything that has left the board back on it.
        ///
        /// The board is walled now, so this should never fire — which is exactly why it is here. A
        /// creature falling into the sea is ALIVE, indefinitely, and the run's whole model of "is
        /// this wave finished" is <see cref="AnyAlive"/>. One faller stuck in Advancing means a
        /// climb that never reaches the next tier, never wipes, and never offers the button: the
        /// same dead end as the one above, arrived at from a different direction.
        ///
        /// Rescued rather than killed. The player never saw it fall — it went over an edge behind
        /// the camera or through a seam — so killing it would be a creature deleted for no visible
        /// reason, and there is no reason to make a physics accident cost them a unit.
        /// </summary>
        private void RescueTheFallen()
        {
            // Quarter-second cadence. A fall takes a second or more to clear the threshold, so
            // checking every frame buys nothing.
            rescueTimer -= Time.deltaTime;
            if (rescueTimer > 0f) return;
            rescueTimer = 0.25f;

            for (int i = 0; i < wave.Count; i++)
            {
                var unit = wave[i];
                if (unit == null || unit.IsDead) continue;
                if (unit.transform.position.y >= fallRescueHeight) continue;

                PlaceOnBoard(unit, ReinforcementPoint());
            }

            foreach (var tier in tierMonsters)
            {
                foreach (var unit in tier)
                {
                    if (unit == null || unit.IsDead) continue;
                    if (!unit.gameObject.activeInHierarchy) continue;
                    if (unit.transform.position.y >= fallRescueHeight) continue;
                    if (!posts.TryGetValue(unit, out Vector3 post)) continue;

                    PlaceOnBoard(unit, post);
                }
            }
        }

        private static void PlaceOnBoard(CreatureUnit unit, Vector3 destination)
        {
            destination.y += 0.5f;

            // Through the Rigidbody as well as the Transform. Interpolation is on, so the physics
            // pose is authoritative — a position written only to the Transform is undone by the
            // interpolator on the next frame, and the creature carries on falling.
            if (unit.TryGetComponent<Rigidbody>(out var body))
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = destination;
            }

            unit.transform.position = destination;
            Debug.Log($"[GauntletDirector] Recovered {unit.name} from off the board.");
        }

        /// <summary>Point the wave at the current tier and let them walk.</summary>
        private void OrderAdvance()
        {
            // Nobody left to order. This is the deadlock that stranded a run on the fourth tier:
            // clearing a tier starts an advance timer, and if the last of the wave died during that
            // delay — a swing already in the air when the tier fell — the timer still expired and
            // put the run into Advancing with an empty wave. Advancing is not a state that offers
            // the send button, and nothing was left alive to ever leave it, so the run simply
            // stopped with no way forward and no message.
            if (!AnyAlive(wave))
            {
                ReturnToPosts();
                SetState(GauntletState.WaveWiped);
                return;
            }

            var tier = arena.Tier(CurrentTier);
            if (tier == null)
            {
                SetState(GauntletState.Cleared);
                return;
            }

            Vector3 objective = tier.ObjectivePosition;
            foreach (var unit in wave)
            {
                if (unit == null || unit.IsDead) continue;
                foreach (var brain in unit.GetComponentsInChildren<CreatureBrain>())
                    brain.MarchTarget = objective;
            }

            SetState(GauntletState.Advancing);
        }

        /// <summary>
        /// Wake the current tier once the leader is close enough.
        ///
        /// The LEADER, not the average: a wave strings out badly on a long climb, and waiting for the
        /// group's centre would let the front runner walk into the middle of a sleeping tier.
        /// </summary>
        private void CheckTierReached()
        {
            var tier = arena.Tier(CurrentTier);
            if (tier == null) return;

            Vector3 objective = tier.ObjectivePosition;
            bool leaderArrived = false;

            foreach (var unit in wave)
            {
                if (unit == null || unit.IsDead) continue;

                Vector3 offset = unit.transform.position - objective;
                offset.y = 0f;
                if (offset.sqrMagnitude <= tierTriggerRadius * tierTriggerRadius) { leaderArrived = true; break; }
            }

            if (!leaderArrived) return;

            WakeTier(CurrentTier);
        }

        private void WakeTier(int index)
        {
            if (index < 0 || index >= tierMonsters.Count) return;

            var monsters = tierMonsters[index];
            int woken = 0;

            foreach (var unit in monsters)
            {
                // A CORPSE IS NOT A DEFENDER. Corpses linger for twelve seconds
                // (CreatureUnit.corpseLifetime), so a tier that was emptied and left behind still
                // has bodies on it — and counting those as woken declared the tier defended and put
                // the run into Engaging waiting for deaths that had already happened.
                if (unit == null || unit.IsDead) continue;

                unit.gameObject.SetActive(true);
                foreach (var brain in unit.GetComponentsInChildren<CreatureBrain>())
                    brain.CombatEnabled = true;

                woken++;
            }

            // An empty tier is not a bug worth stalling on — walk on through.
            if (woken == 0)
            {
                advanceTimer = 0.01f;
                return;
            }

            // Drop the march order so the wave fights here instead of trying to walk past.
            foreach (var unit in wave)
            {
                if (unit == null || unit.IsDead) continue;
                foreach (var brain in unit.GetComponentsInChildren<CreatureBrain>())
                    brain.MarchTarget = null;
            }

            SetState(GauntletState.Engaging);
        }

        private bool IsBossTier(int index)
        {
            var spec = ladder != null ? ladder.Tier(index) : null;
            return spec != null ? spec.isBoss : index >= TierCount - 1;
        }

        /// <summary>
        /// Order every surviving monster back to where it was posted.
        ///
        /// Applied to all tiers, not just the current one. A tier behind the front can still have
        /// survivors — the wave is allowed to lose creatures and press on — and those should also be
        /// standing where they were put rather than at whatever spot they last fought on.
        /// </summary>
        private void ReturnToPosts()
        {
            foreach (var tier in tierMonsters)
            {
                foreach (var unit in tier)
                {
                    if (unit == null || unit.IsDead) continue;
                    if (!unit.gameObject.activeInHierarchy) continue;
                    if (!posts.TryGetValue(unit, out Vector3 post)) continue;

                    foreach (var brain in unit.GetComponentsInChildren<CreatureBrain>())
                        brain.MarchTarget = post;
                }
            }
        }

        private static bool AnyAlive(List<CreatureUnit> units)
        {
            for (int i = 0; i < units.Count; i++)
                if (units[i] != null && !units[i].IsDead) return true;

            return false;
        }

        private void SetState(GauntletState next)
        {
            if (State == next) return;

            State = next;
            StateChanged?.Invoke(next);
        }

        /// <summary>
        /// How many defenders are still standing on the tier being fought over.
        ///
        /// Exposed for GauntletRunProbe, which asserts the deadlock directly: being in
        /// <see cref="GauntletState.Engaging"/> against a tier with zero of these is the exact
        /// signature of the stall described in CheckTierCleared, and it is far more precise than
        /// watching for a run that has stopped moving.
        /// </summary>
        public int DefendersAlive
        {
            get
            {
                if (CurrentTier < 0 || CurrentTier >= tierMonsters.Count) return 0;

                int alive = 0;
                foreach (var unit in tierMonsters[CurrentTier])
                    if (unit != null && !unit.IsDead) alive++;

                return alive;
            }
        }

        /// <summary>Human-readable label for the tier the HUD should be showing.</summary>
        public string CurrentTierLabel
        {
            get
            {
                var spec = ladder != null ? ladder.Tier(CurrentTier) : null;
                if (spec != null && !string.IsNullOrEmpty(spec.label)) return spec.label;
                return $"{CurrentTier + 1}층";
            }
        }
    }
}
