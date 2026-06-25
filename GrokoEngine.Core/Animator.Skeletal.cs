using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiMotor.Mathematics;
using Quaternion = MiMotor.Mathematics.Quaternion;
using Vector3 = MiMotor.Mathematics.Vector3;

namespace GrokoEngine
{
    // Parte pesada del Animator: blending, sampling skeletal, retarget/root motion y utilidades FBX.
    public partial class Animator
    {
        private sealed class BlendClipRuntime
        {
            public string Path { get; init; } = "";
            public string Name { get; init; } = "";
            public SkeletalClip Clip { get; init; } = new();
            public AnimationClipData Settings { get; init; } = new();
        }

        private sealed class BlendAccum
        {
            public Vector3 Position;
            public Vector3 Scale;
            public Quaternion Rotation;
            public float Weight;
        }

        private static float QuatDot(Quaternion a, Quaternion b) =>
            a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

        private bool SampleBlendTree(BlendTreeData tree, float time)
        {
            tree.Normalize();
            var children = tree.Children
                .Where(c => !string.IsNullOrWhiteSpace(c.MotionPath))
                .Select(c => (Child: c, Runtime: LoadBlendClip(c.MotionPath)))
                .Where(x => x.Runtime != null && x.Runtime.Clip.Duration > 0f)
                .Select(x => (x.Child, Runtime: x.Runtime!))
                .ToList();
            if (children.Count == 0)
                return false;

            var weights = ComputeBlendWeights(tree, children.Select(c => c.Child).ToList());
            EnsureBoneMap();

            // ── Sincronización de FASE (estilo Unity) ──
            // Todos los clips mezclados se muestrean en la MISMA fase normalizada (0..1),
            // escalada a la duración mezclada (media ponderada). Así walk/run/idle quedan
            // en sincronía (no patinan los pies) al mezclarse.
            float blendedDuration = 0f, wsumDur = 0f, maxDur = 0f;
            for (int i = 0; i < children.Count; i++)
            {
                float d = children[i].Runtime.Clip.Duration;
                if (d > maxDur) maxDur = d;
                if (weights[i] > 0.0001f) { blendedDuration += weights[i] * d; wsumDur += weights[i]; }
            }
            blendedDuration = wsumDur > 0f ? blendedDuration / wsumDur : maxDur;
            float phase = blendedDuration > 0f ? (time % blendedDuration) / blendedDuration : 0f;
            if (phase < 0f) phase += 1f;

            var accum = new Dictionary<GameObject, BlendAccum>();
            for (int i = 0; i < children.Count; i++)
            {
                float weight = weights[i];
                if (weight <= 0.0001f)
                    continue;

                var child = children[i].Child;
                var runtime = children[i].Runtime;
                // Fase común → tiempo dentro de ESTE clip (cada clip a su propia duración, en fase).
                float childPhase = phase * (child.Speed == 0f ? 1f : child.Speed);
                childPhase -= MathF.Floor(childPhase); // envolver a 0..1
                float childTime = childPhase * runtime.Clip.Duration;

                var settings = runtime.Settings;
                bool originalMirror = settings.Mirror;
                if (child.Mirror)
                    settings.Mirror = !settings.Mirror;

                float sampleTime = ApplyCycleOffset(childTime, runtime.Clip.Duration, settings.CycleOffset);
                foreach (var pose in SampleSkeletalClipPoses(runtime.Clip, settings, sampleTime))
                {
                    if (!accum.TryGetValue(pose.Bone, out var a))
                    {
                        a = new BlendAccum
                        {
                            Position = pose.Position * weight,
                            Scale = pose.Scale * weight,
                            Rotation = pose.Rotation,
                            Weight = weight
                        };
                        accum[pose.Bone] = a;
                    }
                    else
                    {
                        float total = a.Weight + weight;
                        float rotBlend = total > 0f ? weight / total : 0f;
                        a.Position += pose.Position * weight;
                        a.Scale += pose.Scale * weight;
                        // Corrección de hemisferio: si el cuaternión está en el lado opuesto,
                        // se invierte el signo antes de mezclar (si no, Slerp toma el "camino
                        // largo" → tirones/giros raros al mezclar). Es EL bug típico de blending.
                        var pr = pose.Rotation;
                        if (QuatDot(a.Rotation, pr) < 0f)
                            pr = new Quaternion(-pr.X, -pr.Y, -pr.Z, -pr.W);
                        a.Rotation = Quaternion.Slerp(a.Rotation, pr, rotBlend);
                        a.Weight = total;
                    }
                }

                settings.Mirror = originalMirror;
            }

            foreach (var (bone, a) in accum)
            {
                float inv = a.Weight > 0f ? 1f / a.Weight : 1f;
                bone.SetLocalTRS(a.Position * inv, a.Rotation.Normalized(), a.Scale * inv);
            }

            return true;
        }

