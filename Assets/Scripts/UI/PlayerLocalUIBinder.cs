using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// OBSOLETO: se conserva solo porque los prefabs Vehiculo_X lo tienen agregado.
    /// Ahora cada elemento de UI se enlaza solo con el auto local usando
    /// PlayerNetworkData.OnLocalPlayerSpawned (HUD, botones, camara, mini-mapa)
    /// y los controles tactiles escriben en CarInput.
    /// Se puede quitar del prefab sin problema.
    /// </summary>
    public class PlayerLocalUIBinder : MonoBehaviour
    {
    }
}
