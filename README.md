# ODIN integration using FMOD in Unity

This example project shows how to use the [FMOD Plugin](https://www.fmod.com/unity) in combination with the [ODIN Voice Unity SDK](https://docs.4players.io/voice/unity/), the full-service voice chat solution by [4Players](https://www.4players.io/company/about_us/).

You can grab and move both the audio listener and remote peers with the left mouse button. This showcases the 3D positional audio behavior. To test the project on your own, you can create a build and start up the Editor.

Alternatively you can use [the ODIN web app](https://4players.app) to test the project in combination with the editor. Please note that you'll need to set up the `Auth Access Key` in the web app and connect to the `default` ODIN room.

The two essential scripts are the `FMODMicrophoneReader` and `FMODPlaybackComponent` scripts.

## Disabling Unity Audio Engine

To inform ODIN that the Unity Audio engine is disabled (e.g., when using FMOD exclusively for audio processing), you need to add the `ODIN_UNITY_AUDIO_ENGINE_DISABLED` flag in the **Scripting Define Symbols** under **Player Settings** in Unity. This flag ensures ODIN will not attempt to use Unity's audio systems.

## FMODMicrophoneReader

This script replaces ODIN's `MicrophoneReader` component by handling the microphone input data using the FMOD audio engine. It will automatically read microphone input data and send it to ODIN. Simply add this Microphone Reader to the `OdinManager` prefab and disable the `MicrophoneReader` component.

**Important:** For ODIN Plugin Versions < 1.5.9 please don't remove the `MicrophoneReader` component, as this will lead to NullpointerExceptions.

### Limitations

The script currently doesn't automatically switch devices or allow devices to be switched programmatically. If you'd like us to extend the sample, please let us know [on our Discord server](https://4np.de/discord)!

## FMODPlaybackComponent

This script replaces ODIN's `PlaybackComponent`. After setting room name, peer id and media stream id, the component will automatically connect to the relevant ODIN Media Stream and playback voice data using the FMOD engine.

Take a look at the `OdinAutoJoin` script to see a sample implementation of how to handle playback components.

**Important:** Because the `OdinHandler` currently only supports automatically setting up playback using the Unity Audio system, you'll need to switch from `Playback auto creation` to `Manual positional audio` on the `OdinHandler` script. Instead take a look at the `OdinAutoJoin` script to see how to manually handle playback components.
