using System;
using System.Collections.Generic;
using CodeWalker;
using CodeWalker.GameFiles;
using CodeWalker.World;
using SharpDX;

namespace RageLightEditor.Editor
{
    public enum WorldSelectionMode
    {
        None = 0,
        Entity = 1,
        EntityExtension = 2,
        ArchetypeExtension = 3,
        TimeCycleModifier = 4,
        CarGenerator = 5,
        Grass = 6,
        WaterQuad = 7,
        Collision = 8,
        NavMesh = 9,
        Path = 10,
        TrainTrack = 11,
        LodLights = 12,
        MloInstance = 13,
        Scenario = 14,
        PopZone = 15,
        Heightmap = 16,
        Watermap = 17,
        Audio = 18,
        Occlusion = 19,
        CalmingQuad = 20,
        WaveQuad = 21,
        EntityPrecision = 100,
        Light = 101,
    }

    public enum WorldWidgetAxis
    {
        None,
        Z,
        XYZ,
    }

    public partial struct WorldSelection
    {
        public YmapEntityDef EntityDef;
        public Archetype Archetype;
        public YmapTimeCycleModifier TimeCycleModifier;
        public YmapCarGen CarGenerator;
        public YmapGrassInstanceBatch GrassBatch;
        public YmapLODLight LodLight;
        public YmapBoxOccluder BoxOccluder;
        public YmapOccludeModelTriangle OccludeModelTri;
        public YmapEntityDef MloEntityDef;
        public MCMloRoomDef MloRoomDef;
        public MCMloPortalDef MloPortalDef;
        public WaterQuad WaterQuad;
        public WaterCalmingQuad CalmingQuad;
        public WaterWaveQuad WaveQuad;
        public Bounds CollisionBounds;
        public BoundPolygon CollisionPoly;
        public BoundVertex CollisionVertex;
        public YnvPoly NavPoly;
        public YnvPoint NavPoint;
        public YnvPortal NavPortal;
        public YndNode PathNode;
        public YndLink PathLink;
        public TrainTrackNode TrainTrackNode;
        public ScenarioNode ScenarioNode;
        public MCScenarioChainingEdge ScenarioEdge;
        public AudioPlacement Audio;
        public WorldSelection[] MultipleSelectionItems;

        public Vector3 BBOffset;
        public Quaternion BBOrientation;
        public BoundingBox AABB;
        public BoundingSphere BSphere;
        public Vector3 CamRel;
        public float HitDist;

        public static WorldSelection Empty
        {
            get { var s = new WorldSelection(); s.Clear(); return s; }
        }

        public bool HasValue =>
            (EntityDef != null) || (Archetype != null) || (TimeCycleModifier != null) || (CarGenerator != null) ||
            (GrassBatch != null) || (LodLight != null) || (BoxOccluder != null) || (OccludeModelTri != null) ||
            (MloEntityDef != null) || (MloRoomDef != null) || (MloPortalDef != null) || (WaterQuad != null) || (CalmingQuad != null) ||
            (WaveQuad != null) || (CollisionBounds != null) || (CollisionPoly != null) || (CollisionVertex != null) ||
            (NavPoly != null) || (NavPoint != null) || (NavPortal != null) || (PathNode != null) || (PathLink != null) ||
            (TrainTrackNode != null) || (ScenarioNode != null) || (ScenarioEdge != null) || (Audio != null) ||
            (MultipleSelectionItems != null) || (Light != null);

        public bool HasHit => HasValue;

        public YmapFile OwnerYmap =>
            EntityDef?.Ymap ?? CarGenerator?.Ymap ?? LodLight?.Ymap ?? BoxOccluder?.Ymap ?? OccludeModelTri?.Ymap ??
            GrassBatch?.Ymap ?? TimeCycleModifier?.Ymap ?? MloEntityDef?.Ymap;

