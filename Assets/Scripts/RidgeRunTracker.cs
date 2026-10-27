using UnityEngine;

/// <summary>
/// Ridge Survival bookkeeping: notices when a run ENDS IN FAILURE (out of oxygen, fell off the
/// ridge) and records it in <see cref="GameFlow.RidgeRunFailed"/>. The next Ridge load reads the
/// flag and starts over from leg one, so a RETRY is a genuinely fresh run (zero distance, full tank,
/// pristine hull) while the automatic load after a CLEARED leg still carries everything forward.
/// GameBootstrap adds one of these to itself for every Ridge Survival build.
/// </summary>
public class RidgeRunTracker : MonoBehaviour
{
    private void Update()
    {
        OxygenManager oxygen = OxygenManager.Instance;
        if (oxygen != null && oxygen.gameOver)
        {
            GameFlow.RidgeRunFailed = true;
            enabled = false;
        }
    }
}
