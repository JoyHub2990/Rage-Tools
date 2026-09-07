using System.Collections.Generic;

namespace RageLightEditor.Editor
{
    public partial class AppSettings
    {
        public List<SectionCameraPref> SectionCameras { get; set; } = new List<SectionCameraPref>();
    }

    public class SectionCameraPref
    {
        public string Section { get; set; } = "";
        public float TargetX { get; set; }
        public float TargetY { get; set; }
        public float TargetZ { get; set; }
        public float Distance { get; set; }
        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public float TargetDistance { get; set; }
        public float TargetYaw { get; set; }
        public float TargetPitch { get; set; }
        public float FieldOfView { get; set; }
        public float NearClip { get; set; }
        public float FarClip { get; set; }
        public float MaxDistance { get; set; }
        public bool Walk { get; set; }
    }
}

