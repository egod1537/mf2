#if UNITY_EDITOR
using System.Collections;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace MF2.Tests.FMOD
{
    public sealed class FmodPlaybackSmokeTest : MonoBehaviour
    {
        private const string Event2DPath = "event:/Test 2D Tone";
        private const string Event3DPath = "event:/Test 3D Tone";

        private EventInstance event2D;
        private EventInstance event3D;

        private IEnumerator Start()
        {
            EnsureResult(RuntimeManager.StudioSystem.getBankList(out Bank[] banks), "read loaded banks");
            if (banks.Length == 0)
            {
                throw new System.InvalidOperationException("FMOD did not automatically load any banks.");
            }

            EventDescription description2D = GetDescription(Event2DPath);
            EventDescription description3D = GetDescription(Event3DPath);
            EnsureResult(description2D.is3D(out bool is2DActually3D), "inspect the 2D event");
            EnsureResult(description3D.is3D(out bool is3D), "inspect the 3D event");

            if (is2DActually3D || !is3D)
            {
                throw new System.InvalidOperationException(
                    $"FMOD event dimensionality is incorrect (2D is3D={is2DActually3D}, 3D is3D={is3D}).");
            }

            event2D = RuntimeManager.CreateInstance(Event2DPath);
            event3D = RuntimeManager.CreateInstance(Event3DPath);
            EnsureResult(event3D.set3DAttributes(transform.To3DAttributes()), "position the 3D event");
            EnsureResult(event2D.start(), "start the 2D event");
            EnsureResult(event3D.start(), "start the 3D event");

            UnityEngine.Debug.Log($"[FMOD Smoke Test] START: auto-loaded {banks.Length} bank(s); playing 2D and 3D events.");

            yield return new WaitForSecondsRealtime(0.25f);

            EnsureResult(event2D.getPlaybackState(out PLAYBACK_STATE state2D), "read 2D playback state");
            EnsureResult(event3D.getPlaybackState(out PLAYBACK_STATE state3D), "read 3D playback state");

            if (!IsAudibleState(state2D) || !IsAudibleState(state3D))
            {
                throw new System.InvalidOperationException(
                    $"FMOD events did not reach a playing state (2D={state2D}, 3D={state3D}).");
            }

            UnityEngine.Debug.Log($"[FMOD Smoke Test] PASS: 2D={state2D}, 3D={state3D}, dimensionality verified.");
        }

        private static EventDescription GetDescription(string path)
        {
            EnsureResult(RuntimeManager.StudioSystem.getEvent(path, out EventDescription description), $"find {path}");
            return description;
        }

        private static bool IsAudibleState(PLAYBACK_STATE state)
        {
            return state == PLAYBACK_STATE.STARTING || state == PLAYBACK_STATE.PLAYING || state == PLAYBACK_STATE.SUSTAINING;
        }

        private static void EnsureResult(RESULT result, string operation)
        {
            if (result != RESULT.OK)
            {
                throw new System.InvalidOperationException($"Failed to {operation}: {result} ({Error.String(result)})");
            }
        }

        private void OnDestroy()
        {
            StopAndRelease(ref event2D);
            StopAndRelease(ref event3D);
        }

        private static void StopAndRelease(ref EventInstance instance)
        {
            if (!instance.isValid())
            {
                return;
            }

            instance.stop(global::FMOD.Studio.STOP_MODE.IMMEDIATE);
            instance.release();
            instance.clearHandle();
        }
    }
}
#endif
