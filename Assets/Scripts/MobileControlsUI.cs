using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// On-screen driving controls for phones/tablets, built only when DrivingInput detects a
/// touch device. Left/right screen halves are hold-to-steer zones; BOOST, BRAKE and JUMP
/// buttons sit along the bottom (created after the zones so they receive the tap first).
/// A double-tap on a steer zone (hold the second tap) fires the handbrake/drift. Everything
/// writes into DrivingInput; the buggy reads only that. Auto-throttle also lives in
/// DrivingInput - these controls are the deliberate fallback.
/// </summary>
public class MobileControlsUI : MonoBehaviour
{
    private bool built;

    private void Start()
    {
        DrivingInput.ResetHeld();
        DrivingInput.DetectDevice();
        TryBuild();
    }

    private void Update()
    {
        // Detection can flip after Start (the first touch on a device whose user-agent did
        // not read as mobile), so keep trying until the controls exist.
        if (built) return;
        DrivingInput.DetectDevice();
        TryBuild();
    }

    private void TryBuild()
    {
        if (built || !DrivingInput.TouchDevice) return;
        built = true;

        // The play scene ships no EventSystem (OxygenManager creates one lazily for its end
        // screens), so without this every tap fell on the floor and nothing was clickable.
        UiKit.EnsureEventSystem();

        Canvas canvas = UiKit.CreateRootCanvas("TouchCanvas", 110);
        canvas.transform.SetParent(transform, false);

        // Zones cover only the lower band: the top stays clear for end-of-level panels
        // (order 200) and the pause buttons (order 500), which must win those taps.
        BuildSteerZone(canvas.transform, "SteerLeft", new Vector2(0f, 0f), new Vector2(0.5f, 0.55f), -1);
        BuildSteerZone(canvas.transform, "SteerRight", new Vector2(0.5f, 0f), new Vector2(1f, 0.55f), +1);

        BuildButton(canvas.transform, "BrakeButton", "BRAKE", new Vector2(0f, 0f), new Vector2(30f, 34f),
            new Vector2(180f, 130f), new Color(0.7f, 0.12f, 0.1f, 0.35f), TouchControl.Action.Brake);
        BuildButton(canvas.transform, "JumpButton", "JUMP", new Vector2(0.5f, 0f), new Vector2(0f, 34f),
            new Vector2(170f, 130f), new Color(0.2f, 0.5f, 0.85f, 0.35f), TouchControl.Action.Hop);
        BuildButton(canvas.transform, "BoostButton", "BOOST", new Vector2(1f, 0f), new Vector2(-30f, 34f),
            new Vector2(200f, 130f), new Color(0.9f, 0.5f, 0.1f, 0.4f), TouchControl.Action.Boost);
    }

    // A transparent full-height half of the screen that steers while held.
    private static void BuildSteerZone(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, int dir)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Image img = go.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f); // invisible but still receives pointer events
        TouchControl tc = go.AddComponent<TouchControl>();
        tc.action = dir < 0 ? TouchControl.Action.SteerLeft : TouchControl.Action.SteerRight;
    }

    private static void BuildButton(Transform parent, string name, string label, Vector2 anchor, Vector2 anchoredPos,
        Vector2 size, Color color, TouchControl.Action action)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        Image img = go.GetComponent<Image>();
        img.color = color;

        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(go.transform, false);
        RectTransform lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        Text t = labelGo.GetComponent<Text>();
        t.font = HUD.ResolveDefaultFont();
        t.fontSize = 30;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(1f, 1f, 1f, 0.85f);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.text = label;

        TouchControl tc = go.AddComponent<TouchControl>();
        tc.action = action;
    }
}

/// <summary>Press/release hook that feeds the shared DrivingInput touch flags. Used by the
/// steer zones (hold), and the boost/brake (hold) and jump (tap) buttons.</summary>
public class TouchControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public enum Action { SteerLeft, SteerRight, Boost, Brake, Hop }

    public Action action;
    private bool held;

    // Double-tap-to-drift on the steer zones: a second tap inside the window arms the handbrake,
    // and holding it keeps the drift on until release.
    private const float DoubleTapWindow = 0.28f;
    private float lastSteerTapTime = -10f;
    private bool handbrakeHeld;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (action == Action.SteerLeft || action == Action.SteerRight)
        {
            if (Time.unscaledTime - lastSteerTapTime <= DoubleTapWindow)
            {
                handbrakeHeld = true;
                DrivingInput.TouchHandbrake = true;
            }
            lastSteerTapTime = Time.unscaledTime;
        }
        held = true;
        Apply(true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!held) return;
        held = false;
        ReleaseHandbrake();
        Apply(false);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!held) return;
        held = false;
        ReleaseHandbrake();
        Apply(false);
    }

    private void OnDisable()
    {
        if (!held) return;
        held = false;
        ReleaseHandbrake();
        Apply(false);
    }

    private void ReleaseHandbrake()
    {
        if (!handbrakeHeld) return;
        handbrakeHeld = false;
        DrivingInput.TouchHandbrake = false;
    }

    private void Apply(bool down)
    {
        switch (action)
        {
            case Action.SteerLeft:
                if (down) DrivingInput.TouchSteer = -1;
                else if (DrivingInput.TouchSteer == -1) DrivingInput.TouchSteer = 0;
                break;
            case Action.SteerRight:
                if (down) DrivingInput.TouchSteer = 1;
                else if (DrivingInput.TouchSteer == 1) DrivingInput.TouchSteer = 0;
                break;
            case Action.Boost:
                DrivingInput.TouchBoost = down;
                break;
            case Action.Brake:
                DrivingInput.TouchBrake = down;
                break;
            case Action.Hop:
                if (down) DrivingInput.TouchHopPressed = true;
                break;
        }
    }
}
