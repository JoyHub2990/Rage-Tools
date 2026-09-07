using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private const BindingFlags AllMembers =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        partial void RegisterApiVerbs_W4(ApiVerbs api)
        {
            api.Add("api.state.roots",
                "The starting points for a path: the objects the editor is made of. Everything else is reached from these.",
                ApiVerbs.Schema(),
                "{roots:[{name, type, summary}]}",
                false, m => RootsJson_W4());

            api.Add("api.state.list",
                "What is on an object: its fields, properties (with current values) and methods (with signatures). This is how you find out what can be read, set or called.",
                ApiVerbs.Schema(("path", "string", "e.g. \"scene\", \"session.Rooms[0]\", \"form\"; empty for the roots", false),
                                ("filter", "string", "only members whose name contains this", false)),
                "{path, type, fields:[...], properties:[...], methods:[...]}",
                false, m => ListMembers_W4(m.Text("path", 0, ""), m.Text("filter", 1, "")));

            api.Add("api.state.get",
                "Read any value by path - a number, a string, a whole object, a list. Depth controls how far into nested objects it expands.",
                ApiVerbs.Schema(("path", "string", "e.g. \"scene.Lights[0].Falloff\"", true),
                                ("depth", "integer", "how many levels of nested objects to expand (default 1, max 5)", false)),
                "the value as JSON",
                false, m =>
                {
                    var v = ResolveValue_W4(m.Text("path", 0, ""));
                    return ToJson_W4(v, Math.Clamp(m.Int("depth", 1, 1), 0, 5));
                });

            api.Add("api.state.set",
                "Write any value by path. Numbers, strings, booleans, enums (by name or number), vectors and quaternions (as arrays) are all converted to whatever the target actually is.",
                ApiVerbs.Schema(("path", "string", "e.g. \"scene.Lights[0].Intensity\"", true),
                                ("value", "string", "the new value - any JSON: 5, \"Spot\", [1,0,0], true", true)),
                "{path, was, now}",
                true, m => SetValue_W4(m, m.Text("path", 0, "")));

            api.Add("api.state.call",
                "Call any method the editor has - including the ones that keep a format's invariants (removing an MLO room remaps its portals; saving writes the file properly). The path's last part is the method name.",
                ApiVerbs.Schema(("path", "string", "e.g. \"session.AddRoom\", \"scene.SaveOne\"", true),
                                ("args", "array", "arguments in order; JSON values are converted to the parameter types", false)),
                "{called, returned}",
                true, m => CallMethod_W4(m, m.Text("path", 0, "")));
        }

        private (string Name, object Value, string Summary)[] Roots_W4() => new (string, object, string)[]
        {
            ("form",      this,             "the whole editor - every workspace's state lives in here"),
            ("scene",     scene,            "open models, their lights and materials"),
            ("panel",     panel,            "the UI state: workspace, render mode, preview hour, toggles"),
            ("camera",    camera,           "the viewport camera"),
            ("game",      gameFiles,        "the GTA install: archives, textures, the file cache"),
            ("settings",  settings,         "saved preferences"),
            ("timecycle", timecycle,        "the time-of-day model and its modifiers"),
            ("creator",   Creator,          "the MLO workspace's panel"),
            ("session",   Creator?.Session,  "the interior being edited: rooms, portals, entities, sets"),
            ("archive",   panel?.Archive,   "the flat index of every file in every archive"),
            ("project",   projCtl,          "the CodeWalker project controller"),
        };

        private string RootsJson_W4()
        {
            var sb = new StringBuilder("{\"roots\":[");
            bool first = true;
            foreach (var (name, value, summary) in Roots_W4())
            {
                if (!first) sb.Append(','); first = false;
                sb.Append("{\"name\":").Append(DccBridgeProtocol.S(name))
                  .Append(",\"type\":").Append(DccBridgeProtocol.S(value?.GetType().Name ?? "null"))
                  .Append(",\"summary\":").Append(DccBridgeProtocol.S(summary))
                  .Append(",\"present\":").Append(value != null ? "true" : "false")
                  .Append('}');
            }
            return sb.Append("]}").ToString();
        }

        private static (string Name, int[] Indexes)[] ParsePath_W4(string path)
        {
            var steps = new List<(string, int[])>();
            foreach (var raw in (path ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;
                var indexes = new List<int>();
                int br = part.IndexOf('[');
                var name = br < 0 ? part : part.Substring(0, br);
                while (br >= 0)
                {
                    int end = part.IndexOf(']', br);
                    if (end < 0) break;
                    if (int.TryParse(part.Substring(br + 1, end - br - 1), out int ix)) indexes.Add(ix);
                    br = part.IndexOf('[', end);
                }
                steps.Add((name.Trim(), indexes.ToArray()));
            }
            return steps.ToArray();
        }

        private object RootValue_W4(string name)
        {
            foreach (var (n, v, _) in Roots_W4())
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return v;
            throw new ApiRefused("no root called '" + name + "' - api.state.roots lists them");
        }

        private object ResolveValue_W4(string path)
        {
            var steps = ParsePath_W4(path);
            if (steps.Length == 0) throw new ApiRefused("no path");
            object current = RootValue_W4(steps[0].Name);
            current = Index_W4(current, steps[0].Indexes, steps[0].Name);
            for (int i = 1; i < steps.Length; i++)
            {
                if (current == null) throw new ApiRefused("'" + steps[i - 1].Name + "' is null, so '" + steps[i].Name + "' cannot be read");
                current = ReadMember_W4(current, steps[i].Name);
                current = Index_W4(current, steps[i].Indexes, steps[i].Name);
            }
            return current;
        }

        private (object Owner, string Member, int[] Indexes) ResolveOwner_W4(string path)
        {
            var steps = ParsePath_W4(path);
            if (steps.Length == 0) throw new ApiRefused("no path");
            if (steps.Length == 1) throw new ApiRefused("'" + path + "' is a root; a path needs a member to write to");
            object current = RootValue_W4(steps[0].Name);
            current = Index_W4(current, steps[0].Indexes, steps[0].Name);
            for (int i = 1; i < steps.Length - 1; i++)
            {
                if (current == null) throw new ApiRefused("'" + steps[i - 1].Name + "' is null");
                current = ReadMember_W4(current, steps[i].Name);
                current = Index_W4(current, steps[i].Indexes, steps[i].Name);
            }
            var last = steps[steps.Length - 1];
            return (current, last.Name, last.Indexes);
        }

        private static object Index_W4(object value, int[] indexes, string where)
        {
            foreach (var ix in indexes)
            {
                if (value == null) throw new ApiRefused("'" + where + "' is null, so it cannot be indexed");
                if (value is Array arr)
                {
                    if (ix < 0 || ix >= arr.Length) throw new ApiRefused($"'{where}' has {arr.Length} items; [{ix}] is out of range");
                    value = arr.GetValue(ix);
                }
                else if (value is IList list)
                {
                    if (ix < 0 || ix >= list.Count) throw new ApiRefused($"'{where}' has {list.Count} items; [{ix}] is out of range");
                    value = list[ix];
                }
                else throw new ApiRefused("'" + where + "' is not a list, so it cannot be indexed");
            }
            return value;
        }

        private static object ReadMember_W4(object owner, string name)
        {
            var type = owner as Type ?? owner.GetType();
            var target = owner is Type ? null : owner;
            var p = type.GetProperty(name, AllMembers);
            if (p != null && p.CanRead)
            {
                try { return p.GetValue(target); }
                catch (Exception ex) { throw new ApiRefused("reading '" + name + "' threw: " + (ex.InnerException ?? ex).Message); }
            }
            var f = type.GetField(name, AllMembers);
            if (f != null) return f.GetValue(target);
            throw new ApiRefused("'" + type.Name + "' has no '" + name + "' - api.state.list shows what it does have");
        }

        private string ListMembers_W4(string path, string filter)
        {
            if (string.IsNullOrWhiteSpace(path)) return RootsJson_W4();
            var value = ResolveValue_W4(path);
            if (value == null) throw new ApiRefused("'" + path + "' is null");
            var type = value.GetType();
            bool Want(string n) => string.IsNullOrEmpty(filter) || n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

            var sb = new StringBuilder("{\"path\":").Append(DccBridgeProtocol.S(path))
                .Append(",\"type\":").Append(DccBridgeProtocol.S(type.FullName));

            if (value is Array a) sb.Append(",\"count\":").Append(a.Length);
            else if (value is IList l) sb.Append(",\"count\":").Append(l.Count);

            sb.Append(",\"properties\":[");
            bool first = true;
            foreach (var p in type.GetProperties(AllMembers).OrderBy(p => p.Name))
            {
                if (!Want(p.Name) || p.GetIndexParameters().Length > 0) continue;
                if (!first) sb.Append(','); first = false;
                sb.Append("{\"name\":").Append(DccBridgeProtocol.S(p.Name))
                  .Append(",\"type\":").Append(DccBridgeProtocol.S(Pretty_W4(p.PropertyType)))
                  .Append(",\"canWrite\":").Append(p.CanWrite ? "true" : "false")
                  .Append(",\"value\":").Append(SafePreview_W4(() => p.GetValue(value)))
                  .Append('}');
            }
            sb.Append("],\"fields\":[");
            first = true;
            foreach (var f in type.GetFields(AllMembers).OrderBy(f => f.Name))
            {
                if (!Want(f.Name) || f.Name.Contains('<')) continue;
                if (!first) sb.Append(','); first = false;
                sb.Append("{\"name\":").Append(DccBridgeProtocol.S(f.Name))
                  .Append(",\"type\":").Append(DccBridgeProtocol.S(Pretty_W4(f.FieldType)))
                  .Append(",\"canWrite\":").Append(f.IsInitOnly || f.IsLiteral ? "false" : "true")
                  .Append(",\"value\":").Append(SafePreview_W4(() => f.GetValue(value)))
                  .Append('}');
            }
            sb.Append("],\"methods\":[");
            first = true;
            foreach (var mi in type.GetMethods(AllMembers).OrderBy(x => x.Name))
            {
                if (!Want(mi.Name) || mi.IsSpecialName) continue;
                if (mi.DeclaringType == typeof(object)) continue;
                if (!first) sb.Append(','); first = false;
                var ps = string.Join(", ", mi.GetParameters().Select(p => Pretty_W4(p.ParameterType) + " " + p.Name));
                sb.Append("{\"name\":").Append(DccBridgeProtocol.S(mi.Name))
                  .Append(",\"signature\":").Append(DccBridgeProtocol.S($"{Pretty_W4(mi.ReturnType)} {mi.Name}({ps})"))
                  .Append(",\"args\":").Append(mi.GetParameters().Length)
                  .Append('}');
            }
            return sb.Append("]}").ToString();
        }

        private static string Pretty_W4(Type t)
        {
            if (t == null) return "?";
            if (t == typeof(void)) return "void";
            if (t.IsGenericType)
                return t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(Pretty_W4)) + ">";
            return t.Name;
        }

        private static string SafePreview_W4(Func<object> read)
        {
            try { return ToJson_W4(read(), 0); }
            catch (Exception ex) { return DccBridgeProtocol.S("<threw: " + (ex.InnerException ?? ex).Message + ">"); }
        }

        private static string ToJson_W4(object v, int depth)
        {
            switch (v)
            {
                case null: return "null";
                case bool b: return b ? "true" : "false";
                case string s: return DccBridgeProtocol.S(s);
                case float f: return float.IsFinite(f) ? f.ToString("0.#####", CultureInfo.InvariantCulture) : DccBridgeProtocol.S(f.ToString(CultureInfo.InvariantCulture));
                case double d: return double.IsFinite(d) ? d.ToString("0.#####", CultureInfo.InvariantCulture) : DccBridgeProtocol.S(d.ToString(CultureInfo.InvariantCulture));
                case decimal m: return m.ToString(CultureInfo.InvariantCulture);
                case Enum e: return DccBridgeProtocol.S(e.ToString());
                case Vector2 v2: return $"[{N_W4(v2.X)},{N_W4(v2.Y)}]";
                case Vector3 v3: return $"[{N_W4(v3.X)},{N_W4(v3.Y)},{N_W4(v3.Z)}]";
                case Vector4 v4: return $"[{N_W4(v4.X)},{N_W4(v4.Y)},{N_W4(v4.Z)},{N_W4(v4.W)}]";
                case Quaternion q: return $"[{N_W4(q.X)},{N_W4(q.Y)},{N_W4(q.Z)},{N_W4(q.W)}]";
                case IntPtr p: return p.ToInt64().ToString(CultureInfo.InvariantCulture);
            }

            var t = v.GetType();
            if (t.IsPrimitive) return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "null";

            if (v is IDictionary dict)
            {
                if (depth <= 0) return DccBridgeProtocol.S($"<{Pretty_W4(t)}, {dict.Count} entries>");
                var sb = new StringBuilder("{");
                bool first = true; int n = 0;
                foreach (DictionaryEntry kv in dict)
                {
                    if (n++ >= 100) break;
                    if (!first) sb.Append(','); first = false;
                    sb.Append(DccBridgeProtocol.S(Convert.ToString(kv.Key, CultureInfo.InvariantCulture) ?? "")).Append(':').Append(ToJson_W4(kv.Value, depth - 1));
                }
                return sb.Append('}').ToString();
            }

            if (v is IEnumerable seq && !(v is string))
            {
                var items = new List<object>();
                int total = 0;
                foreach (var item in seq) { if (total < 200) items.Add(item); total++; }
                if (depth <= 0) return DccBridgeProtocol.S($"<{Pretty_W4(t)}, {total} items>");
                var sb = new StringBuilder("[");
                for (int i = 0; i < items.Count; i++) { if (i > 0) sb.Append(','); sb.Append(ToJson_W4(items[i], depth - 1)); }
                if (total > items.Count) sb.Append(',').Append(DccBridgeProtocol.S($"<{total - items.Count} more>"));
                return sb.Append(']').ToString();
            }

            if (depth <= 0) return DccBridgeProtocol.S("<" + Pretty_W4(t) + ">");

            var ob = new StringBuilder("{\"$type\":").Append(DccBridgeProtocol.S(Pretty_W4(t)));
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                ob.Append(',').Append(DccBridgeProtocol.S(p.Name)).Append(':').Append(SafePreview_W4(() => p.GetValue(v)));
            }
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.Name.Contains('<')) continue;
                ob.Append(',').Append(DccBridgeProtocol.S(f.Name)).Append(':').Append(SafePreview_W4(() => f.GetValue(v)));
            }
            return ob.Append('}').ToString();
        }

        private static string N_W4(float f) => f.ToString("0.#####", CultureInfo.InvariantCulture);

        private static object Coerce_W4(JsonElement e, Type want)
        {
            if (want == typeof(object)) return FromJsonLoose_W4(e);
            var nullable = Nullable.GetUnderlyingType(want);
            if (nullable != null)
            {
                if (e.ValueKind == JsonValueKind.Null) return null;
                want = nullable;
            }

            if (want == typeof(string))
                return e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText();

            if (want.IsEnum)
            {
                if (e.ValueKind == JsonValueKind.String)
                {
                    var name = e.GetString() ?? "";
                    foreach (var candidate in Enum.GetNames(want))
                        if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) return Enum.Parse(want, candidate);
                    throw new ApiRefused($"'{name}' is not one of {string.Join(", ", Enum.GetNames(want))}");
                }
                if (e.ValueKind == JsonValueKind.Number) return Enum.ToObject(want, e.GetInt64());
            }

            if (want == typeof(bool))
            {
                if (e.ValueKind == JsonValueKind.True) return true;
                if (e.ValueKind == JsonValueKind.False) return false;
                if (e.ValueKind == JsonValueKind.Number) return e.GetDouble() != 0.0;
                if (e.ValueKind == JsonValueKind.String) return e.GetString()?.ToLowerInvariant() is "1" or "true" or "on" or "yes";
            }

            if (want == typeof(Vector2) || want == typeof(Vector3) || want == typeof(Vector4) || want == typeof(Quaternion))
            {
                var f = Floats_W4(e);
                if (want == typeof(Vector2)) return new Vector2(At_W4(f, 0), At_W4(f, 1));
                if (want == typeof(Vector3)) return new Vector3(At_W4(f, 0), At_W4(f, 1), At_W4(f, 2));
                if (want == typeof(Vector4)) return new Vector4(At_W4(f, 0), At_W4(f, 1), At_W4(f, 2), At_W4(f, 3));
                return new Quaternion(At_W4(f, 0), At_W4(f, 1), At_W4(f, 2), f.Length > 3 ? f[3] : 1.0f);
            }

            if (e.ValueKind == JsonValueKind.Null) return null;

            if (want.IsArray && e.ValueKind == JsonValueKind.Array)
            {
                var elem = want.GetElementType()!;
                var arr = Array.CreateInstance(elem, e.GetArrayLength());
                int i = 0;
                foreach (var item in e.EnumerateArray()) arr.SetValue(Coerce_W4(item, elem), i++);
                return arr;
            }

            if (e.ValueKind == JsonValueKind.Number || e.ValueKind == JsonValueKind.String)
            {
                try
                {
                    double d = e.ValueKind == JsonValueKind.Number ? e.GetDouble()
                             : double.Parse(e.GetString() ?? "0", CultureInfo.InvariantCulture);
                    return Convert.ChangeType(d, want, CultureInfo.InvariantCulture);
                }
                catch { }
            }

            throw new ApiRefused($"cannot use {e.ValueKind} as {Pretty_W4(want)}");
        }

        private static float[] Floats_W4(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.Array) throw new ApiRefused("expected an array of numbers, e.g. [1,2,3]");
            var list = new List<float>();
            foreach (var item in e.EnumerateArray())
                list.Add(item.ValueKind == JsonValueKind.Number ? (float)item.GetDouble() : 0.0f);
            return list.ToArray();
        }

        private static float At_W4(float[] f, int i) => i < f.Length ? f[i] : 0.0f;

        private static object FromJsonLoose_W4(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetInt64(out var l) ? l : (object)e.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };

        private string SetValue_W4(DccMessage m, string path)
        {
            if (!m.Json || m.Root.ValueKind != JsonValueKind.Object || !m.Root.TryGetProperty("value", out var wanted))
                throw new ApiRefused("no value - send it as JSON: {\"cmd\":\"api.state.set\",\"path\":\"...\",\"value\":5}");

            var (owner, name, indexes) = ResolveOwner_W4(path);
            if (owner == null) throw new ApiRefused("'" + path + "' has no object to write to");

            if (indexes.Length > 0)
            {
                var container = ReadMember_W4(owner, name);
                container = Index_W4(container, indexes.Take(indexes.Length - 1).ToArray(), name);
                int ix = indexes[indexes.Length - 1];
                if (container is Array arr)
                {
                    if (ix < 0 || ix >= arr.Length) throw new ApiRefused($"[{ix}] is out of range ({arr.Length} items)");
                    var was = ToJson_W4(arr.GetValue(ix), 0);
                    arr.SetValue(Coerce_W4(wanted, arr.GetType().GetElementType()!), ix);
                    return Wrote_W4(path, was, ToJson_W4(arr.GetValue(ix), 0));
                }
                if (container is IList list)
                {
                    if (ix < 0 || ix >= list.Count) throw new ApiRefused($"[{ix}] is out of range ({list.Count} items)");
                    var elem = container.GetType().IsGenericType ? container.GetType().GetGenericArguments()[0] : typeof(object);
                    var was = ToJson_W4(list[ix], 0);
                    list[ix] = Coerce_W4(wanted, elem);
                    return Wrote_W4(path, was, ToJson_W4(list[ix], 0));
                }
                throw new ApiRefused("'" + name + "' is not a list");
            }

            var type = owner.GetType();
            var prop = type.GetProperty(name, AllMembers);
            if (prop != null)
            {
                if (!prop.CanWrite) throw new ApiRefused("'" + name + "' is read-only");
                var was = SafePreview_W4(() => prop.GetValue(owner));
                try { prop.SetValue(owner, Coerce_W4(wanted, prop.PropertyType)); }
                catch (ApiRefused) { throw; }
                catch (Exception ex) { throw new ApiRefused("setting '" + name + "' threw: " + (ex.InnerException ?? ex).Message); }
                return Wrote_W4(path, was, SafePreview_W4(() => prop.GetValue(owner)));
            }
            var field = type.GetField(name, AllMembers);
            if (field != null)
            {
                if (field.IsInitOnly || field.IsLiteral) throw new ApiRefused("'" + name + "' is read-only");
                var was = SafePreview_W4(() => field.GetValue(owner));
                try { field.SetValue(owner, Coerce_W4(wanted, field.FieldType)); }
                catch (ApiRefused) { throw; }
                catch (Exception ex) { throw new ApiRefused("setting '" + name + "' threw: " + (ex.InnerException ?? ex).Message); }
                return Wrote_W4(path, was, SafePreview_W4(() => field.GetValue(owner)));
            }
            throw new ApiRefused("'" + type.Name + "' has no '" + name + "'");
        }

        private string Wrote_W4(string path, string was, string now)
        {
            scene.Dirty = true;
            return "{\"path\":" + DccBridgeProtocol.S(path) + ",\"was\":" + was + ",\"now\":" + now + "}";
        }

        private string CallMethod_W4(DccMessage m, string path)
        {
            var (owner, name, _) = ResolveOwner_W4(path);
            if (owner == null) throw new ApiRefused("'" + path + "' has no object to call on");

            var args = new List<JsonElement>();
            if (m.Json && m.Root.ValueKind == JsonValueKind.Object &&
                m.Root.TryGetProperty("args", out var arr) && arr.ValueKind == JsonValueKind.Array)
                args.AddRange(arr.EnumerateArray());

            var type = owner.GetType();
            var candidates = type.GetMethods(AllMembers)
                .Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) && !x.IsSpecialName)
                .ToList();
            if (candidates.Count == 0)
                throw new ApiRefused("'" + type.Name + "' has no method '" + name + "' - api.state.list shows what it has");

            var method = candidates.FirstOrDefault(x => x.GetParameters().Length == args.Count)
                      ?? candidates.FirstOrDefault(x => x.GetParameters().Count(p => !p.IsOptional) <= args.Count
                                                     && x.GetParameters().Length >= args.Count);
            if (method == null)
                throw new ApiRefused($"'{name}' takes {string.Join(" or ", candidates.Select(c => c.GetParameters().Length))} argument(s), not {args.Count}");

            var ps = method.GetParameters();
            var values = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                if (i < args.Count) values[i] = Coerce_W4(args[i], ps[i].ParameterType);
                else if (ps[i].IsOptional) values[i] = ps[i].DefaultValue;
                else if (ps[i].ParameterType.IsValueType) values[i] = Activator.CreateInstance(ps[i].ParameterType);
                else values[i] = null;
            }

            object returned;
            try { returned = method.Invoke(method.IsStatic ? null : owner, values); }
            catch (Exception ex) { throw new ApiRefused("'" + name + "' threw: " + (ex.InnerException ?? ex).Message); }

            scene.Dirty = true;
            var sb = new StringBuilder("{\"called\":").Append(DccBridgeProtocol.S(method.Name))
                .Append(",\"returned\":").Append(ToJson_W4(returned, 1));
            bool anyOut = ps.Any(p => p.IsOut || p.ParameterType.IsByRef);
            if (anyOut)
            {
                sb.Append(",\"outputs\":{");
                bool first = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    if (!ps[i].IsOut && !ps[i].ParameterType.IsByRef) continue;
                    if (!first) sb.Append(','); first = false;
                    sb.Append(DccBridgeProtocol.S(ps[i].Name ?? ("arg" + i))).Append(':').Append(ToJson_W4(values[i], 1));
                }
                sb.Append('}');
            }
            return sb.Append('}').ToString();
        }
    }
}