        public bool CheckForChanges(in WorldSelection mhit)
        {
            return (EntityDef != mhit.EntityDef)
                || (Archetype != mhit.Archetype)
                || (TimeCycleModifier != mhit.TimeCycleModifier)
                || (CarGenerator != mhit.CarGenerator)
                || (GrassBatch != mhit.GrassBatch)
                || (LodLight != mhit.LodLight)
                || (BoxOccluder != mhit.BoxOccluder)
                || (OccludeModelTri != mhit.OccludeModelTri)
                || (MloEntityDef != mhit.MloEntityDef)
                || (MloRoomDef != mhit.MloRoomDef)
                || (MloPortalDef != mhit.MloPortalDef)
                || (WaterQuad != mhit.WaterQuad)
                || (CalmingQuad != mhit.CalmingQuad)
                || (WaveQuad != mhit.WaveQuad)
                || (CollisionBounds != mhit.CollisionBounds)
                || (CollisionPoly != mhit.CollisionPoly)
                || (CollisionVertex != mhit.CollisionVertex)
                || (NavPoly != mhit.NavPoly)
                || (NavPoint != mhit.NavPoint)
                || (NavPortal != mhit.NavPortal)
                || (PathNode != mhit.PathNode)
                || (PathLink != mhit.PathLink)
                || (TrainTrackNode != mhit.TrainTrackNode)
                || (ScenarioNode != mhit.ScenarioNode)
                || (ScenarioEdge != mhit.ScenarioEdge)
                || (Audio != mhit.Audio)
                || (MultipleSelectionItems != mhit.MultipleSelectionItems)
                || (Light != mhit.Light);
        }

        public bool SameAs(in WorldSelection o) => !CheckForChanges(o);

        public void Clear()
        {
            EntityDef = null;
            Archetype = null;
            TimeCycleModifier = null;
            CarGenerator = null;
            GrassBatch = null;
            LodLight = null;
            BoxOccluder = null;
            OccludeModelTri = null;
            MloEntityDef = null;
            MloRoomDef = null;
            MloPortalDef = null;
            WaterQuad = null;
            CalmingQuad = null;
            WaveQuad = null;
            CollisionBounds = null;
            CollisionPoly = null;
            CollisionVertex = null;
            NavPoly = null;
            NavPoint = null;
            NavPortal = null;
            PathNode = null;
            PathLink = null;
            TrainTrackNode = null;
            ScenarioNode = null;
            ScenarioEdge = null;
            Audio = null;
            MultipleSelectionItems = null;
            ClearLight();
            BBOffset = Vector3.Zero;
            BBOrientation = Quaternion.Identity;
            AABB = new BoundingBox();
            BSphere = new BoundingSphere();
            CamRel = Vector3.Zero;
            HitDist = float.MaxValue;
        }

        public string GetNameString(string defval)
        {
            string name = defval;
            var ename = (EntityDef != null) ? EntityDef._CEntityDef.archetypeName.ToString() : string.Empty;
            var enamec = ename + ((!string.IsNullOrEmpty(ename)) ? ": " : "");
            if (MultipleSelectionItems != null)
            {
                name = "Multiple items";
            }
            else if (Light != null) name = LightNameString();
            else if (CollisionVertex != null)
            {
                name = enamec + "Vertex " + CollisionVertex.Index.ToString() + ((CollisionBounds != null) ? (": " + CollisionBounds.GetName()) : string.Empty);
            }
            else if (CollisionPoly != null)
            {
                name = enamec + "Poly " + CollisionPoly.Index.ToString() + ((CollisionBounds != null) ? (": " + CollisionBounds.GetName()) : string.Empty);
            }
            else if (CollisionBounds != null)
            {
                name = enamec + CollisionBounds.GetName();
            }
            else if (EntityDef != null)
            {
                name = ename;
            }
            else if (Archetype != null)
            {
                name = Archetype.Hash.ToString();
            }
            else if (TimeCycleModifier != null)
            {
                name = TimeCycleModifier.CTimeCycleModifier.name.ToString();
            }
            else if (CarGenerator != null)
            {
                name = (CarGenerator.Ymap?.Name ?? "") + ": " + CarGenerator.NameString();
            }
            else if (GrassBatch != null)
            {
                name = (GrassBatch.Ymap?.Name ?? "") + ": " + (GrassBatch.Archetype?.Name ?? "");
            }
            else if (LodLight != null)
            {
                name = (LodLight.Ymap?.Name ?? "") + ": " + LodLight.Index.ToString();
            }
            else if (BoxOccluder != null)
            {
                name = "BoxOccluder " + (BoxOccluder.Ymap?.Name ?? "") + ": " + BoxOccluder.Index.ToString();
            }
            else if (OccludeModelTri != null)
            {
                name = "OccludeModel " + (OccludeModelTri.Ymap?.Name ?? "") + ": " + (OccludeModelTri.Model?.Index ?? 0).ToString() + ":" + OccludeModelTri.Index.ToString();
            }
            else if (WaterQuad != null)
            {
                name = "WaterQuad " + WaterQuad.ToString();
            }
            else if (CalmingQuad != null)
            {
                name = "WaterCalmingQuad " + CalmingQuad.ToString();
            }
            else if (WaveQuad != null)
            {
                name = "WaterWaveQuad " + WaveQuad.ToString();
            }
            else if (NavPoly != null)
            {
                name = "NavPoly " + NavPoly.ToString();
            }
            else if (NavPoint != null)
            {
                name = "NavPoint " + NavPoint.ToString();
            }
            else if (NavPortal != null)
            {
                name = "NavPortal " + NavPortal.ToString();
            }
            else if (PathNode != null)
            {
                name = "PathNode " + PathNode.AreaID.ToString() + "." + PathNode.NodeID.ToString();
            }
            else if (TrainTrackNode != null)
            {
                name = "TrainTrackNode " + FloatUtil.GetVector3String(TrainTrackNode.Position);
            }
            else if (ScenarioNode != null)
            {
                name = ScenarioNode.ToString();
            }
            else if (Audio != null)
            {
                name = Audio.ShortTypeName + " " + Audio.GetNameString();
            }
            if (MloRoomDef != null)
            {
                name = "MloRoomDef " + MloRoomDef.RoomName;
            }
            if (MloPortalDef != null)
            {
                name = "MloPortalDef " + MloPortalDef.Index + " (room " + MloPortalDef._Data.roomFrom + " -> " + MloPortalDef._Data.roomTo + ")";
            }
            return name;
        }

