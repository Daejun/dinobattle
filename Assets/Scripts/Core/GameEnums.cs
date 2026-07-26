namespace DinoBattle.Core
{
    /// <summary>Which side a creature fights for. Neutral is used for props and scenery.</summary>
    public enum Team
    {
        Neutral = 0,
        Red = 1,
        Blue = 2
    }

    /// <summary>High level state of a match.</summary>
    public enum BattlePhase
    {
        /// <summary>Player is dropping creatures onto the arena. Nothing moves yet.</summary>
        Placement = 0,

        /// <summary>Creature AI is running. The player only watches and controls the camera.</summary>
        Fighting = 1,

        /// <summary>One side has been wiped out. Result panel is up.</summary>
        Finished = 2
    }

    /// <summary>Which mode a match is being played under.</summary>
    public enum GameMode
    {
        /// <summary>Two armies on a round arena. The original mode.</summary>
        Versus = 0,

        /// <summary>Climb a board of ten tiers, fighting what waits on each, up to a boss.</summary>
        Gauntlet = 1
    }

    /// <summary>
    /// Where a gauntlet run has got to.
    ///
    /// Deliberately NOT extra values on <see cref="BattlePhase"/>. Four systems switch on that enum
    /// — the HUD, the music, the camera director and the victory dance — and a new case would be
    /// silently unhandled in every one of them. A gauntlet run stays in
    /// <see cref="BattlePhase.Fighting"/> from start to finish, so all of that keeps working
    /// untouched, and the run's own state lives here where only the mode reads it.
    /// </summary>
    public enum GauntletState
    {
        /// <summary>Setting up. Nothing has been sent in yet.</summary>
        Ready = 0,

        /// <summary>Walking to the next tier. Nothing to fight on the way.</summary>
        Advancing = 1,

        /// <summary>Fighting whatever is on the current tier.</summary>
        Engaging = 2,

        /// <summary>Everything the player sent is dead. Waiting for the next wave.</summary>
        WaveWiped = 3,

        /// <summary>The boss is down.</summary>
        Cleared = 4,

        /// <summary>Wiped out with nothing left to spend.</summary>
        Defeated = 5
    }

    public static class TeamExtensions
    {
        public static Team Opponent(this Team team) => team switch
        {
            Team.Red => Team.Blue,
            Team.Blue => Team.Red,
            _ => Team.Neutral
        };
    }
}
