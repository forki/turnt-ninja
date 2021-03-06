﻿using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TurntNinja.Audio;
using TurntNinja.Core;
using TurntNinja.Generation;
using OpenTK;
using OpenTK.Input;
using QuickFont;
using QuickFont.Configuration;
using Substructio.Core;
using Substructio.Graphics.OpenGL;
using Substructio.GUI;
using OnsetDetection;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Linq;
using System.Net;
using CSCore;
using System.Globalization;
using CSCore.SoundOut;

namespace TurntNinja.Game
{
    internal class Stage : IDisposable
    {
        public double TotalTime { get; private set; }
        public double EndTime { get; private set; }
        private AudioFeatures _audioFeatures;

        private DifficultyOptions _difficultyOptions;

        private Random _random;

        public double Overlap { get; set; }

        private double _warmupTime = 2.0f;
        private double _elapsedWarmupTime;


        private double _easeInTime = 2.0f;
        public bool Running;
        public bool Ended;

        public bool AI { get; private set; }

        public ShaderProgram ShaderProgram { get; set; }
        public SceneManager SceneManager { get; set; }

        public GameFont MultiplierFont;
        public QFontDrawing MultiplierFontDrawing;
        public QFontDrawing ScoreFontDrawing;
        private string _centerText = "";

        public bool Loaded { get; private set; }

        public int Hits
        {
            get { return StageGeometry.Player.Hits; }
        }

        public int Multiplier { get; set; }

        public int CurrentPolygon
        {
            get {return StageGeometry.CurrentOnset;}
        }

        public int PolygonCount
        {
            get { return StageGeometry.OnsetCount; }
        }

        public float ScoreMultiplier
        {
            get { return _difficultyOptions.GetScoreMultiplier(); }
        }

        public bool FinishedEaseIn { get; private set; }
        public Song CurrentSong { get; private set; }
        public DifficultyLevels CurrentDifficulty { get; private set; }

        public StageGeometry StageGeometry;
        public StageAudio _stageAudio;

        private const float WADSWORTH = 0.30f;

        public Stage(SceneManager sceneManager)
        {
            SceneManager = sceneManager;

            MultiplierFont = SceneManager.GameFontLibrary.GetFirstOrDefault(GameFontType.Menu);
            MultiplierFontDrawing = new QFontDrawing();
            MultiplierFontDrawing.ProjectionMatrix = SceneManager.ScreenCamera.ScreenProjectionMatrix;

            ScoreFontDrawing = new QFontDrawing();
            ScoreFontDrawing.ProjectionMatrix = SceneManager.ScreenCamera.ScreenProjectionMatrix;

            _stageAudio = new StageAudio();
        }

        public void LoadAsync(Song song, float audioCorrection, float maxAudioVolume, IProgress<string> progress, PolarPolygon centerPolygon, Player player, DifficultyOptions difficultyOptions, DifficultyLevels difficultyLevel)
        {
            progress.Report("Loading audio");

            if (!song.SongAudioLoaded)
                song.LoadSongAudio();

            song.SongAudio.Position = 0;

            var tempStream = new MemoryStream();
            song.SongAudio.WriteToStream(tempStream);
            tempStream.Position = 0;

            var sourceStream = new MemoryStream();
            tempStream.CopyTo(sourceStream);
            tempStream.Position = sourceStream.Position = 0;

            IWaveSource detectionSource = new CSCore.Codecs.RAW.RawDataReader(tempStream, song.SongAudio.WaveFormat);

            IWaveSource songSource = new CSCore.Codecs.RAW.RawDataReader(sourceStream, song.SongAudio.WaveFormat);
            _stageAudio.Load(songSource);

            _stageAudio.MaxVolume = maxAudioVolume;
            _random = new Random(_stageAudio.AudioHashCode);

            _stageAudio.Volume = 0f;
            _stageAudio.Seek(WADSWORTH);
            _stageAudio.Play();

            _stageAudio.FadeIn(1000, _stageAudio.MaxVolume * 0.5f, 0.01f, FadeEndAction.Nothing);

            LoadAudioFeatures(detectionSource, audioCorrection, progress, song);

            progress.Report("Building stage geometry");
            //Apply difficulty options to builder options
            var bOptions = new GeometryBuilderOptions(ShaderProgram);
            bOptions.ApplyDifficulty(difficultyOptions);
            _difficultyOptions = difficultyOptions;

            //Build stage geometry
            StageGeometry = new StageGeometryBuilder().Build(_audioFeatures, _random, bOptions);
            StageGeometry.ParentStage = this;
            StageGeometry.Player = player;
            StageGeometry.CenterPolygon = centerPolygon;
            StageGeometry.RotationSpeed = _difficultyOptions.RotationSpeed;
            StageGeometry.CurrentColourMode = (ColourMode)ServiceLocator.Settings["ColourMode"];

            progress.Report("Load complete");
            Thread.Sleep(1000);

            _stageAudio.CancelAudioFades();
            _stageAudio.FadeOut(500, 0.0f, 0.01f, FadeEndAction.Stop);

            CurrentSong = song;
            CurrentDifficulty = difficultyLevel;

            song.SongAudioLoaded = true;

            Loaded = true;
        }

