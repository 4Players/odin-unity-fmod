using System;
using System.Collections;
using System.Collections.Generic;
using FMOD;
using UnityEngine;
using UnityEngine.Assertions;
using Debug = UnityEngine.Debug;

/// <summary>
/// Spatializes the voice of the <see cref="FMODPlaybackComponent"/> on this object. Its presence makes the playback
/// component create the FMOD stream in 3D mode; the panning is computed against the FMOD Core listener, which
/// <see cref="FMODCoreListener"/> keeps in sync.
/// </summary>
[RequireComponent(typeof(FMODPlaybackComponent))]
public class FMODPlayback3DPosition : MonoBehaviour
{
    public enum Rolloff
    {
        /// <summary>
        /// FMOD default: volume halves with each doubling of the distance and never reaches silence.
        /// </summary>
        Inverse,
        /// <summary>
        /// Volume falls linearly from full at the min distance to silence at the max distance.
        /// </summary>
        Linear,
        /// <summary>
        /// Like linear, but falls faster near the min distance, which sounds more natural.
        /// </summary>
        LinearSquare
    }

    /// <summary>
    /// Distance up to which the voice plays at full volume.
    /// </summary>
    [SerializeField] private float minDistance = 1f;

    /// <summary>
    /// Distance at which the voice is silent (linear rolloffs) or gets no quieter (inverse rolloff).
    /// </summary>
    [SerializeField] private float maxDistance = 20f;

    /// <summary>
    /// How the volume falls off between min and max distance. Read once when the playback stream is created.
    /// </summary>
    [SerializeField] private Rolloff rolloff = Rolloff.LinearSquare;

    /// <summary>
    /// FMOD mode flag of the selected rolloff, used by <see cref="FMODPlaybackComponent"/> when creating the stream.
    /// </summary>
    public MODE RolloffMode => rolloff switch
    {
        Rolloff.Linear => MODE._3D_LINEARROLLOFF,
        Rolloff.LinearSquare => MODE._3D_LINEARSQUAREROLLOFF,
        _ => MODE._3D_INVERSEROLLOFF
    };

    private FMODPlaybackComponent _playbackComponent;

    private void Awake()
    {
        _playbackComponent = GetComponent<FMODPlaybackComponent>();
        Assert.IsNotNull(_playbackComponent);
    }

    private IEnumerator Start()
    {
        // wait for playback component initialization, give up if the playback component is gone
        while (_playbackComponent && !_playbackComponent.FMODPlaybackChannel.hasHandle())
        {
            yield return null;
        }

        if (!_playbackComponent)
        {
            Debug.LogWarning($"{nameof(FMODPlayback3DPosition)} on {name} has no {nameof(FMODPlaybackComponent)}, disabling 3D positioning");
            enabled = false;
            yield break;
        }

        _playbackComponent.FMODPlaybackChannel.set3DMinMaxDistance(minDistance, maxDistance);
        _playbackComponent.FMODPlaybackChannel.set3DLevel(1);
    }

    private void LateUpdate()
    {
        if (_playbackComponent && _playbackComponent.FMODPlaybackChannel.hasHandle())
        {
            // update the 3d position of the playback in FMOD
            ATTRIBUTES_3D attributes3D = FMODUnity.RuntimeUtils.To3DAttributes(transform);
            _playbackComponent.FMODPlaybackChannel.set3DAttributes(ref attributes3D.position, ref attributes3D.velocity);
        }
    }
}
