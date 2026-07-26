using DinoBattle.Core;
using UnityEngine;

namespace DinoBattle.UI
{
    /// <summary>
    /// Background music, switched by battle phase and crossfaded between the two.
    ///
    /// From a four-year-old's playtest: "노래가 아예 없어. 조용하니까 좀 이상해." Silence was not read
    /// as calm, it was read as something being wrong with the game.
    ///
    /// Two AudioSources rather than one, because a cut between tracks is more jarring than no music
    /// at all — the fight starting should feel like the music leaning in, not like a channel change.
    /// One source holds the outgoing track while the other fades the new one up.
    ///
    /// Ignores <see cref="Time.timeScale"/> throughout: it fades on unscaled time and the sources are
    /// left at pitch 1, so pausing or slowing the simulation does not turn the soundtrack into a
    /// groan. The music is not part of the simulation.
    /// </summary>
    [DisallowMultipleComponent]
    public class BattleMusic : MonoBehaviour
    {
        [Tooltip("Plays while the player is setting up, and over the result screen.")]
        [SerializeField] private AudioClip placementTrack;

        [Tooltip("Plays while creatures are fighting.")]
        [SerializeField] private AudioClip battleTrack;

        [Tooltip("Target volume. Well under 1 — this sits under roars and bites, which are the sounds " +
                 "that actually carry information about the fight.")]
        [Range(0f, 1f)]
        [SerializeField] private float volume = 0.35f;

        [Tooltip("Seconds to crossfade between tracks.")]
        [SerializeField] private float crossfade = 1.2f;

        private AudioSource active;
        private AudioSource fading;
        private AudioClip wanted;
        private float fadeProgress = 1f;

        private BattleManager battleManager;

        private void Awake()
        {
            active = CreateSource("MusicA");
            fading = CreateSource("MusicB");
        }

        private AudioSource CreateSource(string sourceName)
        {
            var host = new GameObject(sourceName);
            host.transform.SetParent(transform, false);

            var source = host.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0f;

            // 2D. Spatialising the soundtrack would pan it as the player orbits the camera.
            source.spatialBlend = 0f;

            // Music is long; decompressing it up front costs several megabytes of memory for nothing.
            source.ignoreListenerPause = true;
            return source;
        }

        private void OnEnable()
        {
            // Deliberately does NOT resolve the manager here. Awake order between GameObjects is
            // undefined, so BattleManager.Instance is very often still null at this point — the same
            // trap that once made the HUD throw on a null Loadout. Binding here and giving up left
            // the placement track playing over the entire battle, because the phase change that
            // should have swapped it was raised before anything was listening.
            TryBind();
        }

        private void OnDisable()
        {
            if (battleManager == null) return;

            battleManager.PhaseChanged -= HandlePhase;
            battleManager = null;
        }

        /// <summary>Attach to the manager the moment one exists, and sync to whatever phase it is in.</summary>
        private void TryBind()
        {
            if (battleManager != null) return;

            battleManager = BattleManager.Instance;
            if (battleManager == null) return;

            battleManager.PhaseChanged += HandlePhase;

            // Sync immediately rather than waiting for the next change, which on the opening screen
            // would otherwise never come.
            HandlePhase(battleManager.Phase);
        }

        private void HandlePhase(BattlePhase phase)
        {
            // Finished keeps the battle track rather than snapping back: the result screen lands
            // within a second of the last kill, and swapping tracks on top of the victory fanfare
            // makes both sound like mistakes.
            AudioClip next = phase == BattlePhase.Fighting || phase == BattlePhase.Finished
                ? battleTrack
                : placementTrack;

            Play(next);
        }

        private void Play(AudioClip clip)
        {
            if (clip == null || clip == wanted) return;
            wanted = clip;

            // Swap the roles: whatever was playing becomes the one fading out.
            (active, fading) = (fading, active);

            active.clip = clip;
            active.volume = 0f;
            active.Play();

            fadeProgress = 0f;
        }

        private void Update()
        {
            TryBind();

            if (fadeProgress >= 1f) return;

            fadeProgress = crossfade <= 0f
                ? 1f
                : Mathf.Clamp01(fadeProgress + Time.unscaledDeltaTime / crossfade);

            active.volume = volume * fadeProgress;
            fading.volume = volume * (1f - fadeProgress);

            if (fadeProgress < 1f) return;

            fading.Stop();
            fading.clip = null;
        }
    }
}
