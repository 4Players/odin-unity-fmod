using System;
using System.Runtime.InteropServices;
using FMOD;
using OdinNative.Core;
using OdinNative.Odin.Room;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Odin_FMod
{
    public class FMODMicrophoneReader : MonoBehaviour
    {
        /// <summary>
        /// Reference to the recording sound create info.
        /// </summary>
        private FMOD.CREATESOUNDEXINFO _recordingSoundInfo = new CREATESOUNDEXINFO();

        /// <summary>
        /// Reference to the FMOD recording sound
        /// </summary>
        private FMOD.Sound _recordingSound;

        /// <summary>
        /// The recording sound length in PCM Samples
        /// </summary>
        private uint _recordingSoundLength = 0;

        /// <summary>
        /// The current position at which we're reading samples from the FMOD recording sound, in PCM samples.
        /// </summary>
        private uint _currentReadPosition = 0;

        /// <summary>
        /// Native microphone sampling rate in Hz
        /// </summary>
        private int _nativeRate;

        /// <summary>
        /// Number of native microphone channels.
        /// </summary>
        private int _nativeChannels;

        /// <summary>
        /// The device id, from which the microphone data is recorded.
        ///
        /// Needs to be adjusted, if the recording should be done from a microphone
        /// other than the Default Device.
        /// </summary>
        private int _currentDeviceId = 0;

        /// <summary>
        /// Key that toggles the microphone mute.
        /// </summary>
        [SerializeField] private KeyCode muteKey = KeyCode.M;

        /// <summary>
        /// True while the microphone is muted. ODIN sends one silent frame and then stops the stream until unmuted.
        /// </summary>
        public bool Muted { get; private set; }

        /// <summary>
        /// Frames per chunk pushed to ODIN (20 ms) and the matching interleaved sample buffer.
        /// </summary>
        private uint _chunkFrames;
        private float[] _chunk;


        private void Start()
        {
            FMODUnity.RuntimeManager.CoreSystem.getRecordNumDrivers(out int numDrivers, out _);
            if (numDrivers <= 0)
            {
                Debug.LogWarning($"{nameof(FMODMicrophoneReader)} found no recording device");
                return;
            }

            // retrieve microphone info like sampling rate and number of channels
            FMODUnity.RuntimeManager.CoreSystem.getRecordDriverInfo(_currentDeviceId, out string deviceName, 256, out _, out _nativeRate,
                out _,out _nativeChannels, out _);
            if (_nativeChannels < 1 || _nativeChannels > 2)
            {
                Debug.LogError($"{nameof(FMODMicrophoneReader)} device \"{deviceName}\" has {_nativeChannels} channels, ODIN encodes mono or stereo");
                return;
            }
            _chunkFrames = (uint)(_nativeRate / 50); // 20 ms
            _chunk = new float[_chunkFrames * _nativeChannels];
            Debug.Log($"{nameof(FMODMicrophoneReader)} recording \"{deviceName}\": {_nativeRate} Hz, {_nativeChannels} channel(s), {_chunkFrames} frames per chunk");
            // Setup recording sound that will contain microphone data
            _recordingSoundInfo.cbsize = Marshal.SizeOf(typeof(CREATESOUNDEXINFO));
            _recordingSoundInfo.numchannels = _nativeChannels;
            _recordingSoundInfo.defaultfrequency = _nativeRate;
            _recordingSoundInfo.format = SOUND_FORMAT.PCMFLOAT;
            // make recording sound one second long
            _recordingSoundInfo.length = (uint)(_nativeRate * sizeof(float) * _nativeChannels);

            // Create recording sound
            FMODUnity.RuntimeManager.CoreSystem.createSound("", MODE.LOOP_NORMAL | MODE.OPENUSER,
                ref _recordingSoundInfo,
                out _recordingSound);

            // Start recording
            FMODUnity.RuntimeManager.CoreSystem.recordStart(_currentDeviceId, _recordingSound, true);
            // retrieve recording sound length in pcm samples.
            _recordingSound.getLength(out _recordingSoundLength, TIMEUNIT.PCM);
        }

        private void OnDestroy()
        {
            FMODUnity.RuntimeManager.CoreSystem.recordStop(_currentDeviceId);
            _recordingSound.release();
        }


        /// <summary>
        /// Mutes or unmutes the microphone stream in every joined room.
        /// </summary>
        public void SetMuted(bool muted)
        {
            Muted = muted;
            if (OdinHandler.Instance)
                foreach (var room in OdinHandler.Instance.Rooms)
                    room.MicrophoneMedia?.MuteStream(muted);
            Debug.Log($"{nameof(FMODMicrophoneReader)} microphone {(muted ? "muted" : "unmuted")}");
        }

        private void Update()
        {
            if (Input.GetKeyDown(muteKey))
                SetMuted(!Muted);

            if (!OdinHandler.Instance || !OdinHandler.Instance.HasConnections || OdinHandler.Instance.Rooms.Count == 0)
                return;
            if (_chunk == null)
                return; // no usable recording device

            // Determine how much has been recorded since we last checked, in PCM frames
            FMODUnity.RuntimeManager.CoreSystem.getRecordPosition(_currentDeviceId, out uint recordPosition);
            uint available = (recordPosition >= _currentReadPosition)
                ? (recordPosition - _currentReadPosition)
                : (recordPosition + _recordingSoundLength - _currentReadPosition);

            // a stall (scene load, paused editor) leaves old audio behind; skip it instead of sending it late
            uint maxBacklog = (uint)(_nativeRate / 5); // 200 ms
            if (available > maxBacklog)
            {
                _currentReadPosition = (_currentReadPosition + available - maxBacklog) % _recordingSoundLength;
                available = maxBacklog;
            }

            // FMOD wants byte offsets, and one frame holds one float per channel
            uint bytesPerFrame = sizeof(float) * (uint)_nativeChannels;
            while (available >= _chunkFrames)
            {
                _recordingSound.@lock(_currentReadPosition * bytesPerFrame, _chunkFrames * bytesPerFrame,
                    out IntPtr ptr1, out IntPtr ptr2, out uint len1, out uint len2);
                int samples1 = (int)(len1 / sizeof(float));
                Marshal.Copy(ptr1, _chunk, 0, samples1);
                // the ring buffer wrapped, the rest of the chunk starts at the beginning of the sound
                if (ptr2 != IntPtr.Zero && len2 > 0)
                    Marshal.Copy(ptr2, _chunk, samples1, (int)(len2 / sizeof(float)));
                _recordingSound.unlock(ptr1, ptr2, len1, len2);

                foreach (var room in OdinHandler.Instance.Rooms)
                {
                    ValidateMicrophoneStream(room);

                    // push microphone data to odin servers; a stream re-created by ValidateMicrophoneStream starts unmuted
                    if (null != room.MicrophoneMedia)
                    {
                        if (room.MicrophoneMedia.IsMuted != Muted)
                            room.MicrophoneMedia.MuteStream(Muted);
                        room.MicrophoneMedia.AudioPushData(_chunk, _chunk.Length);
                    }
                }

                _currentReadPosition = (_currentReadPosition + _chunkFrames) % _recordingSoundLength;
                available -= _chunkFrames;
            }
        }

        /// <summary>
        /// Checks the rooms microphone stream for correct setup and initializes the stream if necessary.
        /// </summary>
        /// <param name="room">Room to check the stream for.</param>
        private void ValidateMicrophoneStream(Room room)
        {
            bool isValidStream = null != room.MicrophoneMedia &&
                                 _nativeChannels == (int)room.MicrophoneMedia.MediaConfig.Channels &&
                                 _nativeRate == (int)room.MicrophoneMedia.MediaConfig.SampleRate;
            if (!isValidStream)
            {
                room.MicrophoneMedia?.Dispose();
                room.CreateMicrophoneMedia(new OdinMediaConfig((MediaSampleRate)_nativeRate,
                    (MediaChannels)_nativeChannels));
            }
        }
    }
}