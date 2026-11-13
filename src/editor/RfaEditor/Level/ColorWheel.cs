using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace RfaEditor;

/// <summary>
/// A colour wheel for a colour property (a light's colour): the hue around it, pale in the middle
/// and full at the rim, with a slider for how bright. The game's colours are red, green and blue
/// from 0 up, and more than 1 is brighter than the screen's white, so the brightness is the
/// largest of the three and the wheel picks the mix.
/// </summary>
sealed class ColorWheel : StackPanel
{
    const int Size = 180;
    readonly Canvas canvas = new() { Width = Size, Height = Size, Cursor = Cursors.Cross };
    readonly Ellipse marker = new() { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
    readonly Slider brightness = new() { Minimum = 0, Margin = new Thickness(0, 8, 0, 0), ToolTip = "How bright (1 is the screen's full brightness; lights are often brighter)" };
    readonly TextBlock brightnessText = new() { Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
    double hue, saturation;  // 0 to 1
    bool showing;

    /// <summary>Raised as the colour is picked: red, green, blue.</summary>
    public event Action<float, float, float>? Picked;

    /// <summary>Raised when a pick is finished (the mouse let go).</summary>
    public event Action? Done;

    public ColorWheel(float red, float green, float blue)
    {
        Margin = new Thickness(10);
        canvas.Children.Add(new Image { Source = WheelPicture(), Width = Size, Height = Size });
        marker.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Color = Colors.Black };
        canvas.Children.Add(marker);
        Children.Add(canvas);
        Children.Add(brightness);
        Children.Add(brightnessText);

        double most = Math.Max(red, Math.Max(green, blue));
        brightness.Maximum = Math.Max(4, Math.Ceiling(most * 2));
        showing = true;
        brightness.Value = most;
        showing = false;
        (hue, saturation) = most <= 0 ? (0, 0) : HueAndSaturation(red / most, green / most, blue / most);
        ShowMarker();

        canvas.MouseLeftButtonDown += (_, e) =>
        {
            canvas.CaptureMouse();
            PickAt(e.GetPosition(canvas));
        };
        canvas.MouseMove += (_, e) =>
        {
            if (canvas.IsMouseCaptured) PickAt(e.GetPosition(canvas));
        };
        canvas.MouseLeftButtonUp += (_, _) =>
        {
            if (!canvas.IsMouseCaptured) return;
            canvas.ReleaseMouseCapture();
            Done?.Invoke();
        };
        brightness.ValueChanged += (_, _) =>
        {
            ShowBrightness();
            if (!showing) Raise();
        };
        brightness.PreviewMouseLeftButtonUp += (_, _) => Done?.Invoke();
        if (brightness.Value <= 0) brightness.Value = 1;  // black: picking a hue should show
        ShowBrightness();
    }

    void ShowBrightness() => brightnessText.Text = $"Brightness {brightness.Value:0.##}";

    void PickAt(Point at)
    {
        double x = at.X - Size / 2.0, y = at.Y - Size / 2.0, radius = Size / 2.0;
        saturation = Math.Min(1, Math.Sqrt(x * x + y * y) / radius);
        hue = (Math.Atan2(y, x) / (2 * Math.PI) + 1) % 1;
        ShowMarker();
        Raise();
    }

    void ShowMarker()
    {
        double angle = hue * 2 * Math.PI, radius = saturation * Size / 2.0;
        Canvas.SetLeft(marker, Size / 2.0 + Math.Cos(angle) * radius - marker.Width / 2);
        Canvas.SetTop(marker, Size / 2.0 + Math.Sin(angle) * radius - marker.Height / 2);
    }

    void Raise()
    {
        (double r, double g, double b) = Mix(hue, saturation);
        double bright = brightness.Value;
        Picked?.Invoke((float)(r * bright), (float)(g * bright), (float)(b * bright));
    }

    /// <summary>The full-brightness colour of a hue and saturation (HSV with value 1).</summary>
    static (double R, double G, double B) Mix(double hue, double saturation)
    {
        double h = hue * 6, c = saturation, x = c * (1 - Math.Abs(h % 2 - 1)), m = 1 - c;
        (double r, double g, double b) = (int)Math.Floor(h) switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return (r + m, g + m, b + m);
    }

    static (double Hue, double Saturation) HueAndSaturation(double r, double g, double b)
    {
        double most = Math.Max(r, Math.Max(g, b)), least = Math.Min(r, Math.Min(g, b)), spread = most - least;
        if (most <= 0 || spread <= 0) return (0, 0);
        double h = most == r ? ((g - b) / spread + 6) % 6 : most == g ? (b - r) / spread + 2 : (r - g) / spread + 4;
        return (h / 6, spread / most);
    }

    static BitmapSource WheelPicture()
    {
        var pixels = new byte[Size * Size * 4];
        double radius = Size / 2.0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                double dx = x + 0.5 - radius, dy = y + 0.5 - radius, distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > radius) continue;
                (double r, double g, double b) = Mix((Math.Atan2(dy, dx) / (2 * Math.PI) + 1) % 1, distance / radius);
                double edge = Math.Clamp(radius - distance, 0, 1);  // a soft rim
                int at = (y * Size + x) * 4;
                pixels[at] = (byte)(b * 255 * edge);
                pixels[at + 1] = (byte)(g * 255 * edge);
                pixels[at + 2] = (byte)(r * 255 * edge);
                pixels[at + 3] = (byte)(255 * edge);
            }
        }
        var picture = BitmapSource.Create(Size, Size, 96, 96, PixelFormats.Pbgra32, null, pixels, Size * 4);
        picture.Freeze();
        return picture;
    }
}
