using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ridge Survival's death-spiral: watches the buggy on the ridge crest and, when it has
/// fallen MORE THAN HALF-WAY DOWN the ridge flank (well off the high road), fades the screen
/// to black and ends the run with the distance reached as the result. It also ends the run on
/// a drop into one of the crest's hop gaps (GAP AHEAD), which is reported as its own reason so
/// the rewarded ad can put the player back BEHIND the gap - the stuck-reset cannot, because the
/// run-up behind a gap is far too short to clear it. Deactivated windows: while a collapse pit
/// is under the rover (still on the crest - not a fall), during the end-sequence, and once the
/// run is already over.
/// </summary>
public class RidgeFallDetector : MonoBehaviour
{
    public BuggyController buggy;
    public MoonTerrainGen terrain;

    [Tooltip("How long the screen takes to fade out after the rover drops off the ridge (seconds).")]
    public float fadeDuration = 1.2f;

    private bool tripped;
    private float tripTime;
    private GameObject fadeGo;
    /// <summary>Whether the trip that is playing out was a hop-gap drop rather than a fall off
    /// the flank - the two end the run differently (see the fade branch below).</summary>
    private bool fellIntoGap;

    private void Update()
    {
        if (!GameFlow.IsRidgeSurvival) return;
        if (buggy == null) { buggy = FindAnyObjectByType<BuggyController>(); if (buggy == null) return; }
        if (terrain == null) { terrain = FindAnyObjectByType<MoonTerrainGen>(); if (terrain == null) return; }

        OxygenManager oxygen = OxygenManager.Instance;
        if (oxygen == null || oxygen.gameOver || oxygen.reachedEnd || oxygen.endSequenceActive) return;

        if (!tripped)
        {
            Vector3 p = buggy.transform.position;
            float z = p.z;
            float cx = terrain.RaceCenterX(z);

            // Dropped into one of the hop gaps. Checked FIRST because it is not a lateral fall
            // and would otherwise sit in the gap being "beside the crest", reading as playable
            // until the stuck-reset eventually fired - and that reset lands the buggy back in
            // the same gap with no run-up, which is unwinnable rather than merely unlucky. It
            // ends the run as its own reason so the rewarded ad can respawn the player BEHIND
            // the gap (OxygenManager.FellIntoGap).
            if (terrain.InRidgeCrevice(p))
            {
                fellIntoGap = true;
                Trip();
                return;
            }

            // Still on / beside the crest - including a collapse pit dug INTO it - is not a
            // fall: the dome and its flank edges stay playable. Only leaving the high road
            // for the deep flank below counts.
            if (Mathf.Abs(p.x - cx) <= terrain.raceRidgeCrestHalfWidth + 6f) return;

            float crestY = terrain.SampleHeight(new Vector3(cx, 0f, z));
            float baseY = terrain.SampleHeight(new Vector3(cx - 140f, 0f, z));
            float drop = Mathf.Max(8f, crestY - baseY);
            if (p.y >= crestY - drop * 0.5f) return;

            Trip();
        }
        else
        {
            float t = Mathf.Clamp01((Time.time - tripTime) / fadeDuration);
            if (fadeGo != null)
            {
                Image img = fadeGo.GetComponent<Image>();
                img.color = new Color(0f, 0f, 0f, t);
            }
            if (t >= 1f)
            {
                // The fade canvas must DIE before the game-over canvas builds (it sits ABOVE
                // the result screens' sort order, so leaving it up would hide them).
                if (fadeGo != null) Destroy(fadeGo);
                fadeGo = null;
                tripped = false;
                // The fade canvas is gone by now either way, so a rewarded-ad respawn is not
                // left staring at a black screen.
                if (fellIntoGap) oxygen.FellIntoGap();
                else oxygen.FallOffRidge();
                fellIntoGap = false;
            }
        }
    }

    private void Trip()
    {
        tripped = true;
        tripTime = Time.time;
        if (buggy != null) buggy.inputEnabled = false;

        Canvas canvas = UiKit.CreateRootCanvas("RidgeFallFade", 400);
        fadeGo = new GameObject("Fade", typeof(RectTransform), typeof(Image));
        fadeGo.transform.SetParent(canvas.transform, false);
        Image img = fadeGo.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = false;
        UiKit.StretchToFull(fadeGo.GetComponent<RectTransform>());
    }
}