        private List<BonePoseSample> SampleSkeletalClipPoses(SkeletalClip clip, AnimationClipData settings, float sampleTime)
        {
            var poses = new Dictionary<GameObject, BonePoseAccumulator>();

            foreach (var ch in clip.Channels)
            {
                bool rootChannel = IsRootMotionChannel(ch.NodeName);
                var sourceChannel = settings.Mirror && !rootChannel ? FindMirroredChannel(clip, ch) : ch;
                if (!TryResolveAnimationBone(ch.NodeName, out var bone, out bool usedMappedName))
                    continue;

                var rest = CaptureRestPose(bone);
                if (!poses.TryGetValue(bone, out var acc))
                {
                    acc = new BonePoseAccumulator
                    {
                        Bone = bone,
                        RestPosition = rest.Position,
                        RestRotation = rest.Rotation,
                        RestScale = rest.Scale,
                        Position = rest.Position,
                        FirstPosition = rest.Position,
                        Scale = rest.Scale
                    };
                    if (TryGetSourceRestPose(settings, ch.NodeName, out var sourceRest))
                    {
                        acc.SourceRestPose = sourceRest;
                        acc.HasSourceRestPose = true;
                    }
                    poses[bone] = acc;
                }

                acc.IsRoot |= rootChannel;
                acc.MirrorPose |= settings.Mirror && !rootChannel;

                var kind = usedMappedName ? GetFbxChannelKind(ch.NodeName) : FbxChannelKind.Transform;
                ApplyChannelSampleToPose(acc, sourceChannel, kind, settings, sampleTime, clip.Duration);
            }

            var result = new List<BonePoseSample>(poses.Count);
            foreach (var acc in poses.Values)
            {
                var pos = acc.HasPosition ? acc.Position : acc.RestPosition;
                var rot = ComposeBoneRotation(acc, firstFrame: false);
                var scale = acc.HasScale ? acc.Scale : acc.RestScale;
                var firstRot = ComposeBoneRotation(acc, firstFrame: true);

                if (acc.MirrorPose && !acc.IsRoot)
                {
                    pos.X = -pos.X;
                    rot = MirrorQuaternionX(rot);
                }

                if (acc.HasSourceRestPose)
                    RetargetPoseFromSourceRest(acc, ref pos, ref rot);

                if (acc.IsRoot)
                    ApplyRootBake(settings, acc.RestPosition, acc.RestRotation, acc.FirstPosition, firstRot, ref pos, ref rot);

                result.Add(new BonePoseSample(acc.Bone, pos, rot, scale, acc.IsRoot, acc.FirstPosition, firstRot, settings.Humanoid));
            }

            return result;
        }

        private static void ApplyChannelSampleToPose(BonePoseAccumulator acc, BoneChannel channel, FbxChannelKind kind,
            AnimationClipData settings, float sampleTime, float duration)
        {
            var pos = SampleVec(channel.Positions, sampleTime, acc.RestPosition);
            var rot = SampleQuat(channel.Rotations, sampleTime, acc.RestRotation);
            var scale = SampleVec(channel.Scales, sampleTime, acc.RestScale);

            if (settings.LoopPose && duration > 0f)
                BlendLoopPose(channel, sampleTime, duration, acc.RestPosition, acc.RestRotation, acc.RestScale, ref pos, ref rot, ref scale);

            var firstPos = channel.Positions.Count > 0 ? channel.Positions[0].Value : acc.RestPosition;
            var firstRot = channel.Rotations.Count > 0 ? channel.Rotations[0].Value : acc.RestRotation;
            var firstScale = channel.Scales.Count > 0 ? channel.Scales[0].Value : acc.RestScale;

            switch (kind)
            {
                case FbxChannelKind.Translation:
                    if (channel.Positions.Count > 0)
                    {
                        acc.Position = pos;
                        acc.FirstPosition = firstPos;
                        acc.HasPosition = true;
                    }
                    break;
                case FbxChannelKind.PreRotation:
                    if (channel.Rotations.Count > 0)
                    {
                        acc.PreRotation = rot;
                        acc.FirstPreRotation = firstRot;
                    }
                    break;
                case FbxChannelKind.Rotation:
                    if (channel.Rotations.Count > 0)
                    {
                        acc.Rotation = rot;
                        acc.FirstRotation = firstRot;
                    }
                    break;
                case FbxChannelKind.Scaling:
                    if (channel.Scales.Count > 0)
                    {
                        acc.Scale = scale;
                        acc.HasScale = true;
                    }
                    break;
                default:
                    if (channel.Positions.Count > 0)
                    {
                        acc.Position = pos;
                        acc.FirstPosition = firstPos;
                        acc.HasPosition = true;
                    }
                    if (channel.Rotations.Count > 0)
                    {
                        acc.TransformRotation = rot;
                        acc.FirstTransformRotation = firstRot;
                    }
                    if (channel.Scales.Count > 0)
                    {
                        acc.Scale = scale;
                        acc.HasScale = true;
                    }
                    break;
            }

            if (channel.Scales.Count > 0 && kind != FbxChannelKind.Scaling)
            {
                _ = firstScale;
            }
        }

        private static Quaternion ComposeBoneRotation(BonePoseAccumulator acc, bool firstFrame)
        {
            var transform = firstFrame ? acc.FirstTransformRotation : acc.TransformRotation;
            var pre = firstFrame ? acc.FirstPreRotation : acc.PreRotation;
            var rot = firstFrame ? acc.FirstRotation : acc.Rotation;

            if (pre.HasValue || rot.HasValue)
            {
                if (pre.HasValue && rot.HasValue)
                    return MultiplyQuaternions(pre.Value, rot.Value);
                return pre ?? rot ?? acc.RestRotation;
            }

            return transform ?? acc.RestRotation;
        }

