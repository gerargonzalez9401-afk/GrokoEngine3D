using System;
using System.IO;
using System.Text.Json;
using GrokoEngine;
using GrokoShaderGraphPro.Models;
using GrokoShaderGraphPro.Services;
using MiMotor.Mathematics;

namespace GrokoEngine.Tests;

internal static class Program
{
    private static int passed;

    [STAThread]
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("BoxCollider uses center and scale in bounds", BoxColliderUsesCenterAndScale),
            ("Debug dispatches logs with severity", DebugDispatchesLogsWithSeverity),
            ("Additional colliders compute bounds and register", AdditionalCollidersComputeBoundsAndRegister),
            ("SceneSerializer writes version and relative asset paths", SceneSerializerWritesVersionAndRelativePaths),
            ("AssetDatabase creates meta and resolves moved asset", AssetDatabaseCreatesMetaAndResolvesMovedAsset),
            ("AssetDatabase move preserves guid and meta", AssetDatabaseMovePreservesGuidAndMeta),
            ("AssetDatabase validate repairs missing and orphan metas", AssetDatabaseValidateRepairsMissingAndOrphanMetas),
            ("AssetDatabase folder meta survives move and delete", AssetDatabaseFolderMetaSurvivesMoveAndDelete),
            ("SceneSerializer resolves moved assets by guid", SceneSerializerResolvesMovedAssetsByGuid),
            ("SceneSerializer loads legacy prefab payload", SceneSerializerLoadsLegacyPrefabPayload),
            ("SceneSerializer normalizes null collections", SceneSerializerNormalizesNullCollections),
            ("MaterialAsset stores texture paths relative to Assets", MaterialAssetStoresTexturePathsRelativeToAssets),
            ("MaterialAsset loads legacy null shader graph dictionaries", MaterialAssetLoadsLegacyNullShaderGraphDictionaries),
            ("MaterialAsset reuses imported materials", MaterialAssetReusesImportedMaterials),
            ("MaterialAsset applies shared material", MaterialAssetAppliesSharedMaterial),
            ("SceneSerializer preserves material instance flag", SceneSerializerPreservesMaterialInstanceFlag),
            ("SceneCommandHistory executes undo and redo", SceneCommandHistoryExecutesUndoRedo),
            ("SelectionService selects toggles and restores by id", SelectionServiceSelectsTogglesAndRestoresById),
            ("AssetService imports unique files and applies material", AssetServiceImportsUniqueFilesAndAppliesMaterial),
            ("ScriptCompiler skips unchanged recompiles", ScriptCompilerSkipsUnchangedRecompiles),
            ("ScriptCompiler creates player controller pro script", ScriptCompilerCreatesPlayerControllerProScript),
            ("ScriptCompiler repairs legacy player controller pro script", ScriptCompilerRepairsLegacyPlayerControllerProScript),
            ("GameObject parent setter adds child only once", GameObjectParentSetterAddsChildOnlyOnce),
            ("GameObject local matrix uses Unity Euler order", GameObjectLocalMatrixUsesUnityEulerOrder),
            ("Animator runtime info reports standalone state", AnimatorRuntimeInfoReportsStandaloneState),
            ("Animator reports blend tree weights", AnimatorReportsBlendTreeWeights),
            ("BlendTree 2D weights favor matching direction", BlendTree2DWeightsFavorMatchingDirection),
            ("BlendTree 1D weights interpolate and clamp", BlendTree1DWeightsInterpolateAndClamp),
            ("EditorSceneGraph attach moves existing object", EditorSceneGraphAttachMovesExistingObject),
            ("EditorSceneGraph rejects cyclic attach without detaching", EditorSceneGraphRejectsCyclicAttachWithoutDetaching),
            ("SceneSerializer loads child only once", SceneSerializerLoadsChildOnlyOnce),
            ("PhysicsEngine syncs components added without engine", PhysicsEngineSyncsComponentsAddedWithoutEngine),
            ("PhysicsEngine does not run MonoBehaviour update", PhysicsEngineDoesNotRunMonoBehaviourUpdate),
            ("BoxCollider uses parent transform in bounds", BoxColliderUsesParentTransformInBounds),
            ("PhysicsEngine lands fast bodies without tunneling", PhysicsEngineLandsFastBodiesWithoutTunneling),
            ("PhysicsEngine handles giant flat floor collider", PhysicsEngineHandlesGiantFlatFloorCollider),
            ("PhysicsEngine pushes dynamic rigidbodies by mass", PhysicsEnginePushesDynamicRigidbodiesByMass),
            ("PhysicsEngine raycast returns closest collider", PhysicsEngineRaycastReturnsClosestCollider),
            ("PhysicsEngine raycast uses broadphase candidates", PhysicsEngineRaycastUsesBroadphaseCandidates),
            ("PhysicsEngine raycast filters by layer mask", PhysicsEngineRaycastFiltersByLayerMask),
            ("PhysicsEngine ensure simulation keeps raycast usable", PhysicsEngineEnsureSimulationKeepsRaycastUsable),
            ("PhysicsEngine rebuilds static collider pose", PhysicsEngineRebuildsStaticColliderPose),
            ("PhysicsEngine rebuilds static quaternion rotation", PhysicsEngineRebuildsStaticQuaternionRotation),
            ("PhysicsEngine overlap box can filter triggers", PhysicsEngineOverlapBoxCanFilterTriggers),
            ("RuntimeScene instantiates prefabs from code", RuntimeSceneInstantiatesPrefabsFromCode),
            ("RuntimeScene finds objects by name", RuntimeSceneFindsObjectsByName),
            ("GameObject SetParent reparents through runtime scene", GameObjectSetParentReparentsThroughRuntimeScene),
            ("RuntimeScene unparents direct parent setter to root", RuntimeSceneUnparentsDirectParentSetterToRoot),
            ("RuntimeScene SetParent can keep local transform", RuntimeSceneSetParentCanKeepLocalTransform),
            ("RuntimeScene destroys objects with delay", RuntimeSceneDestroysObjectsWithDelay),
            ("Static Physics raycast uses runtime context", StaticPhysicsRaycastUsesRuntimeContext),
            ("PhysicsEngine dispatches collision events", PhysicsEngineDispatchesCollisionEvents),
            ("PhysicsEngine dispatches BEPU contact events", PhysicsEngineDispatchesBepuContactEvents),
            ("PhysicsEngine dispatches trigger events", PhysicsEngineDispatchesTriggerEvents),
            ("CharacterController grounds and blocks movement", CharacterControllerGroundsAndBlocksMovement),
            ("CharacterController steps over low obstacles", CharacterControllerStepsOverLowObstacles),
            ("SceneSerializer preserves character controller", SceneSerializerPreservesCharacterController),
            ("SceneSerializer preserves post process enabled flag", SceneSerializerPreservesPostProcessEnabledFlag),
            ("SceneSerializer does not duplicate character capsule", SceneSerializerDoesNotDuplicateCharacterCapsule),
            ("CharacterController ignores same object colliders", CharacterControllerIgnoresSameObjectColliders),
            ("CharacterController auto centers capsule", CharacterControllerAutoCentersCapsule),
            ("CharacterController records last move debug data", CharacterControllerRecordsLastMoveDebugData),
            ("SceneSerializer preserves additional colliders", SceneSerializerPreservesAdditionalColliders),
            ("ParticleSystem rate over distance starts at current position", ParticleSystemRateOverDistanceStartsAtCurrentPosition),
            ("LightmapBaker handles empty scenes", LightmapBakerHandlesEmptyScenes),
            ("LightmapBaker applies object scale", LightmapBakerAppliesObjectScale),
            ("HdrLoader decodes flat RGBE", HdrLoaderDecodesFlatRgbe),
            ("HdrLoader decodes new RLE", HdrLoaderDecodesNewRle),
            ("ShaderGraph serializer normalizes null collections", ShaderGraphSerializerNormalizesNullCollections),
            ("ShaderGraph schema repairs legacy vector pins", ShaderGraphSchemaRepairsLegacyVectorPins),
            ("ShaderGraph nodes use Unity style defaults", ShaderGraphNodesUseUnityStyleDefaults),
            ("ShaderGraph dynamic math pins follow vector connections", ShaderGraphDynamicMathPinsFollowVectorConnections),
            ("ShaderGraph remap uses vector2 range pairs", ShaderGraphRemapUsesVector2RangePairs),
            ("ShaderGraph generator tolerates missing node pins", ShaderGraphGeneratorToleratesMissingNodePins)
        };

        foreach (var test in tests)
        {
            try
            {
                test.Run();
                passed++;
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL {test.Name}");
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        Console.WriteLine($"{passed}/{tests.Length} tests passed.");
        return 0;
    }

    private static void BoxColliderUsesCenterAndScale()
    {
        var obj = new GameObject();
        obj.PosX = 10f;
        obj.PosY = 5f;
        obj.PosZ = -2f;
        obj.ScaleX = 2f;
        obj.ScaleY = 3f;
        obj.ScaleZ = 4f;

        var collider = obj.AddComponent<BoxCollider>();
        collider.Center = new Vector3(1f, -1f, 0.5f);
        collider.Size = new Vector3(2f, 2f, 1f);

        var bounds = collider.GetBounds();

        AssertNear(10f, bounds.Min.X, "Min.X");
        AssertNear(-1f, bounds.Min.Y, "Min.Y");
        AssertNear(-2f, bounds.Min.Z, "Min.Z");
        AssertNear(14f, bounds.Max.X, "Max.X");
        AssertNear(5f, bounds.Max.Y, "Max.Y");
        AssertNear(2f, bounds.Max.Z, "Max.Z");
    }

    private static void DebugDispatchesLogsWithSeverity()
    {
        var messages = new List<(string Message, string Severity)>();
        void Handler(string message, string severity) => messages.Add((message, severity));

        GrokoEngine.Debug.OnLogMessage += Handler;
        try
        {
            GrokoEngine.Debug.Log("hola consola");
            GrokoEngine.Debug.LogWarning("ojo consola");
            GrokoEngine.Debug.LogError("error consola");
            GrokoEngine.Debug.Log(123);
            GrokoEngine.Debug.Log(null);
        }
        finally
        {
            GrokoEngine.Debug.OnLogMessage -= Handler;
        }

        AssertEqual(5, messages.Count, "debug message count");
        AssertEqual("hola consola", messages[0].Message, "debug log message");
        AssertEqual("Info", messages[0].Severity, "debug log severity");
        AssertEqual("Warning", messages[1].Severity, "debug warning severity");
        AssertEqual("Error", messages[2].Severity, "debug error severity");
        AssertEqual("123", messages[3].Message, "debug object message");
        AssertEqual("null", messages[4].Message, "debug null message");
    }

    private static void AdditionalCollidersComputeBoundsAndRegister()
    {
        var physics = new PhysicsEngine();

        var sphereObj = new GameObject { Name = "Sphere" };
        sphereObj.PosX = 2f;
        sphereObj.ScaleX = 2f;
        var sphere = sphereObj.AddComponentWithEngine<SphereCollider>(physics);
        sphere.Radius = 0.5f;
        sphere.Center = new Vector3(1f, 0f, 0f);

        var sphereBounds = sphere.GetBounds();
        AssertNear(3f, sphereBounds.Min.X, "sphere min x");
        AssertNear(5f, sphereBounds.Max.X, "sphere max x");

        var capsuleObj = new GameObject { Name = "Capsule" };
        capsuleObj.ScaleY = 2f;
        var capsule = capsuleObj.AddComponentWithEngine<CapsuleCollider>(physics);
        capsule.Radius = 0.25f;
        capsule.Height = 2f;

        var capsuleBounds = capsule.GetBounds();
        AssertNear(-2f, capsuleBounds.Min.Y, "capsule min y");
        AssertNear(2f, capsuleBounds.Max.Y, "capsule max y");

        var meshObj = new GameObject { Name = "Mesh" };
        meshObj.PosZ = 4f;
        var mesh = meshObj.AddComponentWithEngine<MeshCollider>(physics);
        mesh.UseMeshBounds = false;
        mesh.Size = new Vector3(2f, 4f, 6f);

        var meshBounds = mesh.GetBounds();
        AssertNear(1f, meshBounds.Min.Z, "mesh fallback min z");
        AssertNear(7f, meshBounds.Max.Z, "mesh fallback max z");

        AssertTrue(physics.GetColliders().Contains(sphere), "sphere registered");
        AssertTrue(physics.GetColliders().Contains(capsule), "capsule registered");
        AssertTrue(physics.GetColliders().Contains(mesh), "mesh registered");

        physics.SyncPhysicsComponents(new[] { sphereObj, capsuleObj, meshObj });
        var overlaps = physics.OverlapBox(new Vector3(4f, 0f, 4f), new Vector3(10f, 10f, 10f));
        AssertTrue(overlaps.Contains(sphere), "overlap sees sphere");
        AssertTrue(overlaps.Contains(capsule), "overlap sees capsule");
        AssertTrue(overlaps.Contains(mesh), "overlap sees mesh");
    }

    private static void SceneSerializerWritesVersionAndRelativePaths()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string scenePath = Path.Combine(assets, "Scenes", "Main.gscene");
        string meshPath = Path.Combine(assets, "Models", "ship.obj");
        string texturePath = Path.Combine(assets, "Textures", "ship.png");
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(meshPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(texturePath)!);
        File.WriteAllText(meshPath, "# mesh");
        File.WriteAllText(texturePath, "texture");

        var obj = new GameObject { Name = "Ship", Type = 0 };
        obj.AddComponent<MeshFilter>().MeshPath = meshPath;
        obj.AddComponent<Material>().TexturePath = texturePath;

        SceneSerializer.Save(scenePath, new[] { obj });
        string json = File.ReadAllText(scenePath);

        AssertContains(json, "\"Version\": 2", "scene version");
        AssertContains(json, "Models/ship.obj", "relative mesh path");
        AssertContains(json, "Textures/ship.png", "relative texture path");
        AssertFalse(json.Contains(root, StringComparison.OrdinalIgnoreCase), "scene should not contain absolute project root");

        var loaded = SceneSerializer.Load(scenePath, new PhysicsEngine(), new ScriptCompiler(assets));
        var loadedMesh = loaded[0].GetComponent<MeshFilter>()!;
        AssertEqual(Path.GetFullPath(meshPath), Path.GetFullPath(loadedMesh.MeshPath), "resolved mesh path");
    }

    private static void AssetDatabaseCreatesMetaAndResolvesMovedAsset()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string texturePath = Path.Combine(assets, "Textures", "skin.png");
        Directory.CreateDirectory(Path.GetDirectoryName(texturePath)!);
        File.WriteAllText(texturePath, "texture");

        var database = new AssetDatabase(assets);
        string guid = database.GetOrCreateGuid(texturePath);
        string metaPath = AssetDatabase.GetMetaPath(texturePath);

        AssertTrue(File.Exists(metaPath), "meta file created");
        AssertTrue(guid.Length == 32, "guid format");

        string movedPath = Path.Combine(assets, "Characters", "Hero", "skin.png");
        Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);
        File.Move(texturePath, movedPath);
        File.Move(metaPath, AssetDatabase.GetMetaPath(movedPath));

        AssertTrue(database.TryResolveGuid(guid, out string resolved), "moved guid resolved");
        AssertEqual(Path.GetFullPath(movedPath), Path.GetFullPath(resolved), "moved asset path");
    }

    private static void AssetDatabaseMovePreservesGuidAndMeta()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string source = Path.Combine(assets, "Textures", "crate.png");
        string destination = Path.Combine(assets, "Moved", "crate.png");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "texture");

        var database = new AssetDatabase(assets);
        string guid = database.GetOrCreateGuid(source);

        AssertTrue(database.MoveAsset(source, destination, out string error), "move asset with meta: " + error);
        AssertFalse(File.Exists(source), "source moved");
        AssertTrue(File.Exists(destination), "destination exists");
        AssertFalse(File.Exists(AssetDatabase.GetMetaPath(source)), "old meta moved away");
        AssertTrue(File.Exists(AssetDatabase.GetMetaPath(destination)), "new meta exists");
        AssertTrue(database.TryResolveGuid(guid, out string resolved), "guid resolved after AssetDatabase.MoveAsset");
        AssertEqual(Path.GetFullPath(destination), Path.GetFullPath(resolved), "guid keeps moved path");
    }

    private static void AssetDatabaseValidateRepairsMissingAndOrphanMetas()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string asset = Path.Combine(assets, "Materials", "metal.mat");
        string orphanMeta = Path.Combine(assets, "ghost.png.meta");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        File.WriteAllText(asset, "{}");
        File.WriteAllText(orphanMeta, "{}");

        var database = new AssetDatabase(assets);
        var report = database.ValidateAndRepair(removeOrphanMetaFiles: true);

        AssertTrue(File.Exists(AssetDatabase.GetMetaPath(asset)), "missing meta repaired");
        AssertFalse(File.Exists(orphanMeta), "orphan meta removed");
        AssertTrue(report.AssetCount >= 1, "asset counted");
        AssertTrue(report.CreatedOrRepairedMetaFiles >= 1, "meta creation counted");
        AssertEqual(1, report.RemovedOrphanMetaFiles, "orphan count");
    }


    private static void AssetDatabaseFolderMetaSurvivesMoveAndDelete()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string source = Path.Combine(assets, "Environment");
        string child = Path.Combine(source, "Rock.prefab");
        string destination = Path.Combine(assets, "World", "Environment");
        Directory.CreateDirectory(source);
        File.WriteAllText(child, "prefab");

        var database = new AssetDatabase(assets);
        string folderGuid = database.GetOrCreateGuid(source);
        AssertTrue(File.Exists(AssetDatabase.GetMetaPath(source)), "folder meta created");

        AssertTrue(database.MoveAsset(source, destination, out string moveError), "folder move: " + moveError);
        AssertFalse(Directory.Exists(source), "old folder moved");
        AssertTrue(Directory.Exists(destination), "new folder exists");
        AssertFalse(File.Exists(AssetDatabase.GetMetaPath(source)), "old folder meta moved away");
        AssertTrue(File.Exists(AssetDatabase.GetMetaPath(destination)), "new folder meta exists");
        AssertTrue(database.TryResolveGuid(folderGuid, out string resolved), "folder guid resolved after move");
        AssertEqual(Path.GetFullPath(destination), Path.GetFullPath(resolved), "folder guid keeps moved path");

        AssertTrue(database.DeleteAsset(destination, out string deleteError), "folder delete: " + deleteError);
        AssertFalse(Directory.Exists(destination), "folder deleted");
        AssertFalse(File.Exists(AssetDatabase.GetMetaPath(destination)), "folder meta deleted");
    }

    private static void SceneSerializerResolvesMovedAssetsByGuid()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string scenePath = Path.Combine(assets, "Scenes", "Main.gscene");
        string texturePath = Path.Combine(assets, "Textures", "ship.png");
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(texturePath)!);
        File.WriteAllText(texturePath, "texture");

        var obj = new GameObject { Name = "Ship" };
        obj.AddComponent<Material>().TexturePath = texturePath;

        SceneSerializer.Save(scenePath, new[] { obj });
        string json = File.ReadAllText(scenePath);
        AssertContains(json, "guid:", "scene stores guid reference");
        AssertContains(json, "Textures/ship.png", "scene stores fallback path");

        string movedPath = Path.Combine(assets, "Moved", "ship.png");
        Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);
        File.Move(texturePath, movedPath);
        File.Move(AssetDatabase.GetMetaPath(texturePath), AssetDatabase.GetMetaPath(movedPath));

        var loaded = SceneSerializer.Load(scenePath, new PhysicsEngine(), new ScriptCompiler(assets));
        var mat = loaded[0].GetComponent<Material>()!;
        AssertEqual(Path.GetFullPath(movedPath), Path.GetFullPath(mat.TexturePath), "texture resolved by guid after move");
    }

    private static void SceneSerializerLoadsLegacyPrefabPayload()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string prefabPath = Path.Combine(assets, "Prefabs", "Legacy.prefab");
        Directory.CreateDirectory(Path.GetDirectoryName(prefabPath)!);

        var legacyPayload = new
        {
            Id = "legacy-id",
            Name = "Legacy",
            Type = 1,
            IsCamera = false,
            Position = new { X = 1f, Y = 2f, Z = 3f },
            Rotation = new { X = 0f, Y = 0f, Z = 0f },
            Scale = new { X = 1f, Y = 1f, Z = 1f },
            Components = Array.Empty<object>(),
            Children = Array.Empty<object>()
        };

        File.WriteAllText(prefabPath, JsonSerializer.Serialize(legacyPayload));

        var loaded = SceneSerializer.LoadPrefab(prefabPath, new PhysicsEngine(), new ScriptCompiler(assets));

        AssertEqual("legacy-id", loaded.EditorId, "legacy id");
        AssertEqual("Legacy", loaded.Name, "legacy name");
        AssertNear(1f, loaded.PosX, "legacy position");
    }

    private static void SceneSerializerNormalizesNullCollections()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        var physics = new PhysicsEngine();
        var scripts = new ScriptCompiler(assets);

        var empty = SceneSerializer.Deserialize("""{ "Objects": null }""", physics, scripts);
        AssertEqual(0, empty.Count, "null objects normalized");

        var loaded = SceneSerializer.Deserialize("""
            {
              "Objects": [
                {
                  "Id": null,
                  "Name": null,
                  "Position": null,
                  "Rotation": null,
                  "Scale": null,
                  "Components": null,
                  "Children": null
                }
              ]
            }
            """, physics, scripts);

        AssertEqual(1, loaded.Count, "object loaded");
        AssertEqual("", loaded[0].Name, "null name fallback");
        AssertNear(1f, loaded[0].ScaleX, "null scale fallback");
        AssertEqual(0, loaded[0].Components.Count, "null components normalized");
        AssertEqual(0, loaded[0].Children.Count, "null children normalized");
    }

    private static void MaterialAssetStoresTexturePathsRelativeToAssets()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string materialPath = Path.Combine(assets, "Materials", "Paint.mat");
        string texturePath = Path.Combine(assets, "Textures", "paint.png");
        Directory.CreateDirectory(Path.GetDirectoryName(materialPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(texturePath)!);
        File.WriteAllText(texturePath, "texture");

        MaterialAsset.Save(materialPath, new MaterialAssetData
        {
            Name = "Paint",
            AlbedoPath = texturePath,
            TexturePath = texturePath
        });

        string json = File.ReadAllText(materialPath);
        AssertContains(json, "Textures/paint.png", "relative material texture");
        AssertFalse(json.Contains(root, StringComparison.OrdinalIgnoreCase), "material should not contain absolute project root");

        var loaded = MaterialAsset.Load(materialPath);
        AssertEqual(Path.GetFullPath(texturePath), Path.GetFullPath(loaded.AlbedoPath), "resolved material texture");
    }

    private static void MaterialAssetReusesImportedMaterials()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string materialsDir = Path.Combine(assets, "Characters", "Hero");
        string texturePath = Path.Combine(assets, "Characters", "Hero", "skin.png");
        Directory.CreateDirectory(materialsDir);
        File.WriteAllText(texturePath, "texture");

        string first = MaterialAsset.CreateFromImported(materialsDir, "Hero_Skin", 0.6f, 0.4f, 0.3f, texturePath);
        MaterialAsset.Save(first, new MaterialAssetData { Name = "Hero_Skin_Custom", R = 0.1f, G = 0.2f, B = 0.3f });
        string second = MaterialAsset.CreateFromImported(materialsDir, "Hero_Skin", 0.9f, 0.9f, 0.9f, texturePath);

        AssertEqual(Path.GetFullPath(first), Path.GetFullPath(second), "imported material path reused");
        AssertEqual(1, Directory.GetFiles(materialsDir, "Hero_Skin*.mat").Length, "no duplicate imported materials");
        AssertEqual("Hero_Skin_Custom", MaterialAsset.Load(first).Name, "existing imported material preserved");
    }

    private static void MaterialAssetAppliesSharedMaterial()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string materialPath = Path.Combine(assets, "Materials", "Shared.mat");
        Directory.CreateDirectory(Path.GetDirectoryName(materialPath)!);
        MaterialAsset.Save(materialPath, new MaterialAssetData { R = 0.2f, G = 0.4f, B = 0.6f, Metallic = 0.7f });

        var obj = new GameObject { Name = "Cube" };
        MaterialAsset.ApplyTo(obj, materialPath);

        var mat = obj.GetComponent<Material>()!;
        AssertFalse(mat.IsInstance, "applied material should be shared");
        AssertEqual(Path.GetFullPath(materialPath), Path.GetFullPath(mat.AssetPath), "shared material path");
        AssertNear(0.7f, mat.Metallic, "shared material metallic");
    }

    private static void MaterialAssetLoadsLegacyNullShaderGraphDictionaries()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string materialPath = Path.Combine(assets, "Materials", "Legacy.mat");
        Directory.CreateDirectory(Path.GetDirectoryName(materialPath)!);
        File.WriteAllText(materialPath, """
            {
              "Name": "Legacy",
              "R": 0.25,
              "G": 0.5,
              "B": 0.75,
              "ShaderGraphProperties": null,
              "ShaderGraphTextures": null
            }
            """);

        var data = MaterialAsset.Load(materialPath);
        AssertEqual("Legacy", data.Name, "legacy material name");
        AssertNear(0.25f, data.R, "legacy material r");
        AssertEqual(0, data.ShaderGraphProperties.Count, "shader graph properties normalized");
        AssertEqual(0, data.ShaderGraphTextures.Count, "shader graph textures normalized");
    }

    private static void SceneSerializerPreservesMaterialInstanceFlag()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string scenePath = Path.Combine(assets, "Scenes", "Main.gscene");
        string materialPath = Path.Combine(assets, "Materials", "Source.mat");
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(materialPath)!);
        File.WriteAllText(materialPath, "{}");

        var obj = new GameObject { Name = "InstanceCube" };
        var mat = obj.AddComponent<Material>();
        mat.AssetPath = materialPath;
        mat.IsInstance = true;
        mat.R = 0.9f;

        SceneSerializer.Save(scenePath, new[] { obj });
        var loaded = SceneSerializer.Load(scenePath, new PhysicsEngine(), new ScriptCompiler(assets));
        var loadedMat = loaded[0].GetComponent<Material>()!;

        AssertTrue(loadedMat.IsInstance, "loaded material instance");
        AssertEqual(Path.GetFullPath(materialPath), Path.GetFullPath(loadedMat.AssetPath), "instance source asset path");
        AssertNear(0.9f, loadedMat.R, "instance color");
    }

    private static void SceneCommandHistoryExecutesUndoRedo()
    {
        int value = 0;
        var history = new SceneCommandHistory(maxStates: 4);
        history.Push(new TestCommand(() => value = 10, () => value = 0));
        AssertEqual(10, value, "command execute");
        AssertTrue(history.Undo(), "undo available");
        AssertEqual(0, value, "command undo");
        AssertTrue(history.Redo(), "redo available");
        AssertEqual(10, value, "command redo");
    }

    private static void SelectionServiceSelectsTogglesAndRestoresById()
    {
        var roots = new List<GameObject>();
        var physics = new PhysicsEngine();
        var graph = new EditorSceneGraph(roots, physics);
        var selection = new SelectionService(graph);
        var a = new GameObject { Name = "A" };
        var b = new GameObject { Name = "B" };
        roots.Add(a);
        roots.Add(b);

        selection.SelectSingle(a);
        AssertEqual(a, selection.Current, "single current");
        AssertEqual(1, selection.Selected.Count, "single selected count");

        selection.SelectFromViewport(b, additive: true);
        AssertEqual(b, selection.Current, "toggle add current");
        AssertEqual(2, selection.Selected.Count, "toggle add count");

        selection.SelectFromViewport(a, additive: true);
        AssertEqual(b, selection.Current, "toggle remove current");
        AssertEqual(1, selection.Selected.Count, "toggle remove count");

        var ids = selection.CaptureSelectedIds();
        roots.Remove(b);
        var bReloaded = new GameObject { EditorId = b.EditorId, Name = "B Reloaded" };
        roots.Add(bReloaded);
        selection.RestoreSelectedIds(ids);

        AssertEqual(bReloaded, selection.Current, "restored current by id");
        AssertEqual(bReloaded, selection.Selected[0], "restored selected by id");
    }

    private static void AssetServiceImportsUniqueFilesAndAppliesMaterial()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string sourceDir = Path.Combine(Path.GetTempPath(), "GrokoEngineTests", Guid.NewGuid().ToString("N"));
        string targetDir = Path.Combine(assets, "Imported");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(targetDir);

        string sourceTexture = Path.Combine(sourceDir, "paint.png");
        string existingTexture = Path.Combine(targetDir, "paint.png");
        File.WriteAllText(sourceTexture, "external texture");
        File.WriteAllText(existingTexture, "existing texture");

        var service = new AssetService(assets);
        var result = service.ImportExternalFiles(new[] { sourceTexture }, targetDir);

        AssertEqual(1, result.ImportedCount, "imported count");
        string importedPath = Path.Combine(targetDir, "paint_1.png");
        AssertTrue(File.Exists(importedPath), "unique imported texture");

        string materialPath = MaterialAsset.Create(targetDir, "Paint");
        MaterialAsset.Save(materialPath, new MaterialAssetData { R = 0.25f, G = 0.5f, B = 0.75f });
        var obj = new GameObject { Name = "Cube" };

        AssertTrue(service.ApplyMaterial(obj, materialPath), "apply material");
        var mat = obj.GetComponent<Material>()!;
        AssertEqual(Path.GetFullPath(materialPath), Path.GetFullPath(mat.AssetPath), "material asset path");
        AssertFalse(mat.IsInstance, "material asset should be shared");
        AssertNear(0.25f, mat.R, "material r");
    }

    private static void ScriptCompilerSkipsUnchangedRecompiles()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        Directory.CreateDirectory(assets);
        string scriptPath = Path.Combine(assets, "CacheProbe.cs");
        File.WriteAllText(scriptPath, "using GrokoEngine; public class CacheProbe : MonoBehaviour { public int Value = 1; }");

        using var compiler = new ScriptCompiler(assets);
        var first = compiler.Compile();
        AssertTrue(first.Success, "first compile success");
        var firstAssembly = compiler.UltimoEnsamblado;
        AssertTrue(firstAssembly != null, "first assembly loaded");

        var second = compiler.Compile();
        AssertTrue(second.Success, "second compile success");
        AssertEqual(firstAssembly, compiler.UltimoEnsamblado, "unchanged compile reuses assembly");

        System.Threading.Thread.Sleep(25);
        File.WriteAllText(scriptPath, "using GrokoEngine; public class CacheProbe : MonoBehaviour { public int Value = 2; }");
        var third = compiler.Compile();
        AssertTrue(third.Success, "changed compile success");
        AssertTrue(!ReferenceEquals(firstAssembly, compiler.UltimoEnsamblado), "changed script recompiles assembly");
    }

    private static void ScriptCompilerCreatesPlayerControllerProScript()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");

        using var compiler = new ScriptCompiler(assets);
        string scriptPath = compiler.CreatePlayerControllerScript(assets);
        string source = File.ReadAllText(scriptPath);

        AssertTrue(File.Exists(scriptPath), "player controller script exists");
        AssertContains(source, "class PlayerControllerPro : MonoBehaviour", "player controller class");
        AssertContains(source, "RuntimeScene.FindObjectByName(CameraName)", "camera lookup");
        AssertContains(source, "Input.GetAxisRaw(\"Horizontal\")", "horizontal axis");
        AssertContains(source, "Input.GetKeyDown(KeyCode.Space)", "jump input");
        AssertContains(source, "animator.SetFloat(\"Speed\"", "animator speed");
        AssertContains(source, "animator.SetFloat(\"VerticalSpeed\"", "animator vertical speed");

        var result = compiler.Compile();
        AssertTrue(result.Success, "generated player controller compiles");
    }

    private static void ScriptCompilerRepairsLegacyPlayerControllerProScript()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string scriptPath = Path.Combine(assets, "PlayerControllerPro.cs");
        File.WriteAllText(scriptPath,
            "using System;\n" +
            "using GrokoEngine;\n" +
            "using MiMotor.Mathematics;\n" +
            "namespace GrokoEngine\n" +
            "{\n" +
            "    public class PlayerControllerPro : MonoBehaviour\n" +
            "    {\n" +
            "        public override void Update(double dt)\n" +
            "        {\n" +
            "            Vector3 desiredDirection = Vector3.Zero;\n" +
            "            if (desiredDirection.Length > 0.0001f) { }\n" +
            "        }\n" +
            "        private static Vector3 NormalizeSafe(Vector3 value)\n" +
            "        {\n" +
            "            float length = value.Length;\n" +
            "            return length > 0.0001f ? value / length : Vector3.Zero;\n" +
            "        }\n" +
            "        private static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;\n" +
            "    }\n" +
            "}\n");

        using var compiler = new ScriptCompiler(assets);
        var result = compiler.Compile();
        string repaired = File.ReadAllText(scriptPath);

        AssertTrue(result.Success, "legacy player controller compiles after repair");
        AssertContains(repaired, "Magnitude(desiredDirection)", "desired direction repaired");
        AssertContains(repaired, "float length = Magnitude(value);", "normalize length repaired");
        AssertContains(repaired, "private static float Magnitude(Vector3 value)", "magnitude helper inserted");
    }

    private static void EditorSceneGraphAttachMovesExistingObject()
    {
        var roots = new List<GameObject>();
        var physics = new PhysicsEngine();
        var graph = new EditorSceneGraph(roots, physics);
        var parent = new GameObject { Name = "Parent" };
        var child = new GameObject { Name = "Child" };
        roots.Add(parent);
        roots.Add(child);

        graph.Attach(child, parent, 0);

        AssertFalse(roots.Contains(child), "moved child removed from roots");
        AssertEqual(parent, child.Parent, "moved child parent");
        AssertEqual(1, parent.Children.Count, "moved child count");
        AssertEqual(child, parent.Children[0], "moved child inserted");

        graph.Attach(child, null, 1);

        AssertFalse(parent.Children.Contains(child), "root child removed from parent");
        AssertEqual(null, child.Parent, "root child parent");
        AssertEqual(child, roots[1], "root child index");
    }

    private static void EditorSceneGraphRejectsCyclicAttachWithoutDetaching()
    {
        var roots = new List<GameObject>();
        var physics = new PhysicsEngine();
        var graph = new EditorSceneGraph(roots, physics);
        var parent = new GameObject { Name = "Parent" };
        var child = new GameObject { Name = "Child", Parent = parent };
        roots.Add(parent);

        bool threw = false;
        try { graph.Attach(parent, child, 0); }
        catch (InvalidOperationException) { threw = true; }

        AssertTrue(threw, "cyclic attach should throw");
        AssertEqual(null, parent.Parent, "parent remains root");
        AssertEqual(parent, roots[0], "parent remains in roots");
        AssertEqual(parent, child.Parent, "child parent unchanged");
        AssertEqual(child, parent.Children[0], "child remains attached");
    }

    private static void GameObjectParentSetterAddsChildOnlyOnce()
    {
        var parent = new GameObject { Name = "Parent" };
        var child = new GameObject { Name = "Child", Parent = parent };

        child.Parent = parent;

        AssertEqual(1, parent.Children.Count, "parent child count");
        AssertEqual(child, parent.Children[0], "parent child reference");
    }

    private static void GameObjectLocalMatrixUsesUnityEulerOrder()
    {
        var obj = new GameObject
        {
            Name = "Rotated",
            RotX = 25f,
            RotY = 40f,
            RotZ = -15f,
            ScaleX = 1.2f,
            ScaleY = 0.8f,
            ScaleZ = 1.5f,
            PosX = 2f,
            PosY = 3f,
            PosZ = 4f
        };

        float rx = obj.RotX * MathF.PI / 180f;
        float ry = obj.RotY * MathF.PI / 180f;
        float rz = obj.RotZ * MathF.PI / 180f;
        var expected =
            System.Numerics.Matrix4x4.CreateScale(obj.ScaleX, obj.ScaleY, obj.ScaleZ) *
            System.Numerics.Matrix4x4.CreateRotationZ(rz) *
            System.Numerics.Matrix4x4.CreateRotationX(rx) *
            System.Numerics.Matrix4x4.CreateRotationY(ry) *
            System.Numerics.Matrix4x4.CreateTranslation(obj.PosX, obj.PosY, obj.PosZ);

        var actual = obj.LocalMatrix;
        AssertNear(expected.M11, actual.M11, "local m11");
        AssertNear(expected.M12, actual.M12, "local m12");
        AssertNear(expected.M13, actual.M13, "local m13");
        AssertNear(expected.M21, actual.M21, "local m21");
        AssertNear(expected.M22, actual.M22, "local m22");
        AssertNear(expected.M23, actual.M23, "local m23");
        AssertNear(expected.M31, actual.M31, "local m31");
        AssertNear(expected.M32, actual.M32, "local m32");
        AssertNear(expected.M33, actual.M33, "local m33");
        AssertNear(expected.M41, actual.M41, "local m41");
        AssertNear(expected.M42, actual.M42, "local m42");
        AssertNear(expected.M43, actual.M43, "local m43");
    }

    private static void AnimatorRuntimeInfoReportsStandaloneState()
    {
        var animator = new Animator
        {
            ClipPath = Path.Combine("Assets", "Idle.anim"),
            IsPlaying = true,
            IsVisible = false,
            Speed = 1.5f,
            Time = 0.25f
        };

        var info = animator.GetRuntimeInfo();

        AssertEqual("(Standalone)", info.StateName, "standalone state");
        AssertEqual("Clip", info.MotionType, "standalone motion type");
        AssertEqual("Idle", info.ClipName, "clip display name");
        AssertEqual(animator.ClipPath, info.ClipPath, "clip path");
        AssertTrue(info.IsPlaying, "playing flag");
        AssertFalse(info.IsVisible, "visible flag");
        AssertNear(0.25f, info.Time, "runtime time");
        AssertNear(1.5f, info.EffectiveSpeed, "effective speed");
        AssertEqual(0, info.BlendChildCount, "blend child count");
    }

    private static void AnimatorReportsBlendTreeWeights()
    {
        string dir = Path.Combine(Path.GetTempPath(), "GrokoEngineTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string controllerPath = Path.Combine(dir, "Locomotion.controller");
        var controller = new AnimatorControllerData
        {
            Name = "Locomotion",
            DefaultState = "Move",
            States =
            {
                new AnimatorStateData
                {
                    Name = "Move",
                    MotionType = AnimatorMotionType.BlendTree,
                    BlendTree = new BlendTreeData
                    {
                        ParameterX = "VelX",
                        ParameterY = "VelY",
                        Children =
                        {
                            new BlendTreeChildMotion { MotionPath = "idle.anim", PosX = 0f, PosY = 0f },
                            new BlendTreeChildMotion { MotionPath = "forward.anim", PosX = 0f, PosY = 1f },
                            new BlendTreeChildMotion { MotionPath = "right.anim", PosX = 1f, PosY = 0f }
                        }
                    }
                }
            }
        };
        AnimatorControllerAsset.Save(controllerPath, controller);

        var animator = new Animator { ControllerPath = controllerPath };
        animator.SetFloat("VelX", 0f);
        animator.SetFloat("VelY", 1f);

        var weights = animator.GetBlendWeights();

        AssertEqual(3, weights.Count, "blend weight count");
        AssertTrue(weights.Any(w => w.DisplayName == "forward" && w.Weight > 0.99f), "forward public weight dominates");
        AssertTrue(weights.Any(w => w.DisplayName == "idle" && w.Weight < 0.001f), "idle public weight fades out");
        AssertEqual(3, animator.GetRuntimeInfo().BlendChildCount, "runtime blend child count");
    }

    private static void BlendTree2DWeightsFavorMatchingDirection()
    {
        var animator = new Animator();
        var tree = new BlendTreeData
        {
            BlendType = BlendTreeType.FreeformDirectional2D,
            ParameterX = "VelX",
            ParameterY = "VelY"
        };
        var children = new List<BlendTreeChildMotion>
        {
            new() { MotionPath = "idle.anim", PosX = 0f, PosY = 0f },
            new() { MotionPath = "forward.anim", PosX = 0f, PosY = 1f },
            new() { MotionPath = "right.anim", PosX = 1f, PosY = 0f },
            new() { MotionPath = "back.anim", PosX = 0f, PosY = -1f }
        };
        var method = typeof(Animator).GetMethod(
            "ComputeBlendWeights",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        AssertTrue(method != null, "blend weight method found");

        animator.SetFloat("VelX", 0f);
        animator.SetFloat("VelY", 0f);
        var idleWeights = (List<float>)method!.Invoke(animator, new object[] { tree, children })!;
        AssertNear(1f, idleWeights[0], "idle center weight");

        animator.SetFloat("VelX", 0.25f);
        animator.SetFloat("VelY", 0f);
        var softRightWeights = (List<float>)method.Invoke(animator, new object[] { tree, children })!;
        AssertNear(0.75f, softRightWeights[0], "small right keeps idle");
        AssertNear(0.25f, softRightWeights[2], "small right blends into right");

        animator.SetFloat("VelX", 0f);
        animator.SetFloat("VelY", 1f);
        var forwardWeights = (List<float>)method.Invoke(animator, new object[] { tree, children })!;
        AssertTrue(forwardWeights[1] > 0.99f, "forward dominates at forward input");
        AssertNear(0f, forwardWeights[2], "right does not bleed into forward");
        AssertNear(0f, forwardWeights[3], "back does not bleed into forward");

        animator.SetFloat("VelX", 0.7f);
        animator.SetFloat("VelY", 0.7f);
        var diagonalWeights = (List<float>)method.Invoke(animator, new object[] { tree, children })!;
        AssertTrue(diagonalWeights[1] > 0.45f, "diagonal keeps forward");
        AssertTrue(diagonalWeights[2] > 0.45f, "diagonal keeps right");
        AssertNear(0f, diagonalWeights[3], "diagonal excludes back");

        animator.SetFloat("VelX", 2f);
        animator.SetFloat("VelY", 0f);
        var clampedWeights = (List<float>)method.Invoke(animator, new object[] { tree, children })!;
        AssertTrue(clampedWeights[2] > 0.99f, "out of range right clamps to right");
    }

    private static void BlendTree1DWeightsInterpolateAndClamp()
    {
        var animator = new Animator();
        var tree = new BlendTreeData
        {
            BlendType = BlendTreeType.Simple1D,
            Parameter = "Speed"
        };
        var children = new List<BlendTreeChildMotion>
        {
            new() { MotionPath = "idle.anim", Threshold = 0f },
            new() { MotionPath = "walk.anim", Threshold = 0.5f },
            new() { MotionPath = "run.anim", Threshold = 1f }
        };
        var method = typeof(Animator).GetMethod(
            "ComputeBlendWeights",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        AssertTrue(method != null, "blend weight method found");

        animator.SetFloat("Speed", 0.25f);
        var walkInWeights = (List<float>)method!.Invoke(animator, new object[] { tree, children })!;
        AssertNear(0.5f, walkInWeights[0], "1d lower interpolation");
        AssertNear(0.5f, walkInWeights[1], "1d upper interpolation");
        AssertNear(0f, walkInWeights[2], "1d far clip excluded");

        animator.SetFloat("Speed", -1f);
        var lowWeights = (List<float>)method.Invoke(animator, new object[] { tree, children })!;
        AssertNear(1f, lowWeights[0], "1d low clamp");

        animator.SetFloat("Speed", 2f);
        var highWeights = (List<float>)method.Invoke(animator, new object[] { tree, children })!;
        AssertNear(1f, highWeights[2], "1d high clamp");
    }

    private static void SceneSerializerLoadsChildOnlyOnce()
    {
        var physics = new PhysicsEngine();
        var scripts = new ScriptCompiler(Path.Combine(Path.GetTempPath(), "GrokoEngineTests", Guid.NewGuid().ToString("N"), "Assets"));
        var parent = new GameObject { Name = "Parent" };
        var child = new GameObject { Name = "Child", Parent = parent };
        string json = SceneSerializer.Serialize(new[] { parent });

        var loaded = SceneSerializer.Deserialize(json, physics, scripts);

        AssertEqual(1, loaded.Count, "loaded root count");
        AssertEqual(1, loaded[0].Children.Count, "loaded child count");
        AssertEqual("Child", loaded[0].Children[0].Name, "loaded child name");
        AssertEqual(loaded[0], loaded[0].Children[0].Parent, "loaded child parent");
    }

    private static void PhysicsEngineSyncsComponentsAddedWithoutEngine()
    {
        var physics = new PhysicsEngine();
        var obj = new GameObject { Name = "Dynamic" };
        var rb = obj.AddComponent<Rigidbody>();
        var collider = obj.AddComponent<BoxCollider>();

        physics.Step(new List<GameObject> { obj }, 0.016);

        AssertEqual(physics, rb.Physics, "rigidbody physics");
        AssertTrue(physics.GetColliders().Contains(collider), "collider registered");
    }

    private static void PhysicsEngineDoesNotRunMonoBehaviourUpdate()
    {
        var physics = new PhysicsEngine();
        var obj = new GameObject { Name = "Scripted" };
        var script = obj.AddComponent<TestBehaviour>();

        physics.Step(new List<GameObject> { obj }, 0.016);

        AssertEqual(0, script.UpdateCount, "script update count");
    }

    private static void BoxColliderUsesParentTransformInBounds()
    {
        var parent = new GameObject { Name = "Parent" };
        parent.PosX = 10f;
        parent.ScaleX = 2f;

        var child = new GameObject { Name = "Child", Parent = parent };
        child.PosX = 1f;
        child.ScaleX = 3f;

        var collider = child.AddComponent<BoxCollider>();
        collider.Size = new Vector3(1f, 1f, 1f);

        var bounds = collider.GetBounds();

        AssertNear(8f, bounds.Min.X, "child world min x");
        AssertNear(14f, bounds.Max.X, "child world max x");
    }

    private static void PhysicsEngineLandsFastBodiesWithoutTunneling()
    {
        var physics = new PhysicsEngine();
        var floor = new GameObject { Name = "Floor" };
        var floorCollider = floor.AddComponent<BoxCollider>();
        floorCollider.Size = new Vector3(20f, 1f, 20f);

        var body = new GameObject { Name = "FastBody" };
        body.PosY = 10f;
        var bodyCollider = body.AddComponent<BoxCollider>();
        bodyCollider.Size = new Vector3(1f, 1f, 1f);
        var rb = body.AddComponent<Rigidbody>();
        rb.UseGravity = false;
        rb.Velocity = new Vector3(0f, -100f, 0f);

        physics.Step(new List<GameObject> { floor, body }, 0.2);

        AssertTrue(rb.IsGrounded, "fast body grounded");
        AssertNear(1.0005f, body.Position.Y, "fast body rests on floor", 0.002f);
        AssertNear(0f, rb.Velocity.Y, "fast body vertical velocity stopped", 0.001f);
    }

    private static void PhysicsEnginePushesDynamicRigidbodiesByMass()
    {
        var physics = new PhysicsEngine();
        var pusher = new GameObject { Name = "Pusher" };
        pusher.AddComponent<BoxCollider>();
        var pusherRb = pusher.AddComponent<Rigidbody>();
        pusherRb.UseGravity = false;
        pusherRb.Mass = 4f;
        pusherRb.Velocity = new Vector3(5f, 0f, 0f);

        var pushed = new GameObject { Name = "Pushed" };
        pushed.PosX = 1.2f;
        pushed.AddComponent<BoxCollider>();
        var pushedRb = pushed.AddComponent<Rigidbody>();
        pushedRb.UseGravity = false;
        pushedRb.Mass = 1f;

        physics.Step(new List<GameObject> { pusher, pushed }, 0.1);

        AssertTrue(pushed.Position.X > 1.2f, "pushed body moved");
        AssertTrue(pushedRb.Velocity.X > 0f, "pushed body gained velocity");
        AssertTrue(pusherRb.Velocity.X < 5f, "pusher velocity transferred");
    }

    private static void PhysicsEngineHandlesGiantFlatFloorCollider()
    {
        var physics = new PhysicsEngine();
        var floor = new GameObject { Name = "Giant Floor" };
        floor.ScaleX = 500f;
        floor.ScaleY = 0f;
        floor.ScaleZ = 500f;
        var floorCollider = floor.AddComponent<BoxCollider>();

        var body = new GameObject { Name = "Body" };
        body.Position = new Vector3(0f, 4f, 0f);
        body.AddComponent<BoxCollider>();
        var rb = body.AddComponent<Rigidbody>();
        rb.UseGravity = false;
        rb.Velocity = new Vector3(0f, -25f, 0f);

        physics.Step(new List<GameObject> { floor, body }, 0.2);

        AssertTrue(rb.IsGrounded, "body grounded on giant floor");
        AssertTrue(body.Position.Y >= 0.49f, "body stayed above giant floor");

        bool hit = physics.Raycast(new Vector3(10f, 10f, 0f), new Vector3(0f, -1f, 0f), 50f, out var rayHit);
        AssertTrue(hit, "raycast hits giant floor");
        AssertEqual(floor, rayHit.GameObject, "raycast floor target");

        var overlaps = physics.OverlapBox(new Vector3(0f, 0.5f, 0f), new Vector3(1f, 1f, 1f));
        AssertTrue(overlaps.Contains(floorCollider), "overlap finds giant floor");
    }

    private static void PhysicsEngineRaycastReturnsClosestCollider()
    {
        var physics = new PhysicsEngine();
        var far = new GameObject { Name = "Far" };
        far.PosX = 5f;
        far.AddComponent<BoxCollider>();

        var near = new GameObject { Name = "Near" };
        near.PosX = 2f;
        near.AddComponent<BoxCollider>();

        physics.SyncPhysicsComponents(new[] { far, near });

        bool hit = physics.Raycast(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), 10f, out var rayHit);

        AssertTrue(hit, "raycast hit");
        AssertEqual(near, rayHit.GameObject, "raycast closest object");
        AssertNear(1.5f, rayHit.Distance, "raycast distance");
        AssertNear(-1f, rayHit.Normal.X, "raycast normal");
    }

    private static void PhysicsEngineRaycastUsesBroadphaseCandidates()
    {
        var physics = new PhysicsEngine { SpatialCellSize = 4f };
        var roots = new List<GameObject>();

        for (int i = 0; i < 120; i++)
        {
            var obj = new GameObject { Name = "Far_" + i, PosX = i * 3f, PosY = 100f + i };
            obj.AddComponentWithEngine<BoxCollider>(physics);
            roots.Add(obj);
        }

        var target = new GameObject { Name = "Target", PosX = 5f };
        target.AddComponentWithEngine<BoxCollider>(physics);
        roots.Add(target);

        physics.SyncPhysicsComponents(roots);
        bool hit = physics.Raycast(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), 10f, out var rayHit);

        AssertTrue(hit, "broadphase raycast hit");
        AssertEqual(target, rayHit.GameObject, "broadphase target");
        AssertTrue(physics.LastRaycastCandidateCount < physics.GetColliders().Count / 4, "broadphase reduced candidates");
    }

    private static void PhysicsEngineRaycastFiltersByLayerMask()
    {
        var physics = new PhysicsEngine();
        var defaultObj = new GameObject { Name = "DefaultTarget" };
        defaultObj.PosX = 2f;
        defaultObj.Layer = LayerMask.Default;
        defaultObj.AddComponent<BoxCollider>();

        var enemy = new GameObject { Name = "EnemyTarget" };
        enemy.PosX = 5f;
        enemy.Layer = LayerMask.NameToLayer("Enemy");
        enemy.AddComponent<BoxCollider>();

        physics.SyncPhysicsComponents(new[] { defaultObj, enemy });

        int enemyMask = LayerMask.GetMask("Enemy");
        bool hitEnemy = physics.Raycast(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), 10f, out var enemyHit, enemyMask);

        AssertTrue(hitEnemy, "layer raycast hit enemy");
        AssertEqual(enemy, enemyHit.GameObject, "layer raycast ignores closer default object");

        int groundMask = LayerMask.GetMask("Ground");
        bool hitGround = physics.Raycast(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), 10f, out _, groundMask);

        AssertFalse(hitGround, "layer raycast misses absent layer");
    }

    private static void PhysicsEngineEnsureSimulationKeepsRaycastUsable()
    {
        var physics = new PhysicsEngine();
        var target = new GameObject { Name = "Target", PosX = 3f };
        target.AddComponent<BoxCollider>();
        var roots = new List<GameObject> { target };

        physics.EnsureSimulationBuilt(roots);

        bool hit = physics.Raycast(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), 10f, out var rayHit);

        AssertTrue(hit, "raycast works after ensure simulation");
        AssertEqual(target, rayHit.GameObject, "raycast target after ensure simulation");
    }

    private static void PhysicsEngineRebuildsStaticColliderPose()
    {
        var physics = new PhysicsEngine();
        var target = new GameObject { Name = "StaticTarget", PosX = 2f };
        target.AddComponent<BoxCollider>();
        var roots = new List<GameObject> { target };

        physics.EnsureSimulationBuilt(roots);
        AssertTrue(physics.Raycast(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), 10f, out var firstHit), "initial static hit");
        AssertNear(1.5f, firstHit.Distance, "initial static distance");

        target.PosX = 6f;
        physics.EnsureSimulationBuilt(roots);
        AssertTrue(physics.Raycast(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), 10f, out var movedHit), "moved static hit");
        AssertNear(5.5f, movedHit.Distance, "moved static distance");
    }

    private static void PhysicsEngineRebuildsStaticQuaternionRotation()
    {
        var physics = new PhysicsEngine();
        var target = new GameObject { Name = "StaticQuaternionTarget" };
        var box = target.AddComponent<BoxCollider>();
        box.Size = new Vector3(4f, 1f, 1f);
        var roots = new List<GameObject> { target };

        physics.EnsureSimulationBuilt(roots);
        AssertTrue(physics.Raycast(new Vector3(-5f, 0f, 0f), new Vector3(1f, 0f, 0f), 10f, out var firstHit), "initial quaternion static hit");
        AssertNear(3.0f, firstHit.Distance, "initial quaternion static distance", 0.05f);

        target.SetLocalTRS(target.transform.Position, Quaternion.Euler(0f, 90f, 0f), target.transform.Scale);
        physics.EnsureSimulationBuilt(roots);

        AssertTrue(physics.Raycast(new Vector3(-5f, 0f, 0f), new Vector3(1f, 0f, 0f), 10f, out var rotatedHit), "rotated quaternion static hit");
        AssertNear(4.5f, rotatedHit.Distance, "rotated quaternion static distance", 0.08f);
    }

    private static void PhysicsEngineOverlapBoxCanFilterTriggers()
    {
        var physics = new PhysicsEngine();
        var trigger = new GameObject { Name = "Trigger" };
        var triggerCollider = trigger.AddComponent<BoxCollider>();
        triggerCollider.IsTrigger = true;

        var solid = new GameObject { Name = "Solid" };
        solid.PosX = 2f;
        var solidCollider = solid.AddComponent<BoxCollider>();

        physics.SyncPhysicsComponents(new[] { trigger, solid });

        var all = physics.OverlapBox(new Vector3(0f, 0f, 0f), new Vector3(3f, 3f, 3f));
        var solidsOnly = physics.OverlapBox(new Vector3(0f, 0f, 0f), new Vector3(3f, 3f, 3f), includeTriggers: false);

        AssertTrue(all.Contains(triggerCollider), "overlap includes trigger");
        AssertTrue(all.Contains(solidCollider), "overlap includes solid");
        AssertFalse(solidsOnly.Contains(triggerCollider), "overlap filters trigger");
        AssertTrue(solidsOnly.Contains(solidCollider), "overlap keeps solid");
    }

    private static void RuntimeSceneInstantiatesPrefabsFromCode()
    {
        string root = CreateTempProject();
        var assets = Path.Combine(root, "Assets");
        var physics = new PhysicsEngine();
        var compiler = new ScriptCompiler(assets);
        var roots = new List<GameObject>();

        var prefab = new GameObject { Name = "Bullet" };
        string prefabId = prefab.EditorId;
        prefab.AddComponent<BoxCollider>();
        var child = new GameObject { Name = "Trail" };
        string childId = child.EditorId;
        child.Parent = prefab;

        RuntimeScene.SetContext(roots, physics, compiler);
        try
        {
            var instance = RuntimeScene.Instantiate(prefab, new Vector3(3f, 4f, 5f), Quaternion.identity);

            AssertEqual(1, roots.Count, "instantiated root count");
            AssertEqual(instance, roots[0], "instantiated root");
            AssertTrue(instance.EditorId != prefabId, "instance gets new id");
            AssertTrue(instance.Children[0].EditorId != childId, "child gets new id");
            AssertNear(3f, instance.PosX, "instance pos x");
            AssertNear(4f, instance.PosY, "instance pos y");
            AssertNear(5f, instance.PosZ, "instance pos z");
            AssertNear(90f, instance.RotY, "instance rot y");
            AssertTrue(instance.GetComponent<BoxCollider>() != null, "instance has collider");
            AssertTrue(physics.GetColliders().Contains(instance.GetComponent<BoxCollider>()!), "collider registered");
        }
        finally
        {
            RuntimeScene.ClearContext();
        }
    }

    private static void RuntimeSceneFindsObjectsByName()
    {
        string root = CreateTempProject();
        var assets = Path.Combine(root, "Assets");
        var physics = new PhysicsEngine();
        var compiler = new ScriptCompiler(assets);
        var parent = new GameObject { Name = "Rig" };
        var camera = new GameObject { Name = "Main Camera", Parent = parent };
        var roots = new List<GameObject> { parent };

        RuntimeScene.SetContext(roots, physics, compiler);
        try
        {
            AssertEqual(camera, RuntimeScene.FindObjectByName("Main Camera"), "find nested camera");
            AssertEqual(camera, RuntimeScene.FindObjectByName("main camera"), "find ignores case");
            AssertEqual(null, RuntimeScene.FindObjectByName("Missing"), "missing object");
        }
        finally
        {
            RuntimeScene.ClearContext();
        }
    }

    private static void GameObjectSetParentReparentsThroughRuntimeScene()
    {
        string root = CreateTempProject();
        var assets = Path.Combine(root, "Assets");
        var physics = new PhysicsEngine();
        var compiler = new ScriptCompiler(assets);
        var parent = new GameObject { Name = "Parent" };
        parent.PosX = 10f;
        var child = new GameObject { Name = "Child" };
        child.PosX = 2f;
        child.PosY = 3f;
        var roots = new List<GameObject> { parent, child };

        RuntimeScene.SetContext(roots, physics, compiler);
        try
        {
            var world = child.Position;

            child.SetParent(parent);

            AssertEqual(parent, child.Parent, "child parent assigned");
            AssertFalse(roots.Contains(child), "child removed from roots");
            AssertEqual(child, parent.Children[0], "child inserted under parent");
            AssertNear(world.X, child.Position.X, "world x preserved");
            AssertNear(world.Y, child.Position.Y, "world y preserved");

            child.Unparent();

            AssertEqual(null, child.Parent, "child parent cleared");
            AssertTrue(roots.Contains(child), "child returned to roots");
            AssertFalse(parent.Children.Contains(child), "child removed from parent");
            AssertNear(world.X, child.Position.X, "world x preserved after unparent");
        }
        finally
        {
            RuntimeScene.ClearContext();
        }
    }

    private static void RuntimeSceneUnparentsDirectParentSetterToRoot()
    {
        string root = CreateTempProject();
        var assets = Path.Combine(root, "Assets");
        var physics = new PhysicsEngine();
        var compiler = new ScriptCompiler(assets);
        var parent = new GameObject { Name = "Parent" };
        parent.PosX = 10f;
        var child = new GameObject { Name = "Child", Parent = parent };
        child.PosX = 2f;
        child.PosY = 3f;
        var roots = new List<GameObject> { parent };

        RuntimeScene.SetContext(roots, physics, compiler);
        try
        {
            var world = child.Position;

            child.Parent = null;

            AssertEqual(null, child.Parent, "child parent cleared");
            AssertTrue(roots.Contains(child), "child moved to roots");
            AssertFalse(parent.Children.Contains(child), "child removed from old parent");
            AssertNear(world.X, child.Position.X, "world x preserved");
            AssertNear(world.Y, child.Position.Y, "world y preserved");
            AssertNear(world.Z, child.Position.Z, "world z preserved");
        }
        finally
        {
            RuntimeScene.ClearContext();
        }
    }

    private static void RuntimeSceneSetParentCanKeepLocalTransform()
    {
        string root = CreateTempProject();
        var assets = Path.Combine(root, "Assets");
        var physics = new PhysicsEngine();
        var compiler = new ScriptCompiler(assets);
        var parent = new GameObject { Name = "Parent" };
        parent.PosX = 10f;
        var child = new GameObject { Name = "Child" };
        child.PosX = 2f;
        child.PosY = 3f;
        var roots = new List<GameObject> { parent, child };

        RuntimeScene.SetContext(roots, physics, compiler);
        try
        {
            RuntimeScene.SetParent(child, parent, worldPositionStays: false);

            AssertEqual(parent, child.Parent, "child parent assigned");
            AssertFalse(roots.Contains(child), "child removed from roots");
            AssertEqual(child, parent.Children[0], "child inserted under parent");
            AssertNear(2f, child.PosX, "local x preserved");
            AssertNear(3f, child.PosY, "local y preserved");
            AssertNear(12f, child.Position.X, "world x changed by parent");
        }
        finally
        {
            RuntimeScene.ClearContext();
        }
    }

    private static void RuntimeSceneDestroysObjectsWithDelay()
    {
        string root = CreateTempProject();
        var assets = Path.Combine(root, "Assets");
        var physics = new PhysicsEngine();
        var compiler = new ScriptCompiler(assets);
        var roots = new List<GameObject>();

        var obj = new GameObject { Name = "Explosion" };
        var collider = obj.AddComponentWithEngine<BoxCollider>(physics);
        var recorder = obj.AddComponent<DestroyRecorder>();
        roots.Add(obj);
        physics.SyncPhysicsComponents(roots);

        RuntimeScene.SetContext(roots, physics, compiler);
        try
        {
            RuntimeScene.Destroy(obj, 0.5f);
            RuntimeScene.Tick(0.25);

            AssertTrue(roots.Contains(obj), "object alive before delay");
            AssertTrue(physics.GetColliders().Contains(collider), "collider alive before delay");
            AssertEqual(0, recorder.DestroyCount, "destroy not called before delay");

            RuntimeScene.Tick(0.25);

            AssertFalse(roots.Contains(obj), "object removed after delay");
            AssertFalse(physics.GetColliders().Contains(collider), "collider unregistered after delay");
            AssertEqual(1, recorder.DestroyCount, "destroy called after delay");
        }
        finally
        {
            RuntimeScene.ClearContext();
        }
    }

    private static void StaticPhysicsRaycastUsesRuntimeContext()
    {
        string root = CreateTempProject();
        var assets = Path.Combine(root, "Assets");
        var physics = new PhysicsEngine();
        var compiler = new ScriptCompiler(assets);
        var roots = new List<GameObject>();

        var target = new GameObject { Name = "Target" };
        target.PosX = 2f;
        target.AddComponentWithEngine<BoxCollider>(physics);
        roots.Add(target);
        physics.SyncPhysicsComponents(roots);

        RuntimeScene.SetContext(roots, physics, compiler);
        try
        {
            bool hit = Physics.Raycast(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), 10f, out var rayHit);

            AssertTrue(hit, "static physics raycast hit");
            AssertEqual(target, rayHit.GameObject, "static physics raycast target");
            AssertNear(1.5f, rayHit.Distance, "static physics raycast distance");
        }
        finally
        {
            RuntimeScene.ClearContext();
        }
    }

    private static void PhysicsEngineDispatchesCollisionEvents()
    {
        var physics = new PhysicsEngine();
        var a = new GameObject { Name = "A" };
        a.AddComponent<BoxCollider>();
        var eventsA = a.AddComponent<PhysicsEventRecorder>();

        var b = new GameObject { Name = "B" };
        b.PosX = 0.5f;
        b.AddComponent<BoxCollider>();
        var eventsB = b.AddComponent<PhysicsEventRecorder>();

        var roots = new List<GameObject> { a, b };
        physics.Step(roots, 0.016);

        AssertEqual(1, eventsA.CollisionEnterCount, "collision enter A");
        AssertEqual(1, eventsB.CollisionEnterCount, "collision enter B");
        AssertEqual(b, eventsA.LastCollisionObject, "collision payload object");

        physics.Step(roots, 0.016);

        AssertEqual(1, eventsA.CollisionEnterCount, "collision enter only once");
        AssertEqual(1, eventsA.CollisionStayCount, "collision stay A");
        AssertEqual(1, eventsB.CollisionStayCount, "collision stay B");

        b.PosX = 5f;
        physics.Step(roots, 0.016);

        AssertEqual(1, eventsA.CollisionExitCount, "collision exit A");
        AssertEqual(1, eventsB.CollisionExitCount, "collision exit B");
    }

    private static void PhysicsEngineDispatchesBepuContactEvents()
    {
        BepuBackend.Reset();
        BepuBackend.Enabled = true;
        var physics = new PhysicsEngine();

        var floor = new GameObject { Name = "Floor" };
        floor.PosY = -0.5f;
        var floorCollider = floor.AddComponent<BoxCollider>();
        floorCollider.Size = new Vector3(10f, 1f, 10f);

        var cube = new GameObject { Name = "Dynamic Cube" };
        cube.PosY = 1.0f;
        cube.AddComponent<BoxCollider>();
        var rb = cube.AddComponent<Rigidbody>();
        rb.UseGravity = true;
        var events = cube.AddComponent<PhysicsEventRecorder>();

        var roots = new List<GameObject> { floor, cube };
        for (int i = 0; i < 90; i++)
            physics.Step(roots, 1.0 / 60.0);

        AssertTrue(BepuBackend.HasFreshContactFrame, "bepu contact frame produced");
        AssertTrue(BepuBackend.ContactEvents.Count > 0, "bepu contact event captured");
        AssertTrue(events.CollisionEnterCount > 0 || events.CollisionStayCount > 0, "script received BEPU collision");
        AssertTrue(rb.IsGrounded, "rigidbody grounded from BEPU contact normal");
    }

    private static void PhysicsEngineDispatchesTriggerEvents()
    {
        var physics = new PhysicsEngine();
        var sensor = new GameObject { Name = "Sensor" };
        var sensorCollider = sensor.AddComponent<BoxCollider>();
        sensorCollider.IsTrigger = true;
        var sensorEvents = sensor.AddComponent<PhysicsEventRecorder>();

        var player = new GameObject { Name = "Player" };
        player.PosX = 0.5f;
        player.AddComponent<BoxCollider>();
        var playerEvents = player.AddComponent<PhysicsEventRecorder>();

        var roots = new List<GameObject> { sensor, player };
        physics.Step(roots, 0.016);

        AssertEqual(1, sensorEvents.TriggerEnterCount, "trigger enter sensor");
        AssertEqual(1, playerEvents.TriggerEnterCount, "trigger enter player");
        AssertEqual(player, sensorEvents.LastTriggerObject, "trigger payload object");

        physics.Step(roots, 0.016);

        AssertEqual(1, sensorEvents.TriggerEnterCount, "trigger enter only once");
        AssertEqual(1, sensorEvents.TriggerStayCount, "trigger stay sensor");
        AssertEqual(1, playerEvents.TriggerStayCount, "trigger stay player");

        player.PosX = 5f;
        physics.Step(roots, 0.016);

        AssertEqual(1, sensorEvents.TriggerExitCount, "trigger exit sensor");
        AssertEqual(1, playerEvents.TriggerExitCount, "trigger exit player");
    }

    private static void CharacterControllerGroundsAndBlocksMovement()
    {
        var physics = new PhysicsEngine();
        var floor = new GameObject { Name = "Floor" };
        floor.PosY = 0f;
        var floorCollider = floor.AddComponent<BoxCollider>();
        floorCollider.Size = new Vector3(20f, 1f, 20f);

        var wall = new GameObject { Name = "Wall" };
        wall.PosX = 2f;
        wall.PosY = 1.5f;
        var wallCollider = wall.AddComponent<BoxCollider>();
        wallCollider.Size = new Vector3(1f, 3f, 3f);

        var player = new GameObject { Name = "Player" };
        player.PosY = 2f;
        var cc = player.AddComponentWithEngine<CharacterController>(physics);
        cc.Radius = 0.5f;
        cc.Height = 2f;
        cc.StepOffset = 0.25f;

        for (int i = 0; i < 90; i++)
            physics.Step(new List<GameObject> { floor, wall, player }, 1.0 / 60.0);

        AssertTrue(cc.IsGrounded, "character grounded");
        AssertNear(0.52f, player.Position.Y, "character rests on floor", 0.04f);

        var flags = cc.Move(new Vector3(4f, 0f, 0f));

        AssertTrue((flags & CollisionFlags.Sides) != 0, "character side collision");
        AssertTrue(player.Position.X < 1.02f, "character stopped before wall");
    }

    private static void CharacterControllerStepsOverLowObstacles()
    {
        var physics = new PhysicsEngine();
        var floor = new GameObject { Name = "Floor" };
        var floorCollider = floor.AddComponent<BoxCollider>();
        floorCollider.Size = new Vector3(20f, 1f, 20f);

        var step = new GameObject { Name = "Step" };
        step.PosX = 1.2f;
        step.PosY = 0.625f;
        var stepCollider = step.AddComponent<BoxCollider>();
        stepCollider.Size = new Vector3(0.5f, 0.25f, 2f);

        var player = new GameObject { Name = "Player" };
        player.PosY = 0.52f;
        var cc = player.AddComponentWithEngine<CharacterController>(physics);
        cc.Radius = 0.5f;
        cc.Height = 2f;
        cc.StepOffset = 0.4f;

        physics.Step(new List<GameObject> { floor, step, player }, 1.0 / 60.0);
        var flags = cc.Move(new Vector3(1.1f, 0f, 0f));

        AssertTrue((flags & CollisionFlags.Below) != 0, "character snapped to step");
        AssertTrue(player.Position.X > 1f, "character moved over step");
        AssertTrue(player.Position.Y > 0.7f, "character climbed step");
    }

    private static void SceneSerializerPreservesCharacterController()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string scenePath = Path.Combine(assets, "Scenes", "Character.gscene");
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);

        var obj = new GameObject { Name = "Player" };
        var cc = obj.AddComponent<CharacterController>();
        cc.Height = 1.8f;
        cc.Radius = 0.4f;
        cc.Center = new Vector3(0f, 0.9f, 0f);
        cc.StepOffset = 0.45f;
        cc.JumpSpeed = 7f;
        cc.PushPower = 3f;
        cc.AutoCenter = false;

        SceneSerializer.Save(scenePath, new[] { obj });
        var loaded = SceneSerializer.Load(scenePath, new PhysicsEngine(), new ScriptCompiler(assets));
        var loadedCc = loaded[0].GetComponent<CharacterController>()!;

        AssertNear(1.8f, loadedCc.Height, "character height");
        AssertNear(0.4f, loadedCc.Radius, "character radius");
        AssertNear(0.9f, loadedCc.Center.Y, "character center");
        AssertNear(0.45f, loadedCc.StepOffset, "character step offset");
        AssertNear(7f, loadedCc.JumpSpeed, "character jump speed");
        AssertNear(3f, loadedCc.PushPower, "character push power");
        AssertFalse(loadedCc.AutoCenter, "character auto center");
    }

    private static void SceneSerializerDoesNotDuplicateCharacterCapsule()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string scenePath = Path.Combine(assets, "Scenes", "CharacterCollider.gscene");
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);

        var obj = new GameObject { Name = "Player" };
        var cc = obj.AddComponent<CharacterController>();
        cc.Height = 2f;
        cc.Radius = 0.5f;
        var collider = obj.AddComponent<CapsuleCollider>();
        collider.Radius = 0.5f;
        collider.Height = 2f;

        SceneSerializer.Save(scenePath, new[] { obj });
        var loaded = SceneSerializer.Load(scenePath, new PhysicsEngine(), new ScriptCompiler(assets));

        AssertEqual(1, loaded[0].GetComponents<CharacterController>().Count, "character controller count");
        AssertEqual(1, loaded[0].GetComponents<CapsuleCollider>().Count, "capsule collider count");
    }

    private static void CharacterControllerIgnoresSameObjectColliders()
    {
        var physics = new PhysicsEngine();
        var player = new GameObject { Name = "Player" };
        var cc = player.AddComponentWithEngine<CharacterController>(physics);
        cc.UseGravity = false;
        cc.Radius = 0.5f;
        cc.Height = 2f;
        cc.EnsureCollider();
        var oldBox = player.AddComponentWithEngine<BoxCollider>(physics);
        oldBox.Size = new Vector3(1f, 2f, 1f);

        physics.SyncPhysicsComponents(new[] { player });
        var flags = cc.Move(new Vector3(1f, 0f, 0f));

        AssertFalse((flags & CollisionFlags.Sides) != 0, "same object box should not block character");
        AssertNear(1f, player.Position.X, "character moved despite same object box", 0.001f);
    }

    private static void CharacterControllerAutoCentersCapsule()
    {
        var obj = new GameObject { Name = "Player" };
        var cc = obj.AddComponent<CharacterController>();
        cc.Height = 3f;
        cc.Radius = 0.5f;
        cc.Center = Vector3.Zero;

        var capsule = cc.EnsureCollider();

        AssertNear(1.5f, cc.Center.Y, "auto centered controller center");
        AssertNear(1.5f, capsule.Center.Y, "auto centered capsule center");

        cc.AutoCenter = false;
        cc.Center = new Vector3(0f, 0.25f, 0f);
        cc.EnsureCollider();

        AssertNear(0.25f, capsule.Center.Y, "manual capsule center");
    }

    private static void CharacterControllerRecordsLastMoveDebugData()
    {
        var obj = new GameObject { Name = "Player" };
        var cc = obj.AddComponent<CharacterController>();

        var flags = cc.Move(new Vector3(0.25f, 0.5f, -0.75f));

        AssertEqual(CollisionFlags.None, flags, "move without physics flags");
        AssertEqual(CollisionFlags.None, cc.LastMoveFlags, "last move flags");
        AssertNear(0.25f, cc.LastMoveDelta.X, "last move x");
        AssertNear(0.5f, cc.LastMoveDelta.Y, "last move y");
        AssertNear(-0.75f, cc.LastMoveDelta.Z, "last move z");
    }

    private static void SceneSerializerPreservesAdditionalColliders()
    {
        string root = CreateTempProject();
        string assets = Path.Combine(root, "Assets");
        string scenePath = Path.Combine(assets, "Scenes", "Colliders.gscene");
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);

        var obj = new GameObject { Name = "ColliderZoo" };
        var sphere = obj.AddComponent<SphereCollider>();
        sphere.Radius = 1.25f;
        sphere.Center = new Vector3(1f, 2f, 3f);
        sphere.IsTrigger = true;
        sphere.PhysicMaterial = "Rubber";

        var capsule = obj.AddComponent<CapsuleCollider>();
        capsule.Radius = 0.3f;
        capsule.Height = 3f;
        capsule.Axis = CapsuleAxis.Z;

        var mesh = obj.AddComponent<MeshCollider>();
        mesh.UseMeshBounds = false;
        mesh.Size = new Vector3(2f, 3f, 4f);
        mesh.Friction = 0.25f;

        SceneSerializer.Save(scenePath, new[] { obj });
        var loaded = SceneSerializer.Load(scenePath, new PhysicsEngine(), new ScriptCompiler(assets))[0];

        var loadedSphere = loaded.GetComponent<SphereCollider>()!;
        var loadedCapsule = loaded.GetComponent<CapsuleCollider>()!;
        var loadedMesh = loaded.GetComponent<MeshCollider>()!;

        AssertNear(1.25f, loadedSphere.Radius, "loaded sphere radius");
        AssertNear(2f, loadedSphere.Center.Y, "loaded sphere center");
        AssertTrue(loadedSphere.IsTrigger, "loaded sphere trigger");
        AssertEqual("Rubber", loadedSphere.PhysicMaterial, "loaded sphere material");
        AssertNear(0.3f, loadedCapsule.Radius, "loaded capsule radius");
        AssertNear(3f, loadedCapsule.Height, "loaded capsule height");
        AssertEqual(CapsuleAxis.Z, loadedCapsule.Axis, "loaded capsule axis");
        AssertFalse(loadedMesh.UseMeshBounds, "loaded mesh use bounds");
        AssertNear(4f, loadedMesh.Size.Z, "loaded mesh size");
        AssertNear(0.25f, loadedMesh.Friction, "loaded mesh friction");
    }

    private static void ParticleSystemRateOverDistanceStartsAtCurrentPosition()
    {
        var obj = new GameObject { Name = "Emitter" };
        obj.PosX = 100f;
        var particles = obj.AddComponent<ParticleSystem>();
        particles.EmitRate = 0f;
        particles.RateOverDistanceEnabled = true;
        particles.RateOverDistance = 1f;
        particles.ShapeRadius = 0f;

        particles.Play();
        particles.Update(0.016);

        AssertEqual(0, particles.Particles.Count, "initial rate-over-distance particles");
    }

    private static void LightmapBakerHandlesEmptyScenes()
    {
        string root = CreateTempProject();
        var baker = new LightmapBaker(root);
        float progress = 0f;
        baker.OnProgress += p => progress = p;

        baker.BakeAsync(
            Array.Empty<GameObject>(),
            new BakeLightingInfo(),
            _ => null,
            resolution: 4).GetAwaiter().GetResult();

        AssertNear(1f, progress, "empty bake progress");
        AssertEqual(0, baker.BakedPaths.Count, "empty bake paths");
    }

    private static void LightmapBakerAppliesObjectScale()
    {
        string root = CreateTempProject();
        var obj = new GameObject { Name = "Scaled", IsStatic = true };
        obj.ScaleX = 2f;
        obj.AddComponent<MeshFilter>().MeshPath = "mesh";

        var lighting = new BakeLightingInfo
        {
            Ambient = new BakeAmbientLight { R = 0f, G = 0f, B = 0f, Intensity = 0f, SkyStrength = 0f },
            Directional = new BakeDirectionalLight { R = 0f, G = 0f, B = 0f, Intensity = 0f }
        };
        lighting.PointLights.Add(new BakePointLight
        {
            Position = new Vector3(3f, 0f, 0f),
            R = 1f,
            G = 0f,
            B = 0f,
            Intensity = 1f,
            Range = 10f
        });

        var mesh = new LightmapBaker.BakedMeshData(
            new[] { 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f },
            new[] { 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f },
            new[] { 0f, 0f, 0f, 0f, 0f, 0f },
            1);

        var baker = new LightmapBaker(root);
        baker.BakeAsync(new[] { obj }, lighting, _ => mesh, resolution: 1).GetAwaiter().GetResult();

        string path = baker.BakedPaths[obj.EditorId];
        byte red = ReadBmpPixelRed(path);
        AssertTrue(red > 190, "scaled lightmap red channel");
    }

    private static string CreateTempProject()
    {
        string root = Path.Combine(Path.GetTempPath(), "GrokoEngineTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        return root;
    }

    private static void AssertContains(string value, string expected, string label)
    {
        if (!value.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"{label}: expected to contain '{expected}'.");
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'.");
    }

    private static void AssertNear(float expected, float actual, string label)
    {
        if (Math.Abs(expected - actual) > 0.0001f)
            throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'.");
    }

    private static void AssertNear(float expected, float actual, string label, float tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'.");
    }

    private static void AssertTrue(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"{label}: expected true.");
    }

    private static void AssertFalse(bool value, string label)
    {
        if (value) throw new InvalidOperationException($"{label}: expected false.");
    }

    private static void HdrLoaderDecodesFlatRgbe()
    {
        // 2x1, ambos pixeles (0.5, 0.25, 1.0) -> round-trip exacto en RGBE.
        byte R = 64, G = 32, B = 128, E = 129;
        using var ms = new MemoryStream();
        WriteHdrHeader(ms, 2, 1);
        for (int i = 0; i < 2; i++) { ms.WriteByte(R); ms.WriteByte(G); ms.WriteByte(B); ms.WriteByte(E); }
        ms.Position = 0;

        var img = HdrLoader.Parse(ms);
        AssertTrue(img.Width == 2 && img.Height == 1, "HDR flat dims");
        AssertClose(img.Pixels[0], 0.5f, "HDR flat R");
        AssertClose(img.Pixels[1], 0.25f, "HDR flat G");
        AssertClose(img.Pixels[2], 1.0f, "HDR flat B");
        AssertClose(img.Pixels[3], 0.5f, "HDR flat R (px2)");
    }

    private static void HdrLoaderDecodesNewRle()
    {
        // 8x1, todos (0.5,0.25,1.0), codificado en RLE adaptativo nuevo (un run por canal).
        using var ms = new MemoryStream();
        WriteHdrHeader(ms, 8, 1);
        ms.WriteByte(2); ms.WriteByte(2); ms.WriteByte(0); ms.WriteByte(8); // cabecera de scanline, width=8
        byte[] vals = { 64, 32, 128, 129 };
        foreach (var v in vals) { ms.WriteByte(136); ms.WriteByte(v); } // 136 = 128+8: run de 8
        ms.Position = 0;

        var img = HdrLoader.Parse(ms);
        AssertTrue(img.Width == 8 && img.Height == 1, "HDR rle dims");
        for (int x = 0; x < 8; x++)
        {
            AssertClose(img.Pixels[x * 3 + 0], 0.5f, $"HDR rle R[{x}]");
            AssertClose(img.Pixels[x * 3 + 1], 0.25f, $"HDR rle G[{x}]");
            AssertClose(img.Pixels[x * 3 + 2], 1.0f, $"HDR rle B[{x}]");
        }
    }

    private static void WriteHdrHeader(Stream s, int w, int h)
    {
        void W(string t) { foreach (char c in t) s.WriteByte((byte)c); }
        W("#?RADIANCE\n");
        W("FORMAT=32-bit_rle_rgbe\n");
        W("\n");
        W($"-Y {h} +X {w}\n");
    }

    private static void AssertClose(float actual, float expected, string label)
    {
        if (MathF.Abs(actual - expected) > 1e-4f)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }

    private static byte ReadBmpPixelRed(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int offset = BitConverter.ToInt32(bytes, 10);
        return bytes[offset + 2];
    }

    private static void ShaderGraphSerializerNormalizesNullCollections()
    {
        string root = CreateTempProject();
        string graphPath = Path.Combine(root, "Assets", "Broken.shadergraph");
        File.WriteAllText(graphPath, """
            {
              "Version": null,
              "Name": null,
              "Surface": null,
              "Nodes": [
                {
                  "Kind": "Output",
                  "Title": null,
                  "Inputs": null,
                  "Outputs": null
                }
              ],
              "Groups": null,
              "Properties": null,
              "SubGraphs": null,
              "Connections": null
            }
            """);

        var graph = GraphSerializer.Load(graphPath);
        AssertEqual("2.0", graph.Version, "graph version fallback");
        AssertEqual("Untitled Shader", graph.Name, "graph name fallback");
        AssertEqual(1, graph.Nodes.Count, "graph node count");
        AssertTrue(graph.Nodes[0].Inputs.Count > 0, "output pins repaired");
        AssertEqual(0, graph.Connections.Count, "graph connections normalized");

        string shader = new ShaderCodeGenerator().GenerateFragmentShader(graph);
        AssertContains(shader, "FragColor", "generated shader output");
    }

    private static void ShaderGraphSchemaRepairsLegacyVectorPins()
    {
        var graph = new ShaderGraphModel();
        var uv = NodeFactory.Create(NodeKind.TextureCoord, 80, 100);
        var panner = NodeFactory.Create(NodeKind.Panner, 300, 100);
        var output = NodeFactory.Create(NodeKind.Output, 560, 100);

        var speed = panner.Input("Speed")!;
        speed.Type = PinType.Float;
        speed.DefaultValue = "0.1";

        var uvInput = panner.Input("UV")!;
        uvInput.Type = PinType.Float;
        uvInput.DefaultValue = "0.0";

        graph.Nodes.Add(uv);
        graph.Nodes.Add(panner);
        graph.Nodes.Add(output);
        graph.Connections.Add(new GraphConnection
        {
            FromPinId = uv.Output("UV")!.Id,
            ToPinId = uvInput.Id
        });
        graph.Connections.Add(new GraphConnection
        {
            FromPinId = panner.Output("UV")!.Id,
            ToPinId = output.Input("Base Color")!.Id
        });

        var report = ShaderGraphSchemaRepair.Repair(graph);
        AssertTrue(report.Changed, "schema repair detected legacy pins");
        AssertEqual(PinType.Vec2, panner.Input("Speed")!.Type, "panner speed repaired to vec2");
        AssertEqual("vec2(0.1, 0.0)", panner.Input("Speed")!.DefaultValue, "panner speed default repaired");
        AssertEqual(PinType.Vec2, panner.Input("UV")!.Type, "panner uv repaired to vec2");

        string shader = new ShaderCodeGenerator().GenerateFragmentShader(graph);
        AssertContains(shader, "vec2(0.1, 0.0)", "repaired vec2 default in shader");
        AssertContains(shader, "FragColor", "generated shader output");
    }

    private static void ShaderGraphNodesUseUnityStyleDefaults()
    {
        var floatNode = NodeFactory.Create(NodeKind.ConstantFloat, 0, 0);
        AssertEqual(0f, floatNode.FloatValue, "float node default value");

        var colorNode = NodeFactory.Create(NodeKind.ConstantColor, 0, 0);
        AssertEqual("#FFFFFFFF", colorNode.ColorHex, "color node default white");

        var vector3 = NodeFactory.Create(NodeKind.ConstantVector3, 0, 0);
        AssertEqual("0.0, 0.0, 0.0", vector3.TextValue, "vector3 default zero");

        var texture = NodeFactory.Create(NodeKind.TextureSample, 0, 0);
        AssertEqual("Sample Texture 2D", texture.Title, "sample texture title");
        AssertTrue(texture.Output("A") is not null, "sample texture alpha output uses A");
        AssertTrue(texture.Output("Alpha") is null, "sample texture old alpha output removed");

        var sine = NodeFactory.Create(NodeKind.Sin, 0, 0);
        AssertEqual("Sine", sine.Title, "sine title");
        AssertEqual("In", sine.Inputs[0].Name, "sine input name");
        AssertEqual("Out", sine.Outputs[0].Name, "sine output name");

        var smooth = NodeFactory.Create(NodeKind.Smoothstep, 0, 0);
        AssertEqual("Edge1", smooth.Inputs[0].Name, "smoothstep edge1");
        AssertEqual("Edge2", smooth.Inputs[1].Name, "smoothstep edge2");
        AssertEqual("In", smooth.Inputs[2].Name, "smoothstep input");
        AssertEqual("Out", smooth.Outputs[0].Name, "smoothstep output");

        var power = NodeFactory.Create(NodeKind.Power, 0, 0);
        AssertEqual("A", power.Inputs[0].Name, "power A input");
        AssertEqual("B", power.Inputs[1].Name, "power B input");
        AssertEqual("Out", power.Outputs[0].Name, "power output");

        var voronoi = NodeFactory.Create(NodeKind.Voronoi, 0, 0);
        AssertTrue(voronoi.Input("Angle Offset") is not null, "voronoi angle offset unity name");
        AssertTrue(voronoi.Input("Cell Density") is not null, "voronoi cell density unity name");

        var tiling = NodeFactory.Create(NodeKind.TilingOffset, 0, 0);
        AssertEqual("Out", tiling.Outputs[0].Name, "tiling and offset output");

        var sceneDepth = NodeFactory.Create(NodeKind.SceneDepth, 0, 0);
        AssertEqual("Eye", sceneDepth.TextValue, "scene depth default sampling");
        AssertEqual(PinType.Vec4, sceneDepth.Input("UV")!.Type, "scene depth uv vector4");
        AssertEqual("v_ScreenPos", sceneDepth.Input("UV")!.DefaultValue, "scene depth uv default");
        AssertEqual(PinType.Float, sceneDepth.Output("Out")!.Type, "scene depth output float");

        var screenPosition = NodeFactory.Create(NodeKind.ScreenPosition, 0, 0);
        AssertEqual("Raw", screenPosition.TextValue, "screen position default mode");
        AssertEqual(PinType.Vec4, screenPosition.Output("Out")!.Type, "screen position output vector4");

        var split = NodeFactory.Create(NodeKind.Split, 0, 0);
        AssertEqual(PinType.Vec4, split.Input("In")!.Type, "split input vector4");
        AssertEqual("vec4(0.0)", split.Input("In")!.DefaultValue, "split input zero default");
        AssertTrue(split.Output("R") is not null, "split R output");
        AssertTrue(split.Output("G") is not null, "split G output");
        AssertTrue(split.Output("B") is not null, "split B output");
        AssertTrue(split.Output("A") is not null, "split A output");

        var sceneGraph = new ShaderGraphModel();
        var output = NodeFactory.Create(NodeKind.Output, 500, 0);
        sceneGraph.Nodes.Add(sceneDepth);
        sceneGraph.Nodes.Add(output);
        sceneGraph.Connections.Add(new GraphConnection
        {
            FromPinId = sceneDepth.Output("Out")!.Id,
            ToPinId = output.Input("Alpha")!.Id
        });

        string sceneShader = new ShaderCodeGenerator().GenerateFragmentShader(sceneGraph);
        AssertContains(sceneShader, "sceneDepthLinearEye", "scene depth eye sampling shader");
        AssertContains(sceneShader, "v_ScreenPos", "scene depth uses screen position vector4");
        AssertContains(sceneShader, "* 0.5 + 0.5", "scene depth converts clip screen position to uv");

        var splitGraph = new ShaderGraphModel();
        var splitOutput = NodeFactory.Create(NodeKind.Output, 700, 0);
        splitGraph.Nodes.Add(screenPosition);
        splitGraph.Nodes.Add(split);
        splitGraph.Nodes.Add(splitOutput);
        splitGraph.Connections.Add(new GraphConnection
        {
            FromPinId = screenPosition.Output("Out")!.Id,
            ToPinId = split.Input("In")!.Id
        });
        splitGraph.Connections.Add(new GraphConnection
        {
            FromPinId = split.Output("A")!.Id,
            ToPinId = splitOutput.Input("Alpha")!.Id
        });

        string splitShader = new ShaderCodeGenerator().GenerateFragmentShader(splitGraph);
        AssertContains(splitShader, "v_ScreenPos", "screen position raw shader");
        AssertContains(splitShader, "sceneDepthLinearEye(v_FragDepth)", "screen position raw alpha uses surface eye depth");
        AssertContains(splitShader, ".a", "split alpha channel shader");

        var depthFadeGraph = new ShaderGraphModel();
        var depthScreen = NodeFactory.Create(NodeKind.ScreenPosition, 80, 120);
        var depthSplit = NodeFactory.Create(NodeKind.Split, 280, 120);
        var depthScene = NodeFactory.Create(NodeKind.SceneDepth, 80, 20);
        var subtract = NodeFactory.Create(NodeKind.Subtract, 480, 80);
        var fadeOutput = NodeFactory.Create(NodeKind.Output, 720, 80);
        depthFadeGraph.Nodes.Add(depthScreen);
        depthFadeGraph.Nodes.Add(depthSplit);
        depthFadeGraph.Nodes.Add(depthScene);
        depthFadeGraph.Nodes.Add(subtract);
        depthFadeGraph.Nodes.Add(fadeOutput);
        depthFadeGraph.Connections.Add(new GraphConnection
        {
            FromPinId = depthScreen.Output("Out")!.Id,
            ToPinId = depthSplit.Input("In")!.Id
        });
        depthFadeGraph.Connections.Add(new GraphConnection
        {
            FromPinId = depthScene.Output("Out")!.Id,
            ToPinId = subtract.Input("A")!.Id
        });
        depthFadeGraph.Connections.Add(new GraphConnection
        {
            FromPinId = depthSplit.Output("A")!.Id,
            ToPinId = subtract.Input("B")!.Id
        });
        depthFadeGraph.Connections.Add(new GraphConnection
        {
            FromPinId = subtract.Output("Out")!.Id,
            ToPinId = fadeOutput.Input("Alpha")!.Id
        });

        string depthFadeShader = new ShaderCodeGenerator().GenerateFragmentShader(depthFadeGraph);
        AssertContains(depthFadeShader, "texture(u_SceneDepth", "scene depth samples the captured depth texture");
        AssertContains(depthFadeShader, "* 0.5 + 0.5", "scene depth graph samples depth with uv space");
        AssertContains(depthFadeShader, "sceneDepthLinearEye(v_FragDepth)", "screen position alpha matches scene depth eye units");
    }

    private static void ShaderGraphDynamicMathPinsFollowVectorConnections()
    {
        var graph = new ShaderGraphModel();
        var uv = NodeFactory.Create(NodeKind.TextureCoord, 80, 100);
        var add = NodeFactory.Create(NodeKind.Add, 300, 100);
        var output = NodeFactory.Create(NodeKind.Output, 560, 100);

        graph.Nodes.Add(uv);
        graph.Nodes.Add(add);
        graph.Nodes.Add(output);
        graph.Connections.Add(new GraphConnection
        {
            FromPinId = uv.Output("UV")!.Id,
            ToPinId = add.Input("A")!.Id
        });
        graph.Connections.Add(new GraphConnection
        {
            FromPinId = add.Output("Out")!.Id,
            ToPinId = output.Input("Base Color")!.Id
        });

        AssertTrue(ShaderGraphDynamicTypes.Synchronize(graph), "dynamic types changed");
        AssertEqual(PinType.Vec2, add.Input("A")!.Type, "add A follows vec2");
        AssertEqual(PinType.Vec2, add.Input("B")!.Type, "add B promotes to vec2");
        AssertEqual(PinType.Vec2, add.Output("Out")!.Type, "add output promotes to vec2");

        string shader = new ShaderCodeGenerator().GenerateFragmentShader(graph);
        AssertContains(shader, "vec2(0.0)", "vec2 math default in shader");
        AssertContains(shader, "FragColor", "generated shader output");
    }

    private static void ShaderGraphRemapUsesVector2RangePairs()
    {
        var graph = new ShaderGraphModel();
        var remap = NodeFactory.Create(NodeKind.Remap, 120, 100);
        var output = NodeFactory.Create(NodeKind.Output, 420, 100);

        graph.Nodes.Add(remap);
        graph.Nodes.Add(output);
        graph.Connections.Add(new GraphConnection
        {
            FromPinId = remap.Output("Out")!.Id,
            ToPinId = output.Input("Alpha")!.Id
        });

        ShaderGraphSchemaRepair.Repair(graph);
        ShaderGraphDynamicTypes.Synchronize(graph);

        AssertEqual(PinType.Float, remap.Input("In")!.Type, "remap in float");
        AssertEqual("0.5", remap.Input("In")!.DefaultValue, "remap in default");
        AssertEqual(PinType.Vec2, remap.Input("In Min Max")!.Type, "remap input range vec2");
        AssertEqual("vec2(0.0, 1.0)", remap.Input("In Min Max")!.DefaultValue, "remap input range default");
        AssertEqual(PinType.Vec2, remap.Input("Out Min Max")!.Type, "remap output range vec2");
        AssertEqual("vec2(0.0, 1.0)", remap.Input("Out Min Max")!.DefaultValue, "remap output range default");
        AssertEqual(PinType.Float, remap.Output("Out")!.Type, "remap out float");

        string shader = new ShaderCodeGenerator().GenerateFragmentShader(graph);
        AssertContains(shader, "vec2(0.0, 1.0)", "remap vec2 range default in shader");
        AssertContains(shader, ".x", "remap uses range min");
        AssertContains(shader, ".y", "remap uses range max");
        AssertContains(shader, "FragColor", "generated shader output");

        var legacyGraph = new ShaderGraphModel();
        var legacyRemap = new ShaderNode { Kind = NodeKind.Remap, Title = "Remap", X = 120, Y = 100 };
        GraphPin LegacyInput(string name, string defaultValue) => new()
        {
            NodeId = legacyRemap.Id,
            Name = name,
            Direction = PinDirection.Input,
            Type = PinType.Float,
            DefaultValue = defaultValue
        };

        legacyRemap.Inputs.Add(LegacyInput("X", "0.25"));
        legacyRemap.Inputs.Add(LegacyInput("From Min", "0.0"));
        legacyRemap.Inputs.Add(LegacyInput("From Max", "1.0"));
        legacyRemap.Inputs.Add(LegacyInput("To Min", "0.0"));
        legacyRemap.Inputs.Add(LegacyInput("To Max", "1.0"));
        legacyRemap.Outputs.Add(new GraphPin
        {
            NodeId = legacyRemap.Id,
            Name = "Result",
            Direction = PinDirection.Output,
            Type = PinType.Float
        });

        var legacyOutput = NodeFactory.Create(NodeKind.Output, 420, 100);
        legacyGraph.Nodes.Add(legacyRemap);
        legacyGraph.Nodes.Add(legacyOutput);
        legacyGraph.Connections.Add(new GraphConnection
        {
            FromPinId = legacyRemap.Output("Result")!.Id,
            ToPinId = legacyOutput.Input("Alpha")!.Id
        });

        var repair = ShaderGraphSchemaRepair.Repair(legacyGraph);
        ShaderGraphDynamicTypes.Synchronize(legacyGraph);

        AssertTrue(repair.Changed, "legacy remap repaired");
        AssertEqual(3, legacyRemap.Inputs.Count, "legacy remap compact input count");
        AssertEqual(1, legacyRemap.Outputs.Count, "legacy remap compact output count");
        AssertTrue(legacyRemap.Input("X") is null, "legacy x pin removed");
        AssertTrue(legacyRemap.Input("From Min") is null, "legacy from min pin removed");
        AssertTrue(legacyRemap.Input("From Max") is null, "legacy from max pin removed");
        AssertTrue(legacyRemap.Input("To Min") is null, "legacy to min pin removed");
        AssertTrue(legacyRemap.Input("To Max") is null, "legacy to max pin removed");
        AssertEqual(PinType.Float, legacyRemap.Input("In")!.Type, "legacy remap in float");
        AssertEqual(PinType.Vec2, legacyRemap.Input("In Min Max")!.Type, "legacy input range vec2");
        AssertEqual(PinType.Vec2, legacyRemap.Input("Out Min Max")!.Type, "legacy output range vec2");
        AssertEqual(PinType.Float, legacyRemap.Output("Out")!.Type, "legacy remap out float");
        AssertEqual(legacyRemap.Output("Out")!.Id, legacyGraph.Connections[0].FromPinId, "legacy output connection redirected");
    }

    private static void ShaderGraphGeneratorToleratesMissingNodePins()
    {
        var graph = new ShaderGraphModel();
        var output = NodeFactory.Create(NodeKind.Output, 300, 100);
        var brokenSin = new ShaderNode { Kind = NodeKind.Sin, Title = "Broken Sin" };
        var sinOut = new GraphPin
        {
            NodeId = brokenSin.Id,
            Name = "Result",
            Direction = PinDirection.Output,
            Type = PinType.Float
        };
        brokenSin.Outputs.Add(sinOut);

        graph.Nodes.Add(output);
        graph.Nodes.Add(brokenSin);
        graph.Connections.Add(new GraphConnection
        {
            FromPinId = sinOut.Id,
            ToPinId = output.Input("Base Color")!.Id
        });

        string shader = new ShaderCodeGenerator().GenerateFragmentShader(graph);
        AssertContains(shader, "sin(0.0)", "missing input default");
        AssertContains(shader, "FragColor", "generated shader output");
    }

    private static void SceneSerializerPreservesPostProcessEnabledFlag()
    {
        var physics = new PhysicsEngine();
        var scripts = new ScriptCompiler(Path.Combine(Path.GetTempPath(), "GrokoEngineTests", Guid.NewGuid().ToString("N"), "Assets"));
        var obj = new GameObject { Name = "Post FX" };
        var pp = obj.AddComponent<PostProcessSettings>();
        pp.PostProcessEnabled = false;
        pp.Exposure = 1.75f;
        pp.ToneMapping = false;

        string json = SceneSerializer.Serialize(new[] { obj });
        AssertContains(json, "PostProcessEnabled", "new post process flag serialized");
        var loaded = SceneSerializer.Deserialize(json, physics, scripts);
        var loadedPp = loaded[0].GetComponent<PostProcessSettings>()!;
        AssertFalse(loadedPp.PostProcessEnabled, "new post process flag restored");
        AssertNear(1.75f, loadedPp.Exposure, "post process exposure restored");
        AssertFalse(loadedPp.ToneMapping, "post process tone mapping restored");

        string legacyJson = json.Replace("PostProcessEnabled", "Enabled");
        var legacyLoaded = SceneSerializer.Deserialize(legacyJson, physics, scripts);
        var legacyPp = legacyLoaded[0].GetComponent<PostProcessSettings>()!;
        AssertFalse(legacyPp.PostProcessEnabled, "legacy enabled flag restored");
    }

    private sealed class TestCommand : ISceneCommand
    {
        private readonly Action execute;
        private readonly Action undo;

        public TestCommand(Action execute, Action undo)
        {
            this.execute = execute;
            this.undo = undo;
        }

        public void Execute() => execute();
        public void Undo() => undo();
    }

    private sealed class TestBehaviour : MonoBehaviour
    {
        public int UpdateCount;
        public override void Update(double dt) => UpdateCount++;
    }

    private sealed class DestroyRecorder : MonoBehaviour
    {
        public int DestroyCount;
        public override void OnDestroy() => DestroyCount++;
    }

    private sealed class PhysicsEventRecorder : MonoBehaviour
    {
        public int CollisionEnterCount;
        public int CollisionStayCount;
        public int CollisionExitCount;
        public int TriggerEnterCount;
        public int TriggerStayCount;
        public int TriggerExitCount;
        public GameObject? LastCollisionObject;
        public GameObject? LastTriggerObject;

        public override void OnCollisionEnter(Collision collision)
        {
            CollisionEnterCount++;
            LastCollisionObject = collision.GameObject;
        }

        public override void OnCollisionStay(Collision collision)
        {
            CollisionStayCount++;
            LastCollisionObject = collision.GameObject;
        }

        public override void OnCollisionExit(Collision collision)
        {
            CollisionExitCount++;
            LastCollisionObject = collision.GameObject;
        }

        public override void OnTriggerEnter(Collider other)
        {
            TriggerEnterCount++;
            LastTriggerObject = other.gameObject;
        }

        public override void OnTriggerStay(Collider other)
        {
            TriggerStayCount++;
            LastTriggerObject = other.gameObject;
        }

        public override void OnTriggerExit(Collider other)
        {
            TriggerExitCount++;
            LastTriggerObject = other.gameObject;
        }
    }
}
