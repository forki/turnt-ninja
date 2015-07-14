﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSCore;
using CSCore.Codecs;
using CSCore.SoundOut;
using OpenTK;
using Substructio.Core;
using Wav2Flac;


namespace BeatDetection.Game
{
    class StageAudio : IDisposable
    {
        public int AudioHashCode { get; private set; }
        private const int HashCount = 10000;
        //private WaveOut _waveOut;
        //private WaveStream _waveProvider;
        private float _maxVolume;

        private CancellationTokenSource _tokenSource = new CancellationTokenSource();

        private IAudio _audio;

        /// <summary>
        /// Volume is clamped between 0 and MaxVolume
        /// </summary>
        public float Volume
        {
            get { return _audio.Volume; }
            set { _audio.Volume = MathHelper.Clamp(value, 0, 1); }
        }

        /// <summary>
        /// Max volume is between 0 and 1
        /// </summary>
        public float MaxVolume
        {
            get { return _maxVolume; }
            set { _maxVolume = MathHelper.Clamp(value, 0, 1); }
        }

        public bool IsStopped
        {
            get { return _audio.PlaybackState == PlaybackState.Stopped; }
        }

        public void Load(string audioPath)
        {
            //assert that the audio path given is valid.
            Debug.Assert(!string.IsNullOrWhiteSpace(audioPath));
            //var hashBytes = new byte[HashCount];
            //_waveOut = new WaveOut();

            //if (Path.GetExtension(audioPath).Equals(".flac", StringComparison.CurrentCultureIgnoreCase))
            //{
            //    var str = new MemoryStream();
            //    var output = new WavWriter(str);
            //    var fr = new FlacReader(audioPath, output);
            //    fr.Process();
            //    str.Position = 0;
            //    str.Read(hashBytes, 0, HashCount);
            //    str.Position = 0;

            //    var fmt = new WaveFormat(fr.inputSampleRate, fr.inputBitDepth, fr.inputChannels);
            //    _waveProvider = new RawSourceWaveStream(str, fmt);
            //    _waveOut.Init(_waveProvider);
            //}
            //else
            //{

            //    var audioReader = new AudioFileReader(audioPath);
            //    audioReader.Read(hashBytes, 0, HashCount);
            //    audioReader.Position = 0;
            //    _waveProvider = audioReader;
            //    _waveOut.Init(_waveProvider);
            //}

            //AudioHashCode = CRC16.Instance().ComputeChecksum(hashBytes);

            _audio = new CSCoreAudio();
            _audio.Init(audioPath);
            AudioHashCode = CRC16.Instance().ComputeChecksum(_audio.GetHashBytes(HashCount));
        }

        public void Play()
        {
            _audio.Play();
        }

        public void Pause()
        {
            _audio.Pause();
        }

        public void Stop()
        {
            _audio.Stop();
            _audio.Seek(0);
            //_waveProvider.Position = 0;
        }

        public void Resume()
        {
            _audio.Resume();
        }

        public void Seek(float percent)
        {
            _audio.Seek(percent);
            //int newPos = (int) (percent*_waveProvider.Length);
            //newPos = newPos - newPos%_waveProvider.BlockAlign;
            //_waveProvider.Position = newPos;
        }

        /// <summary>
        /// Fades out the audio.
        /// </summary>
        /// <param name="time"></param>
        /// <param name="minVolume"></param>
        /// <param name="dVolume"></param>
        /// <param name="pauseOrStop">0 for nothing, 1 for pause, 2 for stop</param>
        public void FadeOut(float time, float minVolume, float dVolume, int pauseOrStop)
        {
            var ct = _tokenSource.Token;
            Task.Run(async () =>
            {
                int dt = (int)(time / ((Volume - minVolume) / dVolume));
                while (Volume > minVolume && !ct.IsCancellationRequested)
                {
                    Volume -= dVolume;
                    await Task.Delay(dt, ct);
                }
                switch (pauseOrStop)
                {
                    case 1:
                        Pause();
                        break;
                    case 2:
                        Stop();
                        break;
                }
            }, ct);
        }