        private static FbxChannelKind GetFbxChannelKind(string nodeName)
        {
            if (nodeName.EndsWith("_$AssimpFbx$_Translation", StringComparison.Ordinal))
                return FbxChannelKind.Translation;
            if (nodeName.EndsWith("_$AssimpFbx$_PreRotation", StringComparison.Ordinal))
                return FbxChannelKind.PreRotation;
            if (nodeName.EndsWith("_$AssimpFbx$_Rotation", StringComparison.Ordinal))
                return FbxChannelKind.Rotation;
            if (nodeName.EndsWith("_$AssimpFbx$_Scaling", StringComparison.Ordinal))
                return FbxChannelKind.Scaling;
            return FbxChannelKind.Transform;
        }

        private bool TryGetSourceRestPose(AnimationClipData settings, string channelName, out BoneRestPose pose)
        {
            pose = default;
            if (!settings.Humanoid || string.IsNullOrWhiteSpace(settings.SourceModelPath) || !File.Exists(settings.SourceModelPath))
                return false;

            var map = GetSourceRestPoseMap(settings.SourceModelPath);
            string mapped = MapAssimpFbxRotationNode(channelName);
            return map.TryGetValue(mapped, out pose) || map.TryGetValue(channelName, out pose);
        }

        private Dictionary<string, BoneRestPose> GetSourceRestPoseMap(string modelPath)
        {
            string key;
            try { key = Path.GetFullPath(modelPath); }
            catch { key = modelPath; }

            if (sourceRestPoseMap != null && string.Equals(sourceRestPoseKey, key, StringComparison.OrdinalIgnoreCase))
                return sourceRestPoseMap;

            var map = new Dictionary<string, BoneRestPose>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var mesh = ObjLoader.Load(modelPath);
                if (mesh?.Hierarchy != null)
                    CollectSourceRestPoses(mesh.Hierarchy, map);
            }
            catch { }

            sourceRestPoseMap = map;
            sourceRestPoseKey = key;
            return map;
        }

        private static void CollectSourceRestPoses(ModelNode node, Dictionary<string, BoneRestPose> map)
        {
            if (!string.IsNullOrWhiteSpace(node.Name))
            {
                var pose = new BoneRestPose(
                    new Vector3(node.PosX, node.PosY, node.PosZ),
                    new Quaternion(node.Qx, node.Qy, node.Qz, node.Qw),
                    new Vector3(node.ScaleX, node.ScaleY, node.ScaleZ));

                map[node.Name] = pose;
                map[MapAssimpFbxRotationNode(node.Name)] = pose;
            }

            foreach (var child in node.Children)
                CollectSourceRestPoses(child, map);
        }

        private static void RetargetPoseFromSourceRest(BonePoseAccumulator acc, ref Vector3 pos, ref Quaternion rot)
        {
            var sourceRest = acc.SourceRestPose;
            pos = acc.RestPosition + (pos - sourceRest.Position);

            var sourceInv = InverseQuaternion(sourceRest.Rotation);
            var delta = MultiplyQuaternions(sourceInv, rot);
            rot = MultiplyQuaternions(acc.RestRotation, delta);
        }

        private List<float> ComputeBlendWeights(BlendTreeData tree, List<BlendTreeChildMotion> children)
        {
            var weights = Enumerable.Repeat(0f, children.Count).ToList();
            if (children.Count == 1)
            {
                weights[0] = 1f;
                return weights;
            }

            if (tree.BlendType == BlendTreeType.Simple1D)
            {
                float value = GetFloat(tree.Parameter);
                var ordered = children.Select((c, i) => (Child: c, Index: i)).OrderBy(x => x.Child.Threshold).ToList();
                if (value <= ordered[0].Child.Threshold)
                {
                    weights[ordered[0].Index] = 1f;
                    return weights;
                }
                if (value >= ordered[^1].Child.Threshold)
                {
                    weights[ordered[^1].Index] = 1f;
                    return weights;
                }
                for (int i = 0; i < ordered.Count - 1; i++)
                {
                    var a = ordered[i];
                    var b = ordered[i + 1];
                    if (value < a.Child.Threshold || value > b.Child.Threshold)
                        continue;
                    float span = b.Child.Threshold - a.Child.Threshold;
                    float t = Math.Abs(span) > 0.0001f ? Math.Clamp((value - a.Child.Threshold) / span, 0f, 1f) : 0f;
                    weights[a.Index] = 1f - t;
                    weights[b.Index] = t;
                    return weights;
                }
            }
            else
            {
                return ComputeFreeformDirectionalWeights(tree, children);
            }

            if (weights.Sum() <= 0f)
                weights[0] = 1f;
            return weights;
        }

