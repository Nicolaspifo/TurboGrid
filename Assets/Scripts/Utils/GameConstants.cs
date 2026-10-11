namespace TurboGrid
{
    /// <summary>
    /// Valores de balance centralizados (ver Docs: turbo-grid-preparacion-desarrollo.md).
    /// Ajusta aqui sin tocar el resto del codigo.
    /// </summary>
    public static class GameConstants
    {
        // Jugadores
        public const int MIN_PLAYERS = 2;
        public const int MAX_PLAYERS = 4;

        // Partida
        public const float RACE_DURATION_SECONDS = 180f; // 3 minutos
        public const float COUNTDOWN_SECONDS = 3f;
        public const int WIN_SCORE = 1000;               // victoria instantanea

        // Gasolina
        public const int TOTAL_FUEL_TOKENS = 50;
        public const int POINTS_PER_FUEL = 20;
        public const float FUEL_RESPAWN_SECONDS = 1f;
        public const float FUEL_PICKUP_RADIUS = 40f;     // unidades de mundo (la pista mide ~1385 x 795)

        // Power-ups en pista (1 DRS + 1 Pinchazo simultaneos)
        public const float POWERUP_RESPAWN_SECONDS = 5f;
        public const float POWERUP_PICKUP_RADIUS = 45f;

        public const float DRS_DURATION = 3f;
        public const float DRS_SPEED_MULTIPLIER = 1.6f;

        public const float PINCHAZO_STUN_DURATION = 2f;

        // Auto (unidades de mundo por segundo; el auto mide ~35 x 56)
        public const float CAR_MAX_SPEED = 170f;
        public const float CAR_ACCELERATION = 220f;
        public const float CAR_BRAKE = 380f;
        public const float CAR_TURN_SPEED = 170f;        // grados por segundo a velocidad alta

        // Nombres de escena
        public const string MAIN_MENU_SCENE = "MainMenu";
    }
}
