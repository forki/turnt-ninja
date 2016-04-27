﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BeatDetection.Audio;
using BeatDetection.Core;
using BeatDetection.Game;
using OpenTK;
using OpenTK.Graphics;
using QuickFont;
using Substructio.Core.Math;
using Substructio.Graphics.OpenGL;
using Substructio.GUI;
using OpenTK.Graphics.OpenGL4;
using Substructio.Core;
using System.Diagnostics;

namespace BeatDetection.GUI
{
    class LoadingScene : Scene
    {
        private float _audioCorrection;
        private float _maxAudioVolume;

        private Task _loadTask;
        private Stage _stage;
        private Player _player;
        private PolarPolygon _centerPolygon;
        private ProcessedText _loadingText;
        private ProcessedText _songText;
        private Vector3 _loadingTextPosition;
        private QFont _loadingFont;
        private QFontDrawing _loadingFontDrawing;
        private QFontRenderOptions _loadingFontRenderOptions;
        private ShaderProgram _shaderProgram;
        private bool usePlaylist = false;
        private List<string> _files = new List<string>();

        private Song _song;

        private string _loadingStatus = "";

        public LoadingScene(float audioCorrection, float maxAudioVolume, PolarPolygon centerPolygon, Player player, ShaderProgram shaderProgram, Song song)
        {
            Exclusive = true;
            _audioCorrection = audioCorrection;
            _maxAudioVolume = maxAudioVolume;
            _centerPolygon = centerPolygon;
            _player = player;
            _shaderProgram = shaderProgram;

            _song = song;
        }

        public override void Load()
        {
            SceneManager.GameWindow.Cursor = MouseCursor.Empty;

            _stage = new Stage(this.SceneManager);
            _stage.ShaderProgram = _shaderProgram;

            _loadingFontRenderOptions = new QFontRenderOptions();
            _loadingFontRenderOptions.DropShadowActive = true;
            _loadingFont = new QFont(SceneManager.FontPath, 30, new QFontBuilderConfiguration(true), FontStyle.Regular);
            _loadingFontDrawing = new QFontDrawing();
            _loadingFontDrawing.ProjectionMatrix = SceneManager.ScreenCamera.ScreenProjectionMatrix;
            _loadingText = QFontDrawingPrimitive.ProcessText(_loadingFont, _loadingFontRenderOptions, "Loading", new SizeF(200, -1), QFontAlignment.Centre);
            _loadingTextPosition = CalculateTextPosition(new Vector3((float)SceneManager.GameWindow.Width/ 2, SceneManager.GameWindow.Height/ 2, 0f), _loadingText);

            _songText = QFontDrawingPrimitive.ProcessText(_loadingFont, _loadingFontRenderOptions, _song.SongBase.Identifier, new SizeF(SceneManager.GameWindow.Width - 40, -1), QFontAlignment.Centre);

            var dOptions = new DifficultyOptions(600f, 0.15f, 0.35f, 1.5f);

            var progress = new Progress<string>(status =>
            {
                _loadingStatus = status;
            });
            _loadTask = Task.Factory.StartNew(() => _stage.LoadAsync(_song, _audioCorrection, _maxAudioVolume, progress, _centerPolygon, _player, dOptions));

            Loaded = true;
        }

        public override void CallBack(GUICallbackEventArgs e)
        {
            throw new NotImplementedException();
        }

        public override void Resize(EventArgs e)
        {
            _loadingText = QFontDrawingPrimitive.ProcessText(_loadingFont, _loadingFontRenderOptions, "Loading", new SizeF(1000, -1), QFontAlignment.Centre);
            _loadingFontDrawing.ProjectionMatrix = SceneManager.ScreenCamera.ScreenProjectionMatrix;
            _loadingTextPosition = CalculateTextPosition(new Vector3(SceneManager.ScreenCamera.PreferredWidth / 2, SceneManager.ScreenCamera.PreferredHeight / 2, 0f), _loadingText);
        }

        public override void Update(double time, bool focused = false)
        {
            if (_loadTask.Exception != null)
            {
                Trace.WriteLine(_loadTask.Exception.Message);
                throw _loadTask.Exception;
            }
            if (_loadTask.IsCompleted)
            {
                SceneManager.RemoveScene(this);
                SceneManager.AddScene(new GameScene(_stage){ShaderProgram = _shaderProgram, UsingPlaylist = usePlaylist, PlaylistFiles = _files}, this);
            }

            _player.Update(time);
            _centerPolygon.Update(time, false);
            //SceneManager.ScreenCamera.TargetScale += new Vector2(0.1f, 0.1f);
        }

        public override void Draw(double time)
        {
            GL.Disable(EnableCap.CullFace);
            _shaderProgram.Bind();
            _shaderProgram.SetUniform("mvp", SceneManager.ScreenCamera.ModelViewProjection);
            _shaderProgram.SetUniform("in_color", Color4.White);

            //Draw the player
            _player.Draw(time);

            //Draw the center polygon
            _centerPolygon.Draw(time);

            _shaderProgram.SetUniform("in_color", Color4.Black);
            _centerPolygon.DrawOutline(time);

            //Cleanup the program
            _shaderProgram.UnBind();

            _loadingFontDrawing.DrawingPrimitives.Clear();
            float yOffset = 0;
            yOffset += _loadingFontDrawing.Print(_loadingFont, _loadingText, _loadingTextPosition).Height;
            yOffset = MathHelper.Clamp(yOffset + 200 - 50*SceneManager.ScreenCamera.Scale.Y, yOffset, SceneManager.GameWindow.Height*0.5f); 
            var pos = new Vector3(0, -yOffset, 0);
            yOffset += _loadingFontDrawing.Print(_loadingFont, _songText, pos).Height;
            yOffset += _loadingFontDrawing.Print(_loadingFont, _loadingStatus, new Vector3(0, -yOffset, 0), QFontAlignment.Centre).Height;
            _loadingFontDrawing.RefreshBuffers();
            _loadingFontDrawing.Draw();
        }

        public override void Dispose()
        {
            if (_loadingFont != null)
            {
                _loadingFont.Dispose();
                _loadingFontDrawing.Dispose();
            }
        }

        private Vector3 CalculateTextPosition(Vector3 center, ProcessedText text)
        {
            var size = _loadingFont.Measure(text);
            return new Vector3(0, size.Height/2, 0f);
        }
    }
}
