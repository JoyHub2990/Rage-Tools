using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SharpDX;

namespace RageLightEditor.Editor
{
    public static class DccBridgeProtocol
    {
        public const int Version = 1;

        public static readonly string[] Commands = { "hello", "ping", "bye" };

        private static readonly HashSet<string> Known = new HashSet<string>(Commands, StringComparer.OrdinalIgnoreCase);

        public static bool IsApiVerb(string cmd) =>
            cmd != null && cmd.Length > 4 && cmd.StartsWith("api.", StringComparison.OrdinalIgnoreCase);

        public static MloBridgeMessage Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            line = line.Trim();
            try
            {
                if (line.StartsWith("{")) return ParseJson(line);
                var parts = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return null;
                string cmd = parts[0].ToLowerInvariant();
                if (!Known.Contains(cmd) && !IsApiVerb(cmd)) return null;
                var args = new string[parts.Length - 1];
                Array.Copy(parts, 1, args, 0, args.Length);
                return new DccMessage { Command = cmd, Raw = line, Json = false, Args = args };
            }
            catch { return null; }
        }

        private static MloBridgeMessage ParseJson(string line)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string cmd = root.TryGetProperty("cmd", out var c) ? c.GetString()
                       : (root.TryGetProperty("type", out var t) ? t.GetString() : null);
            if (string.IsNullOrEmpty(cmd)) return null;
            cmd = cmd.ToLowerInvariant();
            if (!Known.Contains(cmd) && !IsApiVerb(cmd)) return null;
            var m = new DccMessage { Command = cmd, Raw = line, Json = true, Root = root.Clone() };
            return m;
        }

        public static string N(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
        public static string S(string s) => JsonSerializer.Serialize(s ?? "");
        public static string V(Vector3 v) => "[" + N(v.X) + "," + N(v.Y) + "," + N(v.Z) + "]";
        public static string Q(Quaternion q) => "[" + N(q.X) + "," + N(q.Y) + "," + N(q.Z) + "," + N(q.W) + "]";
        public static string P(Vector3 v) => N(v.X) + " " + N(v.Y) + " " + N(v.Z);

        public static string Ok(bool json, string what) =>
            json ? "{\"type\":\"ok\",\"text\":" + S(what) + "}" : "ok " + what;

        public static string Err(bool json, string what) =>
            json ? "{\"type\":\"error\",\"text\":" + S(what) + "}" : "error " + what;

        public static string Hello(bool json, string appVersion, string workspace, string interior, int clients)
        {
            if (!json)
                return $"hello RAGE_Tools protocol {Version} version {appVersion} workspace {workspace} interior {(string.IsNullOrEmpty(interior) ? "-" : interior.Replace(' ', '_'))}";
            var sb = new StringBuilder("{\"type\":\"hello\",\"app\":\"RAGE Tools\",\"protocol\":").Append(Version)
                .Append(",\"api\":1")
                .Append(",\"version\":").Append(S(appVersion))
                .Append(",\"workspace\":").Append(S(workspace))
                .Append(",\"interior\":").Append(S(interior))
                .Append(",\"clients\":").Append(clients)
                .Append(",\"commands\":[");
            for (int i = 0; i < Commands.Length; i++) { if (i > 0) sb.Append(','); sb.Append(S(Commands[i])); }
            return sb.Append("]}").ToString();
        }

        public static string Event(string name, string body) =>
            "{\"type\":\"event\",\"event\":" + S(name) + (string.IsNullOrEmpty(body) ? "" : "," + body) + "}";

        public static string CameraBody(Vector3 pos, Vector3 target, float fovDeg) =>
            "\"pos\":" + V(pos) + ",\"target\":" + V(target) + ",\"fov\":" + N(fovDeg) + ",\"up\":[0,0,1]";

        public static string CameraLine(bool json, Vector3 pos, Vector3 target, float fovDeg) =>
            json ? "{\"type\":\"camera\"," + CameraBody(pos, target, fovDeg) + "}"
                 : "camera " + P(pos) + " " + P(target) + " " + N(fovDeg);
    }

    public class DccMessage : MloBridgeMessage
    {
        public string[] Args = Array.Empty<string>();
        public JsonElement Root;

        private static float F(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0f;

        private bool Prop(string name, out JsonElement e)
        {
            e = default;
            return Json && Root.ValueKind == JsonValueKind.Object && Root.TryGetProperty(name, out e);
        }

        private bool Arg(int i, out string s)
        {
            s = null;
            if (Json || Args == null || i < 0 || i >= Args.Length) return false;
            s = Args[i];
            return true;
        }

        public bool Has(string name, int argIndex) => Prop(name, out _) || Arg(argIndex, out _);

        public float Num(string name, int argIndex, float dflt = 0.0f)
        {
            if (Prop(name, out var e)) return e.ValueKind == JsonValueKind.Number ? (float)e.GetDouble()
                                            : (e.ValueKind == JsonValueKind.String ? F(e.GetString()) : dflt);
            return Arg(argIndex, out var s) ? F(s) : dflt;
        }

        public int Int(string name, int argIndex, int dflt = 0) => (int)Math.Round(Num(name, argIndex, dflt));

        public string Text(string name, int argIndex, string dflt = "")
        {
            if (Prop(name, out var e))
                return e.ValueKind == JsonValueKind.String ? e.GetString()
                     : (e.ValueKind == JsonValueKind.Number ? e.GetRawText() : dflt);
            return Arg(argIndex, out var s) ? s : dflt;
        }

        public bool Flag(string name, int argIndex, bool dflt = false)
        {
            if (Prop(name, out var e))
            {
                if (e.ValueKind == JsonValueKind.True) return true;
                if (e.ValueKind == JsonValueKind.False) return false;
                if (e.ValueKind == JsonValueKind.Number) return e.GetDouble() != 0.0;
                if (e.ValueKind == JsonValueKind.String) { var t = e.GetString(); return t == "1" || t.Equals("on", StringComparison.OrdinalIgnoreCase) || t.Equals("true", StringComparison.OrdinalIgnoreCase); }
            }
            if (Arg(argIndex, out var s)) return s == "1" || s.Equals("on", StringComparison.OrdinalIgnoreCase) || s.Equals("true", StringComparison.OrdinalIgnoreCase);
            return dflt;
        }

        public Vector3 Vec(string name, int argIndex, Vector3 dflt)
        {
            if (Prop(name, out var e))
            {
                if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() >= 3)
                    return new Vector3((float)e[0].GetDouble(), (float)e[1].GetDouble(), (float)e[2].GetDouble());
                if (e.ValueKind == JsonValueKind.Object)
                    return new Vector3(
                        e.TryGetProperty("x", out var x) ? (float)x.GetDouble() : 0,
                        e.TryGetProperty("y", out var y) ? (float)y.GetDouble() : 0,
                        e.TryGetProperty("z", out var z) ? (float)z.GetDouble() : 0);
                return dflt;
            }
            if (Json && Root.ValueKind == JsonValueKind.Object && Root.TryGetProperty(name + "x", out _))
                return new Vector3(Num(name + "x", -1), Num(name + "y", -1), Num(name + "z", -1));
            if (!Json && Args != null && argIndex >= 0 && argIndex + 2 < Args.Length)
                return new Vector3(F(Args[argIndex]), F(Args[argIndex + 1]), F(Args[argIndex + 2]));
            return dflt;
        }

        public Quaternion Rot(string name, int argIndex, Quaternion dflt)
        {
            if (Prop(name, out var e) && e.ValueKind == JsonValueKind.Array)
            {
                int n = e.GetArrayLength();
                if (n >= 4) return Quaternion.Normalize(new Quaternion((float)e[0].GetDouble(), (float)e[1].GetDouble(), (float)e[2].GetDouble(), (float)e[3].GetDouble()));
                if (n == 3) return Euler((float)e[0].GetDouble(), (float)e[1].GetDouble(), (float)e[2].GetDouble());
                if (n == 1) return Quaternion.RotationAxis(Vector3.UnitZ, MathUtil.DegreesToRadians((float)e[0].GetDouble()));
            }
            if (Prop(name, out var one) && one.ValueKind == JsonValueKind.Number)
                return Quaternion.RotationAxis(Vector3.UnitZ, MathUtil.DegreesToRadians((float)one.GetDouble()));
            if (!Json && Args != null && argIndex >= 0)
            {
                if (argIndex + 3 < Args.Length) return Quaternion.Normalize(new Quaternion(F(Args[argIndex]), F(Args[argIndex + 1]), F(Args[argIndex + 2]), F(Args[argIndex + 3])));
                if (argIndex < Args.Length) return Quaternion.RotationAxis(Vector3.UnitZ, MathUtil.DegreesToRadians(F(Args[argIndex])));
            }
            return dflt;
        }

        public static Quaternion Euler(float rxDeg, float ryDeg, float rzDeg) =>
            Quaternion.RotationAxis(Vector3.UnitZ, MathUtil.DegreesToRadians(rzDeg)) *
            Quaternion.RotationAxis(Vector3.UnitY, MathUtil.DegreesToRadians(ryDeg)) *
            Quaternion.RotationAxis(Vector3.UnitX, MathUtil.DegreesToRadians(rxDeg));

        public Vector3[] Quad()
        {
            var c = new Vector3[4];
            if (Prop("corners", out var e) && e.ValueKind == JsonValueKind.Array && e.GetArrayLength() >= 4)
            {
                for (int i = 0; i < 4; i++)
                {
                    var it = e[i];
                    if (it.ValueKind == JsonValueKind.Array && it.GetArrayLength() >= 3)
                        c[i] = new Vector3((float)it[0].GetDouble(), (float)it[1].GetDouble(), (float)it[2].GetDouble());
                }
                return c;
            }
            if (!Json && Args != null && Args.Length >= 12)
                for (int i = 0; i < 4; i++) c[i] = new Vector3(F(Args[i * 3]), F(Args[i * 3 + 1]), F(Args[i * 3 + 2]));
            return c;
        }

        public List<DccMessage> Items(string name)
        {
            var list = new List<DccMessage>();
            if (Prop(name, out var e) && e.ValueKind == JsonValueKind.Array)
                foreach (var it in e.EnumerateArray())
                    if (it.ValueKind == JsonValueKind.Object)
                        list.Add(new DccMessage { Command = Command, Json = true, Root = it.Clone(), From = From, Raw = Raw });
            return list;
        }
    }

    public struct DccLight
    {
        public int Index;
        public string Kind;
        public Vector3 Position;
        public Vector3 Direction;
        public Vector3 Colour;
        public float Intensity;
        public float Range;
        public float InnerDeg, OuterDeg;
        public string Owner;

        public string ToJson()
        {
            var sb = new StringBuilder("{\"index\":").Append(Index)
                .Append(",\"kind\":").Append(DccBridgeProtocol.S(Kind))
                .Append(",\"pos\":").Append(DccBridgeProtocol.V(Position))
                .Append(",\"dir\":").Append(DccBridgeProtocol.V(Direction))
                .Append(",\"colour\":").Append(DccBridgeProtocol.V(Colour))
                .Append(",\"intensity\":").Append(DccBridgeProtocol.N(Intensity))
                .Append(",\"range\":").Append(DccBridgeProtocol.N(Range))
                .Append(",\"inner\":").Append(DccBridgeProtocol.N(InnerDeg))
                .Append(",\"outer\":").Append(DccBridgeProtocol.N(OuterDeg))
                .Append(",\"owner\":").Append(DccBridgeProtocol.S(Owner))
                .Append('}');
            return sb.ToString();
        }

        public string ToLine() =>
            "light " + Index + " " + Kind + " " + DccBridgeProtocol.P(Position) + " " + DccBridgeProtocol.P(Direction) +
            " " + DccBridgeProtocol.P(Colour) + " " + DccBridgeProtocol.N(Intensity) + " " + DccBridgeProtocol.N(Range) +
            " " + DccBridgeProtocol.N(InnerDeg) + " " + DccBridgeProtocol.N(OuterDeg);
    }
}

