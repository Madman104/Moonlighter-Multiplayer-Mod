using System;
using System.IO;

namespace Coop.Core.TestMode
{
    /// <summary>
    /// Where the local test client keeps its saves, so it never touches the real ones.
    /// </summary>
    public static class TestSavePaths
    {
        public const string FolderName = "CoopTestClient";

        public static string Resolve(string persistentDataPath, string file)
        {
            if (string.IsNullOrEmpty(file))
                throw new ArgumentException("Empty save file name", nameof(file));
            if (Path.IsPathRooted(file) || file.StartsWith("\\") || file.StartsWith("/"))
                throw new ArgumentException("Save file must be relative: " + file, nameof(file));

            var parts = file.Split('\\', '/');
            foreach (var p in parts)
                if (p == "..")
                    throw new ArgumentException("Save file may not leave the test folder: " + file, nameof(file));

            return persistentDataPath.TrimEnd('\\', '/') + "\\" + FolderName + "\\" + string.Join("\\", parts);
        }
    }
}
