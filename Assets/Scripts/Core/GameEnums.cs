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
