using System;
using System.Linq;

namespace Coop.Plugin.TestMode
{
    /// <summary>
    /// Local test client: a second copy of the game on the same PC, started with
    /// --coop-test-client. It runs without Steam and keeps its saves in CoopTestClient\.
    /// </summary>
    internal static class TestClientMode
    {
        public const string Argument = "--coop-test-client";

        public static readonly bool Active =
            Environment.GetCommandLineArgs().Any(a => string.Equals(a, Argument, StringComparison.OrdinalIgnoreCase));
    }
}
