namespace Coop.Core.Dungeon
{
    /// <summary>
    /// Settles travel conflicts (both players use different doors at nearly the same time):
    /// the host's choice wins. The client always follows the host; the host ignores a remote
    /// travel that arrives within the window after its own (the client will follow the host).
    /// </summary>
    public sealed class TravelArbiter
    {
        private readonly bool _isHost;
        private readonly double _window;
        private double _lastLocal = double.NegativeInfinity;

        public TravelArbiter(bool isHost, double window = 0.5)
        {
            _isHost = isHost;
            _window = window;
        }

        public void LocalTravel(double now) => _lastLocal = now;

        public bool ShouldApplyRemote(double now) => !_isHost || now - _lastLocal > _window;
    }
}
