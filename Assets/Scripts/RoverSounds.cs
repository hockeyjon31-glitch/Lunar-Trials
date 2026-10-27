using UnityEngine;

/// <summary>
/// Audio for the Apollo LRV drive. Its generated engine loop is baked with a seamless PCM
/// overlap and played continuously by one looping AudioSource to avoid audible gain pumping.
///   roll   - the imported CrunchyWheelSound.mp3 (Assets/Resources) looped at a quiet volume
///            whenever the buggy moves, even with no throttle (coasting downhill). The loop
///            is made seamless (crossfade at the seam) so it never clicks or stutters.
///   boost  - a higher-pitched, slightly louder pass of the same generated engine sound.
///   chime  - a C5-E5-G5-C6 arpeggio played on gold-contact harvest (the "collect" cue).
/// Clips are baked once and cached, so there is no per-frame allocation after startup.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class RoverSounds : MonoBehaviour
{
    private const float MotorHz = 45f;
    private const float TopSpeedKmh = 32f; // BuggyController's maxSpeedKmh speed shelf

    private static AudioClip motorClip;
    // Liaison radio ping for the Q prospector scan; apologetic beep when oxygen runs critical.
    private static AudioClip radarClip;
    private static AudioClip warnClip;
    private static AudioClip chimeClip;
    private static AudioClip collectClip;
    private static AudioClip oxygenCueClip;
    private static AudioClip celebrateClip;
    private static AudioClip rocketClip;
    private static AudioClip takeOffClip;
    private static AudioClip padClankClip;
    private static AudioClip toolsClankClip;
    private static AudioClip commisClip;
    private static AudioClip claxonClip;
    private static AudioClip stormAlertClip;
    private static AudioClip meteorWhistleClip;
    private static AudioClip meteorImpactClip;
    private static AudioClip slideClip;
    private static AudioClip eruptRumbleClip;
    private static AudioClip rockfallClip;
    private static AudioClip volcanoExplosionClip;
    private static AudioClip lightningClip;
    private static AudioClip tokenClip;
    private static bool baking = false;

    private AudioSource claxonSrc;
    [Tooltip("How long the claxon keeps ringing after the countdown ends (the player is dead) before it cuts out.")]
    [Range(0f, 3f)]
    public float claxonCutDelay = 1f;
    // When we should stop the claxon after the grace countdown ends (-1 = no cut pending).
    private float claxonStopAt = -1f;

    private float chimeCooldown = 0f;
    private float pingCooldown = 0f;
    private float warnNextTime = 0f;
    private float lastSonarTick = -10f;

    // Rolling crunch: wheels over the ground regardless of throttle, even when coasting,
    // volume bands by road speed (tweakable live in the Inspector during editor playtest).
    [Header("Rolling crunch volume (0-1 of full source scale)")]
    [Tooltip("Rolling crunch volume for speeds up to 10 km/h.")]
    [Range(0f, 0.5f)]
    public float rollVolumeLow = 0.05f;
    [Tooltip("Rolling crunch volume from 10 to 20 km/h.")]
    [Range(0f, 0.5f)]
    public float rollVolumeMid = 0.10f;
    [Tooltip("Rolling crunch volume from 20 to 32 km/h.")]
    [Range(0f, 0.5f)]
    public float rollVolumeHigh = 0.15f;
    [Tooltip("Rolling crunch volume above 32 km/h.")]
    [Range(0f, 0.5f)]
    public float rollVolumePeak = 0.20f;
    [Tooltip("Extra rolling crunch volume while braking - the tyres scrub harder across the regolith.")]
    [Range(0f, 0.6f)]
    public float rollVolumeBrake = 0.35f;

    [Header("Handbrake slide")]
    [Tooltip("Minimum yaw rate (deg/s) for the skid to sound - a handbrake that barely turns the "
            + "rover stays quiet, so only a real handbrake TURN plays the sample.")]
    public float slideYawThresholdDeg = 70f;
    [Tooltip("Yaw-rate threshold on a LOW-GRAVITY world. With less grip the buggy slides for ages "
            + "before the tail breaks right around, so a much smaller yaw still counts as a skid - "
            + "this is what lets the skid sound fire off Venus.")]
    public float slideYawThresholdLowGravityDeg = 34f;
    [Tooltip("Minimum road speed (km/h) for the skid - a stationary lever pull is silent.")]
    public float slideMinSpeed = 3f;
    [Tooltip("Skid sample volume (0-1) at EARTH-like gravity (the base).")]
    [Range(0f, 1f)]
    public float slideVolume = 0.8f;
    [Tooltip("Skid volume multiplier on a HIGH-gravity world (Venus): more grip and a louder, more "
            + "frequent scrub, so the sample is dialled back to blend.")]
    [Range(0f, 1f)]
    public float slideVolumeHighGravityScale = 0.6f;
    [Tooltip("Skid volume multiplier on a LOW-gravity world: less grip, so a lighter and quieter "
            + "slide (40% of the base).")]
    [Range(0f, 1f)]
    public float slideVolumeLowGravityScale = 0.4f;
    [Tooltip("Gravity (in g, Earth = 9.81) at or above which the HIGH-gravity skid tuning applies.")]
    public float slideGravityHighNorm = 0.9f;
    [Tooltip("Gravity (in g, Earth = 9.81) at or below which the LOW-gravity skid tuning applies.")]
    public float slideGravityLowNorm = 0.5f;
    [Tooltip("Minimum gap between skid retriggers while one continuous slide is held (seconds).")]
    public float slideRepeatGap = 0.16f;
    private float slideRetriggerAt = -10f;

    private static AudioClip rollClip;
    private AudioSource rollSrc;

    private AudioSource motor;
    private BuggyController buggy;

    [Header("Engine loop")]
    [Tooltip("Idle layer volume: quieter than the regular engine, while retaining a faint hum.")]
    [Range(0f, 0.3f)]
    public float idleEngineVolume = 0.1f;
    [Tooltip("Pitch multiplier for the idle version of the generated engine; lower values make a deeper hum.")]
    [Range(0.3f, 1f)]
    public float idleEnginePitch = 0.62f;
    [Tooltip("Time used to crossfade between idle, regular and boost engine layers.")]
    [Range(0.05f, 1f)]
    public float engineCrossfadeSeconds = 0.3f;
    [Tooltip("PCM overlap baked into the procedural loop seam, in milliseconds.")]
    [Range(10f, 50f)]
    public float engineLoopCrossfadeMilliseconds = 30f;

    // Blend weights are normalized so quick state changes remain continuous, even if a previous
    // crossfade has not finished yet.
    private float idleEngineMix = 1f;
    private float driveEngineMix;
    private float boostEngineMix;
    private float engineActiveMix = 1f;

    // Suspension state: while the game is PAUSED (Time.timeScale = 0) the Update that drives
    // the motor/roll volumes stops converging, so the loops would keep humming at their last
    // level. SetDriveSuspended zeroes them for the pause overlay and restores them on resume.
    private bool driveSuspended;
    private float rollVolBeforeSuspend;

    private void Awake()
    {
        buggy = GetComponent<BuggyController>();

        motor = GetComponent<AudioSource>();
        motor.spatialBlend = 0f;
        motor.playOnAwake = false;
        motor.volume = 0f;
        motor.pitch = 0.7f;
        AudioClip engineLoop = LoadMotorClip(engineLoopCrossfadeMilliseconds);
        motor.clip = engineLoop;
        motor.loop = true;
        motor.Play();

        // A second source for rolling crunch that plays whenever the buggy moves forward,
        // even when not applying throttle, based on wheel RPM over the terrain.
        rollSrc = gameObject.AddComponent<AudioSource>();
        rollSrc.spatialBlend = 0f;
        rollSrc.playOnAwake = false;
        rollSrc.loop = true;
        rollSrc.volume = 0f;
        rollSrc.pitch = 0.9f;
        rollSrc.clip = LoadRollClip();
        rollSrc.Play();
    }

    /// <summary>Silences the drive loops (engine + rolling crunch) for the pause overlay and
    /// restores their pre-pause levels on resume. Called by PauseSystem, since a frozen
    /// Time.timeScale stops the per-frame volume control but not the looping AudioSources.</summary>
    public void SetDriveSuspended(bool suspended)
    {
        if (driveSuspended == suspended) return;
        driveSuspended = suspended;
        if (suspended)
        {
            rollVolBeforeSuspend = rollSrc != null ? rollSrc.volume : 0f;
            if (motor != null) motor.volume = 0f;
            if (rollSrc != null) rollSrc.volume = 0f;
        }
        else
        {
            if (rollSrc != null) rollSrc.volume = rollVolBeforeSuspend;
        }
    }

    // ------------------------------------------------------------ motor

    private static float lpPrev = 0f;
    private static float Lowpass(float input)
    {
        // One-pole IIR at ~220 Hz cutoff for a 44.1 kHz stream (per-sample state carries).
        const float alpha = 0.065f;
        lpPrev = lpPrev * (1f - alpha) + input * alpha;
        return lpPrev;
    }

    private static AudioClip LoadMotorClip(float crossfadeMilliseconds)
    {
        if (motorClip != null) return motorClip;
        // Use the generated motor instead of the imported MP3, which still blipped when it
        // restarted. Blend the clip seam once in PCM, then let Unity loop that clip continuously.
        AudioClip procedural = BakeMotorClip();
        motorClip = SeamlessLoop(procedural, "LRV_ProceduralEngine", crossfadeMilliseconds);
        return motorClip;
    }

    private static AudioClip BakeMotorClip()
    {
        if (motorClip != null) return motorClip;
        if (baking) { Debug.LogWarning("RoverSounds.BakeMotorClip re-entered; returning cached null."); return null; }
        baking = true;
        try
        {
            lpPrev = 0f;
            int sr = 44100;
            float dur = 1f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            float inv = 1f / sr;
            for (int i = 0; i < samples; i++)
            {
                float t = i * inv;

                // Band-limited sawtooth stack. With a 45 Hz fundamental and a 1.0 s clip the
                // phase repeats exactly, so the loop is seamless.
                float s = 0f;
                float amp = 1f;
                for (int h = 1; h <= 12; h++)
                {
                    s += amp * Mathf.Sin(Mathf.PI * 2f * MotorHz * h * t);
                    amp *= 0.72f;
                }
                s *= 0.25f;

                data[i] = Mathf.Clamp(Lowpass(s) * 2.2f, -1f, 1f);
            }

            motorClip = AudioClip.Create("LRV_Motor", samples, 1, sr, false);
            motorClip.SetData(data, 0);
            motorClip.name = "LRV_Motor";
            return motorClip;
        }
        finally
        {
            baking = false;
        }
    }

    // ------------------------------------------------------------ chime

    private static AudioClip BakeChimeClip()
    {
        if (chimeClip != null) return chimeClip;
        if (baking) { Debug.LogWarning("RoverSounds.BakeChimeClip re-entered; returning cached null."); return null; }
        baking = true;
        try
        {
            int sr = 44100;
            float[] freqs = { 523.25f, 659.25f, 783.99f, 1046.5f }; // C5 E5 G5 C6
            float noteDur = 0.16f;
            float release = 0.12f;
            int samples = Mathf.RoundToInt(sr * (4f * noteDur + 0.2f));
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sr;
                float s = 0f;
                int idx = Mathf.Min((int)(t / noteDur), freqs.Length - 1);
                float local = t - idx * noteDur;
                float env = Mathf.Min(1f, local / 0.01f) * Mathf.Exp(-local / 0.05f);
                env *= Mathf.Clamp01((noteDur + release - local) / release);
                if (env <= 0f) env = 0f;
                float f = freqs[idx];
                s = (Mathf.Sin(Mathf.PI * 2f * f * t) * 0.6f
                     + Mathf.Sin(Mathf.PI * 2f * f * 2f * t) * 0.2f
                     + Mathf.Sin(Mathf.PI * 2f * f * 3f * t) * 0.08f) * env;
                data[i] = Mathf.Clamp(s * 0.9f, -1f, 1f);
            }

            chimeClip = AudioClip.Create("LRV_Chime", samples, 1, sr, false);
            chimeClip.SetData(data, 0);
            chimeClip.name = "LRV_Chime";
            return chimeClip;
        }
        finally
        {
            baking = false;
        }
    }

    // ------------------------------------------------------------ token clink (wallet transfer)

    private static AudioClip LoadTokenClip()
    {
        if (tokenClip != null) return tokenClip;
        tokenClip = BakeTokenClip();
        return tokenClip;
    }

    private static AudioClip BakeTokenClip()
    {
        if (tokenClip != null) return tokenClip;
        if (baking) { Debug.LogWarning("RoverSounds.BakeTokenClip re-entered; returning cached null."); return null; }
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 0.14f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            // A bright metallic "clink": a high glassy partial over the struck note, both
            // decaying fast, with a sub-tone weight under it - like a coin landing on a pile.
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sr;
                float s = Mathf.Sin(Mathf.PI * 2f * 2093f * t)   * Mathf.Exp(-t / 0.020f)
                        + Mathf.Sin(Mathf.PI * 2f * 3135f * t) * 0.55f * Mathf.Exp(-t / 0.016f)
                        + Mathf.Sin(Mathf.PI * 2f * 6272f * t) * 0.2f * Mathf.Exp(-t / 0.010f)
                        + Mathf.Sin(Mathf.PI * 2f * 783f * t)  * 0.25f * Mathf.Exp(-t / 0.028f);
                s *= Mathf.Min(1f, t / 0.0012f);   // instant attack ramp, no click
                data[i] = Mathf.Clamp(s * 0.8f, -1f, 1f);
            }

            tokenClip = AudioClip.Create("LRV_TokenClink", samples, 1, sr, false);
            tokenClip.SetData(data, 0);
            tokenClip.name = "LRV_TokenClink";
            return tokenClip;
        }
        finally
        {
            baking = false;
        }
    }

    /// <summary>A short bright metallic clink for coins landing in the banked-token badge.</summary>
    public static void PlayTokenClink(float volume = 0.7f)
    {
        AudioClip clip = LoadTokenClip();
        if (clip == null) return;
        PlayOneShot2DBoosted(clip, Mathf.Clamp01(volume), 0.12f);
    }

    // ------------------------------------------------------------ radar ping + low-O2 beep

    private static AudioClip BakeRadarClip()
    {
        if (radarClip != null) return radarClip;
        if (baking) { Debug.LogWarning("RoverSounds.BakeRadarClip re-entered; returning cached null."); return null; }
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 0.26f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            // Sweeping sonar blip plus a short echo, like a handheld field radar.
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sr;
                float f = Mathf.Exp(Mathf.Lerp(Mathf.Log(1700f), Mathf.Log(950f), t / 0.14f));
                float s = Mathf.Sin(Mathf.PI * 2f * f * t) * Mathf.Exp(-t / 0.07f);
                float echoT = t - 0.13f;
                if (echoT > 0f)
                {
                    s += Mathf.Sin(Mathf.PI * 2f * 1050f * echoT) * 0.35f * Mathf.Exp(-echoT / 0.04f);
                }
                s *= Mathf.Min(1f, t / 0.004f);
                data[i] = Mathf.Clamp(s * 0.8f, -1f, 1f);
            }

            radarClip = AudioClip.Create("LRV_RadarPing", samples, 1, sr, false);
            radarClip.SetData(data, 0);
            radarClip.name = "LRV_RadarPing";
            return radarClip;
        }
        finally
        {
            baking = false;
        }
    }

    private static AudioClip BakeWarnClip()
    {
        if (warnClip != null) return warnClip;
        if (baking) { Debug.LogWarning("RoverSounds.BakeWarnClip re-entered; returning cached null."); return null; }
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 0.22f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            // Two short square-ish blips - the "air supply" advisory tone.
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sr;
                float s = 0f;
                float b1 = (t > 0.0f && t < 0.07f) ? 1f : 0f;
                float b2 = (t > 0.11f && t < 0.18f) ? 1f : 0f;
                s = (b1 + b2) * (Mathf.Sin(Mathf.PI * 2f * 880f * t) * 0.5f + Mathf.Sin(Mathf.PI * 2f * 1760f * t) * 0.15f);
                // Rounded edges so the blips don't click.
                float edge = Mathf.Clamp01(Mathf.Min(t / 0.005f, 1f)) * Mathf.Clamp01(Mathf.Min((0.07f - t) / 0.005f, 1f));
                float edge2 = Mathf.Clamp01(Mathf.Min((t - 0.11f) / 0.005f, 1f)) * Mathf.Clamp01(Mathf.Min((0.18f - t) / 0.005f, 1f));
                s *= Mathf.Max(edge, edge2);
                data[i] = Mathf.Clamp(s * 0.7f, -1f, 1f);
            }

            warnClip = AudioClip.Create("LRV_WarnBeep", samples, 1, sr, false);
            warnClip.SetData(data, 0);
            warnClip.name = "LRV_WarnBeep";
            return warnClip;
        }
        finally
        {
            baking = false;
        }
    }

    /// <summary>Plays the prospector radar ping (Q key).</summary>
    public void PlayProspectPing()
    {
        if (pingCooldown > Time.time) return;
        pingCooldown = Time.time + 0.3f;
        if (radarClip == null) radarClip = BakeRadarClip();
        if (radarClip == null) return;
        AudioSource.PlayClipAtPoint(radarClip, transform.position, 0.7f);
    }

    /// <summary>Plays the low-oxygen warning beep (loops on a cadence while O2 is critical).</summary>
    public void PlayWarnBeep()
    {
        if (warnClip == null) warnClip = BakeWarnClip();
        if (warnClip == null) return;
        AudioSource.PlayClipAtPoint(warnClip, transform.position, 0.55f);
    }

    /// <summary>
    /// Plays a short radar blip as a proximity tick for the passive cluster sonar. Louder
    /// the closer the rover is to an ore cluster (<paramref name="intensity"/> 0..1).
    /// </summary>
    public void PlaySonarTick(float intensity)
    {
        if (Time.time - lastSonarTick < 0.12f) return;
        lastSonarTick = Time.time;
        if (radarClip == null) radarClip = BakeRadarClip();
        if (radarClip == null) return;
        AudioSource.PlayClipAtPoint(radarClip, transform.position, 0.08f + 0.5f * Mathf.Clamp01(intensity));
    }

    // ------------------------------------------------------------ rolling crunch

    /// <summary>
    /// Loads the imported wheel-crunch sample from Resources (the asset lives at
    /// Assets/Resources/CrunchyWheelSound.mp3 so it survives the WebGL build). Falls back to
    /// the procedural crunch if the asset is missing, so the sound never silently disappears.
    /// </summary>
    private static AudioClip LoadRollClip()
    {
        if (rollClip != null) return rollClip;
        AudioClip loaded = Resources.Load<AudioClip>("CrunchyWheelSound");
        if (loaded == null)
        {
            Debug.LogWarning("RoverSounds: 'CrunchyWheelSound' not found in Resources; using procedural roll clip.");
            rollClip = BakeRollClip();
            return rollClip;
        }
        // The mp3 as imported has a non-seamless loop point, so it clicks/stutters as it wraps
        // (it was long enough to not be a "file too small" issue - the restart was the wobble).
        // Bake a copy whose last slice crossfades into the head so the loop is continuous.
        rollClip = SeamlessLoop(loaded, "LRV_RollLoop");
        return rollClip;
    }

    // ------------------------------------------------------------ handbrake skid

    /// <summary>Loads the handbrake skid from Resources (Assets/Resources/Slide.mp3) so it
    /// survives the WebGL build. Falls back to a short procedural tyre-scrub if the asset is
    /// missing, so a hard slide is never silent.</summary>
    private static AudioClip LoadSlideClip()
    {
        if (slideClip != null) return slideClip;
        AudioClip loaded = Resources.Load<AudioClip>("Slide");
        if (loaded == null)
        {
            Debug.LogWarning("RoverSounds: 'Slide' not found in Resources; using procedural skid.");
            slideClip = BakeSlideClip();
            return slideClip;
        }
        slideClip = loaded;
        return slideClip;
    }

    /// <summary>Fallback skid: a brief band of filtered noise with a small tonal scrape over it,
    /// gated by a fast attack/decay so it reads as tyres locked and dragging.</summary>
    private static AudioClip BakeSlideClip()
    {
        if (slideClip != null) return slideClip;
        if (baking) return null;
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 0.45f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            System.Random rng = new System.Random(70707);
            float lp = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sr;
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = lp * 0.7f + w * 0.3f;
                float env = Mathf.Min(1f, t / 0.02f) * Mathf.Exp(-t / 0.22f);
                float scrape = Mathf.Sin(Mathf.PI * 2f * (330f + 90f * Mathf.Sin(t * 40f)) * t) * 0.18f;
                data[i] = Mathf.Clamp((lp * 0.8f + scrape) * env, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("LRV_Skid", samples, 1, sr, false);
            clip.SetData(data, 0);
            clip.name = "LRV_Skid";
            slideClip = clip;
            return clip;
        }
        finally
        {
            baking = false;
        }
    }

    /// <summary>
    /// Crossfades the tail into the head, removing the overlapped head from the loop so the
    /// resulting end transitions continuously into its new start. Reads back decoded PCM.
    /// </summary>
    private static AudioClip SeamlessLoop(AudioClip src, string loopName = "LRV_SeamlessLoop",
        float crossfadeMilliseconds = 45f)
    {
        try
        {
            int channels = src.channels;
            int frames = src.samples;
            int sr = src.frequency;
            if (channels < 1 || frames < 100) return src;

            float[] sourceData = new float[frames * channels];
            if (!src.GetData(sourceData, 0) || sourceData.Length == 0) return src;

            int fx = Mathf.Clamp(Mathf.RoundToInt(sr * crossfadeMilliseconds / 1000f),
                1, Mathf.Max(1, frames / 3));
            int loopFrames = frames - fx;
            float[] data = new float[loopFrames * channels];
            for (int c = 0; c < channels; c++)
            {
                int bodyFrames = frames - 2 * fx;
                for (int i = 0; i < bodyFrames; i++)
                    data[i * channels + c] = sourceData[(i + fx) * channels + c];

                for (int k = 0; k < fx; k++)
                {
                    float t = (k + 1f) / (fx + 1f);
                    int outIdx = (bodyFrames + k) * channels + c;
                    int endIdx = (frames - fx + k) * channels + c;
                    int headIdx = k * channels + c;
                    data[outIdx] = sourceData[headIdx] * t + sourceData[endIdx] * (1f - t);
                }
            }

            AudioClip seamless = AudioClip.Create(loopName, loopFrames, channels, sr, false);
            seamless.SetData(data, 0);
            seamless.name = loopName;
            return seamless;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("RoverSounds.SeamlessLoop failed (" + e.Message + "); using clip as-is.");
            return src;
        }
    }

    private static AudioClip BakeRollClip()
    {
        if (rollClip != null) return rollClip;
        if (baking) { Debug.LogWarning("RoverSounds.BakeRollClip re-entered; returning cached null."); return null; }
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 1f; // seamless 1 s loop
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];

            // Deterministic seed so the texture is identical between sessions.
            System.Random rng = new System.Random(20260907);

            // Lowpassed white noise gives the crushed-regolith "body" of the crunch.
            float[] lp = new float[samples];
            float lpState = 0f;
            const float alpha = 0.11f; // ~2.4 kHz lowpass: soft, not harsh
            for (int i = 0; i < samples; i++)
            {
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpState = lpState * (1f - alpha) + w * alpha;
                lp[i] = lpState;
            }

            // Sparse crunchy bursts: short decaying envelopes at pseudo-random offsets.
            const int bursts = 16;
            float[] bT0 = new float[bursts];
            float[] bTau = new float[bursts];
            float[] bAmp = new float[bursts];
            for (int i = 0; i < bursts; i++)
            {
                bT0[i] = (float)(rng.NextDouble() * 0.85 + 0.02);
                bTau[i] = 0.012f + (float)rng.NextDouble() * 0.022f;
                bAmp[i] = 0.45f + (float)rng.NextDouble() * 0.55f;
            }

            float inv = 1f / sr;
            for (int i = 0; i < samples; i++)
            {
                float t = i * inv;

                // Two close low frequencies beat into an uneven, lumpy rumble.
                float rumble = Mathf.Sin(Mathf.PI * 2f * 31f * t) * 0.35f
                             + Mathf.Sin(Mathf.PI * 2f * 47f * t) * 0.22f;

                // Pebbly crunch rides the noise, gated by the burst envelopes.
                float e = 0f;
                for (int b = 0; b < bursts; b++)
                {
                    float dt = t - bT0[b];
                    if (dt < 0f) continue;
                    e += bAmp[b] * Mathf.Exp(-dt / bTau[b]);
                }
                e = Mathf.Min(e, 1.6f);

                data[i] = rumble + lp[i] * e * 0.9f;
            }

            // Soft fade at the loop seam so the 1 s loop has no click.
            int fade = Mathf.RoundToInt(sr * 0.012f);
            for (int i = 0; i < fade; i++)
            {
                float g = Mathf.Clamp01(i / (float)fade);
                data[i] *= g;
                data[samples - 1 - i] *= g;
            }

            // Normalise to a soft peak (the volume slider does the rest).
            float peak = 0f;
            for (int i = 0; i < samples; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            if (peak > 0.001f)
            {
                float g = 0.5f / peak;
                for (int i = 0; i < samples; i++) data[i] *= g;
            }

            rollClip = AudioClip.Create("LRV_Roll", samples, 1, sr, false);
            rollClip.SetData(data, 0);
            rollClip.name = "LRV_Roll";
            return rollClip;
        }
        finally
        {
            baking = false;
        }
    }

    // ------------------------------------------------------------ runtime

    private void Update()
    {
        if (buggy == null) return;

        // Blend the generated engine's idle, regular and boost character. Keep one source
        // looping continuously: per-loop source hand-offs caused an audible swell on each seam.
        float throttle = Mathf.Abs(buggy.ThrottleInput);
        bool driving = throttle > 0.05f;
        float norm = Mathf.Clamp01(buggy.CurrentSpeedKmh / TopSpeedKmh);
        bool boosting = driving && buggy.IsBoosting;

        float normalPitch = 0.75f + 0.55f * norm;
        float normalVol = driving ? 0.10f + 0.18f * norm : 0f;
        float boostVol = boosting ? normalVol + 0.14f : 0f;

        float fadeRate = 1f / Mathf.Max(0.05f, engineCrossfadeSeconds);
        float idleTarget = !driving && !boosting ? 1f : 0f;
        float driveTarget = driving && !boosting ? 1f : 0f;
        float boostTarget = boosting ? 1f : 0f;
        engineActiveMix = Mathf.MoveTowards(engineActiveMix, buggy.inputEnabled ? 1f : 0f, Time.deltaTime * fadeRate);
        idleEngineMix = Mathf.MoveTowards(idleEngineMix, idleTarget, Time.deltaTime * fadeRate);
        driveEngineMix = Mathf.MoveTowards(driveEngineMix, driveTarget, Time.deltaTime * fadeRate);
        boostEngineMix = Mathf.MoveTowards(boostEngineMix, boostTarget, Time.deltaTime * fadeRate);

        float mixTotal = idleEngineMix + driveEngineMix + boostEngineMix;
        if (mixTotal > 0.0001f)
        {
            idleEngineMix /= mixTotal;
            driveEngineMix /= mixTotal;
            boostEngineMix /= mixTotal;
        }

        float engineVolume = (idleEngineVolume * idleEngineMix
            + normalVol * driveEngineMix + boostVol * boostEngineMix) * engineActiveMix;
        float enginePitch = idleEnginePitch * idleEngineMix
            + normalPitch * driveEngineMix + normalPitch * 1.45f * boostEngineMix;
        motor.volume = Mathf.MoveTowards(motor.volume, engineVolume, Time.deltaTime * 5f);
        motor.pitch = Mathf.MoveTowards(motor.pitch, enginePitch, Time.deltaTime * 3f);

        // Rolling crunch: wheels over the terrain whenever the buggy is moving, whether the
        // throttle is held or it is coasting. Deliberately quiet - a subtle presence under the
        // motor - with discrete volume bands by road speed (tweakable in the Inspector, live
        // in editor playtest mode). The top band (>32 km/h) only ever exists while boosting, so
        // it is gated off that to stop the volume fluttering at the 32 km/h cruise shelf.
        float rollVol = 0f;
        if (buggy.CurrentSpeedKmh > 0.5f)
        {
            float bandSpeed = buggy.IsBoosting ? buggy.CurrentSpeedKmh : Mathf.Min(buggy.CurrentSpeedKmh, 32f);
            if (bandSpeed <= 10f) rollVol = rollVolumeLow;
            else if (bandSpeed <= 20f) rollVol = rollVolumeMid;
            else if (bandSpeed <= 32f) rollVol = rollVolumeHigh;
            else rollVol = rollVolumePeak;
        }

        // Braking scrubs the wheels hard across the regolith - the crunch swells up distinctly.
        if (buggy.IsBraking && buggy.CurrentSpeedKmh > 1f)
        {
            float scrub = rollVolumeBrake * Mathf.Clamp01(buggy.CurrentSpeedKmh / 20f);
            rollVol = Mathf.Max(rollVol, scrub);
        }
        rollSrc.volume = Mathf.MoveTowards(rollSrc.volume, rollVol, Time.deltaTime * 6f);
        rollSrc.pitch = Mathf.MoveTowards(rollSrc.pitch, 0.9f + 0.2f * Mathf.Clamp01(buggy.CurrentSpeedKmh / 28f), Time.deltaTime * 4f);

        // Handbrake skid: only a handbrake that actually SWINGS the tail sounds. The lever alone
        // is not enough (a straight-line lever pull stays silent) - it needs both a real yaw rate
        // and some road speed, then it retriggers on a short gap for a continuous slide.
        // GRAVITY MATTERS: grip follows the planet's gravity, so on a heavy world like Venus the
        // handbrake snaps the tail right around (high yaw, loud scrub) while on a light world the
        // buggy keeps sliding almost straight (low yaw, faint scrub). We therefore lower the yaw
        // threshold and the volume as gravity drops, so the skid fires on every world but reads
        // quieter where there is less grip. Lerp is anchored at Venus-high and low-g.
        if (buggy.Drifting && buggy.CurrentSpeedKmh > slideMinSpeed)
        {
            float gNorm = Mathf.Clamp01(Physics.gravity.magnitude / LevelManager.EarthGravity);
            float lowG = Mathf.InverseLerp(slideGravityHighNorm, slideGravityLowNorm, gNorm);
            float yawThreshold = Mathf.Lerp(slideYawThresholdDeg, slideYawThresholdLowGravityDeg, lowG);
            float slideVol = slideVolume * Mathf.Lerp(slideVolumeHighGravityScale, slideVolumeLowGravityScale, lowG);
            // A world may name its own skid volume outright (soft surfaces like Titan's mud or
            // Mars' dust read quieter than gravity alone implies).
            LevelDefinition slideLevel = LevelManager.CurrentLevel;
            if (slideLevel != null && slideLevel.slideVolumeOverride >= 0f)
                slideVol = slideLevel.slideVolumeOverride;
            if (Mathf.Abs(buggy.YawRate) * Mathf.Rad2Deg > yawThreshold && Time.time >= slideRetriggerAt)
            {
                slideRetriggerAt = Time.time + slideRepeatGap;
                if (slideClip == null) slideClip = LoadSlideClip();
                if (slideClip != null) PlayOneShot2D(slideClip, slideVol);
            }
        }

        // Low-oxygen advisory: while still alive but at or under 10%, beep on a cadence.
        // Once the tank is EMPTY the warning hands off to the looping claxon, which plays
        // flat (fixed volume and pitch - no ramp) for the whole grace countdown. When the
        // countdown ends the run is over: the claxon keeps ringing briefly so the cut reads
        // as "finished", then stops so the defeat screen does not wail underneath it.
        if (OxygenManager.Instance != null)
        {
            if (OxygenManager.Instance.gameOver)
            {
                if (claxonSrc != null && claxonSrc.isPlaying)
                {
                    if (claxonStopAt < 0f) claxonStopAt = Time.time + claxonCutDelay;
                    else if (Time.time >= claxonStopAt) claxonSrc.Stop();
                }
            }
            else
            {
                claxonStopAt = -1f;
                float o2 = OxygenManager.Instance.CurrentOxygen;
                bool low = o2 > 0f && o2 <= OxygenManager.Instance.maxOxygen * 0.10f;
                if (low && Time.time >= warnNextTime)
                {
                    warnNextTime = Time.time + 0.9f;
                    PlayWarnBeep();
                }

                bool grace = OxygenManager.Instance.OxygenGraceActive;
                if (grace)
                {
                    float remaining = OxygenManager.Instance.OxygenGraceRemaining;
                    if (remaining <= 5f && remaining > 0f)
                    {
                        EnsureClaxon();
                        if (!claxonSrc.isPlaying)
                        {
                            // The horn sounds as itself: steady volume and pitch throughout.
                            claxonSrc.volume = 0.9f;
                            claxonSrc.pitch = 1f;
                            claxonSrc.Play();
                        }
                    }
                    else if (claxonSrc != null && claxonSrc.isPlaying)
                    {
                        claxonSrc.Stop();
                    }
                }
                else if (claxonSrc != null && claxonSrc.isPlaying)
                {
                    claxonSrc.Stop();
                }
            }
        }
    }

    // ------------------------------------------------------------ collect (harvestable pickup)

    /// <summary>Loads the harvestable pickup sound from Resources (place the file at
    /// Assets/Resources/collect.mp3 so it survives the WebGL build). Falls back to the
    /// procedural chime, so the cue never disappears if the file is missing.</summary>
    private static AudioClip LoadCollectClip()
    {
        if (collectClip != null) return collectClip;
        collectClip = Resources.Load<AudioClip>("collect");
        if (collectClip == null)
        {
            Debug.LogWarning("RoverSounds: 'collect' not found in Resources; using procedural chime.");
            collectClip = BakeChimeClip();
        }
        return collectClip;
    }

    /// <summary>Plays the collect sound. Called on each harvested nugget during a run.</summary>
    public void PlayCollectChime()
    {
        if (chimeCooldown > Time.time) return;
        chimeCooldown = Time.time + 0.25f; // a burst of nuggets = one soft chime
        if (collectClip == null) collectClip = LoadCollectClip();
        if (collectClip == null) return;
        AudioSource.PlayClipAtPoint(collectClip, transform.position, 0.8f);
    }

    // ------------------------------------------------------------ oxygen pod swoosh

    /// <summary>
    /// Loads the oxygen-pod pickup swoosh from Resources (place the file at
    /// Assets/Resources/Oxygen_Swoosh.mp3 so it survives the WebGL build). Falls back to a
    /// procedural air-swoosh, so the cue never silently disappears.
    /// </summary>
    private static AudioClip LoadOxygenCue()
    {
        if (oxygenCueClip != null) return oxygenCueClip;
        oxygenCueClip = Resources.Load<AudioClip>("Oxygen_Swoosh");
        if (oxygenCueClip == null)
        {
            Debug.LogWarning("RoverSounds: 'Oxygen_Swoosh' not found in Resources; using procedural swoosh.");
            oxygenCueClip = BakeSwooshClip();
        }
        return oxygenCueClip;
    }

    /// <summary>An air-swoosh: filtered noise swells up then trails away (0.5 s).</summary>
    private static AudioClip BakeSwooshClip()
    {
        if (oxygenCueClip != null) return oxygenCueClip;
        if (baking) return null;
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 0.5f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            System.Random rng = new System.Random(20260908);
            float lpState = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sr;
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpState = lpState * 0.86f + w * 0.14f;
                // Swell in, then let go.
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur));
                data[i] = Mathf.Clamp(lpState * env * 0.7f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("LRV_OxygenSwoosh", samples, 1, sr, false);
            clip.SetData(data, 0);
            clip.name = "LRV_OxygenSwoosh";
            oxygenCueClip = clip;
            return clip;
        }
        finally
        {
            baking = false;
        }
    }

    /// <summary>Plays the oxygen-pod pickup swoosh as a 3D cue anchored at the buggy, so it
    /// reads as coming from the pod rather than from nowhere.</summary>
    public void PlayOxygenCue()
    {
        if (oxygenCueClip == null) oxygenCueClip = LoadOxygenCue();
        if (oxygenCueClip == null) return;
        PlayOneShot3D(oxygenCueClip, transform.position, 0.95f);
    }

    // ------------------------------------------------------------ celebration fanfare

    /// <summary>
    /// Loads the level-clear celebration clip from Resources (place the file at
    /// Assets/Resources/Congratulations.mp3 so it survives the WebGL build). Falls back to a
    /// procedural fanfare, so the congrats screen always rings.
    /// </summary>
    private static AudioClip LoadCelebrateClip()
    {
        if (celebrateClip != null) return celebrateClip;
        celebrateClip = Resources.Load<AudioClip>("Congratulations");
        if (celebrateClip == null)
        {
            Debug.LogWarning("RoverSounds: 'Congratulations' not found in Resources; using procedural fanfare.");
            celebrateClip = BakeCelebrateClip();
        }
        return celebrateClip;
    }

    /// <summary>A short bright fanfare: C5-E5-G5-C6 rises, then a sustained C-major shimmer.</summary>
    private static AudioClip BakeCelebrateClip()
    {
        if (celebrateClip != null) return celebrateClip;
        if (baking) return null;
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 2.2f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            float[] freqs = { 523.25f, 659.25f, 783.99f, 1046.5f };
            float[] starts = { 0.00f, 0.15f, 0.30f, 0.45f };
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sr;
                float s = 0f;
                for (int n = 0; n < freqs.Length; n++)
                {
                    float lt = t - starts[n];
                    if (lt < 0f || lt > 0.7f) continue;
                    float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(lt / 0.7f));
                    s += (Mathf.Sin(Mathf.PI * 2f * freqs[n] * lt) * 0.6f
                        + Mathf.Sin(Mathf.PI * 2f * freqs[n] * 2f * lt) * 0.2f
                        + Mathf.Sin(Mathf.PI * 2f * freqs[n] * 3f * lt) * 0.08f) * env;
                }
                float end = t - 1.0f;
                if (end > 0f && end < 1.2f)
                {
                    float eenv = Mathf.Sin(Mathf.PI * Mathf.Clamp01(end / 1.2f));
                    s += (Mathf.Sin(Mathf.PI * 2f * 523.25f * end)
                        + Mathf.Sin(Mathf.PI * 2f * 659.25f * end)
                        + Mathf.Sin(Mathf.PI * 2f * 1046.5f * end)) * 0.18f * eenv;
                }
                data[i] = Mathf.Clamp(s * 0.5f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("LRV_Celebration", samples, 1, sr, false);
            clip.SetData(data, 0);
            clip.name = "LRV_Celebration";
            celebrateClip = clip;
            return clip;
        }
        finally
        {
            baking = false;
        }
    }

    /// <summary>Plays the level-clear celebration (called from the congratulations screen).
    /// Full-length 2D playback at the listener, so the whole mp3 rings out no matter where
    /// the buggy ended up.</summary>
    public static void PlayCelebration()
    {
        if (celebrateClip == null) celebrateClip = LoadCelebrateClip();
        if (celebrateClip == null) return;
        PlayOneShot2D(celebrateClip, 0.95f);
    }

    // ------------------------------------------------------------ game-over music

    /// <summary>
    /// Loads the game-over music track from Resources (place the file at
    /// Assets/Resources/Game_Over_Music.mp3 so it survives the WebGL build). Falls back to
    /// a soft descending two-note so the screen never rings silent.
    /// </summary>
    private static AudioClip LoadGameOverClip()
    {
        if (commisClip != null) return commisClip;
        commisClip = Resources.Load<AudioClip>("Game_Over_Music");
        if (commisClip == null)
        {
            Debug.LogWarning("RoverSounds: 'Game_Over_Music' not found in Resources; using procedural sting.");
            commisClip = BakeCommisClip();
        }
        return commisClip;
    }

    /// <summary>A gentle descending "sigh" (E5 down to C5) so a failed run reads as sympathetic.</summary>
    private static AudioClip BakeCommisClip()
    {
        if (commisClip != null) return commisClip;
        if (baking) return null;
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 1.1f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sr;
                float e = Mathf.Exp(-t / 0.45f);
                float s = Mathf.Sin(Mathf.PI * 2f * 659.25f * t) * e
                        + Mathf.Sin(Mathf.PI * 2f * 523.25f * t) * e * 0.55f;
                s *= Mathf.Min(1f, t / 0.01f);
                data[i] = Mathf.Clamp(s * 0.55f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("LRV_CommisSting", samples, 1, sr, false);
            clip.SetData(data, 0);
            clip.name = "LRV_CommisSting";
            commisClip = clip;
            return clip;
        }
        finally
        {
            baking = false;
        }
    }

    /// <summary>Loops the game-over music on the commiserations screen. The track repeats until
    /// the scene changes (retry, world selector, leaderboard) - the restart is a full scene
    /// reload, so the carrier is destroyed with the screen.</summary>
    private static GameObject gameOverMusicGo;

    public static void PlayGameOverMusic()
    {
        StopGameOverMusic();
        AudioClip clip = LoadGameOverClip();
        if (clip == null) return;
        gameOverMusicGo = new GameObject("GameOverMusic");
        AudioSource src = gameOverMusicGo.AddComponent<AudioSource>();
        src.spatialBlend = 0f;
        src.loop = true;
        src.playOnAwake = false;
        src.volume = 0.9f;
        src.clip = clip;
        src.Play();
    }

    /// <summary>Stops any looping game-over music (a fresh run must never open over the track).</summary>
    public static void StopGameOverMusic()
    {
        if (gameOverMusicGo != null)
        {
            AudioSource src = gameOverMusicGo.GetComponent<AudioSource>();
            if (src != null) src.Stop();
            Object.Destroy(gameOverMusicGo);
        }
        gameOverMusicGo = null;
    }

    // ------------------------------------------------------------ oxygen claxon

    /// <summary>
    /// Loads the zero-oxygen claxon from Resources (place the file at
    /// Assets/Resources/Oxygen_Claxon.mp3 so it survives the WebGL build). Falls back to a
    /// procedural two-tone horn, so the final seconds always warn loudly.
    /// </summary>
    private static AudioClip LoadClaxonClip()
    {
        if (claxonClip != null) return claxonClip;
        claxonClip = Resources.Load<AudioClip>("Oxygen_Claxon");
        if (claxonClip == null)
        {
            Debug.LogWarning("RoverSounds: 'Oxygen_Claxon' not found in Resources; using procedural claxon.");
            claxonClip = BakeClaxonClip();
        }
        return claxonClip;
    }

    /// <summary>A harsh alternating two-tone horn (1 s loop) for the zero-oxygen countdown.</summary>
    private static AudioClip BakeClaxonClip()
    {
        if (claxonClip != null) return claxonClip;
        if (baking) return null;
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 1f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sr;
                // Two alternating 0.2 s horn notes with a quick attack so it reads as a siren.
                float seg = Mathf.Floor(t / 0.2f) % 2f;
                float f = seg < 1f ? 470f : 350f;
                float edge = Mathf.Clamp01(Mathf.Min(t % 0.2f, 0.2f - (t % 0.2f)) / 0.003f);
                float s = Mathf.Sin(Mathf.PI * 2f * f * t) * 0.6f
                        + Mathf.Sin(Mathf.PI * 2f * f * 2f * t) * 0.3f
                        + Mathf.Sin(Mathf.PI * 2f * f * 3f * t) * 0.12f;
                // Small amplitude pulse on top for urgency.
                s *= 0.75f + 0.25f * Mathf.Sin(Mathf.PI * 2f * 7f * t);
                data[i] = Mathf.Clamp(s * 0.7f * edge, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("LRV_Claxon", samples, 1, sr, false);
            clip.SetData(data, 0);
            clip.name = "LRV_Claxon";
            claxonClip = clip;
            return clip;
        }
        finally
        {
            baking = false;
        }
    }

    /// <summary>Creates the looping claxon source used once, when the zero-oxygen countdown starts.</summary>
    private void EnsureClaxon()
    {
        if (claxonSrc != null) return;
        claxonSrc = gameObject.AddComponent<AudioSource>();
        claxonSrc.loop = true;
        claxonSrc.spatialBlend = 0f;
        claxonSrc.playOnAwake = false;
        claxonSrc.clip = LoadClaxonClip();
    }

    // ------------------------------------------------------------ meteor storm

    /// <summary>
    /// A two-tone storm-siren alert (slow bass blips) the meteor director plays at the start
    /// of a barrage, distinct from the oxygen claxon and the advisory warn beep.
    /// </summary>
    private static AudioClip BakeStormAlertClip()
    {
        if (stormAlertClip != null) return stormAlertClip;
        if (baking) { Debug.LogWarning("RoverSounds.BakeStormAlertClip re-entered; returning cached null."); return null; }
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 1.5f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sr;
                float s = 0f;
                // Two long low blips, each with a quick attack and a soft release.
                for (int n = 0; n < 2; n++)
                {
                    float lt = t - (0.12f + n * 0.65f);
                    if (lt < 0f || lt > 0.5f) continue;
                    float env = Mathf.Min(1f, lt / 0.02f) * Mathf.Clamp01((0.5f - lt) / 0.08f);
                    float f = n == 0 ? 330f : 415f;
                    s += (Mathf.Sin(Mathf.PI * 2f * f * lt) * 0.6f
                        + Mathf.Sin(Mathf.PI * 2f * f * 2f * lt) * 0.25f) * env;
                }
                data[i] = Mathf.Clamp(s * 0.8f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("LRV_StormAlert", samples, 1, sr, false);
            clip.SetData(data, 0);
            clip.name = "LRV_StormAlert";
            stormAlertClip = clip;
            return clip;
        }
        finally
        {
            baking = false;
        }
    }

    /// <summary>A descending fire-whistle as a meteor streaks down: a sine that sweeps fast
    /// with the harmonics and a touch of filtered noise so it reads as an atmospheric burn.</summary>
    private static AudioClip BakeMeteorWhistleClip()
    {
        if (meteorWhistleClip != null) return meteorWhistleClip;
        if (baking) { Debug.LogWarning("RoverSounds.BakeMeteorWhistleClip re-entered; returning cached null."); return null; }
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 1.1f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            System.Random rng = new System.Random(20260909);
            float lpState = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sr;
                // Slide from a high shriek down into a lower rumble as it closes in.
                float f = Mathf.Exp(Mathf.Lerp(Mathf.Log(1500f), Mathf.Log(420f), t / dur));
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpState = lpState * 0.9f + w * 0.1f;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur));
                float s = Mathf.Sin(Mathf.PI * 2f * f * t) * 0.5f
                        + Mathf.Sin(Mathf.PI * 2f * f * 2f * t) * 0.12f;
                data[i] = Mathf.Clamp((s + lpState * 0.45f) * env * 0.85f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("LRV_MeteorWhistle", samples, 1, sr, false);
            clip.SetData(data, 0);
            clip.name = "LRV_MeteorWhistle";
            meteorWhistleClip = clip;
            return clip;
        }
        finally
        {
            baking = false;
        }
    }

    /// <summary>A heavy impact boom: low thump + filtered noise blast collapsing fast.</summary>
    private static AudioClip BakeMeteorImpactClip()
    {
        if (meteorImpactClip != null) return meteorImpactClip;
        if (baking) { Debug.LogWarning("RoverSounds.BakeMeteorImpactClip re-entered; returning cached null."); return null; }
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 0.9f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            System.Random rng = new System.Random(20260909);
            float lpState = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sr;
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpState = lpState * 0.96f + w * 0.04f;
                // Deep falling boom + a bright crack of noise in the first instant.
                float boom = Mathf.Sin(Mathf.PI * 2f * 62f * t) * Mathf.Exp(-t / 0.18f)
                           + Mathf.Sin(Mathf.PI * 2f * 118f * t) * 0.5f * Mathf.Exp(-t / 0.09f);
                float crack = lpState * Mathf.Exp(-t / 0.05f) * 0.8f;
                float env = Mathf.Min(1f, t / 0.004f);
                data[i] = Mathf.Clamp((boom + crack) * env * 0.9f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("LRV_MeteorImpact", samples, 1, sr, false);
            clip.SetData(data, 0);
            clip.name = "LRV_MeteorImpact";
            meteorImpactClip = clip;
            return clip;
        }
        finally
        {
            baking = false;
        }
    }

    /// <summary>Plays the incoming-hazard warning at the listener when a hazard telegraphed
    /// (meteor storm, rock fall, landslide, lava surge, rain). Uses the Meteor_Warning.mp3
    /// from Resources (Assets/Resources/Meteor_Warning.mp3); falls back to the procedural
    /// two-tone siren if the asset is missing, so a warning never goes silent.</summary>
    public static void PlayStormAlert()
    {
        if (stormAlertClip == null)
        {
            stormAlertClip = Resources.Load<AudioClip>("Meteor_Warning");
            if (stormAlertClip == null)
            {
                Debug.LogWarning("RoverSounds: 'Meteor_Warning' not found in Resources; using procedural storm alert.");
                stormAlertClip = BakeStormAlertClip();
            }
        }
        if (stormAlertClip == null) return;
        PlayOneShot2D(stormAlertClip, 0.85f);
    }

    /// <summary>Plays a meteor's fall whistle as a 3D cue anchored where the streak is.</summary>
    public static void PlayMeteorWhistle(Vector3 position, float volume)
    {
        if (meteorWhistleClip == null) meteorWhistleClip = BakeMeteorWhistleClip();
        if (meteorWhistleClip == null) return;
        PlayOneShot3D(meteorWhistleClip, position, volume);
    }

    /// <summary>Plays a meteor's impact boom as a 3D cue at the blast point. Uses the supplied
    /// Explosion.mp3 (Assets/Resources/Explosion.mp3); falls back to the procedural low thump if
    /// the asset is missing, so an impact is never silent.</summary>
    public static void PlayMeteorImpact(Vector3 position, float volume)
    {
        if (meteorImpactClip == null)
        {
            meteorImpactClip = Resources.Load<AudioClip>("Explosion");
            if (meteorImpactClip == null)
            {
                Debug.LogWarning("RoverSounds: 'Explosion' not found in Resources; using procedural meteor impact.");
                meteorImpactClip = BakeMeteorImpactClip();
            }
        }
        if (meteorImpactClip == null) return;
        PlayOneShot3D(meteorImpactClip, position, Mathf.Max(0.05f, volume));
    }

    // ------------------------------------------------------------ volcano eruption rumble

    /// <summary>
    /// Loads the Io eruption rumble from Resources (place the file at
    /// Assets/Resources/Volcano_Rumble.mp3 so it survives the WebGL build). Falls back to a
    /// procedural deep lava-rumble, so a violent eruption always rumbles even without the asset.
    /// </summary>
    private static AudioClip LoadEruptRumbleClip()
    {
        if (eruptRumbleClip != null) return eruptRumbleClip;
        eruptRumbleClip = Resources.Load<AudioClip>("Volcano_Rumble");
        if (eruptRumbleClip == null)
        {
            Debug.LogWarning("RoverSounds: 'Volcano_Rumble' not found in Resources; using procedural eruption rumble.");
            eruptRumbleClip = BakeEruptRumbleClip();
        }
        return eruptRumbleClip;
    }

    /// <summary>A deep, uneven lava-rumble: two close sub-bass tones beating into a slow lumpy
    /// roll, thrown over lowpassed noise so it reads as a mountain groan rather than a tone, with
    /// a slightly-rising partial so the blast feels like it is swelling as it erupts.</summary>
    private static AudioClip BakeEruptRumbleClip()
    {
        if (eruptRumbleClip != null) return eruptRumbleClip;
        if (baking) return null;
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 3.2f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            System.Random rng = new System.Random(20260921);
            float lpState = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sr;
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpState = lpState * 0.98f + w * 0.02f;
                float rumble = Mathf.Sin(Mathf.PI * 2f * 31f * t) * 0.55f
                             + Mathf.Sin(Mathf.PI * 2f * 47f * t) * 0.35f
                             + Mathf.Sin(Mathf.PI * 2f * (24f + 8f * Mathf.Min(1f, t / 1.4f)) * t) * 0.4f;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur)); // swell in and out
                data[i] = Mathf.Clamp((rumble + lpState * 1.4f) * env * 0.75f, -1f, 1f);
            }
            float peak = 0f;
            for (int i = 0; i < samples; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            if (peak > 0.001f)
            {
                float g = 0.85f / peak;
                for (int i = 0; i < samples; i++) data[i] *= g;
            }
            AudioClip clip = AudioClip.Create("LRV_EruptRumble", samples, 1, sr, false);
            clip.SetData(data, 0);
            clip.name = "LRV_EruptRumble";
            eruptRumbleClip = clip;
            return clip;
        }
        finally
        {
            baking = false;
        }
    }

    /// <summary>Plays the eruption rumble as a 3D cue anchored at the vent, so it reads as coming
    /// from the volcano rather than from nowhere. Volume scales with the blast's ferocity.</summary>
    public static void PlayEruptRumble(Vector3 position, float volume)
    {
        if (eruptRumbleClip == null) eruptRumbleClip = LoadEruptRumbleClip();
        if (eruptRumbleClip == null) return;
        PlayOneShot3D(eruptRumbleClip, position, Mathf.Max(0.05f, volume));
    }

    // ------------------------------------------------------------ rock fall / volcano / lightning

    /// <summary>Plays a rock fall as a 3D cue where the rocks are landing. Uses the supplied
    /// RockFall.mp3 (Assets/Resources/RockFall.mp3); falls back to a procedural stony cascade so
    /// a wall/overhang fall is never silent.</summary>
    public static void PlayRockfall(Vector3 position, float volume)
    {
        if (rockfallClip == null)
        {
            rockfallClip = Resources.Load<AudioClip>("RockFall");
            if (rockfallClip == null)
            {
                Debug.LogWarning("RoverSounds: 'RockFall' not found in Resources; using procedural rock fall.");
                rockfallClip = BakeRockfallClip();
            }
        }
        if (rockfallClip == null) return;
        PlayOneShot3D(rockfallClip, position, Mathf.Max(0.05f, volume));
    }

    /// <summary>Fallback rock fall: a scatter of stony clacks thinning out over a low shifting
    /// rumble, so the fall reads as boulders shedding down a cliff face.</summary>
    private static AudioClip BakeRockfallClip()
    {
        if (rockfallClip != null) return rockfallClip;
        if (baking) return null;
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 1.5f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            System.Random rng = new System.Random(20261012);
            float lp = 0f;
            for (int k = 0; k < 14; k++)
            {
                int at = Mathf.RoundToInt((0.02f + 1.2f * (float)rng.NextDouble()) * sr);
                float amp = 0.5f + 0.5f * (1f - (float)k / 14f);
                for (int i = at; i < samples; i++)
                {
                    float t = (i - at) / (float)sr;
                    if (t > 0.11f) break;
                    float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp = lp * 0.5f + w * 0.5f;
                    data[i] += lp * Mathf.Exp(-t / 0.03f) * 0.5f * amp;
                }
            }
            float roll = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sr;
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                roll = roll * 0.985f + w * 0.015f;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur));
                data[i] = Mathf.Clamp(data[i] + roll * env * 1.2f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("LRV_Rockfall", samples, 1, sr, false);
            clip.SetData(data, 0);
            clip.name = "LRV_Rockfall";
            rockfallClip = clip;
            return clip;
        }
        finally { baking = false; }
    }

    /// <summary>Plays a volcanic eruption blast as a 3D cue at the vent. Uses the supplied
    /// VolcanoExplosion.mp3 (Assets/Resources/VolcanoExplosion.mp3); falls back to a procedural
    /// deep blast so the opening of a major eruption is never silent.</summary>
    public static void PlayVolcanoExplosion(Vector3 position, float volume)
    {
        if (volcanoExplosionClip == null)
        {
            volcanoExplosionClip = Resources.Load<AudioClip>("VolcanoExplosion");
            if (volcanoExplosionClip == null)
            {
                Debug.LogWarning("RoverSounds: 'VolcanoExplosion' not found in Resources; using procedural volcano blast.");
                volcanoExplosionClip = BakeVolcanoExplosionClip();
            }
        }
        if (volcanoExplosionClip == null) return;
        PlayOneShot3D(volcanoExplosionClip, position, Mathf.Max(0.05f, volume));
    }

    /// <summary>Fallback eruption blast: a low swelling boom with a fast noise crack, longer and
    /// heavier than a meteor impact, so the eruption opening reads as a mountain letting go.</summary>
    private static AudioClip BakeVolcanoExplosionClip()
    {
        if (volcanoExplosionClip != null) return volcanoExplosionClip;
        if (baking) return null;
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 1.6f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            System.Random rng = new System.Random(20261013);
            float lp = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sr;
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = lp * 0.94f + w * 0.06f;
                float boom = Mathf.Sin(Mathf.PI * 2f * 44f * t) * Mathf.Exp(-t / 0.35f)
                           + Mathf.Sin(Mathf.PI * 2f * 90f * t) * 0.5f * Mathf.Exp(-t / 0.14f);
                float debris = lp * Mathf.Exp(-t / 0.7f);
                float env = Mathf.Min(1f, t / 0.006f);
                data[i] = Mathf.Clamp((boom + debris * 1.3f) * env * 0.9f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("LRV_VolcanoExplosion", samples, 1, sr, false);
            clip.SetData(data, 0);
            clip.name = "LRV_VolcanoExplosion";
            volcanoExplosionClip = clip;
            return clip;
        }
        finally { baking = false; }
    }

    /// <summary>Plays a lightning strike as a 3D cue at the bolt. Uses the supplied Lightning.mp3
    /// (Assets/Resources/Lightning.mp3); falls back to a procedural crack + sizzle so a bolt is
    /// never silent.</summary>
    public static void PlayLightningStrike(Vector3 position, float volume)
    {
        if (lightningClip == null)
        {
            lightningClip = Resources.Load<AudioClip>("Lightning");
            if (lightningClip == null)
            {
                Debug.LogWarning("RoverSounds: 'Lightning' not found in Resources; using procedural lightning.");
                lightningClip = BakeLightningClip();
            }
        }
        if (lightningClip == null) return;
        PlayOneShot3D(lightningClip, position, Mathf.Max(0.05f, volume));
    }

    /// <summary>Fallback lightning: a sharp high crack collapsing into a decaying electric sizzle
    /// over a low thunder roll.</summary>
    private static AudioClip BakeLightningClip()
    {
        if (lightningClip != null) return lightningClip;
        if (baking) return null;
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 0.8f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            System.Random rng = new System.Random(20261014);
            float lp = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sr;
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = lp * 0.7f + w * 0.3f;
                float crack = w * Mathf.Exp(-t / 0.012f) * 0.9f;
                float sizzle = lp * Mathf.Exp(-t / 0.28f) * 0.55f;
                float roll = Mathf.Sin(Mathf.PI * 2f * 55f * t) * Mathf.Exp(-t / 0.5f) * 0.4f;
                data[i] = Mathf.Clamp((crack + sizzle + roll) * 0.9f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("LRV_Lightning", samples, 1, sr, false);
            clip.SetData(data, 0);
            clip.name = "LRV_Lightning";
            lightningClip = clip;
            return clip;
        }
        finally { baking = false; }
    }

    // ------------------------------------------------------------ take-off roar

    /// <summary>
    /// Loads the rocket take-off roar from Resources (place the file at
    /// Assets/Resources/Take_Off.mp3 so it survives the WebGL build). Falls back to a
    /// procedural roar, so liftoff always sounds heavy.
    /// </summary>
    private static AudioClip LoadTakeOffClip()
    {
        if (takeOffClip != null) return takeOffClip;
        takeOffClip = Resources.Load<AudioClip>("Take_Off");
        if (takeOffClip == null)
        {
            Debug.LogWarning("RoverSounds: 'Take_Off' not found in Resources; using procedural roar.");
            takeOffClip = BakeTakeOffClip();
        }
        return takeOffClip;
    }

    /// <summary>A heavier, louder burn for liftoff: deep rumble + jet roar swelling over ~2.5 s.</summary>
    private static AudioClip BakeTakeOffClip()
    {
        if (takeOffClip != null) return takeOffClip;
        if (baking) return null;
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 2.5f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            System.Random rng = new System.Random(20260908);
            float lpState = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sr;
                float rumble = Mathf.Sin(Mathf.PI * 2f * 48f * t) * 0.6f
                             + Mathf.Sin(Mathf.PI * 2f * 97f * t) * 0.4f;
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpState = lpState * 0.92f + w * 0.08f;
                // Swell up fast, stay heavy, then die out as the ship climbs.
                float env = Mathf.Clamp01(t / 0.15f) * (1f - Mathf.Clamp01((t - 1.4f) / 1.1f));
                data[i] = Mathf.Clamp((rumble + lpState * 1.1f) * env * 0.8f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("LRV_TakeOff", samples, 1, sr, false);
            clip.SetData(data, 0);
            clip.name = "LRV_TakeOff";
            takeOffClip = clip;
            return clip;
        }
        finally
        {
            baking = false;
        }
    }

    // ------------------------------------------------------------ landing-pad clank

    /// <summary>
    /// Loads the metal clank the rover makes as it rolls up onto the landing pad
    /// (place the file at Assets/Resources/Rover_on_Landing_Pad.mp3 so it survives the
    /// WebGL build). Falls back to a procedural metal clank, so the cue never disappears.
    /// </summary>
    private static AudioClip LoadPadClankClip()
    {
        if (padClankClip != null) return padClankClip;
        padClankClip = Resources.Load<AudioClip>("Rover_on_Landing_Pad");
        if (padClankClip == null)
        {
            Debug.LogWarning("RoverSounds: 'Rover_on_Landing_Pad' not found in Resources; using procedural clank.");
            padClankClip = BakePadClankClip();
        }
        return padClankClip;
    }

    /// <summary>A short metallic clank: an impact transient plus a resonant high-freq ring.</summary>
    private static AudioClip BakePadClankClip()
    {
        if (padClankClip != null) return padClankClip;
        if (baking) return null;
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 0.18f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sr;
                float impact = Mathf.Exp(-t / 0.012f) * Mathf.Sin(Mathf.PI * 2f * 210f * t);
                float ring = Mathf.Exp(-t / 0.05f) * (Mathf.Sin(Mathf.PI * 2f * 1850f * t) * 0.4f
                                                       + Mathf.Sin(Mathf.PI * 2f * 3800f * t) * 0.15f);
                data[i] = Mathf.Clamp((impact + ring) * 0.8f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("LRV_PadClank", samples, 1, sr, false);
            clip.SetData(data, 0);
            clip.name = "LRV_PadClank";
            padClankClip = clip;
            return clip;
        }
        finally
        {
            baking = false;
        }
    }

    /// <summary>Plays the metal landing-pad clank at the rover's position.</summary>
    public void PlayLandingPadClank()
    {
        if (padClankClip == null) padClankClip = LoadPadClankClip();
        if (padClankClip == null) return;
        PlayOneShot3D(padClankClip, transform.position, 0.9f);
    }

    // ------------------------------------------------------------ tools clank (garage)
    /// <summary>
    /// Loads the mechanic's tool-clank played when an upgrade installs in the garage
    /// (place the file at Assets/Resources/tools_clank.mp3 so it survives the WebGL build).
    /// Falls back to the procedural pad clank, so the cue never disappears.
    /// </summary>
    private static AudioClip LoadToolsClankClip()
    {
        if (toolsClankClip != null) return toolsClankClip;
        toolsClankClip = Resources.Load<AudioClip>("tools_clank");
        if (toolsClankClip == null)
        {
            Debug.LogWarning("RoverSounds: 'tools_clank' not found in Resources; using procedural clank.");
            toolsClankClip = BakePadClankClip();
        }
        return toolsClankClip;
    }

    /// <summary>Plays the garage tool-clank as a 2D cue at the listener (menu screen).</summary>
    public static void PlayToolsClank()
    {
        if (toolsClankClip == null) toolsClankClip = LoadToolsClankClip();
        if (toolsClankClip == null) return;
        PlayOneShot2D(toolsClankClip, 0.9f);
    }

    // ------------------------------------------------------------ one-shot helpers

    /// <summary>A positional one-shot source that plays a clip once in the 3D world.</summary>
    private static void PlayOneShot3D(AudioClip clip, Vector3 position, float volume)
    {
        if (clip == null) return;
        GameObject go = new GameObject("StcOneShot");
        go.transform.position = position;
        AudioSource src = go.AddComponent<AudioSource>();
        src.spatialBlend = 0.9f;
        src.volume = volume;
        src.playOnAwake = false;
        src.clip = clip;
        src.Play();
        UnityEngine.Object.Destroy(go, Mathf.Max(clip.length, 0.5f) + 0.2f);
    }

    /// <summary>A listener-anchored one-shot that plays a clip once at full, unattenuated strength.</summary>
    private static void PlayOneShot2D(AudioClip clip, float volume)
    {
        if (clip == null) return;
        GameObject go = new GameObject("StcOneShot2D");
        AudioSource src = go.AddComponent<AudioSource>();
        src.spatialBlend = 0f;
        src.volume = volume;
        src.playOnAwake = false;
        src.clip = clip;
        src.Play();
        UnityEngine.Object.Destroy(go, Mathf.Max(clip.length, 0.5f) + 0.2f);
    }

    /// <summary>
    /// Boosts a one-shot past the single-source 1.0 volume ceiling: layering a second, quieter
    /// copy on top lifts the effective loudness ~30% ("by about a third"), which the user asked
    /// for on the important voice cues (launch sequence, race countdown).
    /// </summary>
    private static void PlayOneShot2DBoosted(AudioClip clip, float volume, float layer = 0.3f)
    {
        if (clip == null) return;
        PlayOneShot2D(clip, Mathf.Min(volume, 1f));
        PlayOneShot2D(clip, Mathf.Min(layer, 1f));
    }

    /// <summary>The race 3-2-1-GO countdown voice, boosted ~30% over the old single 0.85 cue
    /// (a full-strength listener-anchored pass plus a quiet reinforcing layer).</summary>
    public static void PlayCountdownVoice(AudioClip clip)
    {
        PlayOneShot2DBoosted(clip, 0.85f, 0.25f);
    }

    /// <summary>The launch/board voice + burn roar, anchored at the lander, boosted ~30%.</summary>
    public static void PlayTakeOff(Vector3 position)
    {
        if (takeOffClip == null) takeOffClip = LoadTakeOffClip();
        if (takeOffClip == null) return;
        // A second quiet layer rides on the main cue so the voice reads clearly louder than 1.0.
        PlayOneShot3D(takeOffClip, position, 1f);
        PlayOneShot3D(takeOffClip, position, 0.3f);
    }

    // ------------------------------------------------------------ rocket burn (end cinematic)

    /// <summary>
    /// A looping rocket-burn: low rumble + filtered jet noise with a slow turbine throb,
    /// with a built-in attack so starting/stopping the loop never clicks. Used by the
    /// end-of-level lander as it descends and lifts off.
    /// </summary>
    public static AudioClip BakeRocketClip()
    {
        if (rocketClip != null) return rocketClip;
        if (baking) return null;
        baking = true;
        try
        {
            int sr = 44100;
            float dur = 1f;
            int samples = Mathf.RoundToInt(sr * dur);
            float[] data = new float[samples];
            System.Random rng = new System.Random(20260908);
            float lpState = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sr;
                // Integer-cycle rumble (seamless loop) + lowpassed jet noise.
                float rumble = Mathf.Sin(Mathf.PI * 2f * 56f * t) * 0.5f
                             + Mathf.Sin(Mathf.PI * 2f * 113f * t) * 0.3f;
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpState = lpState * 0.93f + w * 0.07f;
                float throb = 0.75f + 0.25f * Mathf.Sin(Mathf.PI * 2f * 9f * t);
                float env = Mathf.Clamp01(t / 0.25f); // soft attack at the loop start
                data[i] = Mathf.Clamp((rumble + lpState * 0.9f) * throb * env * 0.5f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("LRV_Rocket", samples, 1, sr, false);
            clip.SetData(data, 0);
            clip.name = "LRV_Rocket";
            // Remove any residual noise seam so the loop is a continuous burn.
            rocketClip = SeamlessLoop(clip, "LRV_RocketLoop");
            return rocketClip;
        }
        finally
        {
            baking = false;
        }
    }
}