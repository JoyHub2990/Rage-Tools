using System;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private void LoadWorldRenderOptions()
        {
            if (settings == null || !settings.WorldRenderOptionsSaved) return;
            WorldGrass = settings.WorldGrass;
            WorldGrassDistance = settings.WorldGrassDistance;
            WorldHdTextures = settings.WorldHdTextures;
            ToneMapCodeWalker = settings.ToneMapCodeWalker;
            AutoExposure = settings.AutoExposure;
            RageBloom = settings.RageBloom;
            GameFog = settings.GameFog;
            FogScale = settings.FogScale;
            WaterRefraction = settings.WaterRefraction;
            SunShadowDistance = settings.SunShadowDistance;
            SunCascadeCount = settings.SunCascadeCount;
            SkySaturation_V68 = settings.SkySaturation_V68 > 0 ? settings.SkySaturation_V68 : 1.35f;
            ShadowSoftness_V68 = settings.ShadowSoftness_V68 > 0 ? settings.ShadowSoftness_V68 : 0.35f;
            CloudsEnabled = settings.CloudsEnabled;
            if (!string.IsNullOrEmpty(settings.CloudFrag)) CloudFrag = settings.CloudFrag;
            CloudsFollowWeather = settings.CloudsFollowWeather;
            TimecycleHdr = settings.TimecycleHdr;
        }

        public bool openWorldResetConfirm;

        public void ResetWorldDefaults()
        {
            WorldYmapHourFilter = true;
            WorldYmapWeatherFilter = true;
            WorldVariantsIncludeDlc = true;
            WorldScriptIpls = true;
            WorldInteriorSets = 1;
            WorldFrustumCull = true;
            WorldInteriorCull = true; WorldInteriorCullRooms = true;
            if (WorldRef != null)
            {
                WorldRef.ShowNorthYankton = false;
                WorldRef.ShowScriptedYmaps = true;
                WorldRef.ShowScriptedVariants = false;
            }
            if (ProjectWindow != null)
            {
                ProjectWindow.HideGtaMap = false;
                ProjectWindow.RenderProjectItems = true;
            }
            WorldStreamRadius = 500.0f;
            WorldLodScale = 1.0f;
            WorldMaxEntities = 40000;
            RenderMode = 0;
            WorldGrass = true;
            WorldGrassDistance = 1.0f;
            WorldHdTextures = true;
            CloudsEnabled = true;
            CloudFrag = "contrails";
            CloudsFollowWeather = false;
            CloudSpeed = 1.0f;
            WaterRefraction = true;
            ShowCoronas = true;
            WorldShowCollision = false;
            WorldCollisionRange = 500.0f;
            WorldCollisionOpacity = 1.0f;
            ShowSelectionHelpers = true;
            OccluderFill = 0.12f;
            TimecycleHdr = true;
            ToneMapCodeWalker = true;
            AutoExposure = true;
            RageBloom = 1.0f;
            Exposure = 1.0f;
            SkyExposure = 1.0f;
            GameFog = true;
            FogScale = 1.0f;
            SunShadowDistance = 600.0f;
            SunCascadeCount = 4;
            SkySaturation_V68 = 1.35f;
            ShadowSoftness_V68 = 0.35f;
            WorldLightsEnabled = true;
            WorldLodLightsEnabled = true;
            WorldLightsRange = 3000.0f;
            TimecycleEnabled = true;
            ShowSky = true;
            WeatherEnabled = true;
            AutoTime = false;
            TimeSpeed = 30.0f;
            if (WeatherIndex != 0) { WeatherIndex = 0; RequestedWeather = 0; }
            if (Timecycle != null) Timecycle.SelectedRegion = 0;
            PersistWorldRenderOptions(force: true);
            MloStatus = "World settings reset to defaults.";
        }

        private void PersistWorldRenderOptions(bool force = false)
        {
            if (settings == null) return;
            bool changed = !settings.WorldRenderOptionsSaved ||
                settings.WorldGrass != WorldGrass || settings.WorldGrassDistance != WorldGrassDistance ||
                settings.WorldHdTextures != WorldHdTextures || settings.ToneMapCodeWalker != ToneMapCodeWalker ||
                settings.AutoExposure != AutoExposure || settings.RageBloom != RageBloom ||
                settings.GameFog != GameFog || settings.FogScale != FogScale ||
                settings.WaterRefraction != WaterRefraction || settings.SunShadowDistance != SunShadowDistance ||
                settings.SunCascadeCount != SunCascadeCount || settings.CloudsEnabled != CloudsEnabled ||
                Math.Abs(settings.SkySaturation_V68 - SkySaturation_V68) > 0.001f ||
                Math.Abs(settings.ShadowSoftness_V68 - ShadowSoftness_V68) > 0.001f ||
                settings.CloudFrag != CloudFrag || settings.CloudsFollowWeather != CloudsFollowWeather ||
                settings.TimecycleHdr != TimecycleHdr;
            if (!changed) return;
            if (!force && ImGuiNET.ImGui.IsAnyItemActive()) return;
            settings.WorldGrass = WorldGrass;
            settings.WorldGrassDistance = WorldGrassDistance;
            settings.WorldHdTextures = WorldHdTextures;
            settings.ToneMapCodeWalker = ToneMapCodeWalker;
            settings.AutoExposure = AutoExposure;
            settings.RageBloom = RageBloom;
            settings.GameFog = GameFog;
            settings.FogScale = FogScale;
            settings.WaterRefraction = WaterRefraction;
            settings.SunShadowDistance = SunShadowDistance;
            settings.SunCascadeCount = SunCascadeCount;
            settings.SkySaturation_V68 = SkySaturation_V68;
            settings.ShadowSoftness_V68 = ShadowSoftness_V68;
            settings.CloudsEnabled = CloudsEnabled;
            settings.CloudFrag = CloudFrag;
            settings.CloudsFollowWeather = CloudsFollowWeather;
            settings.TimecycleHdr = TimecycleHdr;
            settings.WorldRenderOptionsSaved = true;
            settings.Save();
        }
    }
}

