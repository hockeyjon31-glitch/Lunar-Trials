using UnityEngine;

namespace Gadd420
{
    public class ThirdPersonCamera : MonoBehaviour
    {
        public float minYAngle = -20.0f;
        public float MaxYAngle = 80.0f;

        public Transform lookAt;
        private Transform camTransform;

        [Header("Orbit")]
        public float distance = 5.0f;
        public float mouseSens = 1f;
        public float heightOffset = 2.0f;

        [Header("Cursor")]
        public bool lockCursorOnStart = true;
        public bool relockOnLeftClick = true;

        private float currentX = 0.0f;
        private float currentY = 45.0f;

        private bool cursorLocked;

        private void Start()
        {
            camTransform = transform;

            if (lockCursorOnStart)
                LockCursor();
            else
                UnlockCursor();
        }

        private void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            cursorLocked = true;
        }

        private void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            cursorLocked = false;
        }

        private void Update()
        {
            if (Input_Compat.GetEscapeDown())
                UnlockCursor();

            if (!cursorLocked)
            {
                if (relockOnLeftClick && Input_Compat.GetLeftClickDown())
                    LockCursor();

                return;
            }

            Vector2 delta = Input_Compat.GetMouseDelta();

            currentX += delta.x * mouseSens;
            currentY -= delta.y * mouseSens;

            currentY = Mathf.Clamp(currentY, minYAngle, MaxYAngle);
        }

        private void LateUpdate()
        {
            if (!lookAt) return;

            Vector3 targetPos = lookAt.position + Vector3.up * heightOffset;

            Vector3 dir = new Vector3(0f, 0f, -distance);
            Quaternion rotation = Quaternion.Euler(currentY, currentX, 0f);

            camTransform.position = targetPos + rotation * dir;
            camTransform.LookAt(targetPos);
        }
    }
}
