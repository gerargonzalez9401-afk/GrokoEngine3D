using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GrokoEngine
{
    // Avatar (mapa de esqueleto) de un modelo, estilo Unity. Para Mixamo el "mapa" es
    // simplemente el conjunto de huesos por nombre; el retargeting se hace por nombre.
    public class AvatarData
    {
        public string Name { get; set; } = "Avatar";
        public string SourceModel { get; set; } = "";   // ruta del FBX del que se creó
        public string RootBone { get; set; } = "";       // nombre del hueso raíz (armature)
        public List<string> BoneNames { get; set; } = new();

        public void Normalize()
        {
            Name ??= "Avatar";
            SourceModel ??= "";
            RootBone ??= "";
            BoneNames ??= new List<string>();
        }
    }

    public static class AvatarAsset
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };

        public static bool IsAvatarPath(string path) =>
            path != null && path.EndsWith(".avatar", StringComparison.OrdinalIgnoreCase);

        // Crea un .avatar a partir de un modelo (lee su esqueleto). Devuelve la ruta del avatar.
        public static string CreateFromModel(string modelPath)
        {
            var data = new AvatarData
            {
                Name = Path.GetFileNameWithoutExtension(modelPath) + " Avatar",
                SourceModel = modelPath
            };

            var mesh = ObjLoader.Load(modelPath);
            if (mesh != null)
            {
                // Nombres de hueso para el retargeting (bones de skinning).
                foreach (var bn in mesh.BoneNames)
                    if (!string.IsNullOrWhiteSpace(bn))
                        data.BoneNames.Add(bn);
                // Hueso raíz = primer nodo de la jerarquía.
                if (mesh.Hierarchy != null && mesh.Hierarchy.Children.Count > 0)
                    data.RootBone = mesh.Hierarchy.Children[0].Name;
            }

            string path = modelPath + ".avatar";
            Save(path, data);
            return path;
        }

        public static AvatarData Load(string path)
        {
            try
            {
                var d = JsonSerializer.Deserialize<AvatarData>(File.ReadAllText(path)) ?? new AvatarData();
                d.Normalize();
                return d;
            }
            catch { return new AvatarData { Name = Path.GetFileNameWithoutExtension(path) }; }
        }

        public static bool IsCompatibleWithModel(string avatarPath, string modelPath, out string message)
        {
            message = "";
            if (string.IsNullOrWhiteSpace(avatarPath) || !File.Exists(avatarPath))
            {
                message = "Avatar no asignado.";
                return false;
            }

            try
            {
                var avatar = Load(avatarPath);
                var mesh = ObjLoader.Load(modelPath);
                if (mesh == null)
                {
                    message = "No se pudo leer el modelo.";
                    return false;
                }

                var modelBones = GetModelRigBoneNames(mesh);
                if (avatar.BoneNames.Count == 0 || modelBones.Count == 0)
                {
                    message = mesh.Animations.Count > 0
                        ? "El FBX de animacion no expone huesos compatibles."
                        : "El avatar o el modelo no tienen huesos.";
                    return false;
                }

                int matched = avatar.BoneNames.Count(b => modelBones.Contains(b));
                float ratio = avatar.BoneNames.Count == 0 ? 0f : matched / (float)avatar.BoneNames.Count;
                message = $"{matched}/{avatar.BoneNames.Count} huesos compatibles";
                return ratio >= 0.65f;
            }
            catch
            {
                message = "No se pudo validar compatibilidad.";
                return false;
            }
        }

        private static HashSet<string> GetModelRigBoneNames(ParsedMesh mesh)
        {
            var bones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string bone in mesh.BoneNames)
                AddBoneName(bones, bone);

            if (mesh.Hierarchy != null)
                CollectHierarchyBoneNames(mesh.Hierarchy, bones);

            foreach (var clip in mesh.Clips)
                foreach (var channel in clip.Channels)
                    AddBoneName(bones, channel.NodeName);

            return bones;
        }

        private static void CollectHierarchyBoneNames(ModelNode node, HashSet<string> bones)
        {
            AddBoneName(bones, node.Name);
            foreach (var child in node.Children)
                CollectHierarchyBoneNames(child, bones);
        }

        private static void AddBoneName(HashSet<string> bones, string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            string normalized = NormalizeAssimpFbxNodeName(name);
            if (!string.IsNullOrWhiteSpace(normalized))
                bones.Add(normalized);
        }

        private static string NormalizeAssimpFbxNodeName(string name)
        {
            string[] suffixes =
            {
                "_$AssimpFbx$_Translation",
                "_$AssimpFbx$_PreRotation",
                "_$AssimpFbx$_Rotation",
                "_$AssimpFbx$_Scaling"
            };

            foreach (string suffix in suffixes)
                if (name.EndsWith(suffix, StringComparison.Ordinal))
                    return name[..^suffix.Length];

            return name;
        }

        public static void Save(string path, AvatarData data)
        {
            data.Normalize();
            try { File.WriteAllText(path, JsonSerializer.Serialize(data, Options)); }
            catch { }
        }
    }
}
