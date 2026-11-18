using System.Collections.Generic;
using Coop.Core.Session;
using Coop.Plugin.Runtime;
using UnityEngine;

namespace Coop.Plugin.Sync
{
    /// <summary>Sends the local Will and keeps one puppet per other player (up to three).</summary>
    internal sealed class PlayerSync : MonoBehaviour
    {
        public const float StaleSeconds = 5f;

        private readonly LocalPlayerSampler _sampler = new LocalPlayerSampler();
        private readonly Dictionary<int, RemotePlayerPuppet> _puppets = new Dictionary<int, RemotePlayerPuppet>();
        private CoopLink _hooked;

        // Awake, not Start: a session started by launch options must not be missed.
        private void Awake()
        {
            CoopManager.Instance.SessionStarted += OnSessionStarted;
            CoopManager.Instance.SessionClosed += _ => RemoveAll();
            if (CoopManager.Instance.Session != null) OnSessionStarted();
        }

        private void OnSessionStarted()
        {
            RemoveAll();
            _sampler.Reset();
            _hooked = CoopManager.Instance.Session;
            _hooked.PlayerStateReceived += OnAnyState;
            _hooked.PeerLeft += Remove;
        }

        // The first update from a player we haven't drawn yet: make their puppet.
        private void OnAnyState(Coop.Core.Protocol.PlayerState st)
        {
            int from = _hooked.LastFrom;
            if (from < 0 || from == _hooked.LocalIndex || _puppets.ContainsKey(from)) return;
            _puppets[from] = RemotePlayerPuppet.Spawn(_hooked, from);
        }

        private void Remove(int index)
        {
            if (_puppets.TryGetValue(index, out var p) && p != null) p.Despawn();
            _puppets.Remove(index);
        }

        private void RemoveAll()
        {
            if (_hooked != null)
            {
                _hooked.PlayerStateReceived -= OnAnyState;
                _hooked.PeerLeft -= Remove;
            }
            foreach (var p in _puppets.Values) if (p != null) p.Despawn();
            _puppets.Clear();
        }

        private void Update()
        {
            _sampler.Update(CoopManager.Instance.Session);
            // Guests aren't told when another guest leaves: drop puppets that went quiet.
            var stale = new List<int>();
            foreach (var kv in _puppets)
                if (kv.Value == null || Time.realtimeSinceStartup - kv.Value.LastStateAt > StaleSeconds) stale.Add(kv.Key);
            foreach (var i in stale) Remove(i);
        }
    }
}
