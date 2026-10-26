using System.Numerics;

namespace Mustang;

internal readonly record struct Rgb(byte R, byte G, byte B);

// A chestnut horse built from smooth shapes in model units (ground at y = 0, facing +x),
// shaded with a light from the upper left so the coat reads as round and glossy.
internal static class Horse
{
    public const float MinX = -21f;
    public const float MaxX = 18.5f;
    public const float MinY = -0.3f;
    public const float MaxY = 24.5f;

    private static readonly Vector3 Light = Vector3.Normalize(new(-0.55f, 0.65f, 0.55f));
    private static readonly Vector3 HalfVector = Vector3.Normalize(Light + Vector3.UnitZ);

    private static readonly Vector3 Coat = new(138, 50, 20);
    private static readonly Vector3 Hair = new(66, 26, 12);
    private static readonly Vector3 HoofColor = new(70, 64, 60);
    private static readonly Vector3 Sock = new(232, 226, 214);
    private static readonly Vector3 EyeColor = new(20, 12, 8);

    // Leg joint angles in degrees from straight down (positive swings the hoof forward):
    // upper leg, cannon, pastern. Eight samples across one gallop stride.
    private static readonly Vector3[] ForeStride =
    [
        new(20, 22, 35),      // landing ahead
        new(5, 5, 28),        // weight bearing, fetlock sinks
        new(-12, -12, -5),
        new(-28, -30, -45),   // push-off
        new(-25, -95, -140),  // knee folds as the leg lifts
        new(5, -70, -120),    // swinging forward folded
        new(35, 15, -10),     // unfolding
        new(40, 40, 45),      // reaching
    ];

    private static readonly Vector3[] HindStride =
    [
        new(0, 5, 25),        // landing under the belly
        new(-15, -5, 18),
        new(-30, -18, -10),
        new(-45, -38, -55),   // driving back
        new(-30, -60, -100),  // hock flexes as the leg lifts
        new(-5, -30, -60),
        new(20, 5, -5),
        new(15, 15, 30),      // reaching forward
    ];

    private readonly record struct Leg(Vector2 Root, bool Fore, float Phase, bool Far, bool WhiteSock);

    // Transverse gallop: hinds land close together, then the fores.
    private static readonly Leg[] Legs =
    [
        new(new(-5.4f, 7.9f), Fore: false, Phase: 0.00f, Far: true, WhiteSock: false),
        new(new(7.3f, 7.9f), Fore: true, Phase: 0.45f, Far: true, WhiteSock: false),
        new(new(-6.2f, 7.6f), Fore: false, Phase: 0.10f, Far: false, WhiteSock: true),
        new(new(6.5f, 7.6f), Fore: true, Phase: 0.55f, Far: false, WhiteSock: false),
    ];

    public static Rgb?[][,] RenderFrames(int count, float scale)
    {
        var frames = new Rgb?[count][,];
        Parallel.For(0, count, i => frames[i] = RenderFrame((float)i / count, scale));
        return frames;
    }

