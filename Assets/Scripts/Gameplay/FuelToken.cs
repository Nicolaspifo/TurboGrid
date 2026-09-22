using Unity.Netcode;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Ficha de combustible individual. Detecta colision con el auto y,
    /// SOLO SI CORRE EN EL SERVIDOR, valida y otorga el punto antes de
    /// despawnearse. Esto evita que un cliente modificado se "autootorgue"
    /// fichas que en realidad no toco, o que dos jugadores la recojan a la vez.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Collider2D))]
    public class FuelToken : NetworkBehaviour
    {
        private bool _consumed;

        private void OnTriggerEnter2D(Collider2D other)
        {
            Debug.Log($"[FuelToken] Trigger con: {other.name}, IsServer={IsServer}");
            if (!IsServer) return;   // la logica de recoleccion vive solo en el servidor
            if (_consumed) return;

            var player = other.GetComponentInParent<PlayerNetworkData>();
            if (player == null) return;

            float dist = Vector2.Distance(transform.position, other.transform.position);
            if (dist > GameConstants.TOKEN_COLLECT_RADIUS * 3f) return; // sanity check extra

            _consumed = true;
            player.AddFuelServer(1);

            NetworkObject.Despawn(true); // destruye la instancia en todos los clientes
        }
    }
}
