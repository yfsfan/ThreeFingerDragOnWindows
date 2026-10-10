using System.Collections.Generic;

namespace ThreeFingerDragOnWindows {
    internal static class App {
        public static readonly settings.SettingsData SettingsData = new();
    }
}
namespace ThreeFingerDragOnWindows.settings {
    public class SettingsData {
        public enum ThreeFingerDragButtonType { NONE, LEFT, RIGHT, MIDDLE }
        public ThreeFingerDragButtonType ThreeFingerDragButton = ThreeFingerDragButtonType.LEFT;
        public bool ThreeFingerDragAllowReleaseAndRestart = true;
        public int ThreeFingerDragReleaseDelay = 500;
        public int ThreeFingerDragStartThreshold = 100;
        public int ThreeFingerDragStopThreshold = 10;
        public int ThreeFingerDragCursorAveraging = 1;
        public int ThreeFingerDragMaxFingerMoveDistance = 0;
        public Dictionary<string, ThreeFingerDragConfig> ThreeFingerDeviceDragCursorConfigs = new();
        public class ThreeFingerDragConfig {
            public bool ThreeFingerDragCursorMove = true;
            public float ThreeFingerDragCursorSpeed = 30;
            public float ThreeFingerDragCursorAcceleration = 10;
        }
    }
}
