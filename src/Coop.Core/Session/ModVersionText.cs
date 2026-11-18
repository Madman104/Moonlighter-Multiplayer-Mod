using System;
using System.Text.RegularExpressions;

namespace Coop.Core.Session
{
    /// <summary>Mod version numbers ("1.5.0", "v1.5.0") for the update check.</summary>
    public static class ModVersionText
    {
        public static bool TryParse(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(text)) return false;
            var m = Regex.Match(text.Trim(), @"^v?(\d+)\.(\d+)(?:\.(\d+))?");
            if (!m.Success) return false;
            version = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
            return true;
        }

        /// <summary>True when candidate is a higher version than current (unparseable = false).</summary>
        public static bool IsNewer(string candidate, string current)
            => TryParse(candidate, out var a) && TryParse(current, out var b) && a > b;

        /// <summary>The tag_name of a GitHub "latest release" JSON reply, or null.</summary>
        public static string TagFromReleaseJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