        private void LoadAudioFeatures(CSCore.IWaveSource audioSource, float correction, IProgress<string> progress, Song s)
        {
            var options = DetectorOptions.Default;
            options.MinimumTimeDelta = 7.5f;
            options.ActivationThreshold = (float)SceneManager.GameSettings["OnsetActivationThreshold"];
            options.AdaptiveWhitening = (bool)SceneManager.GameSettings["OnsetAdaptiveWhitening"];
            options.Online = (bool)SceneManager.GameSettings["OnsetOnline"];
            options.SlicePaddingLength = (float)SceneManager.GameSettings["OnsetSlicePaddingLength"];
            options.SliceLength = (float)SceneManager.GameSettings["OnsetSliceLength"];
            _audioFeatures = new AudioFeatures(options, SceneManager.Directories["ProcessedSongs"].FullName, correction + (float)_easeInTime, progress);

            progress.Report("Extracting audio features");
            _audioFeatures.Extract(audioSource, s);
        }

        public void Update(double time)
        {
            if (!Running && !Ended)
            {
                SceneManager.ScreenCamera.TargetScale = new Vector2(1.3f);
                _elapsedWarmupTime += time;
                _centerText = (Math.Ceiling(_easeInTime + _warmupTime - _elapsedWarmupTime)).ToString();
                if (_elapsedWarmupTime > _warmupTime)
                {
                    Running = true;
                    time = _elapsedWarmupTime - _warmupTime;
                }
            }

            if (Running || Ended)
            {
                TotalTime += time;

                if (Running)
                {
                    if (StageGeometry.CurrentOnset == StageGeometry.OnsetCount && _stageAudio.IsStopped)
                    {
                        EndTime = TotalTime;
                        Ended = true;
                        Running = false;
                    }
                    _centerText = string.Format("{0}x", Multiplier == -1 ? 0 : Multiplier);

                    if (!FinishedEaseIn)
                    {
                        _centerText = (Math.Ceiling(_easeInTime - TotalTime)).ToString();
                        if (TotalTime > _easeInTime)
                        {
                            _stageAudio.Volume = _stageAudio.MaxVolume;
                            _stageAudio.Play();
                            FinishedEaseIn = true;
                        }
                    }
                }
            }

            if (StageGeometry.CurrentOnset < StageGeometry.OnsetCount)
            {
                SceneManager.ScreenCamera.TargetScale =
                    new Vector2(0.9f*
                                (0.80f +
                                 Math.Min(1,
                                     ((StageGeometry.Onsets.BeatFrequencies[StageGeometry.CurrentOnset] - StageGeometry.Onsets.MinBeatFrequency)/(StageGeometry.Onsets.MaxBeatFrequency - StageGeometry.Onsets.MinBeatFrequency))*
                                     0.5f)));
                if (StageGeometry.Player.IsSlow)
                    SceneManager.ScreenCamera.TargetScale *= 0.1f;
                SceneManager.ScreenCamera.ScaleChangeMultiplier = Math.Min(StageGeometry.Onsets.BeatFrequencies[StageGeometry.CurrentOnset], 2)*2;
            }

            if (!InputSystem.CurrentKeys.Contains(Key.F3))
                StageGeometry.Update(time);

            if (InputSystem.NewKeys.Contains(Key.F2)) AI = !AI;

            //Scale multiplier font with beat
            MultiplierFontDrawing.ProjectionMatrix = Matrix4.Mult(Matrix4.CreateScale((float)(0.75 + 0.24f * StageGeometry.CenterPolygon.PulseWidth / StageGeometry.CenterPolygon.PulseWidthMax)), SceneManager.ScreenCamera.ScreenProjectionMatrix);
        }

        public void Draw(double time)
        {
            StageGeometry.Draw(time);

            MultiplierFontDrawing.DrawingPrimitives.Clear();
            MultiplierFontDrawing.Print(MultiplierFont.Font, _centerText, new Vector3(0, MultiplierFont.Font.Measure("0", QFontAlignment.Centre).Height * 0.5f, 0),
                QFontAlignment.Centre, (Color?)StageGeometry.TextColour);
            MultiplierFontDrawing.RefreshBuffers();
            MultiplierFontDrawing.Draw();

            ScoreFontDrawing.DrawingPrimitives.Clear();
            ScoreFontDrawing.Print(MultiplierFont.Font, StageGeometry.Player.Score.ToString("N0", CultureInfo.CurrentCulture), new Vector3(-SceneManager.Width / 2 + 20, SceneManager.Height / 2 - 10, 0),
                QFontAlignment.Left, (Color?)StageGeometry.TextColour);
            ScoreFontDrawing.RefreshBuffers();
            ScoreFontDrawing.Draw();
        }

        internal void Initialise()
        {
            StageGeometry.UpdateColours(0);
            StageGeometry._p2.ShaderProgram = ShaderProgram;
        }

        public void Dispose()
        {
            MultiplierFontDrawing.Dispose();
            ScoreFontDrawing.Dispose();
            StageGeometry.Dispose();
        }

        public void Reset(bool resetPlayerScore)
        {
            _stageAudio.FadeOut(500, 0, 0.01f, FadeEndAction.Stop).ContinueWith((t) => _stageAudio.Dispose());
            StageGeometry.CenterPolygon.Position.Azimuth = 0;
            if (resetPlayerScore) StageGeometry.Player.Reset();
        }
    }
}
