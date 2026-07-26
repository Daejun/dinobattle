using DinoBattle.Core;
using DinoBattle.Data;
using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>
    /// The autonomous fighter AI. This is the whole point of a spectator simulator: the player
    /// places creatures, presses Start, and this state machine does the fighting.
    ///
    /// Idle -> Seek (walk at nearest enemy) -> Attack (in range) -> back to Seek when the target dies.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CreatureUnit))]
    public class CreatureBrain : MonoBehaviour
    {
        public enum State { Idle, Seek, Attack, Dead }

        [Tooltip("Seconds between target re-evaluations. Staggered per creature to spread the cost.")]
        [SerializeField] private float retargetInterval = 0.4f;

        [Tooltip("Only consider abandoning a chase once the target is at least this far away. Below " +
                 "it, target retention wins and a scrum does not reshuffle over small differences.")]
        [SerializeField] private float abandonChaseDistance = 8f;

        [Tooltip("How much closer another enemy must be before the chase is called off, as a ratio. " +
                 "2 means it switches only to something less than half the distance away.")]
        [Min(1f)]
        [SerializeField] private float retargetAdvantage = 2f;

        [Tooltip("Fraction of the two creatures' combined footprint to stand at while fighting. " +
                 "Well under 1 so the silhouettes overlap — the colliders set the real floor.")]
        [Range(0.2f, 1.2f)]
        [SerializeField] private float meleeContactFactor = 0.35f;

        [Tooltip("Never hold closer than this, so small creatures do not try to occupy one point.")]
        [SerializeField] private float minimumFightDistance = 0.8f;

        [Tooltip("Degrees around the target this creature approaches from. Randomised per creature so " +
                 "a pack surrounds its prey instead of all piling onto the nearest face.")]
        [SerializeField] private float maxFlankAngle = 70f;

        [Tooltip("Strafing speed while circling, as a fraction of full move speed.")]
        [Range(0f, 1f)]
        [SerializeField] private float circleSpeedFactor = 0.45f;

        [Tooltip("Widest angle off the target this creature will bite at. Beyond it the creature " +
                 "stops and turns instead of attacking sideways. Generous enough that fights do not " +
                 "stall into a turning contest.")]
        [Range(5f, 120f)]
        [SerializeField] private float maxAttackAngle = 45f;

        [Header("Steering")]
        [Tooltip("Neighbours inside this radius push this creature away. Reynolds separation — the " +
                 "term that stops a pack collapsing onto a single point.")]
        [SerializeField] private float separationRadius = 3.5f;

        [Tooltip("Weight of separation while closing in. Spreads a pack across the approach so it " +
                 "arrives on several flanks instead of in single file.")]
        [Range(0f, 3f)]
        [SerializeField] private float separationWeight = 1.1f;

        [Tooltip("Weight of separation once in contact. Near zero on purpose: in the reference game " +
                 "attackers pile onto their target and interpenetrate heavily, and keeping full " +
                 "separation here is what held them at a polite distance mid-fight.")]
        [Range(0f, 3f)]
        [SerializeField] private float meleeSeparationWeight = 0.15f;

        [Tooltip("Distance at which Arrive starts easing off, so attackers settle instead of " +
                 "overshooting and oscillating.")]
        [SerializeField] private float slowingRadius = 6f;

        [Header("Hit and run")]
        [Tooltip("Fight this way when the target outweighs this creature by at least this factor. A " +
                 "raptor that plants its feet against a T-Rex simply dies; darting in and out is the " +
                 "only way its size is survivable.")]
        [Min(1f)]
        [SerializeField] private float hitAndRunMassRatio = 3f;

        [Tooltip("Only creatures under this mass skirmish — on the current roster, the Velociraptor " +
                 "at 900 and nothing else; the next lightest is the Parasaurolophus at 3200. " +
                 "Without a ceiling the rule was purely relative, so against a 60-tonne boss even a " +
                 "Triceratops counted as a light harasser, and a ring of heavy dinosaurs all darting " +
                 "in and out read as a standoff rather than a fight. Darting is what you do when you " +
                 "are too fragile to trade, not merely smaller than the other thing.")]
        [SerializeField] private float harasserMassCeiling = 1500f;

        [Tooltip("Seconds spent backing off after landing a bite, before turning in for another pass. " +
                 "Scaled down when the creature is in little danger.")]
        [SerializeField] private float retreatDuration = 1.1f;

        [Tooltip("Danger at or above which this creature breaks off immediately, even mid-approach.")]
        [Range(0f, 1f)]
        [SerializeField] private float fleeDanger = 0.6f;

        [Tooltip("Danger below which it presses the attack: no queueing for a turn, and only a token " +
                 "step back after biting. This is what makes the rest of a pack pile onto a target " +
                 "that has committed to one of their number.")]
        [Range(0f, 1f)]
        [SerializeField] private float boldDanger = 0.25f;

        [Tooltip("Seconds a creature will keep turning for a clean shot before lunging anyway. Stops " +
                 "a slow-turning heavy from being locked out of attacking entirely by a fast circler.")]
        [SerializeField] private float maxAlignWait = 0.9f;

        [Tooltip("Widest angle allowed once patience runs out. Only slightly past the normal gate — " +
                 "a creature cannot bite what is beside or behind it, however long it has waited. " +
                 "The deadlock this used to guard against is handled by turning faster and by " +
                 "switching to whichever enemy is already in front.")]
        [Range(45f, 90f)]
        [SerializeField] private float lungeAttackAngle = 60f;

        [Tooltip("Danger multiplier at zero health. Above 1 so a wounded creature reads the same " +
                 "situation as more threatening and pulls out earlier.")]
        [Range(1f, 2f)]
        [SerializeField] private float woundedCaution = 1.4f;

        [Tooltip("Seconds of unbroken retreat after which the creature is treated as cornered and " +
                 "turns to fight. Running for longer than this is running that is not working.")]
        [SerializeField] private float maxContinuousFlee = 2.5f;

        [Tooltip("Where waiting pack members hold, as a multiple of the target's footprint. Far " +
                 "enough to be out of reach, near enough to close quickly when their turn comes.")]
        [SerializeField] private float standoffFactor = 2.6f;

        [Tooltip("Pack size at which attackers start taking turns rather than all diving in together.")]
        [Min(2)]
        [SerializeField] private int packTurnThreshold = 3;

        [Tooltip("Longest a pack member may hold the attack turn before it passes on regardless. A " +
                 "backstop: a holder that gets knocked away must not stall the whole pack.")]
        [SerializeField] private float turnDuration = 1.8f;

        private float retreatRemaining;
        private float alignWait;
        private float fleeElapsed;

        private float flankAngle;
        private float circleDirection = 1f;

        [SerializeField] private Animator animator;
        [SerializeField] private string speedParameterName = "Speed";
        [SerializeField] private string deathTriggerName = "Die";

        private CreatureUnit self;
        private CreatureLocomotion locomotion;
        private MeleeAttack attack;
        private CreatureDefinition definition;

        private CreatureUnit target;
        private float retargetTimer;

        public State Current { get; private set; } = State.Idle;
        public CreatureUnit Target => target;

        /// <summary>
        /// This creature's read on how exposed it is, 0 to 1. Only meaningful for a light creature
        /// harassing a heavy one; everything else fights the same way regardless. Exposed so the
        /// behaviour is observable rather than something that has to be inferred from movement.
        /// </summary>
        public float Danger { get; private set; }

        /// <summary>
        /// How hopeless this creature's position is, 0 to 1. Scales <see cref="Danger"/> down, so a
        /// creature with nothing left to lose stops behaving carefully. Exposed for the same reason
        /// Danger is: the behaviour is otherwise only inferable from watching it.
        /// </summary>
        public float Desperation { get; private set; }

        /// <summary>Set false during placement so nothing moves until the fight starts.</summary>
        public bool CombatEnabled { get; set; }

        private void Awake()
        {
            self = GetComponent<CreatureUnit>();
            locomotion = GetComponent<CreatureLocomotion>();
            attack = GetComponentInChildren<MeleeAttack>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            // Offset the first retarget tick so a hundred creatures do not all scan on the same frame.
            retargetTimer = Random.Range(0f, retargetInterval);

            // Fixed per creature, not per frame: a flank angle that jittered every tick would make
            // the approach wander instead of committing to one side.
            flankAngle = Random.Range(-maxFlankAngle, maxFlankAngle);
            circleDirection = Random.value < 0.5f ? -1f : 1f;
        }

        private void Start()
        {
            definition = self.Definition;
        }

        private void Update()
        {
            if (Current == State.Dead) return;

            if (!CombatEnabled)
            {
                SetState(State.Idle);
                UpdateAnimator();
                return;
            }

            retargetTimer -= Time.deltaTime;
            if (target == null || target.IsDead || retargetTimer <= 0f)
            {
                retargetTimer = retargetInterval;
                AcquireTarget();
            }

            if (target == null)
            {
                SetState(State.Idle);
                UpdateAnimator();
                return;
            }

            TickCombat();
            UpdateAnimator();
        }

        private void AcquireTarget()
        {
            float aggro = definition != null ? definition.aggroRange : 80f;
            var nearest = UnitRegistry.FindNearestEnemy(self, aggro);

            // Keep the current target unless it is gone — constant switching makes fights look indecisive.
            if (target == null || target.IsDead)
            {
                target = nearest;
                return;
            }

            // Bite what you can already reach.
            //
            // This outranks every other consideration and it is the rule that was missing. Measured
            // mid-fight: a T-Rex standing 1.96 from the centre of the raptor pack — with raptors
            // close enough to hit — sprinting at 5.8 m/s after a different one 5.51 away. The old
            // escape hatch needed the target to be 8+ units off before it would reconsider, so
            // surrounded by enemies it never triggered. Reachability, not absolute distance, is what
            // decides whether chasing is even sensible.
            // Two ways the current target can be the wrong one to be looking at, and both need
            // covering. Out of reach is the obvious case. The other is being surrounded: the target
            // is well within reach but standing behind you, and only checking reachability missed it
            // entirely — a boss ringed by ten hunters always had its target in range, so it never
            // reconsidered and spent the fight turning on the spot. Measured at 3 swings against the
            // pack's 53 before this.
            if (attack != null && (!attack.IsInRange(target, Current == State.Attack)
                                   || FacingErrorTo(target) > lungeAttackAngle))
            {
                var reachable = BestEnemyInReach();
                if (reachable != null && reachable != target)
                {
                    target = reachable;
                    return;
                }
            }

            if (nearest == null || nearest == target) return;

            // The one exception to keeping your target: it has outrun you and something else is
            // right there. Against hit-and-run this was ruinous — a T-Rex would lock onto the first
            // raptor it saw, and because a raptor is nearly twice its speed it could never close.
            // Measured mid-fight: chasing a target 13.3 away at full speed while another raptor sat
            // 3.6 from its flank. From outside it looks like the T-Rex is running away from the
            // battle. A target you cannot catch is not a target, it is a decoy.
            //
            // Both conditions are needed. The distance floor keeps a scrum from reshuffling over
            // small differences, and the ratio means the alternative has to be decisively closer —
            // together they preserve what target retention was for.
            float currentDistance = PlanarDistanceTo(target);
            if (currentDistance <= abandonChaseDistance) return;
            if (currentDistance <= PlanarDistanceTo(nearest) * retargetAdvantage) return;

            target = nearest;
        }

        /// <summary>
        /// Movement is a weighted blend of Reynolds steering behaviours rather than a single Seek.
        /// Pursue/Arrive supplies the intent, Separation keeps the pack from collapsing into one
        /// point, and in melee a tangential term makes attackers circle instead of standing still.
        /// </summary>
        private void TickCombat()
        {
            // Sticky engagement. Entering and leaving on the same threshold meant a creature
            // sitting exactly at its reach flickered between Attack and Seek every frame, braking
            // and re-approaching without ever committing. Commit tight, disengage loose.
            bool inRange = attack != null && attack.IsInRange(target, Current == State.Attack);
            float maxSpeed = locomotion != null ? locomotion.MoveSpeed : 6f;
            float fightDistance = FightDistanceTo(target);

            // The target is excluded: separation keeps the pack spread, it must not repel a creature
            // from the very thing it is closing on.
            Vector3 separation = SteeringBehaviors.Separation(self, separationRadius, maxSpeed, target);

            bool harasses = UsesHitAndRun(target);

            // Desperation cancels caution. A cornered animal does not fight carefully.
            Desperation = harasses ? AssessDesperation() : 0f;
            Danger = harasses ? AssessDanger(target) * (1f - Desperation) : 0f;

            if (harasses)
            {
                // Cut a retreat short the moment the target's attention moves elsewhere. Running out
                // the full timer regardless is what would make the pack look mindless: the whole
                // point is that the other raptors get bolder while the big one is busy.
                if (retreatRemaining > 0f && Danger <= boldDanger) retreatRemaining = 0f;

                // Break off, whether that is finishing a retreat or abandoning an approach. A light
                // creature that stands and trades with something ten times its mass simply dies.
                if (retreatRemaining > 0f || Danger >= fleeDanger)
                {
                    fleeElapsed += Time.deltaTime;
                    retreatRemaining = Mathf.Max(0f, retreatRemaining - Time.deltaTime);
                    SetState(State.Seek);

                    if (locomotion != null)
                    {
                        Vector3 away = SteeringBehaviors.Flee(
                            transform.position, target.transform.position, maxSpeed);

                        // Retreat while still facing the enemy: a creature that turns its back to
                        // sprint away reads as routed, not as circling for another pass.
                        locomotion.Steer(
                            SteeringBehaviors.Blend(maxSpeed, (away, 1f), (separation, separationWeight)),
                            target.transform.position);
                    }

                    return;
                }

                // Not fleeing this frame, so the "running is not working" clock starts over.
                fleeElapsed = 0f;
            }

            // Waiting for a turn. Once three or more are working the same target they go in one at a
            // time: a pack that dives and withdraws in unison leaves the target a free window every
            // cycle, while a rotation keeps something at its flank continuously. It is also what
            // makes the big creature ineffective — whatever it turns to bite is already pulling out,
            // and the next attacker is arriving from a different side.
            // A creature the target is not watching does not queue. Waiting your turn is a way to
            // avoid trading with an alert enemy, and there is nothing to avoid when it is looking
            // the other way — so low danger means go in now, alongside whoever else is already on it.
            if (harasses && Danger > boldDanger
                         && PackTactics.AttackersOn(target, self.Team) >= packTurnThreshold
                         && !PackTactics.TryTakeTurn(target, this, turnDuration))
            {
                SetState(State.Seek);
                HoldAtStandoff(maxSpeed, separation);
                return;
            }

            if (inRange)
            {
                SetState(State.Attack);

                // A creature must be looking at what it bites. Without this gate the only requirement
                // was distance, so a dinosaur standing broadside to its enemy would snap at empty air
                // beside it — the bite landed, but it visibly came out of the creature's flank.
                float facingError = FacingErrorTo(target);
                bool aligned = facingError <= maxAttackAngle;

                if (aligned)
                {
                    alignWait = 0f;
                }
                else
                {
                    // Deadlock breaker. A raptor orbiting at melee range changes bearing faster than
                    // a heavy dinosaur can turn, so a strict gate meant the T-Rex could never line up
                    // at all: measured at 17 raptor bites to 0 of its own, standing and turning the
                    // whole fight. Past a short patience it lunges at a wider angle rather than
                    // waiting for an alignment that is never going to arrive.
                    alignWait += Time.deltaTime;
                    if (alignWait >= maxAlignWait && facingError <= lungeAttackAngle) aligned = true;
                }

                if (locomotion != null)
                {
                    // Rooted for the whole attack, and rooted while turning to line one up:
                    // circling adds angular error faster than the creature can turn it off. At melee
                    // range the orbit radius is small, so a leisurely strafe is ~100 deg/s of bearing
                    // change — more than the heavy dinosaurs can track. Planting the feet lets the
                    // turn actually converge, which reads as the creature squaring up to its enemy.
                    Vector3 desired;

                    if (attack.IsCommitted || attack.IsReady)
                    {
                        // Planted through the swing. Zero is a brake command, not a coast.
                        desired = Vector3.zero;
                    }
                    else if (!aligned)
                    {
                        // Keep closing while turning. Braking here is what made a heavy dinosaur look
                        // like it had given up: it stood rooted, slowly rotating after a raptor that
                        // never stopped moving, and never got a bite in. Pressing forward shortens
                        // the turn it has to make and reads as pursuing rather than hesitating.
                        desired = SteeringBehaviors.Arrive(
                            transform.position, target.transform.position, maxSpeed, slowingRadius, fightDistance);
                    }
                    else
                    {
                        desired = SteeringBehaviors.Blend(maxSpeed,
                            (TangentialVelocity(fightDistance, maxSpeed), 1f),
                            (separation, meleeSeparationWeight));
                    }

                    // Face the body, not the aim point. The aim point sits forward of the target's
                    // chest, so against an enemy facing away it hangs out past the far side and the
                    // attacker turns to look through its target rather than at it.
                    locomotion.Steer(desired, target.transform.position);
                }

                // Landing a bite is what ends a hit-and-run pass: withdraw, and hand the turn to the
                // next pack member. Tied to the swing actually starting rather than to a timer, so a
                // creature that never got its bite in does not give up its slot for nothing.
                bool struck = aligned && attack.TryAttack(target);
                if (struck) alignWait = 0f;

                if (struck && harasses)
                {
                    // How far it withdraws depends on how exposed it is. A raptor working the blind
                    // side takes half a step back and comes straight in again; one that just bit
                    // something now turning toward it clears out properly.
                    retreatRemaining = Mathf.Lerp(retreatDuration * 0.3f, retreatDuration, Danger);
                    PackTactics.EndTurn(target, this);
                }

                return;
            }

            SetState(State.Seek);

            // Out of reach, so patience for a clean shot starts over on the next approach.
            alignWait = 0f;

            if (locomotion == null) return;

            // Harassers come in from behind — the tail, away from the teeth. Everyone else spreads
            // across the flanks so converging attackers do not queue up nose-to-tail on the near side.
            Vector3 anchor = harasses ? RearPosition(fightDistance) : FlankPosition(fightDistance);
            Vector3 targetVelocity = target.Locomotion != null
                ? target.Locomotion.HorizontalVelocity
                : Vector3.zero;

            Vector3 pursue = SteeringBehaviors.Pursue(
                transform.position, anchor, targetVelocity, maxSpeed, slowingRadius);

            // Face the target while closing, not the direction of travel.
            //
            // Without this the creature looked wherever it was walking, and the approach is an arc,
            // so it spent the whole pursuit staring off to one side — measured at 91 degrees from
            // its own target while running past it. That is what reads as fleeing rather than
            // hunting. Passing a face target also skips Steer's travel-direction gate, which was
            // freezing the creature solid every time its desired heading swung more than 60 degrees
            // off its nose; against a fast target that happens constantly, and the resulting
            // stop-start-arc is the other half of what looked like running away.
            locomotion.Steer(SteeringBehaviors.Blend(maxSpeed,
                (pursue, 1f),
                (separation, separationWeight)),
                target.transform.position);
        }

        /// <summary>
        /// The best enemy this creature can already hit from where it stands, or null if none is in
        /// reach. Ranked by how far it would have to turn, since with a fast pivot the cost of
        /// engaging something is mostly the turn, not the distance.
        /// </summary>
        private CreatureUnit BestEnemyInReach()
        {
            var enemies = UnitRegistry.AliveOf(self.Team.Opponent());

            CreatureUnit best = null;
            float bestError = float.MaxValue;

            for (int i = 0; i < enemies.Count; i++)
            {
                var candidate = enemies[i];
                if (candidate == null || candidate.IsDead) continue;
                if (!attack.IsInRange(candidate)) continue;

                float error = FacingErrorTo(candidate);
                if (error >= bestError) continue;

                bestError = error;
                best = candidate;
            }

            return best;
        }

        /// <summary>Ground-plane distance to another creature, ignoring height.</summary>
        private float PlanarDistanceTo(CreatureUnit other)
        {
            Vector3 offset = other.transform.position - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        /// <summary>
        /// How hopeless this creature's position is, 0 (a fair fight) to 1 (nothing left to lose).
        ///
        /// This is the counterweight to <see cref="AssessDanger"/>, and without it the caution was a
        /// trap. A lone raptor facing a T-Rex is by definition always its target, so its danger sat
        /// permanently above the flee threshold: it ran, kept running, pinned itself against the
        /// boundary and stopped there — reading on screen as a creature that had simply frozen. It
        /// never fought back, and it never could have.
        ///
        /// Two things make a position hopeless, and they compose as a maximum rather than a sum
        /// because either alone is enough:
        ///
        /// Outnumbered — being the last of three against four is not a fight you win by being
        /// careful, and a real animal in that position stops conserving itself.
        ///
        /// Cornered — retreat that has gone on this long is retreat that is not working. Rather than
        /// giving the brain a map of the arena to reason about walls with, this just measures whether
        /// running has achieved anything, which is the thing that actually matters and needs no
        /// knowledge of the geometry.
        /// </summary>
        private float AssessDesperation()
        {
            int mine = UnitRegistry.AliveCount(self.Team);
            int theirs = UnitRegistry.AliveCount(self.Team.Opponent());

            float outnumbered = theirs > 0
                ? 1f - Mathf.Clamp01(mine / (float)theirs)
                : 0f;

            float trapped = maxContinuousFlee > 0f
                ? Mathf.Clamp01(fleeElapsed / maxContinuousFlee)
                : 0f;

            return Mathf.Clamp01(Mathf.Max(outnumbered, trapped));
        }

        /// <summary>
        /// How much trouble this creature is in right now, 0 (ignored) to 1 (about to be bitten).
        ///
        /// This is what lets a pack behave like a pack. A raptor the T-Rex has turned to face is in
        /// real danger and should be leaving; the three behind it are in almost none and should be
        /// pressing hard. Scoring the situation per creature — rather than having every raptor use
        /// the same timid rule — is what produces the intended picture: the moment the big animal
        /// commits to one of them, the rest close in on the parts of it that are no longer watching.
        ///
        /// Attention is weighted heaviest because it is the best predictor: what a large creature is
        /// aiming at is what it is about to damage.
        /// </summary>
        private float AssessDanger(CreatureUnit enemy)
        {
            var enemyBrain = enemy.Brain;
            var enemyAttack = enemy.Attack;

            float danger = 0f;

            bool targetedAtMe = enemyBrain != null && enemyBrain.Target == self;
            if (targetedAtMe) danger += 0.5f;

            // Facing matters even when aimed elsewhere: a creature does not have to turn to reach
            // what is already in front of it.
            Vector3 toSelf = transform.position - enemy.transform.position;
            toSelf.y = 0f;
            if (toSelf.sqrMagnitude > 0.0001f)
            {
                Vector3 enemyForward = enemy.transform.forward;
                enemyForward.y = 0f;
                danger += Mathf.Clamp01(Vector3.Dot(enemyForward.normalized, toSelf.normalized)) * 0.2f;
            }

            // Standing inside its reach is the difference between a threat and a distant one.
            if (enemyAttack != null && enemyAttack.IsInRange(self)) danger += 0.15f;

            // A swing already committed at this creature is as bad as it gets.
            if (targetedAtMe && enemyAttack != null && enemyAttack.IsCommitted) danger += 0.3f;

            // A hurt animal has less margin for a bad read, so the same situation weighs heavier.
            float healthFraction = self.Health != null && self.Health.Max > 0f
                ? Mathf.Clamp01(self.Health.Current / self.Health.Max)
                : 1f;
            danger *= Mathf.Lerp(woundedCaution, 1f, healthFraction);

            return Mathf.Clamp01(danger);
        }

        /// <summary>
        /// Does this creature fight <paramref name="enemy"/> by darting in and out rather than
        /// standing and trading? Decided by mass, so it falls out of the roster rather than needing
        /// a flag on every definition: a raptor harasses a T-Rex and brawls another raptor, with no
        /// per-species bookkeeping.
        /// </summary>
        private bool UsesHitAndRun(CreatureUnit enemy)
        {
            var mine = self.Definition;
            var theirs = enemy != null ? enemy.Definition : null;
            if (mine == null || theirs == null || mine.mass <= 0f) return false;
            if (mine.mass > harasserMassCeiling) return false;

            return theirs.mass / mine.mass >= hitAndRunMassRatio;
        }

        /// <summary>
        /// Circle the target at a safe radius while another pack member takes its pass.
        ///
        /// Circling rather than standing still: a waiting raptor that parks reads as a bug, and
        /// keeping it moving around the ring means that when its turn comes it is already somewhere
        /// the target is not facing.
        /// </summary>
        private void HoldAtStandoff(float maxSpeed, Vector3 separation)
        {
            if (locomotion == null) return;

            float footprint = target.Definition != null ? target.Definition.footprintRadius : 2f;
            float standoff = footprint * standoffFactor;

            Vector3 desired = SteeringBehaviors.Blend(maxSpeed,
                (TangentialVelocity(standoff, maxSpeed), 1f),
                (separation, separationWeight));

            // Facing the target throughout, so the pack visibly has it surrounded and watched.
            locomotion.Steer(desired, target.transform.position);
        }

        /// <summary>
        /// A point <paramref name="stopDistance"/> behind the target, past its tail.
        ///
        /// The whole point of harassment is to be where the mouth is not. Approaching the rear also
        /// forces the big creature to turn before it can answer, and turning is the thing heavy
        /// dinosaurs are worst at.
        /// </summary>
        private Vector3 RearPosition(float stopDistance)
        {
            Vector3 behind = -target.transform.forward;
            behind.y = 0f;
            if (behind.sqrMagnitude < 0.0001f) behind = -transform.forward;

            return target.transform.position + behind.normalized * stopDistance;
        }

        /// <summary>
        /// Sideways velocity around the target, plus a radial correction that holds the fighting
        /// distance. Produces a circling orbit instead of a drift that slowly loses contact.
        /// </summary>
        private Vector3 TangentialVelocity(float fightDistance, float maxSpeed)
        {
            Vector3 toSelf = transform.position - target.transform.position;
            toSelf.y = 0f;
            if (toSelf.sqrMagnitude < 0.0001f) return Vector3.zero;

            float distance = toSelf.magnitude;
            Vector3 radial = toSelf / distance;
            Vector3 tangent = Vector3.Cross(Vector3.up, radial) * circleDirection;

            // Positive when too far out, negative when too close: pulls back to fightDistance.
            float correction = Mathf.Clamp((fightDistance - distance) / Mathf.Max(0.5f, fightDistance), -1f, 1f);

            return (tangent + radial * correction).normalized * (maxSpeed * circleSpeedFactor);
        }

        /// <summary>
        /// Degrees between where this creature is looking and where <paramref name="enemy"/> is, on
        /// the ground plane. Yaw only — a T-Rex looming over a raptor is still facing it.
        /// </summary>
        private float FacingErrorTo(CreatureUnit enemy)
        {
            Vector3 toEnemy = enemy.transform.position - transform.position;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude < 0.0001f) return 0f;

            Vector3 forward = transform.forward;
            forward.y = 0f;

            return Vector3.Angle(forward, toEnemy);
        }

        /// <summary>
        /// How close to stand while fighting <paramref name="enemy"/>.
        ///
        /// Deliberately NOT derived from attack range. Range answers "can I land a hit from here",
        /// which is generous by design; standing at that distance leaves the two bodies visibly
        /// apart, trading blows across open ground.
        ///
        /// Nor is footprintRadius usable raw: it comes from the creature's LONGEST dimension, so a
        /// Triceratops 2.2 units wide reports a 2.6-unit radius. Two of them held at the sum of
        /// those radii sit 5.3 apart while their bodies are barely 2 wide — which is exactly the gap
        /// that made fights look like a staring contest. The factor pulls them inside that figure so
        /// the silhouettes actually overlap, and the colliders decide the true minimum.
        /// </summary>
        private float FightDistanceTo(CreatureUnit enemy)
        {
            float mine = self.Definition != null ? self.Definition.footprintRadius : 1f;
            float theirs = enemy.Definition != null ? enemy.Definition.footprintRadius : 1f;

            return Mathf.Max(minimumFightDistance, (mine + theirs) * meleeContactFactor);
        }

        /// <summary>
        /// A point <paramref name="stopDistance"/> from the target, rotated by this creature's own
        /// flank offset. Attackers converge on different sides instead of stacking on the near face.
        /// </summary>
        private Vector3 FlankPosition(float stopDistance)
        {
            Vector3 toSelf = transform.position - target.transform.position;
            toSelf.y = 0f;
            if (toSelf.sqrMagnitude < 0.0001f) toSelf = -transform.forward;

            // Only spread out when there is someone to spread out from. Flanking exists so several
            // attackers arrive on different sides; applied by a lone attacker it just aims it at a
            // patch of ground up to 70 degrees off its target — and against a target quick enough to
            // have left by the time you arrive, that is a permanent near-miss. Measured: velocity 44
            // degrees off the target's bearing, closing on nothing.
            float spread = PackTactics.AttackersOn(target, self.Team) > 1 ? flankAngle : 0f;

            Quaternion offset = Quaternion.Euler(0f, spread, 0f);
            return target.transform.position + offset * toSelf.normalized * stopDistance;
        }

        private void SetState(State next)
        {
            if (Current == next) return;
            Current = next;
        }

        private void UpdateAnimator()
        {
            if (animator == null || string.IsNullOrEmpty(speedParameterName)) return;
            if (locomotion == null) return;

            float normalized = definition != null && definition.moveSpeed > 0f
                ? locomotion.CurrentSpeed / definition.moveSpeed
                : 0f;

            animator.SetFloat(speedParameterName, normalized);
        }

        public void OnUnitDied()
        {
            Current = State.Dead;

            // Hand the attack turn back before forgetting the target, or a raptor that dies mid-lunge
            // holds its pack's slot until the turn times out.
            PackTactics.EndTurn(target, this);

            target = null;
            retreatRemaining = 0f;
            CombatEnabled = false;

            if (animator != null && !string.IsNullOrEmpty(deathTriggerName))
            {
                // Clear Speed too, or the death clip blends against a stale locomotion value.
                if (!string.IsNullOrEmpty(speedParameterName)) animator.SetFloat(speedParameterName, 0f);
                animator.SetTrigger(deathTriggerName);
            }
        }
    }
}
