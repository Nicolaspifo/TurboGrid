namespace TurboGrid
{
    /// <summary>
    /// Valores de balance centralizados. Ajusta aqui sin tocar el resto del codigo.
    /// </summary>
    public static class GameConstants
    {
        public const int MIN_PLAYERS = 2;
        public const int MAX_PLAYERS = 4;

        public const int TOTAL_FUEL_TOKENS = 50;
        public const float RACE_DURATION_SECONDS = 150f; // 2:30, ajustable entre 120-180

        public const float COUNTDOWN_SECONDS = 3f;

        // Habilidades
        public const float DRS_DURATION = 2.5f;
        public const float DRS_SPEED_MULTIPLIER = 1.6f;
        public const float DRS_COOLDOWN = 8f;

        public const float PINCHAZO_STUN_DURATION = 2f;
        public const float PINCHAZO_RANGE = 4f;       // radio para elegir objetivo
        public const float PINCHAZO_COOLDOWN = 10f;

        public const float CAR_BASE_SPEED = 6f;
        public const float CAR_ROTATION_SPEED = 220f;

        public const float TOKEN_COLLECT_RADIUS = 0.4f;
    }
}
