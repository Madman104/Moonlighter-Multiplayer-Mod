using Coop.Core.Session;
using Coop.Plugin.Runtime;
using UnityEngine;

namespace Coop.Plugin.Sync
{
    /// <summary>Sends the local Will and spawns/removes the other player's puppet.</summary>
    internal sealed class PlayerSync : MonoBehaviour
    {
        private readonly LocalPlayerSampler _sampler = new LocalPlayerSampler();
        private RemotePlayerPuppet _puppet;
        private PeerSession _hooked;

        // Awake, not Start: a session started by launch options must not be missed.
        private void Awake()
        {
            CoopManager.Instance.SessionStarted += OnSessionStarted;
            CoopManager.Instance.SessionClosed += _ => RemovePuppet();
            if (CoopManager.Instance.Session != null) OnSessionStarted();
        }

        private void OnSessionStarted()
        {
            RemovePuppet();
            _sampler.Reset();
            _hooked = CoopManager.Instance.Session;
            _hooked.StateChanged += OnStateChanged;
        }

        private void OnStateChanged(SessionState state)
        {
            if (state == SessionState.Connected && _puppet == null)
                _puppet = RemotePlayerPuppet.Spawn(_hooked);
        }

        private void RemovePuppet()
        {
            if (_hooked != null) _hooked.StateChanged -= OnStateChanged;
            if (_puppet != null) _puppet.Despawn();
            _puppet = null;
        }

        private void Update()
        {
            _sampler.Update(CoopManager.Instance.Session);
        }
    }
}
