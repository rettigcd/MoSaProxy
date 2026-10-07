using System.Runtime.CompilerServices;

namespace MoSaProxy;

/// <summary>
/// Writes text with colours, one whole message at a time (so messages from different threads never mix).
/// <code>console.WriteLine($"{Fg.Green}OK{Fg.Restore} {url} took {ms,6:0.0} ms");</code>
/// <para>
/// Created with no argument it writes to the real console, in colour (unless output is redirected).
/// Created with a <see cref="TextWriter"/> it writes only the text, with the colour markers left out,
/// which is what tests use to check what was printed.
/// </para>
/// </summary>
public sealed class ColorConsole {
	// One lock for everything: the real console is shared by every instance, and writers handed to us are not thread-safe.
	static readonly object Lock = new();

	readonly TextWriter? _output;

	/// <summary>The real console.</summary>
	public static ColorConsole Default { get; } = new();

	/// <param name="output">Where to write plain text instead of the real console; null means the real console in colour.</param>
	public ColorConsole(TextWriter? output = null) => _output = output;

	public void Write(ConsoleInterpolatedStringHandler handler) => Output(handler, false);
	public void WriteLine(ConsoleInterpolatedStringHandler handler) => Output(handler, true);

	// a plain string (no colours, no interpolation holes); interpolated strings go to the handler versions above
	public void Write(string text) => Output(new ConsoleInterpolatedStringHandler(text), false);
	public void WriteLine(string text) => Output(new ConsoleInterpolatedStringHandler(text), true);

	void Output(ConsoleInterpolatedStringHandler handler, bool newLine) {
		lock (Lock) {
			var writer = _output ?? Console.Out;
			handler.WriteTo(writer, useColors: _output is null && !Console.IsOutputRedirected);
			if (newLine) writer.WriteLine();
		}
	}
}

/// <summary>Shorthand for <see cref="ColorConsole.Default"/>, the real console. Code that tests need to observe should take a <see cref="ColorConsole"/> instead.</summary>
public static class ConsoleEx {
	public static void Write(ConsoleInterpolatedStringHandler handler) => ColorConsole.Default.Write(handler);
	public static void WriteLine(ConsoleInterpolatedStringHandler handler) => ColorConsole.Default.WriteLine(handler);
	public static void Write(string text) => ColorConsole.Default.Write(text);
	public static void WriteLine(string text) => ColorConsole.Default.WriteLine(text);
}


// ============================================================
// Markers
// ============================================================

public readonly record struct ForegroundColor(ConsoleColor Color);
public readonly record struct BackgroundColor(ConsoleColor Color);

public readonly record struct RestoreForeground;
public readonly record struct RestoreBackground;


// ============================================================
// Foreground
// ============================================================

public static class Fg
{
	public static ForegroundColor Black       => new(ConsoleColor.Black);
	public static ForegroundColor DarkBlue    => new(ConsoleColor.DarkBlue);
	public static ForegroundColor DarkGreen   => new(ConsoleColor.DarkGreen);
	public static ForegroundColor DarkCyan    => new(ConsoleColor.DarkCyan);
	public static ForegroundColor DarkRed     => new(ConsoleColor.DarkRed);
	public static ForegroundColor DarkMagenta => new(ConsoleColor.DarkMagenta);
	public static ForegroundColor DarkYellow  => new(ConsoleColor.DarkYellow);
	public static ForegroundColor Gray        => new(ConsoleColor.Gray);
	public static ForegroundColor DarkGray    => new(ConsoleColor.DarkGray);
	public static ForegroundColor Blue        => new(ConsoleColor.Blue);
	public static ForegroundColor Green       => new(ConsoleColor.Green);
	public static ForegroundColor Cyan        => new(ConsoleColor.Cyan);
	public static ForegroundColor Red         => new(ConsoleColor.Red);
	public static ForegroundColor Magenta     => new(ConsoleColor.Magenta);
	public static ForegroundColor Yellow      => new(ConsoleColor.Yellow);
	public static ForegroundColor White       => new(ConsoleColor.White);

	public static RestoreForeground Restore => new();
}


// ============================================================
// Background
// ============================================================