        /// <summary>
        /// Fades in the audio
        /// </summary>
        /// <param name="time">Time over which to fade the audio in</param>
        /// <param name="maxVolume">The maximum volume to reach</param>
        /// <param name="dVolume">The volume step size to use</param>
        /// <param name="pauseOrStop">0 for nothing, 1 to pause after the fade in, 2 to stop after the fade in</param>
        public void FadeIn(float time, float maxVolume, float dVolume, int pauseOrStop)
        {
            if (maxVolume > MaxVolume) throw new ArgumentOutOfRangeException("The maximum fade in volume " + maxVolume + " was greater than the maximum volume for this audio " + MaxVolume);
            var ct = _tokenSource.Token;
            Task.Run(async () =>
            {
                int dt = (int)(time / ((maxVolume - Volume) / dVolume));
                while (Volume < maxVolume && !ct.IsCancellationRequested)
                {
                    Volume += dVolume;
                    await Task.Delay(dt, ct);
                }
                switch (pauseOrStop)
                {
                    case 1:
                        Pause();
                        break;
                    case 2:
                        Stop();
                        break;
                }
            }, ct);
        }

        public void CancelAudioFades()
        {
            _tokenSource.Cancel();
            _tokenSource = new CancellationTokenSource();
        }

        public void Dispose()
        {
            //_waveOut.Dispose();
            ////may error here if _waveOut disposes _waveProvider?
            //_waveProvider.Dispose();
            if (_audio != null) _audio.Dispose();
        }
    }


    internal interface IAudio : IDisposable
    {
        PlaybackState PlaybackState { get; }
        float Volume { get; set; }
        void Init(string audioFilePath);
        byte[] GetHashBytes(int hashByteCount);
        void Play();
        void Pause();
        void Resume();
        void Stop();
        void Seek(float percent);
    }

    enum PlaybackState
    {
        Paused,
        Playing,
        Stopped
    }

    class CSCoreAudio : IAudio
    {
        private ISoundOut _soundOut;
        private IWaveSource _soundSource;

        public void Dispose()
        {
            if (_soundOut != null)
            {
                _soundOut.Stop();
                _soundOut.Dispose();
                _soundOut = null;
            }
            if (_soundSource != null)
            {
                _soundSource.Dispose();
                _soundSource = null;
            }
        }

        public PlaybackState PlaybackState
        {
            get
            {
                switch (_soundOut.PlaybackState)
                {
                    case CSCore.SoundOut.PlaybackState.Paused:
                        return PlaybackState.Paused;
                    case CSCore.SoundOut.PlaybackState.Playing:
                        return PlaybackState.Playing;
                    case CSCore.SoundOut.PlaybackState.Stopped:
                        return PlaybackState.Stopped;
                }
                return PlaybackState.Stopped;
            }
        }

        public float Volume { get { return _soundOut.Volume; } set { _soundOut.Volume = value; } }

        public void Init(string audioFilePath)
        {
            _soundSource = CodecFactory.Instance.GetCodec(audioFilePath);
            var wo = new WaveOut();
            foreach (var sf in wo.Device.SupportedFormats)
            {
                if (sf == _soundSource.WaveFormat) throw new Exception();
            }
            _soundOut = wo;
            _soundOut.Initialize(_soundSource);
            _soundOut.Play();
        }

        public byte[] GetHashBytes(int hashByteCount)
        {
            var ret = new byte[hashByteCount];
            _soundSource.Read(ret, 0, hashByteCount);
            _soundSource.Position = 0;
            return ret;
        }


        public void Play()
        {
            _soundOut.Play();
        }

        public void Resume()
        {
            _soundOut.Resume();
        }

        public void Stop()
        {
            _soundOut.Stop();
            _soundSource.SetPosition(TimeSpan.Zero);
        }

        public void Seek(float percent)
        {
            _soundSource.SetPosition(TimeSpan.FromMilliseconds(percent * _soundSource.GetLength().Milliseconds));
        }

        public void Pause()
        {
            _soundOut.Pause();
        }
    }
}
