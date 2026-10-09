using UnityEngine;

namespace ViveShuttleVR
{
    public enum MenuGestureAction { None, Pause, Recenter }

    // Vive application-menu button only; grip/side buttons never pause play.
    // A short RELEASE toggles pause. Holding fires reset once, not pause first.
    public sealed class PracticeMenuGesture
    {
        public const float ResetHoldSeconds = 1.1f;
        private bool wasPressed, firedHold;
        private float held;
        public MenuGestureAction Step(bool pressed, float deltaTime)
        {
            if (pressed)
            {
                if (!wasPressed) { held = 0f; firedHold = false; }
                held += Mathf.Clamp(deltaTime, 0f, .1f);
                wasPressed = true;
                if (!firedHold && held >= ResetHoldSeconds)
                { firedHold = true; return MenuGestureAction.Recenter; }
                return MenuGestureAction.None;
            }
            bool tap = wasPressed && !firedHold;
            Reset();
            return tap ? MenuGestureAction.Pause : MenuGestureAction.None;
        }
        public void Reset() { wasPressed = firedHold = false; held = 0f; }
    }
}
