using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Harvester-mode compass driver. Every frame it finds the nearest LIVE oxygen pod (the next
/// route checkpoint) and feeds it to the HUD compass. Live = not yet collected; harvested or
/// passed targets are skipped. Pickups other than oxygen are deliberately NOT tracked here -
/// the player finds them by eye, keeping the oxygen route the only needle on the dial.
/// </summary>
public class HarvesterCompassDriver : MonoBehaviour
{
    [Header("References")]
    public HUD hud;
    public BuggyController buggy;

    [Header("Target pool (injected by GameBootstrap set up)")]
    public List<OxygenPod> pods;

    private void Update()
    {
        if (hud == null || buggy == null) return;

        Vector3 buggyPos = buggy.transform.position;

        // LUNAR TRIALS: the independent ORE dial tracks the nearest uncollected nugget at all
        // times (never just the close-range sonar blips), tinted the ore's own colour. The
        // oxygen dial below keeps its canister guidance - two readouts, two colours. The sonar
        // blip still flashes/beeps on top of the ore dial when a cluster closes in.
        if (ChallengeRunner.Active != null && ChallengeRunner.Active.oreTarget > 0)
        {
            Vector3 bestOre = buggyPos;
            float bestOreDist = float.MaxValue;
            int oreLeft = 0;
            for (int i = 0; i < Harvestable.All.Count; i++)
            {
                Harvestable h = Harvestable.All[i];
                if (h == null || h.Collected) continue;
                oreLeft++;
                float d = (h.transform.position - buggyPos).sqrMagnitude;
                if (d < bestOreDist) { bestOreDist = d; bestOre = h.transform.position; }
            }
            hud.SetOreDirection(buggyPos, bestOre, oreLeft);
            // no return: the oxygen dial below is still driven every frame.
        }

        Vector3 podTarget = buggyPos;
        int podsLeft = 0;
        float podBest = float.MaxValue;
        bool nearestIsFinal = false;

        if (pods != null)
        {
            for (int i = 0; i < pods.Count; i++)
            {
                OxygenPod pod = pods[i];
                if (pod == null || pod.Collected) continue; // inert - skip as target
                podsLeft++;
                float d = (pod.transform.position - buggyPos).sqrMagnitude;
                if (d < podBest)
                {
                    podBest = d;
                    podTarget = pod.transform.position;
                    nearestIsFinal = pod.isFinalPod;
                }
            }
        }

        bool race = GameFlow.Mode == GameMode.Race;
        string label = GameFlow.IsRidgeSurvival ? "NEXT CANISTER" : (race ? "NEXT GATE" : "NEXT POD");
        // LUNAR TRIALS colour coding: the dial marker matches the target canister - BLUE for a
        // regular canister, GREEN when the next one is the final (green-lit) canister.
        Color podTint = nearestIsFinal
            ? new Color(0.2f, 1f, 0.55f, 1f)
            : new Color(0.25f, 0.75f, 1f, 1f);
        hud.SetRouteDirection(buggyPos, podTarget, podsLeft, label, podTint);
    }
}