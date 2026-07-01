using GrokoEngine;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using SN = System.Numerics;
using Vector2 = System.Numerics.Vector2;
using Vector3 = MiMotor.Mathematics.Vector3;

namespace GrokoEngine.ImGuiEditor;

internal sealed partial class ImGuiEditorApp
{
    private void ApplyBoneAttachments()
    {
        foreach (var obj in EnumerateSceneObjects(objects))
        {
            var attachment = obj.GetComponent<BoneAttachment>();
            if (attachment == null || !attachment.Enabled)
                continue;

            var target = ResolveBoneAttachmentTarget(attachment);
            var animator = target?.GetComponent<Animator>();
            if (animator == null || !animator.TryGetBoneWorldMatrix(attachment.BoneName, out var boneWorld))
                continue;

            if (obj.IsStatic)
            {
                obj.IsStatic = false;
                sceneRenderer.InvalidateStaticBatch();
            }

            var offset = BuildBoneAttachmentOffsetMatrix(attachment);
            var world = offset * boneWorld;
            ApplyWorldMatrixToObject(obj, world, attachment);
        }
    }

    private static SN.Matrix4x4 BuildBoneAttachmentOffsetMatrix(BoneAttachment attachment)
    {
        float rx = attachment.RotationOffsetX * MathF.PI / 180f;
        float ry = attachment.RotationOffsetY * MathF.PI / 180f;
        float rz = attachment.RotationOffsetZ * MathF.PI / 180f;

        var scale = SN.Matrix4x4.CreateScale(
            SafeScale(attachment.ScaleOffsetX),
            SafeScale(attachment.ScaleOffsetY),
            SafeScale(attachment.ScaleOffsetZ));
        var rotation =
            SN.Matrix4x4.CreateRotationZ(rz) *
            SN.Matrix4x4.CreateRotationX(rx) *
            SN.Matrix4x4.CreateRotationY(ry);
        var translation = SN.Matrix4x4.CreateTranslation(
            attachment.PositionOffsetX,
            attachment.PositionOffsetY,
            attachment.PositionOffsetZ);

        return scale * rotation * translation;
    }

    private static float SafeScale(float value) =>
        MathF.Abs(value) < 0.0001f ? 1f : value;

    private static void ApplyWorldMatrixToObject(GameObject obj, SN.Matrix4x4 world, BoneAttachment attachment)
    {
        var local = world;
        if (obj.Parent != null && SN.Matrix4x4.Invert(obj.Parent.WorldMatrix, out var parentInv))
            local = world * parentInv;

        if (!SN.Matrix4x4.Decompose(local, out var scale, out var rotation, out var translation))
            return;

        if (attachment.FollowPosition)
        {
            obj.PosX = translation.X;
            obj.PosY = translation.Y;
            obj.PosZ = translation.Z;
        }

        if (attachment.FollowRotation)
        {
            obj.transform.Rotation = new MiMotor.Mathematics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
            obj.UseQuaternionRotation = true;
        }

        if (attachment.FollowScale)
        {
            obj.ScaleX = MathF.Max(0.0001f, scale.X);
            obj.ScaleY = MathF.Max(0.0001f, scale.Y);
            obj.ScaleZ = MathF.Max(0.0001f, scale.Z);
        }

        obj.MarkTransformDirtyRecursive();
    }

    private GameObject? ResolveBoneAttachmentTarget(BoneAttachment attachment)
    {
        if (!string.IsNullOrWhiteSpace(attachment.TargetObjectId))
        {
            var byId = sceneGraph.FindById(attachment.TargetObjectId);
            if (byId?.GetComponent<Animator>() != null)
                return byId;
        }

        if (!string.IsNullOrWhiteSpace(attachment.TargetObjectName))
        {
            foreach (var obj in EnumerateSceneObjects(objects))
            {
                if (obj.GetComponent<Animator>() != null &&
                    string.Equals(obj.Name, attachment.TargetObjectName, StringComparison.OrdinalIgnoreCase))
                    return obj;
            }
        }

        return null;
    }

