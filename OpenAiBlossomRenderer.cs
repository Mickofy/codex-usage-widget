using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodexUsageWidget;

/// <summary>
/// Renders the exact OpenAI Blossom vector shipped in Assets/chatgpt-blossom-white.svg.
/// The asset is sourced from OpenAI's official openai/openai-cookbook repository.
/// </summary>
internal sealed class OpenAiBlossomRenderer : IDisposable
{
    private readonly GraphicsPath _path;
    private readonly RectangleF _bounds;

    private OpenAiBlossomRenderer(GraphicsPath path)
    {
        _path = path;
        _bounds = path.GetBounds();
    }

    public static OpenAiBlossomRenderer Load()
    {
        string assetPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "chatgpt-blossom-white.svg");

        if (!File.Exists(assetPath))
        {
            throw new FileNotFoundException(
                "Official OpenAI Blossom asset was not found.",
                assetPath);
        }

        XDocument document = XDocument.Load(assetPath);
        XElement? pathElement = document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "path");

        string? pathData = pathElement?.Attribute("d")?.Value;
        if (string.IsNullOrWhiteSpace(pathData))
        {
            throw new InvalidDataException(
                "Official OpenAI Blossom SVG does not contain path data.");
        }

        return new OpenAiBlossomRenderer(SvgPathParser.Parse(pathData));
    }

    public void Draw(Graphics graphics, RectangleF target)
    {
        if (_bounds.Width <= 0 || _bounds.Height <= 0)
            return;

        float scale = Math.Min(
            target.Width / _bounds.Width,
            target.Height / _bounds.Height);

        float renderedWidth = _bounds.Width * scale;
        float renderedHeight = _bounds.Height * scale;
        float offsetX = target.X + (target.Width - renderedWidth) / 2f;
        float offsetY = target.Y + (target.Height - renderedHeight) / 2f;

        using var matrix = new Matrix(
            scale,
            0f,
            0f,
            scale,
            offsetX - (_bounds.X * scale),
            offsetY - (_bounds.Y * scale));

        using GraphicsPath path = (GraphicsPath)_path.Clone();
        path.Transform(matrix);

        using var brush = new SolidBrush(Color.White);
        graphics.FillPath(brush, path);
    }

    public void Dispose() => _path.Dispose();

    private static class SvgPathParser
    {
        private static readonly Regex TokenRegex = new(
            @"[A-Za-z]|[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static GraphicsPath Parse(string data)
        {
            string[] tokens = TokenRegex
                .Matches(data)
                .Select(match => match.Value)
                .ToArray();

            var path = new GraphicsPath(FillMode.Winding);
            int index = 0;
            char command = '\0';
            PointF current = PointF.Empty;
            PointF figureStart = PointF.Empty;

            while (index < tokens.Length)
            {
                if (IsCommand(tokens[index]))
                {
                    command = tokens[index][0];
                    index++;

                    if (command is 'Z' or 'z')
                    {
                        path.CloseFigure();
                        current = figureStart;
                        command = '\0';
                        continue;
                    }
                }

                switch (command)
                {
                    case 'M':
                    {
                        PointF point = ReadPoint(tokens, ref index);
                        path.StartFigure();
                        current = point;
                        figureStart = point;
                        command = 'L';
                        break;
                    }
                    case 'L':
                    {
                        PointF point = ReadPoint(tokens, ref index);
                        path.AddLine(current, point);
                        current = point;
                        break;
                    }
                    case 'H':
                    {
                        float x = ReadFloat(tokens, ref index);
                        PointF point = new(x, current.Y);
                        path.AddLine(current, point);
                        current = point;
                        break;
                    }
                    case 'V':
                    {
                        float y = ReadFloat(tokens, ref index);
                        PointF point = new(current.X, y);
                        path.AddLine(current, point);
                        current = point;
                        break;
                    }
                    case 'C':
                    {
                        PointF control1 = ReadPoint(tokens, ref index);
                        PointF control2 = ReadPoint(tokens, ref index);
                        PointF end = ReadPoint(tokens, ref index);
                        path.AddBezier(current, control1, control2, end);
                        current = end;
                        break;
                    }
                    default:
                        path.Dispose();
                        throw new InvalidDataException(
                            $"Unsupported SVG path command '{command}'.");
                }
            }

            return path;
        }

        private static PointF ReadPoint(string[] tokens, ref int index)
        {
            return new PointF(
                ReadFloat(tokens, ref index),
                ReadFloat(tokens, ref index));
        }

        private static float ReadFloat(string[] tokens, ref int index)
        {
            if (index >= tokens.Length || IsCommand(tokens[index]))
                throw new InvalidDataException("Unexpected end of SVG path data.");

            return float.Parse(
                tokens[index++],
                NumberStyles.Float,
                CultureInfo.InvariantCulture);
        }

        private static bool IsCommand(string token)
        {
            return token.Length == 1 && char.IsLetter(token[0]);
        }
    }
}
