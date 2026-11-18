// A little secret: tap the island logo in Settings seven times. The island throws a party —
// a rainbow glow chases around it, it bounces, confetti bursts out of it — and says hi.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DynIsland
{
    partial class IslandWindow
    {
        bool partyOn; Canvas confettiLayer;
        static readonly Random partyRnd = new Random();
        public static readonly Color[] Rainbow = { Color.FromRgb(0xff, 0x5f, 0x6d), Color.FromRgb(0xff, 0xa7, 0x4f), Color.FromRgb(0xff, 0xe0, 0x66), Color.FromRgb(0x7a, 0xe5, 0x82), Color.FromRgb(0x5a, 0xc8, 0xfa), Color.FromRgb(0xa7, 0x8b, 0xfa), Color.FromRgb(0xf4, 0x72, 0xb6) };

        public void Party()
        {
            if (partyOn) return;
            partyOn = true;
            bool still = K.AnimScale <= 0;

            // rainbow glow chasing through the colours
            var glowFx = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 0, Opacity = 0, Color = Rainbow[0], RenderingBias = RenderingBias.Performance };
            fxPath.Effect = glowFx;
            var hue = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1400), RepeatBehavior = new RepeatBehavior(3) };
            for (int i = 0; i <= Rainbow.Length; i++)
                hue.KeyFrames.Add(new LinearColorKeyFrame(Rainbow[i % Rainbow.Length], KeyTime.FromPercent(i / (double)Rainbow.Length)));
            glowFx.BeginAnimation(DropShadowEffect.ColorProperty, hue);
            glowFx.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, 0.95, TimeSpan.FromMilliseconds(300)));

            // a happy bounce
            if (!still) { ss.Vel += 3.2; Kick(); }

            Confetti(56);
            string n = (S.UserName ?? "").Trim();
            SystemEvent("", K.Solid(Rainbow[5]), "You found the secret" + (n.Length > 0 ? ", " + n : "") + "!", "Thanks for poking around · made with ♥ for your desktop", Pri.System, 4200, "party", null);

            var end = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3900) };
            end.Tick += delegate
            {
                end.Stop();
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(500));
                fade.Completed += delegate
                {
                    if (fxPath.Effect == glowFx) fxPath.Effect = null;
                    partyOn = false;
                    UpdateEq();   // gives the playing glow / shadow back if they're on
                };
                glowFx.BeginAnimation(DropShadowEffect.OpacityProperty, fade);
            };
            end.Start();
        }

        // pieces burst up out of the island's bottom edge, then tumble down and fade
        void Confetti(int count)
        {
            if (K.AnimScale <= 0) return;
            if (confettiLayer == null)
            {
                confettiLayer = new Canvas { IsHitTestVisible = false };
                root.Children.Add(confettiLayer);
            }
            double ox = root.ActualWidth / 2, oy = Math.Max(0, sy.Value) + Math.Max(PH, sh.Value) * UiScale - 4;
            for (int i = 0; i < count; i++)
            {
                Shape piece = partyRnd.Next(3) == 0
                    ? (Shape)new Ellipse { Width = 7, Height = 7 }
                    : new Rectangle { Width = 5 + partyRnd.Next(4), Height = 9 + partyRnd.Next(5), RadiusX = 1.5, RadiusY = 1.5 };
                piece.Fill = K.Solid(Rainbow[partyRnd.Next(Rainbow.Length)]);
                Canvas.SetLeft(piece, ox - 3 + partyRnd.Next(-30, 30)); Canvas.SetTop(piece, oy);
                var tt = new TranslateTransform(); var rt = new RotateTransform(partyRnd.Next(360));
                var tg = new TransformGroup(); tg.Children.Add(rt); tg.Children.Add(tt);
                piece.RenderTransform = tg; piece.RenderTransformOrigin = new Point(0.5, 0.5);
                confettiLayer.Children.Add(piece);

                double dur = 1500 + partyRnd.Next(900), dx = partyRnd.Next(-300, 300), up = 40 + partyRnd.Next(110), down = 180 + partyRnd.Next(200);
                var x = new DoubleAnimation(0, dx, TimeSpan.FromMilliseconds(dur)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var y = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(dur) };
                y.KeyFrames.Add(new EasingDoubleKeyFrame(-up, KeyTime.FromPercent(0.28), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                y.KeyFrames.Add(new EasingDoubleKeyFrame(down, KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
                var r = new DoubleAnimation(rt.Angle, rt.Angle + partyRnd.Next(-900, 900), TimeSpan.FromMilliseconds(dur));
                var o = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(dur) };
                o.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0)));
                o.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.7)));
                o.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                var p = piece;
                o.Completed += delegate { confettiLayer.Children.Remove(p); };
                var delay = TimeSpan.FromMilliseconds(partyRnd.Next(120));
                x.BeginTime = y.BeginTime = r.BeginTime = o.BeginTime = delay;
                piece.Opacity = 0;
                tt.BeginAnimation(TranslateTransform.XProperty, x);
                tt.BeginAnimation(TranslateTransform.YProperty, y);
                rt.BeginAnimation(RotateTransform.AngleProperty, r);
                piece.BeginAnimation(OpacityProperty, o);
            }
        }
    }
}
