using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Nota para el equipo visible en el Inspector (no hace nada en el juego).
    /// </summary>
    public class TurboGridNote : MonoBehaviour
    {
        [TextArea(3, 10)] public string note;
    }
}
