using Coop.Core.Session;

namespace Coop.Core.Sync
{
    public static class PuppetVisibility
    {
        /// <summary>
        /// The other player's Will is drawn only while connected and when both Wills are in
        /// the same scene. A scene id below 0 means that player has no Will (menu, loading).
        /// </summary>
        public static bool ShouldShow(int localSceneId, int remoteSceneId, SessionState state)
        {
            return state == SessionState.Connected
                && localSceneId >= 0
                && localSceneId == remoteSceneId;
        }
    }
}
