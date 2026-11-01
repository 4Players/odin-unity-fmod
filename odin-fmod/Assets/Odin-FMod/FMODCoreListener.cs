using FMOD;
using UnityEngine;

/// <summary>
/// Mirrors this transform into the FMOD Core listener. The <see cref="FMODUnity.StudioListener"/> only moves the
/// Studio listener used by Studio events, while the ODIN voice plays on Core channels, which are spatialized against
/// the Core listener. Without this, the Core listener stays at the world origin facing +Z.
/// </summary>
public class FMODCoreListener : MonoBehaviour
{
    private void LateUpdate()
    {
        ATTRIBUTES_3D attributes3D = FMODUnity.RuntimeUtils.To3DAttributes(transform);
        FMODUnity.RuntimeManager.CoreSystem.set3DListenerAttributes(0, ref attributes3D.position,
            ref attributes3D.velocity, ref attributes3D.forward, ref attributes3D.up);
    }
}
