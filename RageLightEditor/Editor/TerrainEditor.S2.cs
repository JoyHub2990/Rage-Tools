using System;
using System.Collections.Generic;

namespace RageLightEditor.Editor
{
    public partial class TerrainEditor
    {

        public TerrainTextureLibrary Library;

        public bool LibraryOpen;
        public int LibrarySlot;
        public string LibraryQuery = "";
        public string LibraryGroup = "All";

        public readonly List<TerrainTextureLibrary.Entry> LibraryVisible = new List<TerrainTextureLibrary.Entry>();

        public bool LibraryWanted;

        public bool IncludeGameTextures;

        public bool RequestSubdivide;
    }
}

