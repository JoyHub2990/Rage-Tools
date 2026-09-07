using System;
using ImGuiNET;
using SDX = SharpDX;

namespace RageLightEditor.Editor
{
    public partial class ModelViewer
    {
        partial void ViewportInput_R3(bool hovered, ref bool handled);

        public float MoveSpeed = 1.0f;

        public float Smoothness;

        public float FlownMetres_R3;

        public string LastCameraAction_R3 = "";

        private float tYaw_R3, tPitch_R3, tDist_R3;
        private float lastYaw_R3, lastPitch_R3, lastDist_R3;
        private SDX.Vector3 lastTarget_R3;
        private bool camPrimed_R3;

        partial void ViewportInput_R3(bool hovered, ref bool handled)
        {
            handled = true;
            var io = ImGui.GetIO();
            float dt = Math.Clamp(io.DeltaTime, 0.0f, 0.25f);

            Cam.Smoothness = Smoothness;
            Cam.RotateInPlace = false;
            Cam.Update(0.0f);

            bool jumped = !camPrimed_R3 ||
                          Math.Abs(Cam.Yaw - lastYaw_R3) > 1e-4f ||
                          Math.Abs(Cam.Pitch - lastPitch_R3) > 1e-4f ||
                          Math.Abs(Cam.Distance - lastDist_R3) > 1e-3f ||
                          (Cam.Target - lastTarget_R3).LengthSquared() > 1e-6f;
            if (jumped) { tYaw_R3 = Cam.Yaw; tPitch_R3 = Cam.Pitch; tDist_R3 = Cam.Distance; camPrimed_R3 = true; }
            Cam.TargetYaw = tYaw_R3;
            Cam.TargetPitch = tPitch_R3;
            Cam.TargetDistance = tDist_R3;

            string did = "";

            if (hovered)
            {
                if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
                {
                    Cam.Pan(-io.MouseDelta.X, io.MouseDelta.Y);
                    did = "pan";
                }
                else if (ImGui.IsMouseDragging(ImGuiMouseButton.Left) ||
                         ImGui.IsMouseDragging(ImGuiMouseButton.Right))
                {
                    Cam.Orbit(io.MouseDelta.X, io.MouseDelta.Y);
                    did = "look";
                }
                if (Math.Abs(io.MouseWheel) > 0.001f)
                {
                    Cam.Zoom(io.MouseWheel * 120.0f);
                    did = did.Length > 0 ? did + "+zoom" : "zoom";
                }
            }

            if (!io.WantTextInput)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.F)) { FrameModel(); did = "frame"; }
                if (ImGui.IsKeyPressed(ImGuiKey.X)) MoveSpeed = Math.Min(MoveSpeed * 1.4f, 40.0f);
                if (ImGui.IsKeyPressed(ImGuiKey.Z)) MoveSpeed = Math.Max(MoveSpeed / 1.4f, 0.05f);

                var move = FlyVector_R3();
                if (move.LengthSquared() > 1e-6f)
                {
                    move.Normalize();
                    Cam.Translate(move * FlySpeed_R3(io.KeyShift, io.KeyCtrl) * dt);
                    FlownMetres_R3 += (move * FlySpeed_R3(io.KeyShift, io.KeyCtrl) * dt).Length();
                    did = did.Length > 0 ? did + "+fly" : "fly";
                }
            }

            Cam.Update(dt);

            tYaw_R3 = Cam.TargetYaw; tPitch_R3 = Cam.TargetPitch; tDist_R3 = Cam.TargetDistance;
            lastYaw_R3 = Cam.Yaw; lastPitch_R3 = Cam.Pitch; lastDist_R3 = Cam.Distance;
            lastTarget_R3 = Cam.Target;
            ReadBackCamera_R3();
            if (did.Length > 0) LastCameraAction_R3 = did;
        }

        public SDX.Vector3 FlyVector_R3()
        {
            var move = SDX.Vector3.Zero;
            var fwd = Cam.GetForward();
            var right = Cam.GetRight();
            if (Down_R3(ImGuiKey.W, ImGuiKey.UpArrow)) move += fwd;
            if (Down_R3(ImGuiKey.S, ImGuiKey.DownArrow)) move -= fwd;
            if (Down_R3(ImGuiKey.D, ImGuiKey.RightArrow)) move += right;
            if (Down_R3(ImGuiKey.A, ImGuiKey.LeftArrow)) move -= right;
            if (Down_R3(ImGuiKey.R, ImGuiKey.PageUp) || ImGui.IsKeyDown(ImGuiKey.Space)) move += SDX.Vector3.UnitZ;
            if (Down_R3(ImGuiKey.C, ImGuiKey.PageDown)) move -= SDX.Vector3.UnitZ;
            return move;
        }

        public float FlySpeed_R3(bool shift, bool ctrl)
        {
            float speed = 50.0f * MoveSpeed * Math.Min(Cam.TargetDistance, 20.0f);
            if (shift) speed *= 5.0f;
            if (ctrl) speed *= 0.2f;
            return speed;
        }

        private static bool Down_R3(ImGuiKey a, ImGuiKey b) => ImGui.IsKeyDown(a) || ImGui.IsKeyDown(b);

        private void ReadBackCamera_R3()
        {
            yaw = Cam.Yaw;
            pitch = Cam.Pitch;
            dist = Cam.Distance;
            target = new System.Numerics.Vector3(Cam.Target.X, Cam.Target.Y, Cam.Target.Z);
        }

        public void PushCameraBack_R3() => ReadBackCamera_R3();

        public void DrawCameraHelp_R3()
        {
            ImGui.TextDisabled("WASD fly · R/C up-down · drag look · middle pan · wheel zoom · Shift fast · Ctrl slow · F frame");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The World workspace's camera, with the same speed and the same smoothing -\n" +
                                 "both read from your own settings, so changing them there changes them here.\n" +
                                 "Z and X change the speed and keep it. Right-drag looks around rather than\n" +
                                 "selecting, because there is nothing in this window to select.");
        }
    }
}