        public string TypeName
        {
            get
            {
                if (MultipleSelectionItems != null) return "Multiple";
                if (Light != null) return "Light";
                if (CollisionVertex != null) return "CollisionVertex";
                if (CollisionPoly != null) return "CollisionPoly";
                if (CollisionBounds != null) return "CollisionBounds";
                if (MloRoomDef != null) return "MloRoom";
                if (MloPortalDef != null) return "MloPortal";
                if (MloEntityDef != null) return "MloInstance";
                if (EntityDef != null) return "Entity";
                if (Archetype != null) return "Archetype";
                if (TimeCycleModifier != null) return "TimeCycleModifier";
                if (CarGenerator != null) return "CarGenerator";
                if (GrassBatch != null) return "GrassBatch";
                if (LodLight != null) return "LodLight";
                if (BoxOccluder != null) return "BoxOccluder";
                if (OccludeModelTri != null) return "OccludeModelTri";
                if (WaterQuad != null) return "WaterQuad";
                if (CalmingQuad != null) return "CalmingQuad";
                if (WaveQuad != null) return "WaveQuad";
                if (NavPoly != null) return "NavPoly";
                if (NavPoint != null) return "NavPoint";
                if (NavPortal != null) return "NavPortal";
                if (PathNode != null) return "PathNode";
                if (TrainTrackNode != null) return "TrainTrackNode";
                if (ScenarioNode != null) return "ScenarioNode";
                if (Audio != null) return "Audio";
                return "None";
            }
        }

        public bool CanShowWidget
        {
            get
            {
                if (MultipleSelectionItems != null) return true;
                if (Light != null) return true;
                if (EntityDef != null && MloEntityDef == null) return true;
                if (MloEntityDef != null) return true;
                if (CarGenerator != null) return true;
                if (LodLight != null) return true;
                if (BoxOccluder != null) return true;
                if (OccludeModelTri != null) return true;
                if (NavPoly != null) return true;
                if (CollisionVertex != null) return true;
                if (CollisionPoly != null) return true;
                if (CollisionBounds != null) return true;
                if (NavPoint != null) return true;
                if (NavPortal != null) return true;
                if (PathNode != null) return true;
                if (TrainTrackNode != null) return true;
                if (ScenarioNode != null) return true;
                if (Audio != null) return true;
                return false;
            }
        }

        public bool CanMarkUndo
        {
            get
            {
                if (MultipleSelectionItems != null) return true;
                if (Light != null) return true;
                if (EntityDef != null) return true;
                if (CarGenerator != null) return true;
                if (LodLight != null) return true;
                if (BoxOccluder != null) return true;
                if (OccludeModelTri != null) return true;
                if (CollisionBounds != null) return true;
                if (CollisionPoly != null) return true;
                if (CollisionVertex != null) return true;
                if (PathNode != null) return true;
                if (NavPoint != null) return true;
                if (NavPortal != null) return true;
                if (TrainTrackNode != null) return true;
                if (ScenarioNode != null) return true;
                if (Audio != null) return true;
                return false;
            }
        }

