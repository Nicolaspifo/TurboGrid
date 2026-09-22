using UnityEngine;

namespace TurboGrid
{
    public class PinchazoAbility : AbilityBase
    {
        protected override void Awake()
        {
            cooldownDuration = GameConstants.PINCHAZO_COOLDOWN;
            base.Awake();
        }

        protected override bool TryApplyEffectOnServer()
        {
            PlayerNetworkData target = FindClosestRivalServer();
            if (target == null) return false;

            target.SetStunnedServer(true, GameConstants.PINCHAZO_STUN_DURATION);
            return true;
        }

        private PlayerNetworkData FindClosestRivalServer()
        {
            PlayerNetworkData closest = null;
            float closestDist = GameConstants.PINCHAZO_RANGE;

            foreach (var player in FindObjectsByType<PlayerNetworkData>(FindObjectsSortMode.None))
            {
                if (player.OwnerClientId == OwnerClientId) continue;

                float dist = Vector2.Distance(transform.position, player.transform.position);
                if (dist <= closestDist)
                {
                    closestDist = dist;
                    closest = player;
                }
            }

            return closest;
        }
    }
}