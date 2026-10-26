using System.Runtime.InteropServices;
using System.Text;

namespace Mustang;

internal static class Program
{
    private const int FrameCount = 16;
    private const int FrameDelayMs = 45;

    private static volatile bool _stop;

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (Console.IsOutputRedirected || Console.IsInputRedirected)
        {
            Console.WriteLine(Screen.ToPlainText(Horse.RenderFrame(0, 1.6f)));
            Console.WriteLine("Run in an interactive terminal to see the mustang gallop.");
            return 0;
        }

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _stop = true;
        };

        Terminal.EnableVirtualTerminal();
        Console.CursorVisible = false;
        Console.Clear();

        try
        {
            Run();
        }
        finally
        {
            Console.Write(Ansi.Reset);
            Console.Clear();
            Console.CursorVisible = true;
            Console.WriteLine("The mustang rode off into the sunset. Bye!");
        }

        return 0;
    }

    private static void Run()
    {
        Rgb?[][,] frames = [];
        var lastWidth = -1;
        var lastHeight = -1;
        var x = 0;
        var frame = 0;
        var tick = 0;
        var output = new StringBuilder();

        while (!_stop)
        {
            while (Console.KeyAvailable)
            {
                if (Console.ReadKey(intercept: true).Key is ConsoleKey.Escape or ConsoleKey.Q)
                {
                    return;
                }
            }

            var width = Math.Max(1, Console.WindowWidth - 1);
            var height = Console.WindowHeight;
            if (width != lastWidth || height != lastHeight)
            {
                Console.Clear();
                frames = Horse.RenderFrames(FrameCount, ScaleFor(width, height));
                x = -frames[0].GetLength(1);
                lastWidth = width;
                lastHeight = height;
            }

            var sprite = frames[frame];
            var spriteWidth = sprite.GetLength(1);
            var spriteRows = sprite.GetLength(0) / 2;
            var top = Math.Max(2, (height - spriteRows - 1) / 2);

            output.Clear();
            output.Append(Ansi.MoveTo(top - 2, 0))
                  .Append(Ansi.Fg(new Rgb(140, 140, 140)))
                  .Append(Fit("  Mustang  -  press Esc, Q or Ctrl+C to stop", width))
                  .Append(Ansi.Reset);
            Screen.RenderSprite(output, sprite, top, x, width);
            Screen.RenderGround(output, top + spriteRows, width, tick, x + spriteWidth * 45 / 100, spriteWidth / 3);

            Console.Out.Write(output);
            Console.Out.Flush();

            Thread.Sleep(FrameDelayMs);

            tick++;
            frame = (frame + 1) % frames.Length;
            x += Math.Max(1, spriteWidth / 40);
            if (x > width)
            {
                x = -spriteWidth;
            }
        }
    }

    // Pixels per model unit: as large as fits, leaving room for the caption and the ground.
    private static float ScaleFor(int width, int height)
    {
        var byHeight = 2f * (height - 5) / (Horse.MaxY - Horse.MinY);
        var byWidth = width * 0.6f / (Horse.MaxX - Horse.MinX);
        return MathF.Round(Math.Clamp(MathF.Min(byHeight, byWidth), 1.2f, 4f) * 4) / 4;
    }

    private static string Fit(string text, int width) =>
        text.Length >= width ? text[..width] : text.PadRight(width);
}

internal static class Ansi
{
    public const string Reset = "\e[0m";
    public const string DefaultBackground = "\e[49m";

    public static string MoveTo(int row, int col) => $"\e[{row + 1};{col + 1}H";

    public static string Fg(Rgb c) => $"\e[38;2;{c.R};{c.G};{c.B}m";

    public static string Bg(Rgb c) => $"\e[48;2;{c.R};{c.G};{c.B}m";
}

internal static class Screen
{
    private static readonly Rgb Grass = new(78, 130, 52);
    private static readonly Rgb GrassLight = new(112, 160, 66);
    private static readonly Rgb Soil = new(92, 112, 48);
    private static readonly Rgb Shadow = new(48, 80, 34);

    // Draws the sprite as truecolor half blocks (two pixels per cell), clipped to the window.
    // Every column is written, so the previous frame never needs clearing.
    public static void RenderSprite(StringBuilder output, Rgb?[,] image, int top, int left, int screenWidth)
    {
        var rows = image.GetLength(0) / 2;
        var width = image.GetLength(1);
        for (var row = 0; row < rows; row++)
        {
            output.Append(Ansi.MoveTo(top + row, 0)).Append(Ansi.Reset);
            Rgb? fg = null;
            Rgb? bg = null;
            for (var col = 0; col < screenWidth; col++)
            {
                var x = col - left;
                var upper = x >= 0 && x < width ? image[row * 2, x] : null;
                var lower = x >= 0 && x < width ? image[row * 2 + 1, x] : null;
                AppendCell(output, upper, lower, ref fg, ref bg);
            }

            output.Append(Ansi.Reset);
        }
    }

    private static void AppendCell(StringBuilder output, Rgb? upper, Rgb? lower, ref Rgb? fg, ref Rgb? bg)
    {
        var (glyph, wantFg, wantBg) = (upper, lower) switch
        {
            (null, null) => (' ', fg, (Rgb?)null),
            (not null, null) => ('▀', upper, null),
            (null, not null) => ('▄', lower, null),
            _ => ('▀', upper, lower),
        };

        if (wantFg != fg && wantFg is { } f)
        {
            output.Append(Ansi.Fg(f));
            fg = wantFg;
        }

        if (wantBg != bg)
        {
            output.Append(wantBg is { } b ? Ansi.Bg(b) : Ansi.DefaultBackground);
            bg = wantBg;
        }

        output.Append(glyph);
    }

    // A strip of grass scrolling backwards, with the horse's shadow under it.
    public static void RenderGround(StringBuilder output, int row, int width, int tick, int shadowCenter, int shadowHalfWidth)
    {
        output.Append(Ansi.MoveTo(row, 0));
        for (var col = 0; col < width; col++)
        {
            var n = (col + tick * 2) % 23;
            var top = n is 3 or 11 or 17 ? GrassLight : Grass;
            var inShadow = Math.Abs(col - shadowCenter) < shadowHalfWidth;
            output.Append(Ansi.Fg(inShadow ? Shadow : top)).Append(Ansi.Bg(Soil)).Append('▀');
        }

        output.Append(Ansi.Reset);
    }

    public static string ToPlainText(Rgb?[,] image)
    {
        var text = new StringBuilder();
        for (var row = 0; row < image.GetLength(0) / 2; row++)
        {
            var line = new StringBuilder();
            for (var x = 0; x < image.GetLength(1); x++)
            {
                line.Append((image[row * 2, x].HasValue, image[row * 2 + 1, x].HasValue) switch
                {
                    (true, true) => '█',
                    (true, false) => '▀',
                    (false, true) => '▄',
                    _ => ' ',
                });
            }

            text.AppendLine(line.ToString().TrimEnd());
        }

        return text.ToString();
    }
}

internal static class Terminal
{
    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    // Windows conhost needs VT processing switched on before it understands ANSI colors.
    public static void EnableVirtualTerminal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = GetStdHandle(StdOutputHandle);
        if (GetConsoleMode(handle, out var mode))
        {
            SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