        public Vector3 WidgetPosition
        {
            get
            {
                if (Light != null) return LightWorldPosition;
                if (CollisionVertex != null)
                {
                    if (EntityDef != null) return EntityDef.Position + EntityDef.Orientation.Multiply(CollisionVertex.Position);
                    return CollisionVertex.Position;
                }
                else if (CollisionPoly != null)
                {
                    if (EntityDef != null) return EntityDef.Position + EntityDef.Orientation.Multiply(CollisionPoly.Position);
                    return CollisionPoly.Position;
                }
                else if (CollisionBounds != null)
                {
                    if (EntityDef != null) return EntityDef.Position + EntityDef.Orientation.Multiply(CollisionBounds.Position);
                    return CollisionBounds.Position;
                }
                else if (EntityDef != null) return EntityDef.Position;
                else if (CarGenerator != null) return CarGenerator.Position;
                else if (LodLight != null) return LodLight.Position;
                else if (BoxOccluder != null) return BoxOccluder.Position;
                else if (OccludeModelTri != null) return OccludeModelTri.Center;
                else if (NavPoly != null) return NavPoly.Position;
                else if (NavPoint != null) return NavPoint.Position;
                else if (NavPortal != null) return NavPortal.Position;
                else if (PathNode != null) return PathNode.Position;
                else if (TrainTrackNode != null) return TrainTrackNode.Position;
                else if (ScenarioNode != null) return ScenarioNode.Position;
                else if (Audio != null) return Audio.InnerPos;
                return Vector3.Zero;
            }
        }

        public Quaternion WidgetRotation
        {
            get
            {
                if (Light != null) return LightWorldRotation;
                if (CollisionVertex != null)
                {
                    if (EntityDef != null) return EntityDef.Orientation;
                    return Quaternion.Identity;
                }
                else if (CollisionPoly != null)
                {
                    if (EntityDef != null) return CollisionPoly.Orientation * EntityDef.Orientation;
                    return CollisionPoly.Orientation;
                }
                else if (CollisionBounds != null)
                {
                    if (EntityDef != null) return CollisionBounds.Orientation * EntityDef.Orientation;
                    return CollisionBounds.Orientation;
                }
                else if (EntityDef != null) return EntityDef.Orientation;
                else if (CarGenerator != null) return CarGenerator.Orientation;
                else if (LodLight != null) return LodLight.Orientation;
                else if (BoxOccluder != null) return BoxOccluder.Orientation;
                else if (OccludeModelTri != null) return OccludeModelTri.Orientation;
                else if (NavPoly != null) return Quaternion.Identity;
                else if (NavPoint != null) return NavPoint.Orientation;
                else if (NavPortal != null) return NavPortal.Orientation;
                else if (PathNode != null) return Quaternion.Identity;
                else if (TrainTrackNode != null) return Quaternion.Identity;
                else if (ScenarioNode != null) return ScenarioNode.Orientation;
                else if (Audio != null) return Audio.Orientation;
                return Quaternion.Identity;
            }
        }

        public WorldWidgetAxis WidgetRotationAxes
        {
            get
            {
                if (Light != null) return WorldWidgetAxis.XYZ;
                if (CollisionVertex != null) return WorldWidgetAxis.None;
                if (CollisionPoly != null) return WorldWidgetAxis.XYZ;
                if (CollisionBounds != null) return WorldWidgetAxis.XYZ;
                if (EntityDef != null) return WorldWidgetAxis.XYZ;
                if (CarGenerator != null) return WorldWidgetAxis.Z;
                if (LodLight != null) return WorldWidgetAxis.XYZ;
                if (BoxOccluder != null) return WorldWidgetAxis.Z;
                if (OccludeModelTri != null) return WorldWidgetAxis.XYZ;
                if (NavPoly != null) return WorldWidgetAxis.None;
                if (NavPoint != null) return WorldWidgetAxis.Z;
                if (NavPortal != null) return WorldWidgetAxis.Z;
                if (PathNode != null) return WorldWidgetAxis.None;
                if (TrainTrackNode != null) return WorldWidgetAxis.None;
                if (ScenarioNode != null) return WorldWidgetAxis.Z;
                if (Audio != null) return WorldWidgetAxis.Z;
                return WorldWidgetAxis.None;
            }
        }