        private List<float> ComputeFreeformDirectionalWeights(BlendTreeData tree, List<BlendTreeChildMotion> children)
        {
            var weights = Enumerable.Repeat(0f, children.Count).ToList();
            float x = GetFloat(tree.ParameterX);
            float y = GetFloat(tree.ParameterY);
            float inputMagnitude = MathF.Sqrt(x * x + y * y);

            int centerIndex = -1;
            var directions = new List<(int Index, float X, float Y, float Radius, float Angle)>();
            for (int i = 0; i < children.Count; i++)
            {
                float cx = children[i].PosX;
                float cy = children[i].PosY;
                float radius = MathF.Sqrt(cx * cx + cy * cy);
                if (radius <= 0.0001f && centerIndex < 0)
                    centerIndex = i;
                else if (radius > 0.0001f)
                    directions.Add((i, cx, cy, radius, MathF.Atan2(cy, cx)));

                if (Distance2D(x, y, cx, cy) <= 0.0001f)
                {
                    weights[i] = 1f;
                    return weights;
                }
            }

            if (centerIndex >= 0 && inputMagnitude <= 0.0001f)
            {
                weights[centerIndex] = 1f;
                return weights;
            }

            if (directions.Count == 0)
            {
                weights[Math.Max(centerIndex, 0)] = 1f;
                return weights;
            }

            directions.Sort((a, b) => a.Angle.CompareTo(b.Angle));
            if (directions.Count == 1)
            {
                float radius = directions[0].Radius;
                float motionWeight = centerIndex >= 0
                    ? Math.Clamp(inputMagnitude / MathF.Max(radius, 0.0001f), 0f, 1f)
                    : 1f;
                if (centerIndex >= 0)
                    weights[centerIndex] = 1f - motionWeight;
                weights[directions[0].Index] = motionWeight;
                return weights;
            }

            float inputAngle = MathF.Atan2(y, x);
            int aIndex = 0;
            int bIndex = 0;
            float angularT = 0f;
            FindAngularBlend(directions, inputAngle, out aIndex, out bIndex, out angularT);

            var a = directions[aIndex];
            var b = directions[bIndex];
            float aAngularWeight = 1f - angularT;
            float bAngularWeight = angularT;
            float blendedRadius = MathF.Max(0.0001f, a.Radius * aAngularWeight + b.Radius * bAngularWeight);

            float centerWeight = centerIndex >= 0
                ? Math.Clamp(1f - inputMagnitude / blendedRadius, 0f, 1f)
                : 0f;
            float motionScale = 1f - centerWeight;

            if (centerIndex >= 0)
                weights[centerIndex] = centerWeight;

            weights[a.Index] += aAngularWeight * motionScale;
            weights[b.Index] += bAngularWeight * motionScale;

            float sum = weights.Sum();
            if (sum <= 0.0001f)
            {
                int best = centerIndex >= 0 ? centerIndex : 0;
                float bestDistance = centerIndex >= 0 ? inputMagnitude : float.MaxValue;
                for (int i = 0; i < children.Count; i++)
                {
                    float d = Distance2D(x, y, children[i].PosX, children[i].PosY);
                    if (d < bestDistance)
                    {
                        best = i;
                        bestDistance = d;
                    }
                }
                weights[best] = 1f;
                return weights;
            }

            for (int i = 0; i < weights.Count; i++)
                weights[i] /= sum;
            return weights;
        }

        private static void FindAngularBlend(
            List<(int Index, float X, float Y, float Radius, float Angle)> directions,
            float inputAngle,
            out int aIndex,
            out int bIndex,
            out float t)
        {
            aIndex = 0;
            bIndex = 0;
            t = 0f;

            for (int i = 0; i < directions.Count; i++)
            {
                int next = (i + 1) % directions.Count;
                float span = PositiveAngleDelta(directions[next].Angle, directions[i].Angle);
                if (span <= 0.0001f || span > MathF.PI)
                    continue;

                float delta = PositiveAngleDelta(inputAngle, directions[i].Angle);
                if (delta <= span + 0.0001f)
                {
                    aIndex = i;
                    bIndex = next;
                    t = Math.Clamp(delta / span, 0f, 1f);
                    return;
                }
            }

            float best = float.MaxValue;
            for (int i = 0; i < directions.Count; i++)
            {
                float d = MathF.Min(
                    PositiveAngleDelta(inputAngle, directions[i].Angle),
                    PositiveAngleDelta(directions[i].Angle, inputAngle));
                if (d < best)
                {
                    best = d;
                    aIndex = i;
                    bIndex = i;
                    t = 0f;
                }
            }
        }

        private static float PositiveAngleDelta(float to, float from)
        {
            float delta = to - from;
            while (delta < 0f) delta += MathF.Tau;
            while (delta >= MathF.Tau) delta -= MathF.Tau;
            return delta;
        }

        public IReadOnlyList<AnimatorBlendWeightInfo> GetBlendWeights()
        {
            var activeState = GetActiveState();
            if (activeState?.MotionType != AnimatorMotionType.BlendTree)
                return Array.Empty<AnimatorBlendWeightInfo>();

            var tree = activeState.BlendTree;
            tree.Normalize();
            var children = tree.Children
                .Where(c => !string.IsNullOrWhiteSpace(c.MotionPath))
                .ToList();
            if (children.Count == 0)
                return Array.Empty<AnimatorBlendWeightInfo>();

            var weights = ComputeBlendWeights(tree, children);
            var result = new AnimatorBlendWeightInfo[children.Count];
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                string displayName = Path.GetFileNameWithoutExtension(child.MotionPath);
                result[i] = new AnimatorBlendWeightInfo(
                    child.MotionPath,
                    string.IsNullOrWhiteSpace(displayName) ? child.MotionPath : displayName,
                    i < weights.Count ? weights[i] : 0f,
                    child.PosX,
                    child.PosY,
                    child.Threshold);
            }

