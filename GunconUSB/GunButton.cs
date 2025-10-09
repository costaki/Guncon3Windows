namespace GunconUSB
{
    /// <summary>
    /// Botones lógicos que expone la Guncon para mapear a teclado/ratón.
    /// Debe ser público para que lo vea Guncon3Console.
    /// </summary>
    public enum GunButton
    {
        // Botones físicos
        Trigger,
        A1,
        A2,
        B1,
        B2,
        C1,
        C2,
        AClick,
        BClick,

        // Ejes del stick izquierdo “digitalizados”
        LUp,
        LDown,
        LLeft,
        LRight
    }
}
