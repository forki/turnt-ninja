﻿
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using BeatDetection.Core.Settings;
using BeatDetection.Game;
using BeatDetection.GUI;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Input;
using QuickFont;
using Substructio.Core;
using Substructio.Core.Settings;
using Substructio.GUI;
using Substructio.IO;

namespace BeatDetection
{
    /// <summary>
    /// Demonstrates the GameWindow class.
    /// </summary>
    public sealed class GameController : GameWindow
    {

        private SceneManager _gameSceneManager;
        private const float prefWidth = 1920;
        private const float prefHeight = 1080;

        private string fontPath = "";
        float correction = 0.0f;

        private Stopwatch _watch;

        private double _lag = 0.0;
        private double _dt = 16.0/1000;

        private Stage _stage;

        private IGameSettings _gameSettings;
        private DirectoryHandler _directoryHandler;
        private static CrashReporter _crashReporter;

        public GameController(IGameSettings gameSettings, int rX, int rY, GraphicsMode graphicsMode, DirectoryHandler directoryHandler)
            : base(rX, rY, graphicsMode)
        {
            KeyDown += Keyboard_KeyDown;
            this.VSync = (bool) gameSettings["VSync"] ? VSyncMode.On : VSyncMode.Off;
            this.WindowState = (WindowState) gameSettings["WindowState"];
            _gameSettings = gameSettings;
            _directoryHandler = directoryHandler;
        }

        #region Keyboard_KeyDown

        /// <summary>
        /// Occurs when a key is pressed.
        /// </summary>
        /// <param name="sender">The KeyboardDevice which generated this event.</param>
        /// <param name="e">The key that was pressed.</param>
        void Keyboard_KeyDown(object sender, KeyboardKeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                _gameSettings["WindowState"] = WindowState = WindowState == WindowState.Fullscreen ? WindowState.Normal : WindowState.Fullscreen;
            }
        }

        #endregion

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (Focused)
                InputSystem.KeyPressed(e);
            base.OnKeyPress(e);
        }

        #region OnLoad

        /// <summary>
        /// Setup OpenGL and load resources here.
        /// </summary>
        /// <param name="e">Not used.</param>
        protected override void OnLoad(EventArgs e)
        {
            //fontPath = Path.Combine(_directoryHandler["Fonts"].FullName, "./Chamgagne Limousines/Champagne & Limousines Italic.ttf");
            fontPath = Path.Combine(_directoryHandler["Fonts"].FullName, "./Ostrich Sans/OstrichSans-Black.otf");

            var gameCamera = new Camera(prefWidth, prefHeight, this.Width, this.Height, this.Mouse);
            gameCamera.CameraBounds = gameCamera.OriginalBounds = new Polygon(new Vector2(-prefWidth * 10, -prefHeight * 10), (int)prefWidth * 20, (int) (prefHeight * 20));
            var gameFont = new QFont(fontPath, 18, new QFontBuilderConfiguration(), FontStyle.Regular);
            _gameSceneManager = new SceneManager(this, gameCamera, gameFont, fontPath, _directoryHandler, _gameSettings);
            _gameSceneManager.AddScene(new MenuScene(), null);

            Keyboard.KeyDown += (o, args) => InputSystem.KeyDown(args);
            Keyboard.KeyUp += (o, args) => InputSystem.KeyUp(args);
            Mouse.ButtonDown += (o, args) => InputSystem.MouseDown(args);
            Mouse.ButtonUp += (o, args) => InputSystem.MouseUp(args);
            Mouse.WheelChanged += (o, args) => InputSystem.MouseWheelChanged(args);
            Mouse.Move += (o, args) => InputSystem.MouseMoved(args);

            GL.ClearColor(Color.CornflowerBlue);

            _watch = new Stopwatch();
        }

        #endregion

        #region OnResize

        /// <summary>
        /// Respond to resize events here.
        /// </summary>
        /// <param name="e">Contains information on the new GameWindow size.</param>
        /// <remarks>There is no need to call the base implementation.</remarks>
        protected override void OnResize(EventArgs e)
        {
            GL.Viewport(0, 0, Width, Height);

            _gameSceneManager.Resize(e);
        }


        #endregion

        #region OnUpdateFrame

        /// <summary>
        /// Add your game logic here.
        /// </summary>
        /// <param name="e">Contains timing information.</param>
        /// <remarks>There is no need to call the base implementation.</remarks>
        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            _lag += e.Time;
            while (_lag >= _dt)
            {
                _gameSceneManager.Update(_dt);

                //only update input system once per frame!
                InputSystem.Update(this.Focused, _dt);

                _lag -= _dt;
            }
        }

        #endregion

        #region OnRenderFrame

        /// <summary>
        /// Add your game rendering code here.
        /// </summary>
        /// <param name="e">Contains timing information.</param>
        /// <remarks>There is no need to call the base implementation.</remarks>
        protected override void OnRenderFrame(FrameEventArgs e)
        {

            GL.Clear(ClearBufferMask.ColorBufferBit);

            _gameSceneManager.Draw(e.Time);

            this.SwapBuffers();
        }

        protected override void OnUnload(EventArgs e)
        {
            _gameSceneManager.Dispose();
            _gameSettings.Save();
            base.OnUnload(e);
        }

        #endregion

        [STAThread]
        public static void Main(string[] args)
        {
            //initialise directory handler
            var directoryHandler = new DirectoryHandler();
            directoryHandler.AddPath("Resources", @"..\..\Resources");
            directoryHandler.AddPath("Fonts", Path.Combine(directoryHandler["Resources"].FullName, @"Fonts"));
            directoryHandler.AddPath("Shaders", Path.Combine(directoryHandler["Resources"].FullName, @"Shaders"));
            directoryHandler.AddPath("Images", Path.Combine(directoryHandler["Resources"].FullName, @"Images"));
            directoryHandler.AddPath("Crash", @"..\..\CrashLogs");

            if (!Debugger.IsAttached)
            {
                //initialise crash reporter
                _crashReporter = new CrashReporter(directoryHandler["Crash"].FullName);

                //attach exception handlers
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            }


            IGameSettings gameSettings = new PropertySettings();
            gameSettings.Load();

            int rX = (int)gameSettings["ResolutionX"];
            int rY = (int)gameSettings["ResolutionY"];
            int FSAASamples = (int)gameSettings["AntiAliasingSamples"];
            GraphicsMode graphicsMode = new GraphicsMode(32, 24, 8, FSAASamples);

            using (GameController game = new GameController(gameSettings, rX, rY, graphicsMode, directoryHandler))
            {
                game.Title = "Codename: turnt-ninja";
                game.Run();
            }
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                Exception ex = (Exception)e.ExceptionObject;
                _crashReporter.LogError(ex);
            }
            finally
            {
                System.Environment.Exit(-1);
            }
        }

        public static string AssemblyDirectory
        {
            get
            {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                UriBuilder uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
            }
        }
    }
}