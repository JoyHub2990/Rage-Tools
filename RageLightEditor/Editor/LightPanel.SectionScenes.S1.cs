using System;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public Func<Space, Scene> SectionSceneLookup_S1;

        private Scene SectionScene_S1() => SectionSceneLookup_S1?.Invoke(Workspace);
    }
}

