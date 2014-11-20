﻿using System;
using System.Collections.Generic;
using BeatDetection.Audio;
using BeatDetection.Core;
using ClipperLib;
using ColorMine.ColorSpaces;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
using Substructio.Core;
using Substructio.Core.Math;

namespace BeatDetection.Game
{
    class StageGeometry
    {
        private StageColours _colours;
        private PolarPolygon[] _polygons;
        private SegmentInformation[] _segments;
        private Color4[] _segmentColours;
        public Stage ParentStage;

        private Random _random;

        public Player Player;
        public PolarPolygon CenterPolygon;
        public PolarPolygon BackgroundPolygon;
        private int _direction = 1;

        private int _segmentIndex = 0;
        private int _colourIndex = 0;
        private int _collidedPolygonIndex;
        private int _polygonsToRemoveCount;

        public int PolygonIndex { get; private set; }
        public int PolygonCount {get { return _polygons.Length; }}

        private bool Collided
        {
            get { return _collidedPolygonIndex == PolygonIndex; }
        }

        internal StageGeometry (PolarPolygon[] polygons, SegmentInformation[] segments, Color4[] segmentColours, Random random)
        {
            _polygons = polygons;
            _segments = segments;
            _segmentColours = segmentColours;
            _random = random;
        }

        public void Update(double time)
        {
            var rotate = time * 0.5 * _direction;

            PolygonIndex += _polygonsToRemoveCount;
            _polygonsToRemoveCount = 0;

            for (int i = PolygonIndex; i < _polygons.Length; i++)
            {
                var poly = _polygons[i];
                poly.Direction = _direction;
                poly.Position.Azimuth = CenterPolygon.Position.Azimuth + rotate;
                poly.Update(time, ParentStage.Running);
                if (ParentStage.Running)
                {
                    if (poly.Destroy)
                        _polygonsToRemoveCount++;
                    else if ((poly.Position.Radius - poly.ImpactDistance) / (poly.Velocity.Radius) <
                             (poly.PulseWidthMax / poly.PulseMultiplier))
                    {
                        CenterPolygon.Pulsing = true;
                    }
                }

                //update polygon colours if they are incorrect (i.e. if it has switched to colliding)
                if (poly.EvenColour != _colours.EvenCollisionColour && poly.EvenColour != _colours.EvenOpposingColour)
                    poly.SetColour(_colours.EvenOpposingColour, _colours.EvenOutlineColour, _colours.OddOpposingColour, _colours.OddOutlineColour);
            }

            if (_polygonsToRemoveCount > 0)
            {
                var d = _random.NextDouble();
                _direction = d > 0.95 ? -_direction : _direction;
                ParentStage.Multiplier += _polygonsToRemoveCount;
            }

            UpdatePlayerOverlap();

            if (ParentStage.AI && PolygonIndex < _polygons.Length)
            {
                var t = _polygons[PolygonIndex].OpeningAngle + rotate * _direction + CenterPolygon.Position.Azimuth;
                t += MathHelper.DegreesToRadians(30);
                Player.DoAI(t);
            }

            Player.Direction = _direction;
            CenterPolygon.Direction = _direction;

            //update center polygon colour if finished colliding 
            if (CenterPolygon.EvenColour == _colours.EvenCollisionColour && !Collided)
                CenterPolygon.SetColour(_colours.EvenOpposingColour, _colours.EvenOutlineColour, _colours.OddOpposingColour, _colours.OddOutlineColour);

            Player.Position.Azimuth += rotate;
            Player.Update(time, ParentStage.AI);

            CenterPolygon.Update(time, false);
            CenterPolygon.Position.Azimuth += rotate;

            BackgroundPolygon.Position.Azimuth = CenterPolygon.Position.Azimuth + rotate;
            BackgroundPolygon.Update(time, false);

            UpdateSegments();
        }

        public void Draw(double time)
        {
            BackgroundPolygon.Draw(time);
            GL.LineWidth(3);
            ParentStage.ShaderProgram.SetUniform("in_color", _colours.EvenOpposingColour);
            for (int i = PolygonIndex; i < Math.Min(_polygons.Length, PolygonIndex + 10); i++)
            {
                _polygons[i].Draw(time, 1);
            }
            if (PolygonIndex < _polygons.Length)
            {
                ParentStage.ShaderProgram.SetUniform("in_color", _polygons[PolygonIndex].EvenColour);
                _polygons[PolygonIndex].Draw(time, 1);
            }
            CenterPolygon.Draw(time, 1);
            ParentStage.ShaderProgram.SetUniform("in_color", _colours.OddOpposingColour);
            for (int i = PolygonIndex; i < Math.Min(_polygons.Length, PolygonIndex + 10); i++)
            {
                _polygons[i].Draw(time, 2);
            }
            if (PolygonIndex < _polygons.Length)
            {
                ParentStage.ShaderProgram.SetUniform("in_color", _polygons[PolygonIndex].OddColour);
                _polygons[PolygonIndex].Draw(time, 2);
            }
            CenterPolygon.Draw(time, 2);
            ParentStage.ShaderProgram.SetUniform("in_color", Color4.White);
            Player.Draw(time);
        }

