// NOVA Island - lightweight vector mascot placeholder.
// Replace the vector artwork with the approved NOVA sprite sheet when ready.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Effects;

namespace DynIsland
{
    public sealed class NovaMascotView : UserControl
    {
        private readonly Ellipse body;
        private readonly Ellipse leftEye;
        private readonly Ellipse rightEye;
        private readonly TextBlock status;

        public NovaMascotView()
        {
            Width = 52;
            Height = 52;

            Grid root = new Grid();
            Ellipse aura = new Ellipse();
            aura.Width = 48;
            aura.Height = 48;
            aura.Fill = new SolidColorBrush(Color.FromArgb(28, 110, 170, 255));
            root.Children.Add(aura);

            body = new Ellipse();
            body.Width = 34;
            body.Height = 34;
            body.Fill = new LinearGradientBrush(
                Color.FromRgb(126, 190, 255), Color.FromRgb(92, 112, 245), 90);
            body.Stroke = new SolidColorBrush(Color.FromRgb(190, 225, 255));
            body.StrokeThickness = 1.2;
            body.Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.28 };
            root.Children.Add(body);

            Grid face = new Grid();
            face.Width = 22;
            face.Height = 12;
            face.VerticalAlignment = VerticalAlignment.Center;
            face.HorizontalAlignment = HorizontalAlignment.Center;
            face.Margin = new Thickness(0, 2, 0, 0);

            StackPanel eyes = new StackPanel();
            eyes.Orientation = Orientation.Horizontal;
            eyes.HorizontalAlignment = HorizontalAlignment.Center;
            leftEye = MakeEye();
            rightEye = MakeEye();
            eyes.Children.Add(leftEye);
            eyes.Children.Add(rightEye);
            face.Children.Add(eyes);
            root.Children.Add(face);

            status = new TextBlock();
            status.FontSize = 8;
            status.FontWeight = FontWeights.Bold;
            status.Foreground = Brushes.White;
            status.HorizontalAlignment = HorizontalAlignment.Center;
            status.VerticalAlignment = VerticalAlignment.Bottom;
            status.Margin = new Thickness(0, 0, 0, 1);
            root.Children.Add(status);

            Content = root;
            SetState(NovaState.Idle);
        }

        private Ellipse MakeEye()
        {
            Ellipse eye = new Ellipse();
            eye.Width = 4;
            eye.Height = 5;
            eye.Margin = new Thickness(2, 0, 2, 0);
            eye.Fill = new SolidColorBrush(Color.FromRgb(18, 35, 65));
            return eye;
        }

        public void SetState(NovaState state)
        {
            status.Text = String.Empty;
            body.Opacity = 1.0;
            leftEye.Height = 5;
            rightEye.Height = 5;

            switch (state)
            {
                case NovaState.Working: status.Text = "•••"; break;
                case NovaState.Thinking:
                    status.Text = "?";
                    leftEye.Height = 3;
                    rightEye.Height = 3;
                    break;
                case NovaState.Permission: status.Text = "!"; break;
                case NovaState.Success: status.Text = "✓"; break;
                case NovaState.Error: status.Text = "×"; break;
                case NovaState.Notification: status.Text = "!"; break;
                default: break;
            }
        }
    }
}
