using System;
using System.Linq;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_TerrainProps_V20(Action<string, bool, string> check)
        {
            var te = new TerrainEditor();

            TerrainEditor.Part Quad(float x)
            {
                var v = new MeshVertex[4];
                v[0].Position = new Vector3(x, 0, 0);
                v[1].Position = new Vector3(x + 1, 0, 0);
                v[2].Position = new Vector3(x + 1, 1, 0);
                v[3].Position = new Vector3(x, 1, 0);
                return new TerrainEditor.Part { Verts = v, Indices = new ushort[] { 0, 1, 2, 0, 2, 3 } };
            }

            int g0 = te.BeginImport_V20("road", @"C:\road.ydr", replace: true);
            te.Parts.Add(Quad(0)); te.Parts.Add(Quad(1));
            foreach (var p in te.Parts) p.Group_V20 = g0;
            check("v20 terrain: an import makes one prop",
                  te.Props_V20.Count == 1 && te.Props_V20[0].Name == "road", te.Props_V20.Count.ToString());

            int g1 = te.BeginImport_V20("verge", @"C:\verge.ydr", replace: false);
            int first = te.Parts.Count;
            te.Parts.Add(Quad(5));
            for (int i = first; i < te.Parts.Count; i++) te.Parts[i].Group_V20 = g1;
            check("v20 terrain: a second import sits BESIDE the first",
                  te.Props_V20.Count == 2 && te.Parts.Count == 3,
                  $"{te.Props_V20.Count} prop(s), {te.Parts.Count} part(s)");
            check("v20 terrain: ...and each prop owns its own parts",
                  te.PartsOf_V20(g0).Count() == 2 && te.PartsOf_V20(g1).Count() == 1,
                  $"{te.PartsOf_V20(g0).Count()} + {te.PartsOf_V20(g1).Count()}");

            te.Props_V20[g1].Visible = false;
            check("v20 terrain: a hidden prop drops out of what is drawn",
                  te.VisibleParts_V20().Count() == 2 && !te.IsVisible_V20(g1),
                  te.VisibleParts_V20().Count() + " visible part(s)");
            te.Props_V20[g1].Visible = true;

            te.TouchProp_V20(g1);
            check("v20 terrain: painting stars the prop it touched, and only that one",
                  te.Props_V20[g1].Dirty && !te.Props_V20[g0].Dirty && te.DirtyPropCount_V20 == 1,
                  te.DirtyPropCount_V20 + " unsaved");

            var keptName = te.Props_V20[g1].Name;
            te.RemoveProp_V20(g0);
            check("v20 terrain: removing a prop takes its parts with it",
                  te.Props_V20.Count == 1 && te.Parts.Count == 1, $"{te.Props_V20.Count} / {te.Parts.Count}");
            check("v20 terrain: ...and the survivor still owns its parts after the renumber",
                  te.Props_V20[0].Name == keptName && te.Parts[0].Group_V20 == 0,
                  $"'{te.Props_V20[0].Name}' group {te.Parts[0].Group_V20}");

            var old = new TerrainEditor();
            old.Parts.Add(Quad(0));
            old.EnsureGroups_V20("legacy");
            check("v20 terrain: a mesh with no groups is adopted into one",
                  old.Props_V20.Count == 1 && old.Parts[0].Group_V20 == 0, old.Props_V20.Count.ToString());
        }
    }
}

