using System;

namespace Coop.Core.Net
{
    /// <summary>Host side: hands out one transport per guest that connects.</summary>
    public interface IGuestListener : IDisposable
    {
        void Poll();
        bool TryAccept(out ITransport transport);
    }
}
