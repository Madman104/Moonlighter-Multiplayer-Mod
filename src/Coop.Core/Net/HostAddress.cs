using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Coop.Core.Net
{
    /// <summary>Validates a host address pasted from the clipboard (IPv4 or host name).</summary>
    public static class HostAddress
    {
        private static readonly Regex HostName = new Regex(@"^[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)*$");

        public static bool TryNormalize(string input, out string address)
        {
            address = null;
            if (input == null) return false;
            var s = input.Trim();
            if (s.Length == 0 || s.Length > 253) return false;

            if (IPAddress.TryParse(s, out var ip))
            {
                if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
                address = s;
                return true;
            }
            if (!HostName.IsMatch(s)) return false;
            address = s;
            return true;
        }
    }
}