    public static Rgb?[,] RenderFrame(float phase, float scale)
    {
        var layers = BuildLayers(phase);
        var width = (int)MathF.Ceiling((MaxX - MinX) * scale);
        var height = (int)MathF.Ceiling((MaxY - MinY) * scale);
        height += height % 2;
        var pixel = 1f / scale;
        var image = new Rgb?[height, width];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = new Vector2(MinX + (x + 0.5f) * pixel, MinY + (height - y - 0.5f) * pixel);

                // Front-most layer first; the first hit is the visible one.
                for (var i = layers.Count - 1; i >= 0; i--)
                {
                    if (layers[i].TryShade(p, pixel, out var color))
                    {
                        image[y, x] = ToRgb(color);
                        break;
                    }
                }
            }
        }

        return image;
    }

    private static List<ILayer> BuildLayers(float phase)
    {
        var legs = Legs.Select(leg => (leg, joints: LegJoints(leg, phase))).ToArray();

        // Keep the lowest hoof on the ground; when every hoof is up the horse is in flight.
        var lowest = legs.Min(l => MathF.Min(l.joints[3].Y, l.joints[4].Y)) - 0.45f;
        var lift = new Vector2(0, MathF.Max(0, -lowest));

        var layers = new List<ILayer>();
        foreach (var (leg, joints) in legs.Where(l => l.leg.Far))
        {
            AddLeg(layers, leg, joints, lift);
        }

        layers.Add(Tail(phase, lift));
        layers.Add(Torso(lift));

        foreach (var (leg, joints) in legs.Where(l => !l.leg.Far))
        {
            AddLeg(layers, leg, joints, lift);
        }

        layers.Add(Mane(phase, lift));
        layers.Add(Details(lift));
        return layers;
    }

    private static Solid Torso(Vector2 o) => new(
        Coat,
        multiplier: 1f,
        thickness: 4.5f,
        blend: 1.6f,
        [
            new Ellipse(o + new Vector2(-7.3f, 11.8f), 4.6f, 3.5f, 6),            // croup and hindquarters
            new Ellipse(o + new Vector2(-6.4f, 9.2f), 2.9f, 3.0f, -20),           // thigh
            new Ellipse(o + new Vector2(-0.5f, 11.2f), 7.6f, 3.5f, 0),            // barrel
            new Ellipse(o + new Vector2(6.0f, 11.3f), 3.3f, 4.0f, -25),           // chest and shoulder
            new Ellipse(o + new Vector2(6.7f, 8.6f), 1.6f, 2.2f, 0),              // forearm
            new Capsule(o + new Vector2(6.6f, 13.0f), o + new Vector2(10.8f, 18.8f), 3.6f, 2.0f), // neck
            new Ellipse(o + new Vector2(8.0f, 17.0f), 1.6f, 3.4f, -35),           // crest of the neck
            new Capsule(o + new Vector2(11.5f, 19.8f), o + new Vector2(16.2f, 15.4f), 2.2f, 1.3f),  // head
            new Capsule(o + new Vector2(12.2f, 18.4f), o + new Vector2(12.2f, 18.4f), 2.0f, 2.0f), // jowl
        ]);
    private static Strands Details(Vector2 o) => new(
    [
        (new Capsule(o + new Vector2(11.9f, 21.0f), o + new Vector2(11.8f, 22.7f), 0.45f, 0.12f), Coat * 0.7f),  // far ear
        (new Capsule(o + new Vector2(11.3f, 21.0f), o + new Vector2(10.9f, 22.8f), 0.45f, 0.12f), Coat),         // near ear
        (new Capsule(o + new Vector2(13.0f, 19.6f), o + new Vector2(13.0f, 19.6f), 0.4f, 0.4f), EyeColor),
        (new Capsule(o + new Vector2(15.8f, 16.2f), o + new Vector2(15.8f, 16.2f), 0.28f, 0.28f), EyeColor),     // nostril
    ]);

    private static Vector2[] LegJoints(Leg leg, float phase)
    {
        var angles = Sample(leg.Fore ? ForeStride : HindStride, phase + leg.Phase);
        var lengths = leg.Fore ? new Vector3(3.9f, 3.2f, 1.0f) : new Vector3(3.7f, 3.4f, 1.0f);

        var knee = leg.Root + Direction(angles.X) * lengths.X;
        var fetlock = knee + Direction(angles.Y) * lengths.Y;
        var coronet = fetlock + Direction(angles.Z) * lengths.Z;
        var toe = coronet + Direction(angles.Z) * 0.45f;
        return [leg.Root, knee, fetlock, coronet, toe];
    }

    private static void AddLeg(List<ILayer> layers, Leg leg, Vector2[] j, Vector2 o)
    {
        var shade = leg.Far ? 0.68f : 1f;
        var upper = leg.Fore ? 1.3f : 1.45f;
        var cannon = leg.Fore ? 0.55f : 0.6f;

        layers.Add(new Solid(
            Coat,
            shade,
            thickness: 1.0f,
            blend: 0.4f,
            [
                new Capsule(o + j[0], o + j[1], upper, 0.72f),
                new Capsule(o + j[1], o + j[2], cannon, 0.42f),
                new Capsule(o + j[2], o + j[3], 0.52f, 0.44f),
            ]));

        var strands = new List<(Capsule, Vector3)>();
        if (leg.WhiteSock)
        {
            strands.Add((new Capsule(o + Vector2.Lerp(j[1], j[2], 0.6f), o + j[3], 0.56f, 0.5f), Sock));
        }

        strands.Add((new Capsule(o + j[3] - (j[4] - j[3]) * 0.2f, o + j[4], 0.58f, 0.55f), HoofColor * shade));
        layers.Add(new Strands(strands));
    }

    private static Solid Tail(float phase, Vector2 o)
    {
        var root = o + new Vector2(-11.0f, 14.2f);
        var shapes = new List<IShape>();
        for (var s = 0; s < 5; s++)
        {
            var previous = root;
            for (var k = 1; k <= 10; k++)
            {
                var t = k / 10f;
                var wave = MathF.Sin(MathF.Tau * phase - t * 4f + s * 0.6f) * 0.9f * t;
                var point = root + new Vector2(-t * 9.5f - s * 0.25f * t, 2.6f * t - 7.5f * t * t - s * 0.45f * t + wave);
                var radius = 1.0f * (1 - 0.6f * t);
                shapes.Add(new Capsule(previous, point, radius * 1.08f, radius));
                previous = point;
            }
        }

        return new Solid(Hair, multiplier: 1f, thickness: 1.4f, blend: 0.5f, [.. shapes]);
    }

    private static Solid Mane(float phase, Vector2 o)
    {
        var poll = o + new Vector2(11.2f, 20.9f);
        var withers = o + new Vector2(4.2f, 15.6f);
        var shapes = new List<IShape>();
        const int count = 12;
        for (var i = 0; i < count; i++)
        {
            var u = i / (count - 1f);
            var previous = Vector2.Lerp(poll, withers, u) + new Vector2(0.2f, -0.4f);
            var angle = MathF.Atan2(0.3f, -1f) + MathF.Sin(MathF.Tau * phase * 2 + u * 7f) * 0.2f;
            var length = 3.6f - u * 1.4f;
            for (var k = 0; k < 4; k++)
            {
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var next = previous + direction * (length / 4);
                var radius = 0.7f - k * 0.14f;
                shapes.Add(new Capsule(previous, next, radius, radius - 0.14f));
                previous = next;
                angle += 0.2f;  // the hair curls down as it streams back
            }
        }

        // Forelock between the ears.
        shapes.Add(new Capsule(o + new Vector2(11.5f, 21.2f), o + new Vector2(12.9f, 20.1f), 0.55f, 0.2f));
        return new Solid(Hair, multiplier: 1f, thickness: 1.0f, blend: 0.45f, [.. shapes]);
    }
    private static Vector3 Sample(Vector3[] table, float phase)
    {
        var t = (phase % 1 + 1) % 1 * table.Length;
        var i = (int)t % table.Length;
        var f = t - MathF.Floor(t);
        f = f * f * (3 - 2 * f);
        return Vector3.Lerp(table[i], table[(i + 1) % table.Length], f);
    }

    private static Vector2 Direction(float degrees)
    {
        var radians = degrees * MathF.PI / 180f;
        return new Vector2(MathF.Sin(radians), -MathF.Cos(radians));
    }

    private static Vector3 Shade(Vector3 baseColor, Vector3 normal, float multiplier)
    {
        var diffuse = MathF.Max(0, Vector3.Dot(normal, Light));
        var specular = MathF.Pow(MathF.Max(0, Vector3.Dot(normal, HalfVector)), 36);
        return baseColor * (0.28f + 0.95f * diffuse) * multiplier + new Vector3(255, 220, 190) * specular * 0.45f * multiplier;
    }

    private static Rgb ToRgb(Vector3 c)
    {
        var v = Vector3.Clamp(c, Vector3.Zero, new Vector3(255));
        return new Rgb((byte)v.X, (byte)v.Y, (byte)v.Z);
    }

    private interface IShape
    {
        float Distance(Vector2 p);

        (Vector2 Min, Vector2 Max) Bounds { get; }
    }

    private interface ILayer
    {
        bool TryShade(Vector2 p, float pixel, out Vector3 color);
    }

    private readonly record struct Ellipse(Vector2 Center, float Rx, float Ry, float AngleDegrees) : IShape
    {
        public float Distance(Vector2 p)
        {
            var a = AngleDegrees * MathF.PI / 180f;
            var d = p - Center;
            var local = new Vector2(d.X * MathF.Cos(a) + d.Y * MathF.Sin(a), -d.X * MathF.Sin(a) + d.Y * MathF.Cos(a));
            return (new Vector2(local.X / Rx, local.Y / Ry).Length() - 1) * MathF.Min(Rx, Ry);
        }

        public (Vector2 Min, Vector2 Max) Bounds
        {
            get
            {
                var r = new Vector2(MathF.Max(Rx, Ry));
                return (Center - r, Center + r);
            }
        }
    }

    private readonly record struct Capsule(Vector2 A, Vector2 B, float Ra, float Rb) : IShape
    {
        public float Distance(Vector2 p) => Distance(p, out _, out _);

        public float Distance(Vector2 p, out Vector2 offset, out float radius)
        {
            var ab = B - A;
            var lengthSquared = ab.LengthSquared();
            var t = lengthSquared == 0 ? 0 : Math.Clamp(Vector2.Dot(p - A, ab) / lengthSquared, 0, 1);
            offset = p - (A + ab * t);
            radius = Ra + (Rb - Ra) * t;
            return offset.Length() - radius;
        }

        public (Vector2 Min, Vector2 Max) Bounds
        {
            get
            {
                var r = new Vector2(MathF.Max(Ra, Rb));
                return (Vector2.Min(A, B) - r, Vector2.Max(A, B) + r);
            }
        }
    }

    // Smoothly blended shapes, shaded as a soft pillow so the joins between them disappear.
    private sealed class Solid(Vector3 color, float multiplier, float thickness, float blend, IShape[] shapes) : ILayer
    {
        private readonly (Vector2 Min, Vector2 Max) _bounds = Union(shapes.Select(s => s.Bounds), blend);

        public bool TryShade(Vector2 p, float pixel, out Vector3 result)
        {
            result = default;
            if (!Inside(_bounds, p) || Field(p) > 0)
            {
                return false;
            }

            // A wide sampling step smooths the creases a distance field has along its middle.
            var e = MathF.Max(pixel, 0.6f);
            var dx = (Height(p + new Vector2(e, 0)) - Height(p - new Vector2(e, 0))) / (2 * e);
            var dy = (Height(p + new Vector2(0, e)) - Height(p - new Vector2(0, e))) / (2 * e);
            result = Shade(color, Vector3.Normalize(new Vector3(-dx, -dy, 1)), multiplier);
            return true;
        }

        private float Field(Vector2 p)
        {
            var d = float.MaxValue;
            foreach (var shape in shapes)
            {
                d = SmoothMin(d, shape.Distance(p), blend);
            }

            return d;
        }

        private float Height(Vector2 p)
        {
            // Steep at the silhouette, levelling off smoothly towards the middle.
            var depth = MathF.Max(0, -Field(p));
            return thickness * MathF.Sqrt(1 - MathF.Exp(-2 * depth / thickness));
        }

        private static float SmoothMin(float a, float b, float k)
        {
            var h = MathF.Max(k - MathF.Abs(a - b), 0) / k;
            return MathF.Min(a, b) - h * h * k * 0.25f;
        }
    }

    // Individually drawn tubes (hair, hooves, eyes): later ones cover earlier ones.
    private sealed class Strands(IReadOnlyList<(Capsule Shape, Vector3 Color)> strands) : ILayer
    {
        private readonly (Vector2 Min, Vector2 Max) _bounds = Union(strands.Select(s => s.Shape.Bounds), 0);

        public bool TryShade(Vector2 p, float pixel, out Vector3 result)
        {
            result = default;
            if (!Inside(_bounds, p))
            {
                return false;
            }

            for (var i = strands.Count - 1; i >= 0; i--)
            {
                var (shape, color) = strands[i];
                if (shape.Distance(p, out var offset, out var radius) <= 0 && radius > 0)
                {
                    var n = offset / radius;
                    var normal = new Vector3(n, MathF.Sqrt(MathF.Max(0, 1 - n.LengthSquared())));
                    result = Shade(color, normal, 1f);
                    return true;
                }
            }

            return false;
        }
    }

    private static (Vector2 Min, Vector2 Max) Union(IEnumerable<(Vector2 Min, Vector2 Max)> boxes, float margin)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var (lo, hi) in boxes)
        {
            min = Vector2.Min(min, lo);
            max = Vector2.Max(max, hi);
        }

        return (min - new Vector2(margin), max + new Vector2(margin));
    }

    private static bool Inside((Vector2 Min, Vector2 Max) box, Vector2 p) =>
        p.X >= box.Min.X && p.X <= box.Max.X && p.Y >= box.Min.Y && p.Y <= box.Max.Y;
}
