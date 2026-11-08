using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Xml.Linq;

namespace LegionFanModeTool;

/// <summary>
/// 极简 SVG 渲染器，只支持图标用到的子集：填充的 path（全部路径命令）、fill-rule、
/// translate 和由 circle 构成的 clipPath。
/// path 写了 fill="#RRGGBB" 时用固定颜色（如品牌色），否则用绘制时传入的颜色。
/// 用 GDI+ 按目标像素尺寸直接绘制矢量。
/// </summary>
internal sealed class SvgIcon
{
    private readonly record struct Shape(GraphicsPath Path, GraphicsPath? Clip, Color? Fill);

    private readonly List<Shape> _shapes = [];
    private readonly RectangleF _viewBox;

    public SvgIcon(Stream stream)
    {
        var root = XDocument.Load(stream).Root ?? throw new InvalidDataException("SVG 为空。");
        XNamespace ns = root.Name.Namespace;

        var vb = ParseNumbers(root.Attribute("viewBox")?.Value ?? "0 0 24 24");
        _viewBox = new RectangleF(vb[0], vb[1], vb[2], vb[3]);

        var clips = root.Elements(ns + "clipPath").ToDictionary(
            clip => (string?)clip.Attribute("id") ?? "",
            clip =>
            {
                var path = new GraphicsPath();
                foreach (var circle in clip.Elements(ns + "circle"))
                {
                    float cx = Attr(circle, "cx"), cy = Attr(circle, "cy"), r = Attr(circle, "r");
                    path.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                }
                return path;
            });

        try
        {
            foreach (var element in root.Elements(ns + "path"))
            {
                var fillMode = (string?)element.Attribute("fill-rule") == "evenodd" ? FillMode.Alternate : FillMode.Winding;
                var path = SvgPathParser.Parse((string?)element.Attribute("d") ?? "", fillMode);

                // 按 SVG 规范，clip-path 处于元素 transform 之后的坐标系，所以两者一起变换
                var clipPath = Unwrap((string?)element.Attribute("clip-path"), "url(#") is { } id
                    && clips.TryGetValue(id, out var clip)
                        ? (GraphicsPath)clip.Clone()
                        : null;

                if (Unwrap((string?)element.Attribute("transform"), "translate(") is { } args)
                {
                    var t = ParseNumbers(args);
                    using var matrix = new Matrix();
                    matrix.Translate(t[0], t.Length > 1 ? t[1] : 0);
                    path.Transform(matrix);
                    clipPath?.Transform(matrix);
                }

                Color? fill = (string?)element.Attribute("fill") is ['#', ..] hex ? ColorTranslator.FromHtml(hex) : null;
                _shapes.Add(new Shape(path, clipPath, fill));
            }
        }
        finally
        {
            foreach (var clip in clips.Values)
                clip.Dispose();
        }
    }

    /// <summary>在 <paramref name="bounds"/> 内用 <paramref name="color"/> 绘制图标。</summary>
    public void Draw(Graphics g, RectangleF bounds, Color color)
    {
        var state = g.Save();
        try
        {
            g.TranslateTransform(bounds.X, bounds.Y);
            g.ScaleTransform(bounds.Width / _viewBox.Width, bounds.Height / _viewBox.Height);
            g.TranslateTransform(-_viewBox.X, -_viewBox.Y);

            using var brush = new SolidBrush(color);
            foreach (var shape in _shapes)
            {
                var clipState = g.Save();
                if (shape.Clip is not null)
                    g.SetClip(shape.Clip, CombineMode.Intersect);
                using var fillBrush = shape.Fill is { } fill ? new SolidBrush(fill) : null;
                g.FillPath(fillBrush ?? brush, shape.Path);
                g.Restore(clipState);
            }
        }
        finally
        {
            g.Restore(state);
        }
    }

    /// <summary>取出形如 <c>prefix…)</c> 的括号内容，例如 <c>url(#id)</c> → <c>id</c>。</summary>
    private static string? Unwrap(string? value, string prefix) =>
        value is not null && value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith(')')
            ? value[prefix.Length..^1]
            : null;

