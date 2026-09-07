using System;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public bool ControlTimeOfDay = true;
        public bool TimeScrubbingByMouse;

        public event Action<Space, Space> WorkspaceSwitching;

        public void SwitchWorkspace(Space space)
        {
            if (Workspace == space) return;
            var was = Workspace;
            Workspace = space;
            if (space == Space.Cinematic) EnterCineWorkspace();
            else if (was == Space.Cinematic) LeaveCineWorkspace();
            ApplyThemeFromSettings(save: false);
            WorkspaceChanged?.Invoke(space == Space.Material);
        }

        public struct WorkspaceTimecycleState
        {
            public bool Valid;
            public int Modifier;
            public float ModifierStrength;
            public bool TimecycleEnabled, WeatherEnabled, ShowSky, AutoTime;
            public int WeatherIndex, GameTimecycleIndex;
            public float PreviewHour;
        }
        private readonly WorkspaceTimecycleState[] workspaceTc = new WorkspaceTimecycleState[Enum.GetValues(typeof(Space)).Length];

        public WorkspaceTimecycleState GetWorkspaceTimecycleState(Space s) => workspaceTc[(int)s];

        private WorkspaceTimecycleState CaptureTimecycleState() => new WorkspaceTimecycleState
        {
            Valid = true,
            Modifier = Timecycle?.SelectedModifier ?? -1,
            ModifierStrength = Timecycle?.ModifierStrength ?? 1.0f,
            TimecycleEnabled = TimecycleEnabled,
            WeatherEnabled = WeatherEnabled,
            ShowSky = ShowSky,
            AutoTime = AutoTime,
            WeatherIndex = WeatherIndex,
            GameTimecycleIndex = GameTimecycleIndex,
            PreviewHour = PreviewHour,
        };

        private void SaveWorkspaceState(Space s)
        {
            workspaceTc[(int)s] = CaptureTimecycleState();
        }

        private void RestoreWorkspaceState(Space s)
        {
            var st = workspaceTc[(int)s];
            if (!st.Valid)
            {
                if (s == Space.World && Timecycle != null) Timecycle.SelectedModifier = -1;
                return;
            }
            int liveWeather = WeatherIndex, liveCycle = GameTimecycleIndex;
            if (Timecycle != null)
            {
                Timecycle.SelectedModifier = st.Modifier;
                Timecycle.ModifierStrength = st.ModifierStrength;
            }
            TimecycleEnabled = st.TimecycleEnabled;
            WeatherEnabled = st.WeatherEnabled;
            ShowSky = st.ShowSky;
            AutoTime = st.AutoTime;
            PreviewHour = st.PreviewHour;
            WeatherIndex = st.WeatherIndex;
            GameTimecycleIndex = st.GameTimecycleIndex;
            if (st.WeatherIndex != liveWeather)
                RequestedWeather = st.WeatherIndex;
            else if (st.GameTimecycleIndex != liveCycle && st.GameTimecycleIndex >= 0)
                RequestLoadGameTimecycle?.Invoke(st.GameTimecycleIndex);
        }

        public void ScrubTimeOfDay(float dx, float dy)
        {
            float tod = PreviewHour + (dx - dy) / 30.0f;
            while (tod >= 24.0f) tod -= 24.0f;
            while (tod < 0.0f) tod += 24.0f;
            PreviewHour = tod;
        }
    }
}