        private void UpdatePlayerOverlap()
        {
            if (PolygonIndex == _collidedPolygonIndex || PolygonIndex >= _polygons.Length)
            {
                ParentStage.Overlap = 0;
                return;
            }
            var c = new Clipper();
            c.AddPaths(_polygons[PolygonIndex].GetPolygonBounds(), PolyType.ptSubject, true);
            c.AddPath(Player.GetBounds(), PolyType.ptClip, true);

            var soln = new List<List<IntPoint>>();
            c.Execute(ClipType.ctIntersection, soln);

            ParentStage.Overlap = soln.Count > 0 ? (int)((Clipper.Area(soln[0]) / Clipper.Area(Player.GetBounds())) * 100) : 0;
            if (ParentStage.Overlap > 80)
            {
                ParentStage.Multiplier = -1;
                Player.Hits++;
                _collidedPolygonIndex = PolygonIndex;
                _polygons[_collidedPolygonIndex].SetColour(_colours.EvenCollisionColour, _colours.EvenCollisionOutlineColour, _colours.OddCollisionColour, _colours.OddCollisionOutlienColour);
                CenterPolygon.SetColour(_colours.EvenCollisionColour, _colours.EvenCollisionOutlineColour, _colours.OddCollisionColour, _colours.OddCollisionOutlienColour);
            }
        }

        private void UpdateSegments()
        {
            var seg = _segments[_segmentIndex];
            if (seg.EndTime > 0 && seg.EndTime < ParentStage.TotalTime)
            {
                _segmentIndex++;
                _colourIndex = _segments[_segmentIndex].ID - 1;

                UpdateColours();
            }   
        }

        public void UpdateColours()
        {
            var evenBackground = Utilities.Color4ToColorSpace(_segmentColours[_colourIndex]).ToRgb();
            var oddBackground = evenBackground.To<Hsl>();
            oddBackground.S += 5;
            oddBackground.L += 5;
            BackgroundPolygon.SetColour(Utilities.ColorSpaceToColor4(evenBackground),
                Utilities.ColorSpaceToColor4(evenBackground), Utilities.ColorSpaceToColor4(oddBackground),
                Utilities.ColorSpaceToColor4(oddBackground));

            var c = evenBackground;
            var d = oddBackground;

            var opp = c.To<Hsl>();
            opp.S += 30;
            opp.L += 20;
            _colours.EvenCollisionColour = Utilities.ColorSpaceToColor4(opp);
            _colours.EvenCollisionOutlineColour = Utilities.ColorSpaceToColor4(GetOutlineColour(opp));
            opp = d.To<Hsl>();
            opp.S += 30;
            opp.L += 20;
            _colours.OddCollisionColour = Utilities.ColorSpaceToColor4(opp);
            _colours.OddCollisionOutlienColour = Utilities.ColorSpaceToColor4(GetOutlineColour(opp));

            var hsl = c.To<Hsl>();
            hsl.H = MathUtilities.Normalise(hsl.H + 180, 0, 360);
            hsl.S = 50;
            c = hsl.ToRgb();
            _colours.EvenOpposingColour = Utilities.ColorSpaceToColor4(c);
            _colours.EvenOutlineColour = Utilities.ColorSpaceToColor4(GetOutlineColour(hsl));
            hsl = d.To<Hsl>();
            hsl.H = MathUtilities.Normalise(hsl.H + 180, 0, 360);
            hsl.S += 20;
            c = hsl.ToRgb();
            _colours.OddOpposingColour = Utilities.ColorSpaceToColor4(c);
            _colours.OddOutlineColour = Utilities.ColorSpaceToColor4(GetOutlineColour(hsl));

            CenterPolygon.SetColour(_colours.EvenOpposingColour, _colours.EvenOutlineColour, _colours.OddOpposingColour, _colours.OddOutlineColour);

            GL.ClearColor(_segmentColours[_colourIndex]);
        }

        private Hsl GetOutlineColour(Hsl col)
        {
            col.L += 10;
            col.S += 20;
            return col;
        }
    }

    struct StageColours
    {
        public Color4 EvenOpposingColour;
        public Color4 EvenCollisionColour;
        public Color4 EvenCollisionOutlineColour;
        public Color4 EvenOutlineColour;

        public Color4 OddOpposingColour;
        public Color4 OddCollisionColour;
        public Color4 OddOutlineColour;
        public Color4 OddCollisionOutlienColour;
    }
}
