namespace HoloTable.Entities
{
    /// <summary>Lifecycle of a hologram. Guards which actions are legal at any moment.</summary>
    public enum EntityState
    {
        Dormant = 0,
        Spawning = 1,
        Idle = 2,
        Attacking = 3,
        Hurt = 4,
        Transforming = 5,
        Dying = 6,
        Dead = 7,
    }

    public enum LookMode
    {
        NearestRivalThenPlayer = 0,
        PlayerOnly = 1,
        RivalOnly = 2,
        None = 3,
    }

    public enum AnimationDriveMode
    {
        /// <summary>Sets triggers; transitions/blend times are authored in the Animator Controller.</summary>
        Triggers = 0,

        /// <summary>Cross-fades directly to named states; works with a controller that has no transitions.</summary>
        CrossFadeStates = 1,
    }
}