            return result;
        }

        private static float Distance2D(float ax, float ay, float bx, float by)
        {
            float dx = ax - bx;
            float dy = ay - by;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        private BlendClipRuntime? GetDominantBlendTreeClip(BlendTreeData tree)
        {
            tree.Normalize();
            var children = tree.Children.Where(c => !string.IsNullOrWhiteSpace(c.MotionPath)).ToList();
            if (children.Count == 0)
                return null;
            var weights = ComputeBlendWeights(tree, children);
            if (weights.Count == 0 || weights.Sum() <= 0.0001f)
                return null;
            int best = 0;
            for (int i = 1; i < weights.Count; i++)
                if (weights[i] > weights[best])
                    best = i;
            return LoadBlendClip(children[best].MotionPath);
        }

        private BlendClipRuntime? LoadBlendClip(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            if (AnimationClipAsset.IsAnimationPath(path))
            {
                var data = AnimationClipAsset.Load(path);
                if (string.IsNullOrWhiteSpace(data.SourceModelPath) || !File.Exists(data.SourceModelPath))
                    return null;
                var mesh = ObjLoader.Load(data.SourceModelPath);
                var clip = ResolveClip(mesh, data.SourceClipName);
                return clip == null ? null : new BlendClipRuntime
                {
                    Path = path,
                    Name = data.Name,
                    Clip = clip,
                    Settings = data
                };
            }

            if (!ObjLoader.IsSupportedMesh(path))
                return null;
            var model = ObjLoader.Load(path);
            var modelClip = ResolveClip(model, "");
            return modelClip == null ? null : new BlendClipRuntime
            {
                Path = path,
                Name = Path.GetFileNameWithoutExtension(path),
                Clip = modelClip,
                Settings = FromImportSettings(ModelImportSettingsAsset.Load(path))
            };
        }

        private static SkeletalClip? ResolveClip(ParsedMesh? mesh, string clipName)
        {
            if (mesh?.Clips == null || mesh.Clips.Count == 0)
                return null;
            if (!string.IsNullOrWhiteSpace(clipName))
            {
                var exact = mesh.Clips.FirstOrDefault(c => string.Equals(c.Name, clipName, StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                    return exact;
            }
            return mesh.Clips[0];
        }

        private void ApplySkeletalRootMotion(SkeletalClip skeletal, float previousTime, float currentTime, bool wrapped)
        {
            var settings = GetPlaybackSettings();
            if (!ApplyRootMotion || skeletal.Duration <= 0f || !HasExtractableRootMotion(settings))
            {
                ResetRootMotion();
                return;
            }

            string key = EffectiveClipPath() + "|" + skeletal.Name + "|" + CurrentState;
            if (!string.Equals(rootMotionKey, key, StringComparison.Ordinal))
            {
                rootMotionKey = key;
                rootMotionInitialized = false;
            }

            var current = SampleRootMotionFrame(skeletal, settings, currentTime);
            if (!current.Valid)
            {
                ResetRootMotion();
                return;
            }

            if (!rootMotionInitialized)
            {
                lastRootMotionFrame = current;
                rootMotionInitialized = true;
                return;
            }

            if (wrapped && previousTime > currentTime)
            {
                var end = SampleRootMotionFrame(skeletal, settings, skeletal.Duration);
                if (end.Valid)
                    ApplyRootMotionDelta(settings, lastRootMotionFrame, end);

                var start = SampleRootMotionFrame(skeletal, settings, 0f);
                if (start.Valid)
                    ApplyRootMotionDelta(settings, start, current);
            }
            else
            {
                ApplyRootMotionDelta(settings, lastRootMotionFrame, current);
            }

            lastRootMotionFrame = current;
        }

        private void ResetRootMotion()
        {
            lastRootMotionFrame = default;
            rootMotionKey = "";
            rootMotionInitialized = false;
        }

        private static bool HasExtractableRootMotion(AnimationClipData settings)
        {
            return !settings.BakeRootPositionYIntoPose ||
                   !settings.BakeRootPositionXZIntoPose ||
                   !settings.BakeRootRotationIntoPose;
        }

        private static BoneChannel? FindRootMotionChannel(SkeletalClip skeletal)
        {
            return skeletal.Channels
                       .Where(c => IsRootMotionChannel(c.NodeName))
                       .OrderByDescending(c => c.Positions.Count)
                       .FirstOrDefault() ??
                   skeletal.Channels
                       .Where(c => MapAssimpFbxRotationNode(c.NodeName).Contains("Hips", StringComparison.OrdinalIgnoreCase))
                       .OrderByDescending(c => c.Positions.Count)
                       .FirstOrDefault() ??
                   skeletal.Channels
                       .Where(c => MapAssimpFbxRotationNode(c.NodeName).Contains("Root", StringComparison.OrdinalIgnoreCase))
                       .OrderByDescending(c => c.Positions.Count)
                       .FirstOrDefault();
        }

        private static RootMotionFrame SampleRootMotionFrame(SkeletalClip skeletal, AnimationClipData settings, float rawTime)
        {
            var channel = FindRootMotionChannel(skeletal);
            if (channel == null)
                return new RootMotionFrame(false, Vector3.Zero, 0f);

            var rotationChannel = FindRootRotationChannel(skeletal) ?? channel;
            float sampleTime = ApplyCycleOffset(rawTime, skeletal.Duration, settings.CycleOffset);
            var firstPos = channel.Positions.Count > 0 ? channel.Positions[0].Value : Vector3.Zero;
            var firstRot = rotationChannel.Rotations.Count > 0 ? rotationChannel.Rotations[0].Value : new Quaternion(0f, 0f, 0f, 1f);
            var pos = SampleVec(channel.Positions, sampleTime, firstPos);
            var rot = SampleQuat(rotationChannel.Rotations, sampleTime, firstRot);

            if (string.Equals(settings.RootPositionYBasedUpon, "Feet", StringComparison.OrdinalIgnoreCase) &&
                TrySampleFeetY(skeletal, sampleTime, out float feetY))
            {
                pos.Y = feetY;
            }

            float yaw = YawFromQuaternion(rot);
            if (settings.Mirror)
            {
                pos.X = -pos.X;
                yaw = -yaw;
            }

            return new RootMotionFrame(true, pos, yaw);
        }

        private static BoneChannel? FindRootRotationChannel(SkeletalClip skeletal)
        {
            return skeletal.Channels
                       .Where(c => IsExplicitRootMotionChannel(c.NodeName))
                       .OrderByDescending(c => c.Rotations.Count)
                       .FirstOrDefault(c => c.Rotations.Count > 0);
        }

        private void ApplyRootMotionDelta(AnimationClipData settings, RootMotionFrame from, RootMotionFrame to)
        {
            if (!from.Valid || !to.Valid)
                return;

            var delta = to.Position - from.Position;
            if (settings.BakeRootPositionYIntoPose)
                delta.Y = 0f;
            if (settings.BakeRootPositionXZIntoPose)
            {
                delta.X = 0f;
                delta.Z = 0f;
            }

            if (MathF.Abs(delta.X) > 0.000001f || MathF.Abs(delta.Y) > 0.000001f || MathF.Abs(delta.Z) > 0.000001f)
            {
                delta.X *= gameObject.ScaleX;
                delta.Y *= gameObject.ScaleY;
                delta.Z *= gameObject.ScaleZ;
                var worldDelta = RotateY(delta, gameObject.RotY * MathF.PI / 180f);
                gameObject.PosX += worldDelta.X;
                gameObject.PosY += worldDelta.Y;
                gameObject.PosZ += worldDelta.Z;
            }

            if (!settings.BakeRootRotationIntoPose)
            {
                float yawDelta = NormalizeRadians(to.YawRadians - from.YawRadians);
                if (MathF.Abs(yawDelta) > 0.000001f)
                    gameObject.RotY += yawDelta * 180f / MathF.PI;
            }
        }

        private static void RemoveExtractedRootMotionFromPose(BoneChannel channel, AnimationClipData settings, ref Vector3 pos, ref Quaternion rot)
        {
            var firstPos = channel.Positions.Count > 0 ? channel.Positions[0].Value : pos;
            if (!settings.BakeRootPositionYIntoPose)
                pos.Y = firstPos.Y + settings.RootPositionYOffset;
            if (!settings.BakeRootPositionXZIntoPose)
            {
                pos.X = firstPos.X;
                pos.Z = firstPos.Z;
            }

            if (!settings.BakeRootRotationIntoPose && channel.Rotations.Count > 0)
            {
                float firstYaw = YawFromQuaternion(channel.Rotations[0].Value);
                float currentYaw = YawFromQuaternion(rot);
                float deltaYaw = NormalizeRadians(currentYaw - firstYaw);
                rot = MultiplyYaw(rot, -deltaYaw);
            }
        }

        private static void RemoveExtractedRootMotionFromPose(AnimationClipData settings, Vector3 firstPos, Quaternion firstRot, ref Vector3 pos, ref Quaternion rot)
        {
            if (!settings.BakeRootPositionYIntoPose)
                pos.Y = firstPos.Y + settings.RootPositionYOffset;
            if (!settings.BakeRootPositionXZIntoPose)
            {
                pos.X = firstPos.X;
                pos.Z = firstPos.Z;
            }

            if (!settings.BakeRootRotationIntoPose)
            {
                float firstYaw = YawFromQuaternion(firstRot);
                float currentYaw = YawFromQuaternion(rot);
                float deltaYaw = NormalizeRadians(currentYaw - firstYaw);
                rot = MultiplyYaw(rot, -deltaYaw);
            }
        }

        private static bool TrySampleFeetY(SkeletalClip skeletal, float sampleTime, out float y)
        {
            y = 0f;
            bool found = false;
            foreach (var channel in skeletal.Channels)
            {
                string name = MapAssimpFbxRotationNode(channel.NodeName);
                if (!name.Contains("Foot", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Toe", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (channel.Positions.Count == 0)
                    continue;

                var fallback = channel.Positions.Count > 0 ? channel.Positions[0].Value : Vector3.Zero;
                var pos = SampleVec(channel.Positions, sampleTime, fallback);
                if (!found || pos.Y < y)
                {
                    y = pos.Y;
                    found = true;
                }
            }

            return found;
        }

        private static Vector3 RotateY(Vector3 value, float radians)
        {
            float s = MathF.Sin(radians);
            float c = MathF.Cos(radians);
            return new Vector3(
                value.X * c + value.Z * s,
                value.Y,
                -value.X * s + value.Z * c);
        }

        private static float YawFromQuaternion(Quaternion rot)
        {
            float siny = 2f * (rot.W * rot.Y + rot.X * rot.Z);
            float cosy = 1f - 2f * (rot.Y * rot.Y + rot.X * rot.X);
            return MathF.Atan2(siny, cosy);
        }

        private static Quaternion MultiplyYaw(Quaternion rot, float yawRadians)
        {
            var yaw = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, yawRadians);
            var q = new System.Numerics.Quaternion(rot.X, rot.Y, rot.Z, rot.W);
            var result = System.Numerics.Quaternion.Normalize(yaw * q);
            return new Quaternion(result.X, result.Y, result.Z, result.W);
        }

        private static Quaternion RemoveYaw(Quaternion rot)
        {
            float yaw = YawFromQuaternion(rot);
            return MultiplyYaw(rot, -yaw);
        }

        private static Quaternion MirrorQuaternionX(Quaternion rot)
        {
            var result = System.Numerics.Quaternion.Normalize(
                new System.Numerics.Quaternion(rot.X, -rot.Y, -rot.Z, rot.W));
            return new Quaternion(result.X, result.Y, result.Z, result.W);
        }

        private static float NormalizeRadians(float angle)
        {
            while (angle > MathF.PI) angle -= MathF.PI * 2f;
            while (angle < -MathF.PI) angle += MathF.PI * 2f;
            return angle;
        }

        private static float ApplyCycleOffset(float time, float duration, float cycleOffset)
        {
            if (duration <= 0f || MathF.Abs(cycleOffset) < 0.0001f)
                return time;
            float t = time + duration * cycleOffset;
            t %= duration;
            if (t < 0f) t += duration;
            return t;
        }

        private static BoneChannel FindMirroredChannel(SkeletalClip clip, BoneChannel channel)
        {
            string mirroredName = MirrorBoneName(channel.NodeName);
            if (string.Equals(mirroredName, channel.NodeName, StringComparison.Ordinal))
                return channel;

            return clip.Channels.FirstOrDefault(c => string.Equals(c.NodeName, mirroredName, StringComparison.OrdinalIgnoreCase))
                   ?? channel;
        }

        private static string MirrorBoneName(string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName))
                return nodeName;
            if (nodeName.Contains("Left", StringComparison.Ordinal))
                return nodeName.Replace("Left", "Right", StringComparison.Ordinal);
            if (nodeName.Contains("Right", StringComparison.Ordinal))
                return nodeName.Replace("Right", "Left", StringComparison.Ordinal);
            return nodeName;
        }

        private static void BlendLoopPose(BoneChannel channel, float time, float duration, Vector3 fallbackPos, Quaternion fallbackRot, Vector3 fallbackScale, ref Vector3 pos, ref Quaternion rot, ref Vector3 scale)
        {
            if (duration <= 0f)
                return;
            float normalized = Math.Clamp(time / duration, 0f, 1f);
            if (normalized < 0.85f)
                return;

            float blend = (normalized - 0.85f) / 0.15f;
            blend = blend * blend * (3f - 2f * blend);
            var firstPos = channel.Positions.Count > 0 ? channel.Positions[0].Value : fallbackPos;
            var firstRot = channel.Rotations.Count > 0 ? channel.Rotations[0].Value : fallbackRot;
            var firstScale = channel.Scales.Count > 0 ? channel.Scales[0].Value : fallbackScale;
            pos = Vector3.Lerp(pos, firstPos, blend);
            rot = Quaternion.Slerp(rot, firstRot, blend);
            scale = Vector3.Lerp(scale, firstScale, blend);
        }

        private static void ApplyAdditiveReferencePose(BoneChannel channel, Vector3 fallbackPos, Vector3 fallbackScale, ref Vector3 pos, ref Vector3 scale)
        {
            var firstPos = channel.Positions.Count > 0 ? channel.Positions[0].Value : fallbackPos;
            var firstScale = channel.Scales.Count > 0 ? channel.Scales[0].Value : fallbackScale;
            pos = fallbackPos + (pos - firstPos);
            scale = fallbackScale + (scale - firstScale);
        }

        private static bool IsRootMotionChannel(string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName))
                return false;
            string mapped = MapAssimpFbxRotationNode(nodeName);
            return mapped.EndsWith(":Hips", StringComparison.OrdinalIgnoreCase) ||
                   mapped.Equals("Hips", StringComparison.OrdinalIgnoreCase) ||
                   mapped.EndsWith(":Root", StringComparison.OrdinalIgnoreCase) ||
                   mapped.Equals("Root", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExplicitRootMotionChannel(string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName))
                return false;
            string mapped = MapAssimpFbxRotationNode(nodeName);
            return mapped.EndsWith(":Root", StringComparison.OrdinalIgnoreCase) ||
                   mapped.Equals("Root", StringComparison.OrdinalIgnoreCase) ||
                   mapped.EndsWith(":Armature", StringComparison.OrdinalIgnoreCase) ||
                   mapped.Equals("Armature", StringComparison.OrdinalIgnoreCase) ||
                   mapped.EndsWith(":mixamorig", StringComparison.OrdinalIgnoreCase) ||
                   mapped.Equals("mixamorig", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyRootBake(BoneChannel channel, AnimationClipData settings, Vector3 fallbackPos, Quaternion fallbackRot, ref Vector3 pos, ref Quaternion rot)
        {
            var firstPos = channel.Positions.Count > 0 ? channel.Positions[0].Value : fallbackPos;
            var firstRot = channel.Rotations.Count > 0 ? channel.Rotations[0].Value : fallbackRot;
            ApplyRootBake(settings, fallbackPos, fallbackRot, firstPos, firstRot, ref pos, ref rot);
        }

        private static void ApplyRootBake(AnimationClipData settings, Vector3 fallbackPos, Quaternion fallbackRot, Vector3 firstPos, Quaternion firstRot, ref Vector3 pos, ref Quaternion rot)
        {
            if (!settings.BakeRootRotationIntoPose)
            {
                float firstYaw = YawFromQuaternion(firstRot);
                float currentYaw = YawFromQuaternion(rot);
                float deltaYaw = NormalizeRadians(currentYaw - firstYaw);
                rot = MultiplyYaw(rot, -deltaYaw);
            }
            if (MathF.Abs(settings.RootRotationOffset) > 0.0001f)
                rot = ApplyRootRotationOffset(rot, settings.RootRotationOffset);

            if (settings.BakeRootPositionYIntoPose)
            {
                pos.Y = string.Equals(settings.RootPositionYBasedUpon, "Original", StringComparison.OrdinalIgnoreCase)
                    ? fallbackPos.Y + settings.RootPositionYOffset
                    : firstPos.Y + settings.RootPositionYOffset;
            }

            if (settings.BakeRootPositionXZIntoPose)
            {
                if (string.Equals(settings.RootPositionXZBasedUpon, "Center Of Mass", StringComparison.OrdinalIgnoreCase))
                {
                    pos.X = 0f;
                    pos.Z = 0f;
                }
                else
                {
                    pos.X = fallbackPos.X;
                    pos.Z = fallbackPos.Z;
                }
            }
        }

        private static Quaternion ApplyRootRotationOffset(Quaternion rot, float offsetDegrees)
        {
            var q = new System.Numerics.Quaternion(rot.X, rot.Y, rot.Z, rot.W);
            var offset = System.Numerics.Quaternion.CreateFromAxisAngle(
                System.Numerics.Vector3.UnitY,
                offsetDegrees * MathF.PI / 180f);
            var result = System.Numerics.Quaternion.Normalize(offset * q);
            return new Quaternion(result.X, result.Y, result.Z, result.W);
        }

        private static Quaternion MultiplyQuaternions(Quaternion a, Quaternion b)
        {
            var qa = new System.Numerics.Quaternion(a.X, a.Y, a.Z, a.W);
            var qb = new System.Numerics.Quaternion(b.X, b.Y, b.Z, b.W);
            var result = System.Numerics.Quaternion.Normalize(qa * qb);
            return new Quaternion(result.X, result.Y, result.Z, result.W);
        }

        private static Quaternion InverseQuaternion(Quaternion q)
        {
            var source = new System.Numerics.Quaternion(q.X, q.Y, q.Z, q.W);
            var result = System.Numerics.Quaternion.Inverse(source);
            return new Quaternion(result.X, result.Y, result.Z, result.W);
        }

        private bool TryResolveAnimationBone(string channelName, out GameObject bone)
            => TryResolveAnimationBone(channelName, out bone, out _);

        private bool TryResolveAnimationBone(string channelName, out GameObject bone, out bool usedMappedName)
        {
            EnsureBoneMap();
            usedMappedName = false;
            if (boneMap!.TryGetValue(channelName, out bone!))
                return true;

            string mapped = MapAssimpFbxRotationNode(channelName);
            if (!string.Equals(mapped, channelName, StringComparison.Ordinal) &&
                boneMap.TryGetValue(mapped, out bone!))
            {
                usedMappedName = true;
                return true;
            }

            return false;
        }

        private static string MapAssimpFbxRotationNode(string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName))
                return nodeName;

            string[] suffixes =
            {
                "_$AssimpFbx$_Translation",
                "_$AssimpFbx$_PreRotation",
                "_$AssimpFbx$_Rotation",
                "_$AssimpFbx$_Scaling"
            };

            foreach (string suffix in suffixes)
                if (nodeName.EndsWith(suffix, StringComparison.Ordinal))
                    return nodeName[..^suffix.Length];

            return nodeName;
        }

        private static Vector3 SampleVec(List<BoneVecKey> keys, float t, Vector3 fallback)
        {
            if (keys.Count == 0) return fallback;
            if (keys.Count == 1 || t <= keys[0].Time) return keys[0].Value;
            if (t >= keys[^1].Time) return keys[^1].Value;
            for (int i = 0; i < keys.Count - 1; i++)
            {
                if (t >= keys[i].Time && t <= keys[i + 1].Time)
                {
                    float span = keys[i + 1].Time - keys[i].Time;
                    float f = span > 1e-6f ? (t - keys[i].Time) / span : 0f;
                    return Vector3.Lerp(keys[i].Value, keys[i + 1].Value, f);
                }
            }
            return keys[^1].Value;
        }

        private static Quaternion SampleQuat(List<BoneQuatKey> keys, float t, Quaternion fallback)
        {
            if (keys.Count == 0) return fallback;
            if (keys.Count == 1 || t <= keys[0].Time) return keys[0].Value;
            if (t >= keys[^1].Time) return keys[^1].Value;
            for (int i = 0; i < keys.Count - 1; i++)
            {
                if (t >= keys[i].Time && t <= keys[i + 1].Time)
                {
                    float span = keys[i + 1].Time - keys[i].Time;
                    float f = span > 1e-6f ? (t - keys[i].Time) / span : 0f;
                    return Quaternion.Slerp(keys[i].Value, keys[i + 1].Value, f);
                }
            }
            return keys[^1].Value;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float LerpAngle(float a, float b, float t)
        {
            float delta = b - a;
            delta -= MathF.Floor((delta + 180f) / 360f) * 360f;
            return a + delta * t;
        }
    }
}
