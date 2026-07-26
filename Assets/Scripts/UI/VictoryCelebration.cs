using DinoBattle.Core;
using DinoBattle.Units;
using UnityEngine;

namespace DinoBattle.UI
{
    /// <summary>
    /// Confetti, a fanfare, and the winner roaring, when a match is won.
    ///
    /// From a four-year-old's playtest: "이겼는데 그냥 까만 네모만 떠. 재미없어. 색종이 팡! 나오고
    /// 크아아앙! 소리 나고 티라노가 발 쿵쿵 하고 그래야지." He could not read the result panel — he
    /// cannot read at all — so as far as he was concerned winning produced a black rectangle and
    /// nothing else. A win has to be legible without words.
    ///
    /// The particle system is built in code rather than authored as a prefab, for the same reason
    /// the scene is: it is then reviewable in a diff and reproducible from a fresh clone.
    /// </summary>
    [DisallowMultipleComponent]
    public class VictoryCelebration : MonoBehaviour
    {
        [Tooltip("Fanfare played the moment a side wins.")]
        [SerializeField] private AudioClip fanfare;

        [Range(0f, 1f)]
        [SerializeField] private float fanfareVolume = 0.8f;

        [Tooltip("How many confetti pieces the burst throws.")]
        [SerializeField] private int confettiCount = 220;

        [Tooltip("Height above the winner that the confetti bursts from. Low, because the victory " +
                 "shot frames one creature tightly — at 7 the whole burst launched above the top of " +
                 "the screen and only drifted into view seconds later, by which point the moment it " +
                 "was celebrating had passed.")]
        [SerializeField] private float burstHeight = 2.5f;

        private ParticleSystem confetti;
        private AudioSource audioSource;
        private BattleManager battleManager;
        private CameraRig.BattleCameraDirector director;

        private void Awake()
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            confetti = BuildConfetti();
        }

        private void OnEnable() => TryBind();

        private void OnDisable()
        {
            if (battleManager == null) return;

            battleManager.BattleEnded -= HandleBattleEnded;
            battleManager = null;
        }

        private void Update() => TryBind();

        /// <summary>
        /// Attach to the manager the moment one exists.
        ///
        /// Not done once in OnEnable, because Awake order between GameObjects is undefined and
        /// BattleManager.Instance is usually still null then. Binding once and giving up meant
        /// BattleEnded was raised into an empty delegate and no match ever got a celebration.
        /// </summary>
        private void TryBind()
        {
            if (battleManager != null) return;

            battleManager = BattleManager.Instance;
            if (battleManager == null) return;

            battleManager.BattleEnded += HandleBattleEnded;
            director = FindAnyObjectByType<CameraRig.BattleCameraDirector>();
        }

        private void HandleBattleEnded(Team winner)
        {
            // A mutual wipe gets no celebration. There is nobody to cheer for, and confetti over an
            // empty arena reads as the game congratulating itself.
            if (winner == Team.Neutral) return;

            if (fanfare != null) audioSource.PlayOneShot(fanfare, fanfareVolume);

            // The director picks its victor in LateUpdate, which for the frame the match ends on has
            // not run yet. Deferring one frame means Victor is populated by the time we ask.
            StartCoroutine(BurstNextFrame(winner));
        }

        private System.Collections.IEnumerator BurstNextFrame(Team winner)
        {
            yield return null;
            Burst(winner);
        }

        private void Burst(Team winner)
        {

            // Ask the camera who it is looking at rather than choosing independently. Picking our
            // own winner put the burst over a creature that was not on screen.
            CreatureUnit hero = director != null ? director.Victor : null;
            hero ??= LoudestSurvivor(winner);
            if (hero == null) return;

            // Burst over the winner rather than the arena centre, which after a one-sided fight can
            // be a patch of empty ground.
            confetti.transform.position = hero.transform.position + Vector3.up * burstHeight;

            // Play BEFORE Emit. A stopped system still accepts Emit and still reports the particles
            // in particleCount, but it does not simulate or draw them — the first version emitted a
            // full 220 flakes that hung frozen and invisible. Emission is disabled on the module, so
            // Play only starts the clock; every particle still comes from this one burst.
            confetti.Play();
            confetti.Emit(confettiCount);

            hero.Roar();
        }

        /// <summary>The survivor with the most health — the one that looks like it won.</summary>
        private static CreatureUnit LoudestSurvivor(Team winner)
        {
            var units = UnitRegistry.AliveOf(winner);

            CreatureUnit best = null;
            float bestHealth = -1f;

            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null || unit.IsDead || unit.Health == null) continue;
                if (unit.Health.Current <= bestHealth) continue;

                bestHealth = unit.Health.Current;
                best = unit;
            }

            return best;
        }

        /// <summary>
        /// A one-shot confetti burst: bright flat quads that fall and tumble.
        ///
        /// Unlit and additive-free on purpose — the arena is hazy green and a soft particle would
        /// sink into it exactly the way the creatures were already doing.
        /// </summary>
        private ParticleSystem BuildConfetti()
        {
            var host = new GameObject("VictoryConfetti");
            host.transform.SetParent(transform, false);

            var system = host.AddComponent<ParticleSystem>();
            system.Stop();

            var main = system.main;
            main.duration = 4f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4.5f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.9f;
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // Unscaled, so a celebration is not slowed or frozen by the simulation clock.
            main.useUnscaledTime = true;

            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.85f, 0.10f), new Color(1f, 0.25f, 0.35f));

            var emission = system.emission;
            emission.enabled = false;   // burst-only, driven by Emit()

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 42f;
            shape.radius = 0.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f);   // fire upward

            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-3.5f, 3.5f);
            rotation.z = new ParticleSystem.MinMaxCurve(-3.5f, 3.5f);

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = ConfettiMesh();
            renderer.alignment = ParticleSystemRenderSpace.Local;

            // Sprites/Default, NOT Unlit/Color. Unlit/Color has no vertex-colour input at all, so
            // every flake renders in the material's single colour and the per-particle gradient set
            // above is silently discarded — white paper instead of confetti. Sprites/Default
            // multiplies by vertex colour, which is how the particle system communicates its tint.
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader != null) renderer.material = new Material(shader) { color = Color.white };

            // An explicit white pixel for _MainTex. Sprites/Default multiplies texture by vertex
            // colour, and it is handed no sprite here — leaving the texture null made every flake
            // sample nothing and the whole burst rendered invisible while happily reporting 220 live
            // particles. One white texel turns the multiply into an identity.
            var white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            renderer.material.mainTexture = white;

            return system;
        }

        /// <summary>A single quad, used as the confetti flake.</summary>
        private static Mesh ConfettiMesh()
        {
            var mesh = new Mesh { name = "ConfettiFlake" };
            mesh.SetVertices(new System.Collections.Generic.List<Vector3>
            {
                new(-0.5f, -0.3f, 0f), new(0.5f, -0.3f, 0f),
                new(0.5f, 0.3f, 0f), new(-0.5f, 0.3f, 0f),
            });

            // Both windings, so a tumbling flake does not vanish for half its rotation.
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 }, 0);
            mesh.SetUVs(0, new System.Collections.Generic.List<Vector2>
            {
                new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f),
            });
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
