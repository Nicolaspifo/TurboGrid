using Unity.Netcode;
using UnityEngine;

namespace TurboGrid
{
    public abstract class AbilityBase : NetworkBehaviour
    {
        [SerializeField] protected float cooldownDuration;
        protected CooldownTimer LocalCooldown;

        public NetworkVariable<float> ServerCooldownEndTime = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float _serverReadyAtTime;

        protected virtual void Awake()
        {
            LocalCooldown = new CooldownTimer(cooldownDuration);
        }

        public bool CanUseLocally => LocalCooldown.IsReady;
        public float NormalizedCooldown => LocalCooldown.NormalizedRemaining;

        public void TryActivate()
        {
            if (!IsOwner) return;
            if (!LocalCooldown.IsReady) return;

            LocalCooldown.Trigger();
            RequestActivateServerRpc();
        }

        [ServerRpc]
        private void RequestActivateServerRpc()
        {
            if (Time.time < _serverReadyAtTime) return;

            bool activated = TryApplyEffectOnServer();
            if (!activated) return;

            _serverReadyAtTime = Time.time + cooldownDuration;
            ServerCooldownEndTime.Value = _serverReadyAtTime;
        }

        protected abstract bool TryApplyEffectOnServer();
    }
}