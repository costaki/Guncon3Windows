namespace GunconUSB
{
    /// <summary>
    /// Logical buttons exposed by the Guncon to map to keyboard/mouse.
    /// Must be public so that Guncon3Console can use it.
    /// </summary>
    public enum GunButton
    {
        // Physical buttons
        Trigger,
        A1,
        A2,
        B1,
        B2,
        C1,
        C2,
        AClick,
        BClick,

        // Left stick axes “digitalized”
        LUp,
        LDown,
        LLeft,
        LRight,

        // Right stick axes “digitalized”
        RUp,
        RDown,
        RLeft,
        RRight
    }
}
