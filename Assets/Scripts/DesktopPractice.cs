using UnityEngine;

namespace ViveShuttleVR
{
    public sealed class DesktopPractice : MonoBehaviour
    {
        public BadmintonSession session;
        private void Update()
        {
            if (session.IsVR) return;
            if (Input.GetMouseButtonDown(0))
                session.DesktopSwing(Mathf.Clamp((Input.mousePosition.x / Mathf.Max(1, Screen.width) - 0.5f) * 2f, -1f, 1f));
        }
        private void OnGUI()
        {
            if (session.IsVR) return;
            GUI.Box(new Rect(15, Screen.height - 94, Screen.width - 30, 80),
                "DESKTOP | SPACE: start  Click near ring: hit  P: pause  R: reset + recenter | " + session.EquipmentSummary + "\n" +
                "Paused, no shuttle: F4 AI/training  F5 Easy  F6 Club  F7 Advanced  B 4U/3U  [ / ] tension\n" +
                "1/2/3 training drills  M fixed/mixed feeds  A assist  - / = shuttle mass  Backspace: 25 LB / 5.4 G");
        }
    }
}
