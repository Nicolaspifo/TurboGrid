namespace TurboGrid
{
    /// <summary>
    /// Pinchazo: al activarse bloquea ~2 segundos a un rival elegido al azar
    /// (nunca a quien lo lanzo). Se recoge en pista y se activa con su boton.
    /// </summary>
    public class PinchazoAbility : AbilityBase
    {
        public override PowerUpType Type => PowerUpType.Pinchazo;
    }
}