    private static float Attr(XElement element, string name) =>
        float.Parse((string?)element.Attribute(name) ?? "0", CultureInfo.InvariantCulture);

    private static float[] ParseNumbers(string text) =>
        text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => float.Parse(s, CultureInfo.InvariantCulture))
            .ToArray();
}

/// <summary>把 SVG path 的 d 属性转换成 GraphicsPath。</summary>
internal ref struct SvgPathParser
{
    private readonly ReadOnlySpan<char> _d;
    private int _pos;

    private SvgPathParser(ReadOnlySpan<char> d) => _d = d;

    public static GraphicsPath Parse(string d, FillMode fillMode)
    {
        var path = new GraphicsPath(fillMode);
        new SvgPathParser(d).Run(path);
        return path;
    }

    private void Run(GraphicsPath path)
    {
        PointF current = default, start = default, lastControl = default;
        char command = ' ', previous = ' ';

        while (true)
        {
            SkipSeparators();
            if (_pos >= _d.Length)
                break;

            if (char.IsLetter(_d[_pos]))
                command = _d[_pos++];
            else if (command is 'M')
                command = 'L'; // M 后面的隐式坐标按 L 处理
            else if (command is 'm')
                command = 'l';

            var relative = char.IsLower(command);
            PointF Point(float x, float y) => relative ? new(current.X + x, current.Y + y) : new(x, y);

            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    path.StartFigure();
                    current = start = Point(Number(), Number());
                    break;
                case 'L':
                {
                    var p = Point(Number(), Number());
                    path.AddLine(current, p);
                    current = p;
                    break;
                }
                case 'H':
                {
                    var x = Number();
                    var p = new PointF(relative ? current.X + x : x, current.Y);
                    path.AddLine(current, p);
                    current = p;
                    break;
                }
                case 'V':
                {
                    var y = Number();
                    var p = new PointF(current.X, relative ? current.Y + y : y);
                    path.AddLine(current, p);
                    current = p;
                    break;
                }
                case 'C':
                {
                    var c1 = Point(Number(), Number());
                    var c2 = Point(Number(), Number());
                    var p = Point(Number(), Number());
                    path.AddBezier(current, c1, c2, p);
                    lastControl = c2;
                    current = p;
                    break;
                }
                case 'S':
                {
                    var c1 = char.ToUpperInvariant(previous) is 'C' or 'S' ? Reflect(lastControl, current) : current;
                    var c2 = Point(Number(), Number());
                    var p = Point(Number(), Number());
                    path.AddBezier(current, c1, c2, p);
                    lastControl = c2;
                    current = p;
                    break;
                }
                case 'Q':
                {
                    var c = Point(Number(), Number());
                    var p = Point(Number(), Number());
                    AddQuadratic(path, current, c, p);
                    lastControl = c;
                    current = p;
                    break;
                }
                case 'T':
                {
                    var c = char.ToUpperInvariant(previous) is 'Q' or 'T' ? Reflect(lastControl, current) : current;
                    var p = Point(Number(), Number());
                    AddQuadratic(path, current, c, p);
                    lastControl = c;
                    current = p;
                    break;
                }
                case 'A':
                {
                    float rx = Number(), ry = Number(), rotation = Number();
                    bool largeArc = Flag(), sweep = Flag();
                    var p = Point(Number(), Number());
                    AddArc(path, current, rx, ry, rotation, largeArc, sweep, p);
                    current = p;
                    break;
                }
                case 'Z':
                    path.CloseFigure();
                    current = start;
                    break;
                default:
                    throw new InvalidDataException($"不支持的路径命令：{command}");
            }
            previous = command;
        }
    }

    private static PointF Reflect(PointF control, PointF about) =>
        new(2 * about.X - control.X, 2 * about.Y - control.Y);

    private static void AddQuadratic(GraphicsPath path, PointF p0, PointF c, PointF p) =>
        path.AddBezier(
            p0,
            new PointF(p0.X + 2f / 3 * (c.X - p0.X), p0.Y + 2f / 3 * (c.Y - p0.Y)),
            new PointF(p.X + 2f / 3 * (c.X - p.X), p.Y + 2f / 3 * (c.Y - p.Y)),
            p);

    /// <summary>SVG 端点参数圆弧 → 若干段三次贝塞尔（SVG 规范附录 B.2.4）。</summary>
    private static void AddArc(GraphicsPath path, PointF p0, float rx, float ry, float rotationDeg,
        bool largeArc, bool sweep, PointF p1)
    {
        if (rx == 0 || ry == 0)
        {
            path.AddLine(p0, p1);
            return;
        }

        double phi = rotationDeg * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
        double dx = (p0.X - p1.X) / 2.0, dy = (p0.Y - p1.Y) / 2.0;
        double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;
        double rxa = Math.Abs(rx), rya = Math.Abs(ry);

        var lambda = x1 * x1 / (rxa * rxa) + y1 * y1 / (rya * rya);
        if (lambda > 1)
        {
            rxa *= Math.Sqrt(lambda);
            rya *= Math.Sqrt(lambda);
        }

        var num = rxa * rxa * rya * rya - rxa * rxa * y1 * y1 - rya * rya * x1 * x1;
        var den = rxa * rxa * y1 * y1 + rya * rya * x1 * x1;
        var coef = (largeArc == sweep ? -1 : 1) * Math.Sqrt(Math.Max(0, num / den));
        double cx1 = coef * rxa * y1 / rya, cy1 = -coef * rya * x1 / rxa;
        double cx = cos * cx1 - sin * cy1 + (p0.X + p1.X) / 2.0;
        double cy = sin * cx1 + cos * cy1 + (p0.Y + p1.Y) / 2.0;

        var theta1 = Math.Atan2((y1 - cy1) / rya, (x1 - cx1) / rxa);
        var delta = Math.Atan2((-y1 - cy1) / rya, (-x1 - cx1) / rxa) - theta1;
        if (sweep && delta < 0)
            delta += 2 * Math.PI;
        else if (!sweep && delta > 0)
            delta -= 2 * Math.PI;

        var segments = (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2));
        var step = delta / segments;
        var k = 4.0 / 3 * Math.Tan(step / 4);

        PointF Map(double x, double y) => new((float)(cos * rxa * x - sin * rya * y + cx), (float)(sin * rxa * x + cos * rya * y + cy));

        var theta = theta1;
        var from = p0;
        for (var i = 0; i < segments; i++)
        {
            double c1 = Math.Cos(theta), s1 = Math.Sin(theta);
            double c2 = Math.Cos(theta + step), s2 = Math.Sin(theta + step);
            var to = i == segments - 1 ? p1 : Map(c2, s2);
            path.AddBezier(from, Map(c1 - k * s1, s1 + k * c1), Map(c2 + k * s2, s2 - k * c2), to);
            from = to;
            theta += step;
        }
    }

    private void SkipSeparators()
    {
        while (_pos < _d.Length && (char.IsWhiteSpace(_d[_pos]) || _d[_pos] == ','))
            _pos++;
    }

    private bool Flag()
    {
        SkipSeparators();
        return _d[_pos++] == '1';
    }

    private float Number()
    {
        SkipSeparators();
        var begin = _pos;
        if (_pos < _d.Length && _d[_pos] is '+' or '-')
            _pos++;

        var seenDot = false;
        var seenExp = false;
        while (_pos < _d.Length)
        {
            var c = _d[_pos];
            if (char.IsAsciiDigit(c))
                _pos++;
            else if (c == '.' && !seenDot && !seenExp)
            {
                seenDot = true;
                _pos++;
            }
            else if (c is 'e' or 'E' && !seenExp)
            {
                seenExp = true;
                _pos++;
                if (_pos < _d.Length && _d[_pos] is '+' or '-')
                    _pos++;
            }
            else
                break;
        }
        return float.Parse(_d[begin.._pos], NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
