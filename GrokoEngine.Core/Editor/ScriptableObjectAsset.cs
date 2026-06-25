using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrokoEngine
{
    // Persiste instancias de ScriptableObject como archivos ".asset" (JSON).
    // El tipo concreto se guarda junto a los datos para poder resolverlo de nuevo
    // (puede vivir en GrokoEngine.Core o ser un script de usuario compilado).
    public static class ScriptableObjectAsset
    {
        // Prefijo para serializar campos GameObject que referencian un ASSET de prefab
        // (".prefab" del proyecto). Una instancia viva de la escena NO es persistible
        // desde un asset (solo existe mientras la escena está cargada), así que esos
        // casos se guardan como null — ver GameObjectPrefabConverter.
        private const string PrefabRefPrefix = "@@prefab:";

        // Contexto ambiental para el GameObjectPrefabConverter durante Save/Load:
        // System.Text.Json no permite pasar parámetros adicionales a los converters,
        // así que usamos una variable por hilo fijada justo antes de (de)serializar.
        [ThreadStatic]
        private static SerializationContext? _context;

        private sealed class SerializationContext
        {
            public PhysicsEngine? PhysicsEngine;
            public ScriptCompiler? ScriptCompiler;
            public string? BaseAssetsPath;
        }

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
            Converters = { new GameObjectPrefabConverter() }
        };

        private sealed class AssetEnvelope
        {
            public string TypeName { get; set; } = "";
            public JsonElement Data { get; set; }

            public void Normalize()
            {
                TypeName ??= "";
            }
        }

        // Convierte campos "GameObject" de un ScriptableObject en referencias a ASSETS de
        // prefab (ruta relativa con prefijo "@@prefab:"). Si el GameObject asignado no es
        // un prefab (es una instancia viva de la escena, sin AssetPath), se guarda como
        // null: igual que en Unity, un asset no puede retener instancias de escena.
        private sealed class GameObjectPrefabConverter : JsonConverter<GameObject>
        {
            public override bool CanConvert(Type typeToConvert) =>
                typeof(GameObject).IsAssignableFrom(typeToConvert);

            public override GameObject? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.String)
                {
                    reader.Skip();
                    return null;
                }

                string? raw = reader.GetString();
                var ctx = _context;
                if (string.IsNullOrWhiteSpace(raw) || ctx?.PhysicsEngine == null || ctx.ScriptCompiler == null ||
                    !raw.StartsWith(PrefabRefPrefix, StringComparison.Ordinal))
                    return null;

                try
                {
                    string? fullPath = SceneSerializer.ResolveAssetPath(raw.Substring(PrefabRefPrefix.Length), ctx.BaseAssetsPath);
                    if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                        return null;

                    return SceneSerializer.LoadPrefab(fullPath, ctx.PhysicsEngine, ctx.ScriptCompiler);
                }
                catch
                {
                    return null;
                }
            }

            public override void Write(Utf8JsonWriter writer, GameObject? value, JsonSerializerOptions options)
            {
                if (value == null || string.IsNullOrWhiteSpace(value.PrefabAssetPath))
                {
                    writer.WriteNullValue();
                    return;
                }

                var ctx = _context;
                string relative = ctx != null
                    ? SceneSerializer.SerializeAssetPath(value.PrefabAssetPath, ctx.BaseAssetsPath)
                    : value.PrefabAssetPath;
                writer.WriteStringValue(PrefabRefPrefix + relative);
            }
        }

        public static bool IsAssetPath(string path) =>
            path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);

        public static string Create(string directory, Type type, string? baseName = null, PhysicsEngine? physicsEngine = null)
        {
            if (type.IsAbstract || !typeof(ScriptableObject).IsAssignableFrom(type))
                throw new ArgumentException("El tipo debe heredar de ScriptableObject.", nameof(type));

            Directory.CreateDirectory(directory);
            string name = string.IsNullOrWhiteSpace(baseName) ? type.Name : baseName!;
            string path = GetUniquePath(Path.Combine(directory, name + ".asset"));

            var instance = (ScriptableObject)Activator.CreateInstance(type)!;
            instance.Name = Path.GetFileNameWithoutExtension(path);
            instance.AssetPath = path;
            Save(path, instance, physicsEngine);
            return path;
        }

        public static bool Save(string path, ScriptableObject instance, PhysicsEngine? physicsEngine = null)
        {
            instance.Name = string.IsNullOrWhiteSpace(instance.Name)
                ? Path.GetFileNameWithoutExtension(path)
                : instance.Name;
            instance.AssetPath = path;

            _context = new SerializationContext
            {
                PhysicsEngine = physicsEngine,
                ScriptCompiler = null,
                BaseAssetsPath = SceneSerializer.InferAssetsRoot(path)
            };
            try
            {
                // Un ScriptableObject se persiste como JSON plano: si algún campo referencia
                // objetos vivos de la escena (GameObject/Component/Transform), la serialización
                // podía fallar (ciclos, tipos no soportados) y lanzaba una excepción no
                // controlada que cerraba el motor. Lo capturamos para no perder el proceso.
                // Las referencias a ASSETS de prefab sí se soportan (GameObjectPrefabConverter).
                string dataJson = JsonSerializer.Serialize(instance, instance.GetType(), Options);
                using var doc = JsonDocument.Parse(dataJson);
                var envelope = new AssetEnvelope
                {
                    TypeName = GetSerializableTypeName(instance.GetType()),
                    Data = doc.RootElement.Clone()
                };
                File.WriteAllText(path, JsonSerializer.Serialize(envelope, Options));
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                _context = null;
            }
        }

        public static ScriptableObject? Load(string path, PhysicsEngine physicsEngine, ScriptCompiler scriptCompiler)
        {
            _context = new SerializationContext
            {
                PhysicsEngine = physicsEngine,
                ScriptCompiler = scriptCompiler,
                BaseAssetsPath = SceneSerializer.InferAssetsRoot(path)
            };
            try
            {
                var envelope = JsonSerializer.Deserialize<AssetEnvelope>(File.ReadAllText(path), Options);
                if (envelope == null) return null;
                envelope.Normalize();

                var type = ResolveType(envelope.TypeName, scriptCompiler);
                if (type == null) return null;

                var instance = (ScriptableObject?)envelope.Data.Deserialize(type, Options);
                if (instance == null) return null;

                instance.AssetPath = path;
                if (string.IsNullOrWhiteSpace(instance.Name))
                    instance.Name = Path.GetFileNameWithoutExtension(path);
                return instance;
            }
            catch
            {
                return null;
            }
            finally
            {
                _context = null;
            }
        }

        public static string? GetTypeName(string path)
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<AssetEnvelope>(File.ReadAllText(path), Options);
                envelope?.Normalize();
                return envelope?.TypeName;
            }
            catch
            {
                return null;
            }
        }

        private static Type? ResolveType(string typeName, ScriptCompiler scriptCompiler)
        {
            var fromScripts = scriptCompiler.FindScriptableObjectType(typeName);
            if (fromScripts != null) return fromScripts;

            var builtin = typeof(ScriptableObject).Assembly.GetType("GrokoEngine." + typeName)
                ?? typeof(ScriptableObject).Assembly.GetType(typeName);
            return builtin != null && !builtin.IsAbstract && typeof(ScriptableObject).IsAssignableFrom(builtin)
                ? builtin
                : null;
        }

        private static string GetSerializableTypeName(Type type) =>
            type.Assembly == typeof(ScriptableObject).Assembly ? type.Name : (type.FullName ?? type.Name);

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
