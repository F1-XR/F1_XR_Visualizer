namespace F1XR.Interaction.World
{
    /// <summary>
    /// The four cardinal directions the gear lever can be tilted toward, plus a neutral state.
    /// A UI item is bound to one of these via the Inspector (never hard-coded), so the same rig can
    /// drive any four features.
    /// </summary>
    public enum GearDirection
    {
        None,
        Up,
        Down,
        Left,
        Right
    }
}
