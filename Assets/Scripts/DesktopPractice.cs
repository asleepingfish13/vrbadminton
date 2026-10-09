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
            GUI.Box(new Rect(15, Screen.height - 72, Screen.width - 30, 58),
                "DESKTOP | SPACE: start  Click near ring: hit  P: pause  R: reset + recenter | " + session.EquipmentSummary + "\n" +
                "Paused, no shuttle: 1/2/3 drills  M fixed/mixed feeds  A assist  [ / ] tension  - / = mass  Backspace: 25 LB / 5.4 G");
        }
    }
}
