using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GrokoEngine
{
    public enum AnimatorParameterType { Bool, Float, Int, Trigger }

    // Modo de comparación de una condición de transición (estilo Mecanim de Unity).
    public enum AnimatorConditionMode { If, IfNot, Greater, Less, Equals, NotEquals }

    public class AnimatorParameter
    {
        public string Name { get; set; } = "Param";
        public AnimatorParameterType Type { get; set; } = AnimatorParameterType.Bool;
        public float DefaultValue { get; set; } // bool/trigger: 0/1; float/int: valor
    }

    public class AnimatorCondition
    {
        public string Parameter { get; set; } = "";
        public AnimatorConditionMode Mode { get; set; } = AnimatorConditionMode.If;
        public float Threshold { get; set; }
    }

    public enum AnimatorInterruptionSource { None, CurrentState, NextState, CurrentStateThenNextState, NextStateThenCurrentState }
    public enum AnimatorMotionType { Clip, BlendTree }
    public enum BlendTreeType { Simple1D, FreeformDirectional2D }

    public class AnimatorTransition
    {
        public string ToState { get; set; } = "";
        public bool HasExitTime { get; set; } = true;
        public float ExitTime { get; set; } = 0.75f;       // normalizado 0..1
        public bool FixedDuration { get; set; } = true;
        public float TransitionDuration { get; set; } = 0.25f; // segundos (o normalizado si !FixedDuration)
        public float TransitionOffset { get; set; }            // 0..1
        public AnimatorInterruptionSource Interruption { get; set; } = AnimatorInterruptionSource.None;
        public bool OrderedInterruption { get; set; } = true;
        public bool Solo { get; set; }
        public bool Mute { get; set; }
        public List<AnimatorCondition> Conditions { get; set; } = new();
    }

    public class AnimatorStateData
    {
        public string Name { get; set; } = "New State";
        public string ClipPath { get; set; } = "";
        public AnimatorMotionType MotionType { get; set; } = AnimatorMotionType.Clip;
        public BlendTreeData BlendTree { get; set; } = new();
        public float Speed { get; set; } = 1f;
        public bool Loop { get; set; } = true;
        public float EditorX { get; set; } = 120f; // posición del nodo en el grafo
        public float EditorY { get; set; } = 80f;
        public List<AnimatorTransition> Transitions { get; set; } = new();
    }

    public class BlendTreeChildMotion
    {
        public string MotionPath { get; set; } = "";
        public float Threshold { get; set; }
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float Speed { get; set; } = 1f;
        public bool Mirror { get; set; }
    }

    public class BlendTreeData
    {
        public string Name { get; set; } = "Blend Tree";
        public BlendTreeType BlendType { get; set; } = BlendTreeType.FreeformDirectional2D;
        public string Parameter { get; set; } = "Blend";
        public string ParameterX { get; set; } = "VelX";
        public string ParameterY { get; set; } = "VelY";
        public List<BlendTreeChildMotion> Children { get; set; } = new();

        public void Normalize()
        {
            Name ??= "Blend Tree";
            Parameter ??= "Blend";
            ParameterX ??= "VelX";
            ParameterY ??= "VelY";
            Children ??= new List<BlendTreeChildMotion>();
            foreach (var child in Children)
            {
                child.MotionPath ??= "";
                if (!float.IsFinite(child.Threshold)) child.Threshold = 0f;
                if (!float.IsFinite(child.PosX)) child.PosX = child.Threshold;
                if (!float.IsFinite(child.PosY)) child.PosY = 0f;
                if (!float.IsFinite(child.Speed) || MathF.Abs(child.Speed) < 0.0001f) child.Speed = 1f;
            }
        }
    }

    public class AnimatorControllerData
    {
        public string Name { get; set; } = "New Animator Controller";
        public List<AnimatorStateData> States { get; set; } = new();
        public string DefaultState { get; set; } = "";
        public List<AnimatorTransition> AnyStateTransitions { get; set; } = new();
        public List<AnimatorParameter> Parameters { get; set; } = new();

        public void Normalize()
        {
            Name ??= "New Animator Controller";
            States ??= new List<AnimatorStateData>();
            AnyStateTransitions ??= new List<AnimatorTransition>();
            Parameters ??= new List<AnimatorParameter>();
            foreach (var s in States)
            {
                s.Name ??= "New State";
                s.ClipPath ??= "";
                s.BlendTree ??= new BlendTreeData();
                s.BlendTree.Normalize();
                s.Transitions ??= new List<AnimatorTransition>();
                foreach (var t in s.Transitions)
                {
                    t.ToState ??= "";
                    t.Conditions ??= new List<AnimatorCondition>();
                    foreach (var c in t.Conditions)
                        c.Parameter ??= "";
                }
            }
            foreach (var t in AnyStateTransitions)
            {
                t.ToState ??= "";
                t.Conditions ??= new List<AnimatorCondition>();
                foreach (var c in t.Conditions)
                    c.Parameter ??= "";
            }
            foreach (var p in Parameters)
                p.Name ??= "Param";
            if (string.IsNullOrWhiteSpace(DefaultState) && States.Count > 0)
                DefaultState = States[0].Name;
            var defaultState = GetDefaultState();
            if (defaultState != null && string.IsNullOrWhiteSpace(defaultState.ClipPath) && defaultState.MotionType != AnimatorMotionType.BlendTree)
            {
                var firstPlayable = States.FirstOrDefault(IsPlayableState);
                if (firstPlayable != null)
                    DefaultState = firstPlayable.Name;
            }
        }

        public AnimatorStateData? GetDefaultState()
        {
            if (States.Count == 0) return null;
            return States.FirstOrDefault(s => s.Name == DefaultState) ?? States[0];
        }

        public AnimatorStateData? FindState(string name) =>
            string.IsNullOrWhiteSpace(name) ? null
            : States.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        public int ClipCount() => States.Count(IsPlayableState);

        private static bool IsPlayableState(AnimatorStateData s) =>
            s.MotionType == AnimatorMotionType.BlendTree
                ? s.BlendTree.Children.Any(c => !string.IsNullOrWhiteSpace(c.MotionPath))
                : !string.IsNullOrWhiteSpace(s.ClipPath);
    }

    public static class AnimatorControllerAsset
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };

        public static bool IsControllerPath(string path) =>
            path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase);

        public static string Create(string directory, string baseName = "New Animator Controller")
        {
            Directory.CreateDirectory(directory);
            string path = GetUniquePath(Path.Combine(directory, baseName + ".controller"));
            var data = new AnimatorControllerData
            {
                Name = Path.GetFileNameWithoutExtension(path)
            };
            Save(path, data);
            return path;
        }

        public static AnimatorControllerData Load(string path)
        {
            try
            {
                var text = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<AnimatorControllerData>(text) ?? new AnimatorControllerData();
                data.Normalize();
                return data;
            }
            catch
            {
                return new AnimatorControllerData { Name = Path.GetFileNameWithoutExtension(path) };
            }
        }

        public static void Save(string path, AnimatorControllerData data)
        {
            data.Normalize();
            data.Name = string.IsNullOrWhiteSpace(data.Name)
                ? Path.GetFileNameWithoutExtension(path)
                : data.Name;
            File.WriteAllText(path, JsonSerializer.Serialize(data, Options));
        }

        private static string GetUniquePath(string desiredPath)
        {
            if (!File.Exists(desiredPath)) return desiredPath;

            string directory = Path.GetDirectoryName(desiredPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(desiredPath);
            string extension = Path.GetExtension(desiredPath);
            int i = 1;
            string candidate;
            do candidate = Path.Combine(directory, $"{name}_{i++}{extension}");
            while (File.Exists(candidate));
            return candidate;
        }
    }
}