        public Vector3 WidgetScale
        {
            get
            {
                if (CollisionVertex != null) return Vector3.One;
                if (CollisionPoly != null) return CollisionPoly.Scale;
                if (CollisionBounds != null) return CollisionBounds.Scale;
                if (EntityDef != null) return EntityDef.Scale;
                if (CarGenerator != null) return new Vector3(CarGenerator._CCarGen.perpendicularLength);
                if (LodLight != null) return LodLight.Scale;
                if (BoxOccluder != null) return BoxOccluder.Size;
                if (OccludeModelTri != null) return OccludeModelTri.Scale;
                return Vector3.One;
            }
        }

        public bool WidgetScaleLockXY
        {
            get
            {
                if (BoxOccluder != null) return false;
                if (OccludeModelTri != null) return false;
                if (CollisionBounds != null) return false;
                if (CollisionPoly != null) return false;
                return true;
            }
        }

        public bool WidgetCanScale =>
            EntityDef != null || CarGenerator != null || LodLight != null || BoxOccluder != null ||
            OccludeModelTri != null || CollisionBounds != null || CollisionPoly != null;

        public void SetPosition(Vector3 newpos)
        {
            if (Light != null) { LightSetWorldPosition(newpos); return; }
            if (CollisionVertex != null)
            {
                if (EntityDef != null) newpos = Quaternion.Invert(EntityDef.Orientation).Multiply(newpos - EntityDef.Position);
                CollisionVertex.Position = newpos;
            }
            else if (CollisionPoly != null)
            {
                if (EntityDef != null) newpos = Quaternion.Invert(EntityDef.Orientation).Multiply(newpos - EntityDef.Position);
                CollisionPoly.Position = newpos;
            }
            else if (CollisionBounds != null)
            {
                if (EntityDef != null) newpos = Quaternion.Invert(EntityDef.Orientation).Multiply(newpos - EntityDef.Position);
                CollisionBounds.Position = newpos;
            }
            else if (EntityDef != null) EntityDef.SetPosition(newpos);
            else if (CarGenerator != null) CarGenerator.SetPosition(newpos);
            else if (LodLight != null) LodLight.SetPosition(newpos);
            else if (BoxOccluder != null) BoxOccluder.Position = newpos;
            else if (OccludeModelTri != null) OccludeModelTri.Center = newpos;
            else if (NavPoly != null) NavPoly.SetPosition(newpos);
            else if (NavPoint != null) NavPoint.SetPosition(newpos);
            else if (NavPortal != null) NavPortal.SetPosition(newpos);
            else if (PathNode != null) PathNode.SetPosition(newpos);
            else if (TrainTrackNode != null) TrainTrackNode.SetPosition(newpos);
            else if (ScenarioNode != null) ScenarioNode.SetPosition(newpos);
            else if (Audio != null) Audio.SetPosition(newpos);
        }

        public void SetRotation(Quaternion newrot)
        {
            if (Light != null) { LightSetWorldRotation(newrot); return; }
            if (CollisionVertex != null)
            {
            }
            else if (CollisionPoly != null)
            {
                if (EntityDef != null) newrot = Quaternion.Normalize(Quaternion.Invert(EntityDef.Orientation) * newrot);
                CollisionPoly.Orientation = newrot;
            }
            else if (CollisionBounds != null)
            {
                if (EntityDef != null) newrot = Quaternion.Normalize(Quaternion.Invert(EntityDef.Orientation) * newrot);
                CollisionBounds.Orientation = newrot;
            }
            else if (EntityDef != null) EntityDef.SetOrientation(newrot);
            else if (CarGenerator != null) CarGenerator.SetOrientation(newrot);
            else if (LodLight != null) LodLight.SetOrientation(newrot);
            else if (BoxOccluder != null) BoxOccluder.Orientation = newrot;
            else if (OccludeModelTri != null) OccludeModelTri.Orientation = newrot;
            else if (NavPoint != null) NavPoint.SetOrientation(newrot);
            else if (NavPortal != null) NavPortal.SetOrientation(newrot);
            else if (ScenarioNode != null) ScenarioNode.SetOrientation(newrot);
            else if (Audio != null) Audio.SetOrientation(newrot);
        }

