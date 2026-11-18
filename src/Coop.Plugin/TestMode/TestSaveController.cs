using System;
using System.IO;
using Coop.Core.TestMode;
using DigitalSunGames.Platforms;
using UnityEngine;

namespace Coop.Plugin.TestMode
{
    /// <summary>
    /// Save storage for the test client. Everything goes to
    /// persistentDataPath\CoopTestClient\, never to Steam Cloud or the real save folder.
    /// The game's own DRM-free controller leaves its async methods empty, so it can't be used.
    /// </summary>
    internal sealed class TestSaveController : IPersistenFilesController
    {
        private static string PathFor(string file) => TestSavePaths.Resolve(Application.persistentDataPath, file);

        public bool Exists(string file) => File.Exists(PathFor(file));

        public bool WriteBytes(string file, byte[] data)
        {
            var path = PathFor(file);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
            return true;
        }

        public byte[] ReadBytes(string file)
        {
            var path = PathFor(file);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        public void ExistsAsync(string file, Action<bool> callback, Action corruptedCallback)
        {
            callback?.Invoke(Exists(file));
        }

        public void WriteBytesAsync(string file, byte[] data, Action callback)
        {
            WriteBytes(file, data);
            callback?.Invoke();
        }

        public void ReadBytesAsync(string file, Action<byte[]> callback, Action corruptedCallback)
        {
            byte[] data;
            try
            {
                data = ReadBytes(file);
            }
            catch (IOException e)
            {
                CoopPlugin.Log.LogWarning("Test client could not read " + file + ": " + e.Message);
                corruptedCallback?.Invoke();
                return;
            }
            callback?.Invoke(data);
        }
    }

    internal sealed class NoAchievements : IAchievementController
    {
        public void Init(AchievementDatabase database) { }
        public void AddExpansion(AchievementDatabase database) { }
        public float GetStatValue(string statID) => 0f;
        public void SetStatValue(string statID, float value) { }
        public void AddToStatValue(string statID, float value) { }
        public void UnlockAchievement(string achievementID) { }
        public void ResetAchievementsAndStats() { }
    }

    internal sealed class NoStore : IStoreController
    {
        public void OpenStoreToDLC() { }
    }
}