public static class Bg
{
	public static BackgroundColor Black       => new(ConsoleColor.Black);
	public static BackgroundColor DarkBlue    => new(ConsoleColor.DarkBlue);
	public static BackgroundColor DarkGreen   => new(ConsoleColor.DarkGreen);
	public static BackgroundColor DarkCyan    => new(ConsoleColor.DarkCyan);
	public static BackgroundColor DarkRed     => new(ConsoleColor.DarkRed);
	public static BackgroundColor DarkMagenta => new(ConsoleColor.DarkMagenta);
	public static BackgroundColor DarkYellow  => new(ConsoleColor.DarkYellow);
	public static BackgroundColor Gray        => new(ConsoleColor.Gray);
	public static BackgroundColor DarkGray    => new(ConsoleColor.DarkGray);
	public static BackgroundColor Blue        => new(ConsoleColor.Blue);
	public static BackgroundColor Green       => new(ConsoleColor.Green);
	public static BackgroundColor Cyan        => new(ConsoleColor.Cyan);
	public static BackgroundColor Red         => new(ConsoleColor.Red);
	public static BackgroundColor Magenta     => new(ConsoleColor.Magenta);
	public static BackgroundColor Yellow      => new(ConsoleColor.Yellow);
	public static BackgroundColor White       => new(ConsoleColor.White);

	public static RestoreBackground Restore => new();
}


// ============================================================
// Interpolated string handler
// ============================================================

[InterpolatedStringHandler]
public ref struct ConsoleInterpolatedStringHandler
{
	// The pieces are collected here, and ColorConsole draws them together (so a message is never half-drawn when something throws or another thread writes).
	private readonly List<object> _parts;    // a string = text; ForegroundColor / BackgroundColor / RestoreForeground / RestoreBackground = a colour change

	public ConsoleInterpolatedStringHandler(int literalLength, int formattedCount) {
		_parts = new List<object>(formattedCount * 2 + 1);
	}

	public ConsoleInterpolatedStringHandler(string text) : this(text.Length, 0) {
		AppendLiteral(text);
	}


	// --------------------------------------------------------
	// Text
	// --------------------------------------------------------

	public void AppendLiteral(string value) {
		_parts.Add(value);
	}


	// --------------------------------------------------------
	// Normal interpolated values
	// --------------------------------------------------------

	public void AppendFormatted<T>(T value) {
		AppendLiteral(value?.ToString() ?? "");
	}

	public void AppendFormatted<T>(T value, string? format) where T : IFormattable {
		AppendLiteral(value.ToString(format, null));
	}

	// {value,-20} and {value,8:0.0}: pad to a width (negative = pad on the right)
	public void AppendFormatted<T>(T value, int alignment, string? format = null) {
		string text = value is IFormattable f ? f.ToString(format, null) : value?.ToString() ?? "";
		AppendLiteral(alignment >= 0 ? text.PadLeft(alignment) : text.PadRight(-alignment));
	}


	// --------------------------------------------------------
	// Colours (they print nothing themselves)
	// --------------------------------------------------------

	public void AppendFormatted(ForegroundColor color) {
		_parts.Add(color);
	}

	// null = "leave the colour as it is", so a message can colour some values and not others without being split in two
	public void AppendFormatted(ForegroundColor? color) {
		if (color is { } value) _parts.Add(value);
	}

	public void AppendFormatted(RestoreForeground restore) {
		_parts.Add(restore);
	}

	public void AppendFormatted(BackgroundColor color) {
		_parts.Add(color);
	}

	public void AppendFormatted(RestoreBackground restore) {
		_parts.Add(restore);
	}


	// --------------------------------------------------------
	// Draw it (called by ColorConsole, under its lock).
	// With colours the console's own colours are put back at the end; without, the colour markers are simply skipped.
	// --------------------------------------------------------

	internal readonly void WriteTo(TextWriter writer, bool useColors) {
		ConsoleColor originalForeground = default, originalBackground = default;
		if (useColors) {
			originalForeground = Console.ForegroundColor;
			originalBackground = Console.BackgroundColor;
		}
		try {
			foreach (object part in _parts) {
				switch (part) {
					case string text: writer.Write(text); break;
					case ForegroundColor fg when useColors: Console.ForegroundColor = fg.Color; break;
					case BackgroundColor bg when useColors: Console.BackgroundColor = bg.Color; break;
					case RestoreForeground when useColors: Console.ForegroundColor = originalForeground; break;
					case RestoreBackground when useColors: Console.BackgroundColor = originalBackground; break;
				}
			}
		} finally {
			if (useColors) {
				Console.ForegroundColor = originalForeground;
				Console.BackgroundColor = originalBackground;
			}
		}
	}
}