        public void SetScale(Vector3 newscale)
        {
            if (CollisionVertex != null)
            {
            }
            else if (CollisionPoly != null) CollisionPoly.Scale = newscale;
            else if (CollisionBounds != null) CollisionBounds.Scale = newscale;
            else if (EntityDef != null) EntityDef.SetScale(newscale);
            else if (CarGenerator != null)
            {
                CarGenerator.SetScale(newscale);
                AABB = new BoundingBox(CarGenerator.BBMin, CarGenerator.BBMax);
            }
            else if (LodLight != null) LodLight.SetScale(newscale);
            else if (BoxOccluder != null) BoxOccluder.SetSize(newscale);
            else if (OccludeModelTri != null) OccludeModelTri.Scale = newscale;
        }

        public object GetProjectObject()
        {
            if (MultipleSelectionItems != null) return null;
            if (Light != null) return Light;
            if (CollisionVertex != null) return CollisionVertex;
            if (CollisionPoly != null) return CollisionPoly;
            if (CollisionBounds != null) return CollisionBounds;
            if (MloEntityDef != null) return MloEntityDef;
            if (EntityDef != null) return EntityDef;
            if (CarGenerator != null) return CarGenerator;
            if (LodLight != null) return LodLight;
            if (BoxOccluder != null) return BoxOccluder;
            if (OccludeModelTri != null) return OccludeModelTri;
            if (GrassBatch != null) return GrassBatch;
            if (TimeCycleModifier != null) return TimeCycleModifier;
            if (WaterQuad != null) return WaterQuad;
            if (CalmingQuad != null) return CalmingQuad;
            if (WaveQuad != null) return WaveQuad;
            if (NavPoly != null) return NavPoly;
            if (NavPoint != null) return NavPoint;
            if (NavPortal != null) return NavPortal;
            if (PathNode != null) return PathNode;
            if (TrainTrackNode != null) return TrainTrackNode;
            if (ScenarioNode != null) return ScenarioNode;
            if (Audio != null) return Audio;
            return null;
        }

        public static WorldSelection FromProjectObject(object o)
        {
            var ms = Empty;
            if (o == null) return ms;
            if (o is YmapEntityDef ent)
            {
                ms.EntityDef = ent;
                ms.Archetype = ent.Archetype;
                if (ent.MloInstance != null) ms.MloEntityDef = ent;
                var a = ent.Archetype;
                if (a != null && a.BBMax.X > a.BBMin.X) ms.AABB = new BoundingBox(a.BBMin, a.BBMax);
                else ms.AABB = new BoundingBox(new Vector3(-0.5f), new Vector3(0.5f));
            }
            else if (o is YmapCarGen cg)
            {
                ms.CarGenerator = cg;
                ms.AABB = new BoundingBox(cg.BBMin, cg.BBMax);
            }
            else if (o is YmapLODLight ll)
            {
                ms.LodLight = ll;
                ms.AABB = new BoundingBox(new Vector3(-0.5f), new Vector3(0.5f));
            }
            else if (o is YmapBoxOccluder bo)
            {
                ms.BoxOccluder = bo;
                ms.AABB = new BoundingBox(bo.BBMin, bo.BBMax);
            }
            else if (o is YmapOccludeModelTriangle ot)
            {
                ms.OccludeModelTri = ot;
                ms.AABB = ot.Box;
            }
            else if (o is YmapGrassInstanceBatch gb)
            {
                ms.GrassBatch = gb;
                ms.AABB = new BoundingBox(gb.AABBMin, gb.AABBMax);
            }
            else if (o is YmapTimeCycleModifier tcm)
            {
                ms.TimeCycleModifier = tcm;
                ms.AABB = new BoundingBox(tcm.BBMin, tcm.BBMax);
            }
            else if (o is WaterQuad wq) { ms.WaterQuad = wq; ms.AABB = QuadBox(wq); }
            else if (o is WaterCalmingQuad cq) { ms.CalmingQuad = cq; ms.AABB = QuadBox(cq); }
            else if (o is WaterWaveQuad wvq) { ms.WaveQuad = wvq; ms.AABB = QuadBox(wvq); }
            else if (o is Bounds b)
            {
                ms.CollisionBounds = b;
                ms.AABB = new BoundingBox(b.BoxMin, b.BoxMax);
                ms.BBOffset = b.Transform.TranslationVector;
                ms.BBOrientation = b.Transform.ToQuaternion();
            }
            else if (o is BoundPolygon bp)
            {
                ms.CollisionPoly = bp;
                ms.CollisionBounds = bp.Owner;
                if (bp.Owner != null)
                {
                    ms.AABB = new BoundingBox(bp.Owner.BoxMin, bp.Owner.BoxMax);
                    ms.BBOffset = bp.Owner.Transform.TranslationVector;
                    ms.BBOrientation = bp.Owner.Transform.ToQuaternion();
                }
            }
            else if (o is BoundVertex bv)
            {
                ms.CollisionVertex = bv;
                ms.CollisionBounds = bv.Owner;
                if (bv.Owner != null)
                {
                    ms.AABB = new BoundingBox(bv.Owner.BoxMin, bv.Owner.BoxMax);
                    ms.BBOffset = bv.Owner.Transform.TranslationVector;
                    ms.BBOrientation = bv.Owner.Transform.ToQuaternion();
                }
            }
            else if (o is YndNode pn) ms.PathNode = pn;
            else if (o is YnvPoly np) ms.NavPoly = np;
            else if (o is YnvPoint npt) ms.NavPoint = npt;
            else if (o is YnvPortal npo) ms.NavPortal = npo;
            else if (o is TrainTrackNode tn) ms.TrainTrackNode = tn;
            else if (o is ScenarioNode sn) ms.ScenarioNode = sn;
            else if (o is AudioPlacement au) ms.Audio = au;
            return ms;
        }

