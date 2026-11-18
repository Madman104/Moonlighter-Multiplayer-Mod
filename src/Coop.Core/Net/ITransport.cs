using System;

namespace Coop.Core.Net
{
    /// <summary>
    /// A two-player, message-based link. All work happens inside Poll(), on the caller's
    /// thread, so the Unity main thread owns every event.
    /// </summary>
    public interface ITransport : IDisposable
    {
        bool IsHost { get; }

        /// <summary>Queues one message. reliable=false may be dropped or reordered.</summary>
        void Send(byte[] payload, bool reliable);

        bool TryReceive(out byte[] payload);

        event Action Connected;
        event Action<string> Disconnected;

        void Poll();
    }
}