    private static IEnumerable<GameObject> EnumerateSceneObjects(IEnumerable<GameObject> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in EnumerateSceneObjects(root.Children))
                yield return child;
        }
    }

    private void DrawBoneAttachmentInspector(BoneAttachment attachment)
    {
        var animators = EnumerateSceneObjects(objects)
            .Where(o => o.GetComponent<Animator>() != null)
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var target = ResolveBoneAttachmentTarget(attachment);
        FieldRow("Target Animator");
        string targetLabel = target != null ? $"{target.Name} ({target.EditorId[..Math.Min(6, target.EditorId.Length)]})" : "None";
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##boneAttachmentTarget", targetLabel))
        {
            if (ImGui.Selectable("None", target == null))
            {
                attachment.TargetObjectId = "";
                attachment.TargetObjectName = "";
            }

            foreach (var candidate in animators)
            {
                bool selectedTarget = target != null && candidate.EditorId == target.EditorId;
                if (ImGui.Selectable(candidate.Name + "##" + candidate.EditorId, selectedTarget))
                {
                    attachment.TargetObjectId = candidate.EditorId;
                    attachment.TargetObjectName = candidate.Name;
                }
                if (selectedTarget)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        DrawString("Target Name", attachment.TargetObjectName, v => attachment.TargetObjectName = v);

        var animator = target?.GetComponent<Animator>();
        var boneNames = animator?.GetBoneNames() ?? Array.Empty<string>();
        FieldRow("Bone");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##boneAttachmentBone", string.IsNullOrWhiteSpace(attachment.BoneName) ? "None" : attachment.BoneName))
        {
            foreach (var bone in boneNames)
            {
                bool selectedBone = string.Equals(bone, attachment.BoneName, StringComparison.Ordinal);
                if (ImGui.Selectable(bone, selectedBone))
                    attachment.BoneName = bone;
                if (selectedBone)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        DrawString("Bone Name", attachment.BoneName, v => attachment.BoneName = v);

        ImGui.Spacing();
        ImGui.TextDisabled("Socket Offset");
        DrawVector3("Position", new Vector3(attachment.PositionOffsetX, attachment.PositionOffsetY, attachment.PositionOffsetZ), v =>
        {
            attachment.PositionOffsetX = v.X;
            attachment.PositionOffsetY = v.Y;
            attachment.PositionOffsetZ = v.Z;
        }, 0.01f);
        DrawVector3("Rotation", new Vector3(attachment.RotationOffsetX, attachment.RotationOffsetY, attachment.RotationOffsetZ), v =>
        {
            attachment.RotationOffsetX = v.X;
            attachment.RotationOffsetY = v.Y;
            attachment.RotationOffsetZ = v.Z;
        }, 0.25f);
        DrawVector3("Scale", new Vector3(attachment.ScaleOffsetX, attachment.ScaleOffsetY, attachment.ScaleOffsetZ), v =>
        {
            attachment.ScaleOffsetX = SafeScale(v.X);
            attachment.ScaleOffsetY = SafeScale(v.Y);
            attachment.ScaleOffsetZ = SafeScale(v.Z);
        }, 0.01f);

        DrawCheckRow("Follow Position", attachment.FollowPosition, v => attachment.FollowPosition = v);
        DrawCheckRow("Follow Rotation", attachment.FollowRotation, v => attachment.FollowRotation = v);
        DrawCheckRow("Follow Scale", attachment.FollowScale, v => attachment.FollowScale = v);

        if (target == null)
            ImGui.TextColored(new SN.Vector4(1f, 0.72f, 0.28f, 1f), "Assign an Animator target.");
        else if (boneNames.Count == 0)
            ImGui.TextColored(new SN.Vector4(1f, 0.72f, 0.28f, 1f), "Animator has no resolved bones yet.");
        else if (!boneNames.Contains(attachment.BoneName, StringComparer.Ordinal))
            ImGui.TextColored(new SN.Vector4(1f, 0.72f, 0.28f, 1f), "Bone name not found in target rig.");

        ImGui.TextDisabled("Tip: keep weapons/accessories outside the skeleton hierarchy and attach them here.");
    }
}
