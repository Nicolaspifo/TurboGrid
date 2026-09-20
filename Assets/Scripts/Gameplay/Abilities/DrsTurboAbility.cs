using UnityEngine;

namespace TurboGrid
{
    [RequireComponent(typeof(CarController))]
    public class DrsTurboAbility : AbilityBase
    {
        private CarController _car;

        protected override void Awake()
        {
            cooldownDuration = GameConstants.DRS_COOLDOWN;
            base.Awake();
            _car = GetComponent<CarController>();
        }

        protected override bool TryApplyEffectOnServer()
        {
            ApplyTurboClientRpc();
            Invoke(nameof(RevertSpeedServer), GameConstants.DRS_DURATION);
            return true;
        }

        [Unity.Netcode.ClientRpc]
        private void ApplyTurboClientRpc()
        {
            _car.SetSpeedMultiplier(GameConstants.DRS_SPEED_MULTIPLIER);
        }

        private void RevertSpeedServer()
        {
            RevertSpeedClientRpc();
        }

        [Unity.Netcode.ClientRpc]
        private void RevertSpeedClientRpc()
        {
            _car.SetSpeedMultiplier(1f);
        }
    }
}