        public static BoundingBox QuadBox(BaseWaterQuad q)
        {
            float z = q.z ?? 0.0f;
            return new BoundingBox(new Vector3(q.minX, q.minY, z), new Vector3(q.maxX, q.maxY, z));
        }

        public IWorldGizmoTarget GizmoTarget()
        {
            if (!HasValue || !CanShowWidget) return null;
            if (EntityDef != null && CollisionBounds == null && CollisionPoly == null && CollisionVertex == null)
                return new EntityGizmoTarget(EntityDef);
            return new SelectionGizmoTarget(this);
        }
    }

    public interface IWorldGizmoTarget
    {
        object Key { get; }
        Vector3 Position { get; }
        Quaternion Orientation { get; }
        Vector3 Scale { get; }
        WorldWidgetAxis RotationAxes { get; }
        bool ScaleLockXY { get; }
        bool CanScale { get; }
        void SetPosition(Vector3 p);
        void SetOrientation(Quaternion q);
        void SetScale(Vector3 s);
    }

    public sealed class EntityGizmoTarget : IWorldGizmoTarget
    {
        public readonly YmapEntityDef Entity;
        public EntityGizmoTarget(YmapEntityDef e) { Entity = e; }
        public object Key => Entity;
        public Vector3 Position => Entity.Position;
        public Quaternion Orientation => Entity.Orientation;
        public Vector3 Scale => Entity.Scale;
        public WorldWidgetAxis RotationAxes => WorldWidgetAxis.XYZ;
        public bool ScaleLockXY => true;
        public bool CanScale => true;
        public void SetPosition(Vector3 p) => Entity.SetPosition(p);
        public void SetOrientation(Quaternion q) => Entity.SetOrientation(q);
        public void SetScale(Vector3 s) => Entity.SetScale(s);
        public override bool Equals(object obj) => obj is EntityGizmoTarget o && ReferenceEquals(o.Entity, Entity);
        public override int GetHashCode() => Entity?.GetHashCode() ?? 0;
    }

    public sealed class SelectionGizmoTarget : IWorldGizmoTarget
    {
        private WorldSelection sel;
        public SelectionGizmoTarget(in WorldSelection s) { sel = s; }
        public WorldSelection Selection => sel;
        public object Key => sel.GetProjectObject();
        public Vector3 Position => sel.WidgetPosition;
        public Quaternion Orientation => sel.WidgetRotation;
        public Vector3 Scale => sel.WidgetScale;
        public WorldWidgetAxis RotationAxes => sel.WidgetRotationAxes;
        public bool ScaleLockXY => sel.WidgetScaleLockXY;
        public bool CanScale => sel.WidgetCanScale;
        public void SetPosition(Vector3 p) => sel.SetPosition(p);
        public void SetOrientation(Quaternion q) => sel.SetRotation(q);
        public void SetScale(Vector3 s) => sel.SetScale(s);
        public override bool Equals(object obj) => obj is SelectionGizmoTarget o && ReferenceEquals(o.Key, Key);
        public override int GetHashCode() => Key?.GetHashCode() ?? 0;
    }
}

