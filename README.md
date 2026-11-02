# ODIN integration using FMOD in Unity

This example project shows how to use the [FMOD Plugin](https://www.fmod.com/unity) in combination with the [ODIN Voice Unity SDK](https://docs.4players.io/voice/unity/), the full-service voice chat solution by [4Players](https://www.4players.io/company/about_us/).

You can grab and move both the audio listener and remote peers with the left mouse button. This showcases the 3D positional audio behavior. To test the project on your own, you can create a build and start up the Editor.

Alternatively you can use [the ODIN web app](https://4players.app) to test the project in combination with the editor. Please note that you'll need to set up the `Auth Access Key` in the web app and connect to the `default` ODIN room.

The two essential scripts are the `FMODMicrophoneReader` and `FMODPlaybackComponent` scripts. For 3D positional audio, add `FMODPlayback3DPosition` to the playback objects and `FMODCoreListener` to the listener.

## Requirements

The sample was created with:

- Unity 6000.3.27f1
- ODIN Voice Unity SDK 1.7.9
- FMOD for Unity 2.03.15 and FMOD Studio 2.03

## Disabling Unity Audio Engine

To inform ODIN that the Unity Audio engine is disabled (e.g., when using FMOD exclusively for audio processing), you need to add the `ODIN_UNITY_AUDIO_ENGINE_DISABLED` flag in the **Scripting Define Symbols** under **Player Settings** in Unity. This flag ensures ODIN will not attempt to use Unity's audio systems.

## FMODMicrophoneReader

This script replaces ODIN's `MicrophoneReader` component by handling the microphone input data using the FMOD audio engine. It will automatically read microphone input data and send it to ODIN. Simply add this Microphone Reader to the `OdinManager` prefab and disable the `MicrophoneReader` component.

**Important:** For ODIN Plugin Versions < 1.5.9 please don't remove the `MicrophoneReader` component, as this will lead to NullpointerExceptions.

### Limitations

Devices with more than two channels, e.g. microphone arrays or multi-channel audio interfaces, are downmixed to mono by averaging all channels. On an audio interface where only one input is in use, this makes the voice quieter.

The script currently doesn't automatically switch devices or allow devices to be switched programmatically. If you'd like us to extend the sample, please let us know [on our Discord server](https://4np.de/discord)!

## FMODPlaybackComponent

This script replaces ODIN's `PlaybackComponent`. After setting room name, peer id and media stream id, the component will automatically connect to the relevant ODIN Media Stream and playback voice data using the FMOD engine.

Take a look at the `OdinAutoJoin` script to see a sample implementation of how to handle playback components.

**Important:** Because the `OdinHandler` currently only supports automatically setting up playback using the Unity Audio system, you'll need to switch from `Playback auto creation` to `Manual positional audio` on the `OdinHandler` script. Instead take a look at the `OdinAutoJoin` script to see how to manually handle playback components.

### Latency Settings

- `Decode Buffer Milliseconds`: size of FMOD's decode buffer for the voice stream. FMOD's default is 400 ms, the Odin `FMODPlaybackComponent` defaults to 40 ms. Values of 40 to 50 ms play without dropouts, while 20 ms produces crackles. Set it to 0 to keep FMOD's default. You can override the value in the inspector or globally in builds with the `-decodebuffer <ms>` command line argument.
- `Target Buffer Milliseconds`: the amount of voice data ODIN should buffer internally during playback (default 40 ms). FMOD reads the voice at exactly the playback rate, so any delay that builds up in ODIN's buffer (at startup, after a network hiccup or while FMOD wasn't reading) would otherwise stay forever. If ODIN buffers more than this value, the voice will play 2% faster, which is practically inaudible, until the buffer is back at the target level.
- `Max Buffer Milliseconds`: above this buffer level, the excess is discarded at once instead of played faster, e.g. after the channel was paused or the mixer suspended (component default 400 ms). ODIN itself can buffer up to 2 seconds of voice data.

## 3D Positional Audio

### FMODPlayback3DPosition

Add this script next to an `FMODPlaybackComponent` to spatialize the voice. If present, the playback component creates its FMOD stream in 3D mode. The script then updates the channel's position from the GameObject's transform every frame.

Settings:

- `Min Distance`: the voice plays at full volume up to this distance.
- `Max Distance`: the voice is silent beyond this distance (linear rolloffs), or stops getting quieter (inverse rolloff).
- `Rolloff`: how the volume falls off between min and max distance.
  - `Inverse`: realistic falloff that never reaches silence.
  - `Linear`: fades evenly to silence from min to max distance.
  - `LinearSquare` (default): fades to silence at the max distance, dropping faster the closer you are to the speaker.

The rolloff setting is read when the playback stream is created, changing this during runtime will not change the playback behaviour.

**Important:** When a Studio master bank is loaded, the Studio master bus takes over the FMOD Core master channel group, so its input format decides how many channels the final mix has. If no events are routed to the master bus, FMOD Studio may build it as mono, which mixes left and right together and removes all panning (directional audio). To correct this, in FMOD Studio, open the Mixer, select the Master Bus, right-click the input meter in the deck, set `Input Format` to `Stereo` (or a surround format) and rebuild the banks. The sample's FMOD Studio project is already set up this way.

### FMODCoreListener

Add this script to the same GameObject as your `StudioListener` script, usually the camera or player object. It copies the position and orientation into the FMOD Core listener every frame.
