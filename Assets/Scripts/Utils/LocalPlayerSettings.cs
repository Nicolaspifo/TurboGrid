using UnityEngine;

namespace TurboGrid
{
    public enum ControlScheme
    {
        Flechas = 0,   // girar izq/der + acelerar/frenar
        Timon = 1,     // timon virtual (arrastrar) + acelerar/frenar
        Joystick = 2   // joystick: el auto va hacia donde apunta el stick
    }

    /// <summary>
    /// Preferencias LOCALES de cada celular (no viajan por red salvo el nombre).
    /// Cada jugador elige su esquema de control de forma independiente.
    /// Se guardan en PlayerPrefs para recordarlas entre partidas.
    /// </summary>
    public static class LocalPlayerSettings
    {
        private const string KEY_SCHEME = "tg_control_scheme";
        private const string KEY_NAME = "tg_player_name";
        private const string KEY_MAP = "tg_map_index";

        public const int MAX_NAME_LENGTH = 14;

        public static System.Action OnChanged;

        public static ControlScheme Scheme
        {
            get => (ControlScheme)Mathf.Clamp(PlayerPrefs.GetInt(KEY_SCHEME, (int)ControlScheme.Flechas), 0, 2);
            set
            {
                PlayerPrefs.SetInt(KEY_SCHEME, (int)value);
                PlayerPrefs.Save();
                OnChanged?.Invoke();
            }
        }

        public static string PlayerName
        {
            get => PlayerPrefs.GetString(KEY_NAME, string.Empty);
            set
            {
                PlayerPrefs.SetString(KEY_NAME, SanitizeName(value));
                PlayerPrefs.Save();
                OnChanged?.Invoke();
            }
        }

        /// <summary>
        /// Mapa elegido por el host. -1 = aleatorio.
        /// </summary>
        public static int MapIndex
        {
            get => PlayerPrefs.GetInt(KEY_MAP, -1);
            set
            {
                PlayerPrefs.SetInt(KEY_MAP, value);
                PlayerPrefs.Save();
                OnChanged?.Invoke();
            }
        }

        public static string SanitizeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string trimmed = raw.Trim();
            if (trimmed.Length > MAX_NAME_LENGTH) trimmed = trimmed.Substring(0, MAX_NAME_LENGTH);
            return trimmed;
        }

        public static string SchemeDisplayName(ControlScheme scheme)
        {
            switch (scheme)
            {
                case ControlScheme.Timon: return "Timon";
                case ControlScheme.Joystick: return "Joystick";
                default: return "Flechas";
            }
        }
    }
}
