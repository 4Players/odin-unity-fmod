using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FMOD;
using OdinNative.Core;
using OdinNative.Odin.Media;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class FMODPlaybackComponent : MonoBehaviour
{
    private PlaybackStream _playbackMedia;


    /// <summary>
    /// Retrieves the connected media stream based on the room name, peer id and media id.
    /// </summary>
    /// <returns>Media Stream as Playbackstream, if room name, peer id and media id point to a valid stream, or null otherwise.</returns>
    private PlaybackStream FindOdinMediaStream() => OdinHandler.Instance?.Rooms?[RoomName]?
        .RemotePeers[PeerId]?
        .Medias[MediaStreamId] as PlaybackStream;


    /// <summary>
    ///     Room name for this playback. Change this value to change the PlaybackStream by Rooms from the Client.
    /// </summary>
    /// <remarks>Invalid values will cause errors.</remarks>
    public string RoomName { get; set; }

    /// <summary>
    ///     Peer id for this playback. Change this value to change the PlaybackStream by RemotePeers in the Room.
    /// </summary>
    /// <remarks>Invalid values will cause errors.</remarks>
    public ulong PeerId { get; set; }

    /// <summary>
    ///     Media id for this playback. Change this value to pick a PlaybackStream by media id from peers Medias.
    /// </summary>
    /// <remarks>Invalid values will cause errors.</remarks>
    public long MediaStreamId { get; set; }

    private Sound _playbackSound;
    /// <summary>
    /// Reference to the FMOD Sound created for playing back the connected ODIN Media Stream.
    /// </summary>
    public Sound FMODPlaybackSound => _playbackSound;

    private Channel _playbackChannel;

    /// <summary>
    /// Reference to the FMOD Channel created for playing back the connected ODIN Media Stream.
    /// </summary>
    public Channel FMODPlaybackChannel => _playbackChannel;

    /// <summary>
    /// Size of the FMOD decode buffer of the playback stream in milliseconds. 0 keeps FMOD's default (400 ms), which
    /// is the main source of the playback latency. 40 to 50 ms measured as the smallest values without dropouts,
    /// 20 ms already crackles. Can be overridden on the command line with <c>-decodebuffer &lt;ms&gt;</c>.
    /// </summary>
    [Tooltip("FMOD decode buffer of the voice stream in milliseconds. 0 = FMOD default (400 ms). Command line: -decodebuffer <ms>")]
    public int DecodeBufferMilliseconds = 50;

    private CREATESOUNDEXINFO _createSoundInfo;
    private float[] _readBuffer = Array.Empty<float>();

    private SOUND_PCMREAD_CALLBACK _pcmReadCallback;

    /// <summary>
    /// ODIN buffer level the playback aims for. Whatever ODIN buffers beyond that (at startup, after a network hiccup
    /// or while FMOD wasn't reading) is played slightly faster until the buffer is back at this level.
    /// </summary>
    [Tooltip("ODIN buffer level in milliseconds the playback aims for. Above it, the voice plays slightly faster to catch up.")]
    public int TargetBufferMilliseconds = 40;

    /// <summary>
    /// Above this ODIN buffer level, the excess is discarded at once instead of played faster, e.g. after the channel
    /// was paused or the mixer suspended. ODIN itself buffers up to 2 seconds.
    /// </summary>
    [Tooltip("ODIN buffer level in milliseconds above which the excess is discarded at once.")]
    public int MaxBufferMilliseconds = 400;

    /// <summary>
    /// Playback speed while catching up to <see cref="TargetBufferMilliseconds"/>, 2% faster is practically inaudible.
    /// </summary>
    private const float CatchUpSpeed = 1.02f;

    private int _sampleRate;
    private int _targetBufferSamples;
    private int _maxBufferSamples;
    private bool _catchingUp;

    private static readonly Dictionary<IntPtr, FMODPlaybackComponent> _soundToPlaybackMap = new();

    // Start is called before the first frame update
    void Start()
    {
        int playBackRate = (int)OdinHandler.Config.RemoteSampleRate;
        int numChannels = (int)OdinHandler.Config.RemoteChannels;
        _sampleRate = playBackRate;
        _targetBufferSamples = playBackRate * numChannels * TargetBufferMilliseconds / 1000;
        _maxBufferSamples = playBackRate * numChannels * MaxBufferMilliseconds / 1000;

        _createSoundInfo.cbsize = Marshal.SizeOf(typeof(FMOD.CREATESOUNDEXINFO));
        _createSoundInfo.numchannels = numChannels;
        _createSoundInfo.defaultfrequency = playBackRate;
        _createSoundInfo.format = SOUND_FORMAT.PCMFLOAT;

        _createSoundInfo.pcmreadcallback = null;
        _createSoundInfo.pcmsetposcallback = null;
        _createSoundInfo.nonblockcallback = null;
        _createSoundInfo.fileuseropen = null;
        _createSoundInfo.fileuserclose = null;
        _createSoundInfo.fileuserread = null;
        _createSoundInfo.fileuserseek = null;
        _createSoundInfo.fileuserasyncread = null;
        _createSoundInfo.fileuserasynccancel = null;

        _pcmReadCallback = new SOUND_PCMREAD_CALLBACK(PcmReadCallback);
        _createSoundInfo.pcmreadcallback = _pcmReadCallback;
        _createSoundInfo.length = (uint)(playBackRate * sizeof(float) * numChannels);

        int decodeBufferMilliseconds = CommandLineInt("-decodebuffer", DecodeBufferMilliseconds);
        if (decodeBufferMilliseconds > 0)
            _createSoundInfo.decodebuffersize = (uint)(playBackRate * decodeBufferMilliseconds / 1000);

        // the 3D mode has to be set when creating the sound, switching a playing stream to 3D is unreliable
        bool spatialize = TryGetComponent(out FMODPlayback3DPosition spatializer);
        MODE mode = MODE.OPENUSER | MODE.LOOP_NORMAL | (spatialize ? MODE._3D | spatializer.RolloffMode : MODE._2D);
        FMODUnity.RuntimeManager.CoreSystem.createStream("", mode, ref _createSoundInfo, out _playbackSound);
        FMODUnity.RuntimeManager.CoreSystem.getMasterChannelGroup(out ChannelGroup masterChannelGroup);
        // start paused so the first mixed block is already panned to the right position
        FMODUnity.RuntimeManager.CoreSystem.playSound(_playbackSound, masterChannelGroup, true,
            out _playbackChannel);
        // highest priority, so FMOD virtualizes other channels first when it runs out of real channels
        _playbackChannel.setPriority(0);
        if (spatialize)
        {
            ATTRIBUTES_3D attributes3D = FMODUnity.RuntimeUtils.To3DAttributes(transform);
            _playbackChannel.set3DAttributes(ref attributes3D.position, ref attributes3D.velocity);
        }
        _playbackChannel.setPaused(false);

        lock (_soundToPlaybackMap)
        {
            _soundToPlaybackMap.Add(_playbackSound.handle, this);
        }
    }

    /// <summary>
    /// Plays the voice slightly faster while ODIN buffers more than <see cref="TargetBufferMilliseconds"/>. FMOD reads
    /// at exactly the playback rate, so without this, any delay that builds up in ODIN's buffer would stay forever.
    /// </summary>
    private void Update()
    {
        PlaybackStream playbackMedia = FindOdinMediaStream();
        if (null == playbackMedia || playbackMedia.IsInvalid || !_playbackChannel.hasHandle())
            return;

        bool catchUp = playbackMedia.AudioDataLength() > _targetBufferSamples;
        if (catchUp != _catchingUp)
        {
            _catchingUp = catchUp;
            _playbackChannel.setFrequency(_sampleRate * (catchUp ? CatchUpSpeed : 1f));
        }
    }

    [AOT.MonoPInvokeCallback(typeof(SOUND_PCMREAD_CALLBACK))]
    private static RESULT PcmReadCallback(IntPtr sound, IntPtr data, uint dataLength)
    {
        // check data pointer for validity
        if (data == IntPtr.Zero)
        {
            return RESULT.ERR_INVALID_PARAM;
        }

        FMODPlaybackComponent playbackComponent = null;
        lock (_soundToPlaybackMap)
        {
            if (!_soundToPlaybackMap.TryGetValue(sound, out playbackComponent))
            {
                return RESULT.OK;
            }
        }

        if (playbackComponent == null)
        {
            return RESULT.OK;

        }
        // retrieve array length of data that is requested
        int requestedDataArrayLength = (int)dataLength / sizeof(float);

        // resize read buffer if necessary
        if (playbackComponent._readBuffer.Length < requestedDataArrayLength)
        {
            playbackComponent._readBuffer = new float[requestedDataArrayLength];
        }

        PlaybackStream playbackMedia = playbackComponent.FindOdinMediaStream();
        // only read if we've joined any room and the connected playback media is valid.
        if (null != playbackMedia && OdinHandler.Instance?.HasConnections == true && !playbackMedia.IsInvalid)
        {
            // after a longer interruption, discard the excess at once instead of catching up slowly
            int buffered = playbackMedia.AudioDataLength();
            if (buffered > playbackComponent._maxBufferSamples)
            {
                int discard = buffered - playbackComponent._targetBufferSamples;
                if (playbackComponent._readBuffer.Length < discard)
                {
                    playbackComponent._readBuffer = new float[discard];
                }
                playbackMedia.AudioReadData(playbackComponent._readBuffer, discard);
            }

            // read voice data from media stream into read buffer
            uint odinReadResult = playbackMedia.AudioReadData(playbackComponent._readBuffer, requestedDataArrayLength);
            if (Utility.IsError(odinReadResult))
            {
                Debug.LogWarning($"{nameof(FMODPlaybackComponent)} AudioReadData failed with error code:{odinReadResult}");
            }
            else
            {
                Marshal.Copy(playbackComponent._readBuffer, 0, data, requestedDataArrayLength);
            }
        }

        return RESULT.OK;
    }

    private static int CommandLineInt(string name, int fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out int value))
                return value;
        return fallback;
    }

    private void OnDestroy()
    {
        lock (_soundToPlaybackMap)
        {
            _soundToPlaybackMap.Remove(_playbackSound.handle);
        }
        _playbackSound.release();
    }
}