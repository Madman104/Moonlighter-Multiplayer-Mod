using System;
using System.Linq;
using Coop.Core.Protocol;

namespace Coop.Core.Session
{
    public static class HandshakeRules
    {
        /// <summary>Returns null when the client may join, otherwise the rejection to send.</summary>
        public static Reject Validate(Hello host, Hello client, bool ignoreDlc)
        {
            if (!string.Equals(host.ModVersion, client.ModVersion, StringComparison.Ordinal))
                return new Reject
                {
                    Reason = RejectReason.ModVersion,
                    Detail = $"Co-op mod version mismatch (mod version: host {host.ModVersion}, you {client.ModVersion})",
                };

            if (!string.Equals(host.GameVersion, client.GameVersion, StringComparison.Ordinal))
                return new Reject
                {
                    Reason = RejectReason.GameVersion,
                    Detail = $"Game version mismatch (host {host.GameVersion}, you {client.GameVersion})",
                };

            if (!ignoreDlc)
            {
                var a = (host.OwnedDlc ?? new string[0]).Distinct().OrderBy(s => s, StringComparer.Ordinal);
                var b = (client.OwnedDlc ?? new string[0]).Distinct().OrderBy(s => s, StringComparer.Ordinal);
                if (!a.SequenceEqual(b))
                    return new Reject
                    {
                        Reason = RejectReason.Dlc,
                        Detail = $"DLC mismatch (host owns: {Describe(host.OwnedDlc)}; you own: {Describe(client.OwnedDlc)})",
                    };
            }

            return null;
        }

        private static string Describe(string[] dlc)
            => dlc == null || dlc.Length == 0 ? "none" : string.Join(", ", dlc);
    }
}
