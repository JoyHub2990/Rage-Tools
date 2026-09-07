using System;
using System.IO;
using System.Text;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private ApiVerbs api_W1;
        private bool apiWired_W1;

        internal ApiVerbs Api_W1()
        {
            EnsureApiWired_W1();
            return api_W1;
        }

        private void EnsureApiWired_W1()
        {
            if (apiWired_W1) return;
            apiWired_W1 = true;
            api_W1 = new ApiVerbs();

            api_W1.Add("api.ping",
                "Liveness. Echoes back what you send, with your id.",
                ApiVerbs.Schema(("echo", "string", "any text to get back", false)),
                "{pong:true, echo}",
                false, m => "{\"pong\":true,\"echo\":" + DccBridgeProtocol.S(m.Text("echo", 0, "")) + "}");

            api_W1.Add("api.describe",
                "The whole surface: every verb with its summary, schema and whether it mutates. Build your tool list from this.",
                ApiVerbs.Schema(),
                "{app, version, protocol, api, workspace, verbs:[...]}",
                false, m =>
                {
                    var sb = new StringBuilder("{\"app\":\"RAGE Tools\",\"version\":").Append(DccBridgeProtocol.S(AppVersion_P5()))
                        .Append(",\"protocol\":").Append(DccBridgeProtocol.Version)
                        .Append(",\"api\":1")
                        .Append(",\"workspace\":").Append(DccBridgeProtocol.S(SpaceNames.NameOf(panel.Workspace)))
                        .Append(",\"verbs\":").Append(api_W1.VerbsJson());
                    return sb.Append('}').ToString();
                });

            api_W1.Add("api.status",
                "Where the editor is right now: workspace, GTA install state, open files, lights, selection, camera, interior.",
                ApiVerbs.Schema(),
                "{workspace, gta, files:[...], lights, selectedLight, camera, mlo, clients, dirty}",
                false, m => StatusJson_W1());

            api_W1.Add("api.workspace",
                "Report the current workspace, or switch to another (Light, Material, Cinematic, Archive, World, Mlo, Particles, NavMesh, Terrain, Animation).",
                ApiVerbs.Schema(("space", "string", "workspace name to switch to; omit to just read", false)),
                "{workspace}",
                true, m =>
                {
                    var want = m.Text("space", 0, "");
                    if (!string.IsNullOrEmpty(want))
                    {
                        if (!SpaceNames.TryParse(want, out var space)) throw new ApiRefused("no workspace called '" + want + "'");
                        if (space != panel.Workspace) panel.SwitchWorkspace(space);
                    }
                    return "{\"workspace\":" + DccBridgeProtocol.S(SpaceNames.NameOf(panel.Workspace)) + "}";
                });

            RegisterApiVerbs_W2(api_W1);
            RegisterApiVerbs_W3(api_W1);
            RegisterApiVerbs_W4(api_W1);
        }

        partial void RegisterApiVerbs_W2(ApiVerbs api);
        partial void RegisterApiVerbs_W3(ApiVerbs api);
        partial void RegisterApiVerbs_W4(ApiVerbs api);

        partial void ApiMessage_W1(MloBridgeMessage m, ref bool handled);

        partial void ApiMessage_W1(MloBridgeMessage m, ref bool handled)
        {
            if (!(m is DccMessage d) || !DccBridgeProtocol.IsApiVerb(m.Command)) return;
            EnsureApiWired_W1();
            m.From?.Send(api_W1.Handle(d));
            handled = true;
        }

        private void Tick_W1()
        {
            if (api_W1 == null) return;
            api_W1.TickJobs();
        }

        private string StatusJson_W1()
        {
            var sb = new StringBuilder("{\"workspace\":").Append(DccBridgeProtocol.S(SpaceNames.NameOf(panel.Workspace)));

            sb.Append(",\"gta\":{\"folder\":").Append(DccBridgeProtocol.S(gameFiles?.Folder ?? ""))
              .Append(",\"ready\":").Append(gameFiles?.Ready == true ? "true" : "false")
              .Append(",\"initialising\":").Append(gameFiles?.Initialising == true ? "true" : "false");
            if (!string.IsNullOrEmpty(gameFiles?.Error)) sb.Append(",\"error\":").Append(DccBridgeProtocol.S(gameFiles.Error));
            else if (!string.IsNullOrEmpty(gameFiles?.Status)) sb.Append(",\"status\":").Append(DccBridgeProtocol.S(gameFiles.Status));
            sb.Append('}');

            sb.Append(",\"archive\":{\"ready\":").Append(panel.Archive?.Ready == true ? "true" : "false")
              .Append(",\"files\":").Append(panel.Archive?.FileCount ?? 0)
              .Append(",\"warming\":").Append(ArchiveIndexWarming_S3 ? "true" : "false")
              .Append('}');

            sb.Append(",\"files\":[");
            for (int i = 0; i < scene.Files.Count; i++)
            {
                var f = scene.Files[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(DccBridgeProtocol.S(Path.GetFileName(f.Path ?? "")))
                  .Append(",\"path\":").Append(DccBridgeProtocol.S(f.Path ?? ""))
                  .Append(",\"kind\":").Append(DccBridgeProtocol.S(f.IsYft ? "yft" : "ydr"))
                  .Append('}');
            }
            sb.Append(']');

            sb.Append(",\"lights\":").Append(scene.Lights.Count)
              .Append(",\"selectedLight\":").Append(scene.SelectedIndex)
              .Append(",\"dirty\":").Append(scene.Dirty ? "true" : "false");

            var pos = camera.Position; var tgt = camera.Target;
            sb.Append(",\"camera\":{\"pos\":").Append(DccBridgeProtocol.V(pos))
              .Append(",\"target\":").Append(DccBridgeProtocol.V(tgt))
              .Append(",\"fov\":").Append(DccBridgeProtocol.N(MathUtil.RadiansToDegrees(camera.FieldOfView)))
              .Append('}');

            var s = Creator?.Session;
            if (s == null) sb.Append(",\"mlo\":null");
            else sb.Append(",\"mlo\":{\"name\":").Append(DccBridgeProtocol.S(s.Name ?? ""))
                   .Append(",\"rooms\":").Append(s.Rooms.Count)
                   .Append(",\"portals\":").Append(s.Portals.Count)
                   .Append(",\"entities\":").Append(s.Entities.Count)
                   .Append('}');

            sb.Append(",\"clients\":").Append(mloBridge?.Clients ?? 0);
            return sb.Append('}').ToString();
        }
    }
}

