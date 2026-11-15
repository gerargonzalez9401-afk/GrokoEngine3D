using GrokoEngine;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Vector2 = System.Numerics.Vector2;
using Vector3 = MiMotor.Mathematics.Vector3;
using Vec4 = System.Numerics.Vector4;
using ShaderGraphModel = GrokoShaderGraphPro.Models.ShaderGraphModel;
using ShaderGraphTemplates = GrokoShaderGraphPro.Services.GraphTemplates;
using ShaderCodeGenerator = GrokoShaderGraphPro.Services.ShaderCodeGenerator;
using ShaderGraphValidator = GrokoShaderGraphPro.Services.GraphValidator;
using GraphPin = GrokoShaderGraphPro.Models.GraphPin;
using GraphConnection = GrokoShaderGraphPro.Models.GraphConnection;
using NodeKind = GrokoShaderGraphPro.Models.NodeKind;
using PinType = GrokoShaderGraphPro.Models.PinType;
using PinDirection = GrokoShaderGraphPro.Models.PinDirection;
using GraphProperty = GrokoShaderGraphPro.Models.GraphProperty;
using PropertyAttribute = GrokoShaderGraphPro.Models.PropertyAttribute;
using PropertyColorMode = GrokoShaderGraphPro.Models.PropertyColorMode;
using GlfwKeys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using GlfwMouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;

namespace GrokoEngine.ImGuiEditor;
internal sealed partial class ImGuiEditorApp
{
    // Alterna la vista 2D (ortográfica, plana de frente) ↔ 3D (perspectiva), como Unity.
    private void SetCamera2D(bool on)
    {
        camera.Orthographic = on;
        if (on)
        {
            camera.Pitch = 0f;
            camera.Yaw = 90f;           // mirar +Z (vista de frente)
            camera.UpdateFront();
            camera.OrthoSize = Math.Clamp(MathF.Abs(camera.Position.Z), 1f, 50f);
        }
        statusMessage = on ? "2D view" : "3D view";
    }

    private void HandleCameraInput(float dt)
    {
        if (isPlaying)
        {
            // En play mode la vista usa la cámara de juego — no mover la cámara del editor
            ResetSceneCameraMouseState();
            return;
        }

        // Si el ratón está sobre una ventana flotante (Animator/Animation/ShaderGraph…),
        // no mover la cámara del viewport que queda detrás.
        if (IsSceneViewportInputBlockedByUi())
        {
            ResetSceneCameraMouseState();
            return;
        }

        if (gizmoDragging || colliderEditDragging)
        {
            ResetSceneCameraMouseState();
            return;
        }

        bool rightDown = MouseState.IsButtonDown(GlfwMouseButton.Right);
        bool middleDown = MouseState.IsButtonDown(GlfwMouseButton.Middle);
        bool altDown = KeyboardState.IsKeyDown(GlfwKeys.LeftAlt) || KeyboardState.IsKeyDown(GlfwKeys.RightAlt);
        bool orbitDown = altDown && MouseState.IsButtonDown(GlfwMouseButton.Left);
        var mouse = new Vector2(MouseState.X, MouseState.Y);
        bool inViewport = IsMouseInsideViewport(mouse.X, mouse.Y);

        if (!inViewport)
        {
            ResetSceneCameraMouseState();
            return;
        }

        float focusDistance = GetSceneCameraFocusDistance();
        float wheel = MouseState.ScrollDelta.Y;
        if (Math.Abs(wheel) > 0.001f)
        {
            if (camera.Orthographic)
                camera.OrthoSize = Math.Clamp(camera.OrthoSize * (1f - wheel * 0.1f), 0.2f, 500f); // 2D: zoom = tamaño ortográfico
            else
            {
                float zoomStep = Math.Max(0.25f, focusDistance * 0.12f);
                camera.Position += camera.Front * (wheel * zoomStep);
            }
        }

        if (middleDown)
        {
            if (previousMiddleMouseDown)
                PanSceneCamera(mouse - lastMousePosition, focusDistance);
            lastMousePosition = mouse;
        }

        if (orbitDown && !camera.Orthographic) // 2D: sin orbitar (vista plana fija)
        {
            if (!previousAltOrbitMouseDown)
            {
                sceneOrbitTarget = selected != null ? GetObjectCenter(selected) : EstimateSceneCenter();
                sceneOrbitDistance = Math.Max(0.5f, Distance(camera.Position, sceneOrbitTarget));
            }
            else
            {
                var delta = mouse - lastMousePosition;
                camera.Yaw += delta.X * 0.18f;
                camera.Pitch = Math.Clamp(camera.Pitch - delta.Y * 0.18f, -89f, 89f);
                camera.UpdateFront();
                camera.Position = sceneOrbitTarget - camera.Front * sceneOrbitDistance;
            }

            lastMousePosition = mouse;
        }

        if (rightDown && inViewport)
        {
            if (previousRightMouseDown && !camera.Orthographic) // 2D: sin rotación con clic derecho
            {
                var delta = mouse - lastMousePosition;
                camera.Yaw += delta.X * 0.18f;
                camera.Pitch = Math.Clamp(camera.Pitch - delta.Y * 0.18f, -89f, 89f);
                camera.UpdateFront();
            }

            lastMousePosition = mouse;
        }

        previousRightMouseDown = rightDown;
        previousMiddleMouseDown = middleDown;
        previousAltOrbitMouseDown = orbitDown;
        if (!rightDown || !inViewport) return;

        float speed = sceneCameraSpeed;
        if (KeyboardState.IsKeyDown(GlfwKeys.LeftShift) || KeyboardState.IsKeyDown(GlfwKeys.RightShift))
            speed *= 2.4f;
        if (KeyboardState.IsKeyDown(GlfwKeys.LeftControl) || KeyboardState.IsKeyDown(GlfwKeys.RightControl))
            speed *= 0.35f;
        var right = Vector3.Cross(camera.Front, camera.Up).Normalized();
        if (KeyboardState.IsKeyDown(GlfwKeys.W)) camera.Position += camera.Front * speed * dt;
        if (KeyboardState.IsKeyDown(GlfwKeys.S)) camera.Position -= camera.Front * speed * dt;
        if (KeyboardState.IsKeyDown(GlfwKeys.A)) camera.Position -= right * speed * dt;
        if (KeyboardState.IsKeyDown(GlfwKeys.D)) camera.Position += right * speed * dt;
        if (KeyboardState.IsKeyDown(GlfwKeys.Q)) camera.Position -= camera.Up * speed * dt;
        if (KeyboardState.IsKeyDown(GlfwKeys.E)) camera.Position += camera.Up * speed * dt;
    }

    private void ResetSceneCameraMouseState()
    {
        previousRightMouseDown = false;
        previousMiddleMouseDown = false;
        previousAltOrbitMouseDown = false;
    }

    private bool IsSceneViewportInputBlockedByUi()
    {
        return toolWindowMouseCapture || ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopup);
    }

    private void PanSceneCamera(Vector2 delta, float focusDistance)
    {
        float scale = Math.Max(0.0025f, focusDistance * 0.0018f);
        var right = Vector3.Cross(camera.Front, camera.Up).Normalized();
        camera.Position -= right * (delta.X * scale);
        camera.Position += camera.Up * (delta.Y * scale);
    }

    private float GetSceneCameraFocusDistance()
    {
        var target = selected != null ? GetObjectCenter(selected) : EstimateSceneCenter();
        return Math.Max(1f, Distance(camera.Position, target));
    }

    private Vector3 EstimateSceneCenter()
    {
        var all = sceneGraph.Flatten().ToList();
        if (all.Count == 0)
            return Vector3.Zero;

        return new Vector3(
            all.Average(o => o.PosX),
            all.Average(o => o.PosY),
            all.Average(o => o.PosZ));
    }

    private void FrameScene()
    {
        var all = sceneGraph.Flatten().ToList();
        if (all.Count == 0)
        {
            camera.Position = new Vector3(0, 3, 8);
            camera.SetLookDirection(new Vector3(0, -0.28f, -1f).Normalized());
            statusMessage = "Camera framed empty scene";
            return;
        }

        var center = new Vector3(
            all.Average(o => o.PosX),
            all.Average(o => o.PosY),
            all.Average(o => o.PosZ));
        camera.Position = center + new Vector3(0, 3, 8);
        camera.SetLookDirection((center - camera.Position).Normalized());
        statusMessage = "Camera framed scene";
    }

    /// <summary>
    /// Equivalente a Unity: GameObject → Align With View (Ctrl+Shift+F).
    /// Copia la pose de la cámara de Scene a la cámara que usa Game, sin alterar su lente,
    /// post-process ni otras propiedades de cámara.
    /// </summary>
    private void AlignGameCameraToSceneView()
    {
        if (isPlaying)
        {
            statusMessage = "Stop Play mode before aligning the Game Camera.";
            return;
        }

        var gameCamera = FindGameCamera(objects);
        if (gameCamera == null)
        {
            statusMessage = "No active Game Camera found to align.";
            return;
        }

        var before = CaptureSceneState();

        // Las cámaras de escena se editan en mundo. Para cámaras hijas convertimos primero
        // la posición a local respecto al padre; las cámaras creadas por el editor son raíz,
        // que mantiene el camino exacto y sin conversión.
        Vector3 targetPosition = camera.Position;
        if (gameCamera.Parent != null)
        {
            var parentPosition = gameCamera.Parent.GlobalPosition;
            targetPosition = WorldOffsetToLocalParentOffset(gameCamera, camera.Position - parentPosition);
        }

        gameCamera.PosX = targetPosition.X;
        gameCamera.PosY = targetPosition.Y;
        gameCamera.PosZ = targetPosition.Z;

        // ComputeGameCameraState usa la conversión inversa: Yaw = 90 - RotY y Pitch = -RotX.
        // La cámara de editor no tiene roll, por lo que se restablece RotZ para igualar la vista.
        gameCamera.RotX = -camera.Pitch;
        gameCamera.RotY = 90f - camera.Yaw;
        gameCamera.RotZ = 0f;

        NotifyObjectTransformChanged(gameCamera);
        PushSceneState("Align Game Camera With Scene View", before, CaptureSceneState());
        sceneDirty = true;
        selected = gameCamera;
        selection.SelectSingle(gameCamera);
        statusMessage = "Game Camera aligned with Scene view (Ctrl+Shift+F).";
    }

    private void FrameObject(GameObject obj)
    {
        var center = GetObjectCenter(obj);
        float distance = Math.Max(3f, Math.Max(Math.Max(Math.Abs(obj.ScaleX), Math.Abs(obj.ScaleY)), Math.Abs(obj.ScaleZ)) * 4f);
        camera.Position = center + new Vector3(0, distance * 0.35f, distance);
        camera.SetLookDirection((center - camera.Position).Normalized());
        statusMessage = "Camera framed " + obj.Name;
    }

    private void FrameSelection()
    {
        var targets = selection.GetTargets();
        if (targets.Count == 0)
        {
            FrameScene();
            return;
        }

        var first = GetSelectionBounds(targets[0]);
        var min = first.Min;
        var max = first.Max;
        for (int i = 1; i < targets.Count; i++)
        {
            var b = GetSelectionBounds(targets[i]);
            min = new Vector3(Math.Min(min.X, b.Min.X), Math.Min(min.Y, b.Min.Y), Math.Min(min.Z, b.Min.Z));
            max = new Vector3(Math.Max(max.X, b.Max.X), Math.Max(max.Y, b.Max.Y), Math.Max(max.Z, b.Max.Z));
        }

        var center = (min + max) * 0.5f;
        var size = max - min;
        float radius = Math.Max(1f, Math.Max(Math.Max(Math.Abs(size.X), Math.Abs(size.Y)), Math.Abs(size.Z)) * 0.5f);
        float distance = Math.Max(3f, radius * 2.8f);
        camera.Position = center + new Vector3(0, distance * 0.35f, distance);
        camera.SetLookDirection((center - camera.Position).Normalized());
        statusMessage = $"Camera framed {targets.Count} selected objects";
    }

    private int SanitizeExtremeTransforms()
    {
        int fixedCount = 0;
        int slot = 0;

        foreach (var obj in sceneGraph.Flatten())
        {
            bool extremePosition =
                !float.IsFinite(obj.PosX) || !float.IsFinite(obj.PosY) || !float.IsFinite(obj.PosZ) ||
                Math.Abs(obj.PosX) > 1000f || Math.Abs(obj.PosY) > 1000f || Math.Abs(obj.PosZ) > 1000f;
            bool invalidScale =
                !float.IsFinite(obj.ScaleX) || !float.IsFinite(obj.ScaleY) || !float.IsFinite(obj.ScaleZ) ||
                Math.Abs(obj.ScaleX) < 0.0001f || Math.Abs(obj.ScaleY) < 0.0001f || Math.Abs(obj.ScaleZ) < 0.0001f ||
                Math.Abs(obj.ScaleX) > 1000f || Math.Abs(obj.ScaleY) > 1000f || Math.Abs(obj.ScaleZ) > 1000f;

            if (extremePosition)
            {
                obj.PosX = (slot % 5) * 1.6f;
                obj.PosY = obj.Type == 1 ? 0.5f : 0f;
                obj.PosZ = (slot / 5) * 1.6f;
                fixedCount++;
            }

            if (invalidScale)
            {
                obj.ScaleX = 1f;
                obj.ScaleY = 1f;
                obj.ScaleZ = 1f;
                fixedCount++;
            }

            if (obj.GetComponent<Rigidbody>() is { } rb &&
                (!float.IsFinite(rb.Velocity.X) || !float.IsFinite(rb.Velocity.Y) || !float.IsFinite(rb.Velocity.Z) ||
                 Math.Abs(rb.Velocity.X) > 1000f || Math.Abs(rb.Velocity.Y) > 1000f || Math.Abs(rb.Velocity.Z) > 1000f))
            {
                rb.Velocity = Vector3.Zero;
            }

            slot++;
        }

        return fixedCount;
    }

    private void HandleViewportSelection()
    {
        if (isPlaying) return;
        if (IsSceneViewportInputBlockedByUi())
        {
            previousLeftMouseDown = MouseState.IsButtonDown(GlfwMouseButton.Left);
            selectionBoxActive = false;
            return;
        }

        bool leftDown = MouseState.IsButtonDown(GlfwMouseButton.Left);
        var mouse = new Vector2(MouseState.X, MouseState.Y);
        bool pressed = leftDown && !previousLeftMouseDown;
        bool released = !leftDown && previousLeftMouseDown;
        previousLeftMouseDown = leftDown;

        // Graphic Raycaster: la UI bloquea selección/caja del viewport cuando el puntero está encima.
        if (UIRaycast.PointerOverUI && IsMouseInsideViewport(mouse.X, mouse.Y))
        {
            selectionBoxActive = false;
            return;
        }

        bool altOrbit = (KeyboardState.IsKeyDown(GlfwKeys.LeftAlt) || KeyboardState.IsKeyDown(GlfwKeys.RightAlt)) && leftDown;
        if (altOrbit || MouseState.IsButtonDown(GlfwMouseButton.Middle))
        {
            selectionBoxActive = false;
            return;
        }

        // Mientras se arrastra/hover un gizmo de transform O un handle de edición de collider O el view gizmo
        // (cubo de ejes), no iniciar selección ni caja de selección.
        if (gizmoDragging || gizmoMouseCaptured || colliderEditDragging || colliderEditMouseCaptured || viewGizmoMouseCaptured)
            return;

        // Editando un elemento de UI con el Rect Tool (mover/redimensionar desde el rect o un handle):
        // ese arrastre es para el propio elemento, no para (re)seleccionar. Sin esto, arrastrar desde
        // un handle que cae FUERA del rect no activaba PointerOverUI y el rubber-band deseleccionaba
        // el elemento al soltar (se sentía como si el click arrastrara la selección).
        if (activeUiRectToolObject != null)
        {
            selectionBoxActive = false;
            return;
        }

        // Si hay un Terrain seleccionado y el click cae sobre su plano, ese click es para el
        // pincel de sculpt/paint/árboles/detalles (HandleTerrainSculpting, que corre DESPUÉS en
        // el mismo frame), no para (re)seleccionar. Sin esto, soltar el mouse tras cada trazo
        // volvía a correr el picking normal de acá abajo y deseleccionaba el Terrain (o
        // seleccionaba lo que hubiera debajo), justo cuando se estaba por terminar el trazo.
        if (selected?.GetComponent<Terrain>() is { } selectedTerrainForClick && (leftDown || released) &&
            TryGetTerrainBrushHit(selectedTerrainForClick, mouse, out _, out _))
        {
            selectionBoxActive = false;
            return;
        }

        if (pressed)
        {
            selectionBoxActive = false;
            if (IsMouseOverSelectedGizmo(mouse)) return;
            if (!IsMouseInsideViewport(mouse.X, mouse.Y)) return;

            selectionBoxActive = true;
            selectionBoxStart = mouse;
            selectionBoxEnd = mouse;
            return;
        }

        if (selectionBoxActive && leftDown)
        {
            selectionBoxEnd = mouse;
            return;
        }

        if (!selectionBoxActive || !released)
            return;

        selectionBoxActive = false;
        selectionBoxEnd = mouse;

        var selectionMode = GetViewportSelectionMode(boxSelection: false);

        if ((selectionBoxEnd - selectionBoxStart).Length() >= 6f)
        {
            SelectObjectsInBox(selectionBoxStart, selectionBoxEnd, GetViewportSelectionMode(boxSelection: true));
            return;
        }

        if (!IsMouseInsideViewport(mouse.X, mouse.Y))
            return;

        var localMouse = new Vector2(mouse.X - viewportContentMin.X, mouse.Y - viewportContentMin.Y);
        var hit = PickObjectAt(localMouse.X, localMouse.Y);
        selectedAssetPath = null;
        if (hit != null)
            selection.Select(hit, selectionMode);
        else if (selectionMode == SelectionService.SelectionMode.Replace)
            selection.Clear();
    }

    private SelectionService.SelectionMode GetViewportSelectionMode(bool boxSelection)
    {
        bool ctrl = KeyboardState.IsKeyDown(GlfwKeys.LeftControl) || KeyboardState.IsKeyDown(GlfwKeys.RightControl);
        bool shift = KeyboardState.IsKeyDown(GlfwKeys.LeftShift) || KeyboardState.IsKeyDown(GlfwKeys.RightShift);
        if (ctrl) return boxSelection ? SelectionService.SelectionMode.Remove : SelectionService.SelectionMode.Toggle;
        if (shift) return SelectionService.SelectionMode.Add;
        return SelectionService.SelectionMode.Replace;
    }

    private void DrawSelectionBoxOverlay(ImDrawListPtr drawList)
    {
        if (!selectionBoxActive || (selectionBoxEnd - selectionBoxStart).Length() < 6f)
            return;

        var min = new Vector2(Math.Min(selectionBoxStart.X, selectionBoxEnd.X), Math.Min(selectionBoxStart.Y, selectionBoxEnd.Y));
        var max = new Vector2(Math.Max(selectionBoxStart.X, selectionBoxEnd.X), Math.Max(selectionBoxStart.Y, selectionBoxEnd.Y));
        min = ClampToViewport(min);
        max = ClampToViewport(max);

        var mode = GetViewportSelectionMode(boxSelection: true);
        System.Numerics.Vector4 fillColor = mode switch
        {
            SelectionService.SelectionMode.Add => new System.Numerics.Vector4(0.25f, 0.75f, 0.42f, 0.18f),
            SelectionService.SelectionMode.Remove => new System.Numerics.Vector4(0.95f, 0.35f, 0.30f, 0.16f),
            _ => new System.Numerics.Vector4(0.25f, 0.55f, 0.95f, 0.18f)
        };
        System.Numerics.Vector4 outlineColor = mode switch
        {
            SelectionService.SelectionMode.Add => new System.Numerics.Vector4(0.42f, 0.95f, 0.58f, 0.95f),
            SelectionService.SelectionMode.Remove => new System.Numerics.Vector4(1.00f, 0.48f, 0.42f, 0.95f),
            _ => new System.Numerics.Vector4(0.38f, 0.68f, 1f, 0.95f)
        };
        uint fill = ImGui.GetColorU32(fillColor);
        uint outline = ImGui.GetColorU32(outlineColor);
        drawList.AddRectFilled(min, max, fill);
        drawList.AddRect(min, max, outline, 0f, ImDrawFlags.None, 1.4f);
    }

    private void DrawSelectedObjectBounds(ImDrawListPtr drawList)
    {
        if (gameViewActive || isPlaying || viewportContentSize.X <= 1f || viewportContentSize.Y <= 1f)
            return;

        var targets = selection.GetTargets();
        if (targets.Count == 0)
            return;

        uint primary = ImGui.GetColorU32(new System.Numerics.Vector4(1.00f, 0.74f, 0.22f, 0.95f));
        uint secondary = ImGui.GetColorU32(new System.Numerics.Vector4(0.42f, 0.70f, 1.00f, 0.70f));
        uint fill = ImGui.GetColorU32(new System.Numerics.Vector4(0.18f, 0.42f, 0.85f, 0.06f));
        int activeIndex = selected != null ? targets.FindIndex(o => o.EditorId == selected.EditorId) : targets.Count - 1;
        if (activeIndex < 0) activeIndex = targets.Count - 1;

        for (int i = 0; i < targets.Count; i++)
        {
            var obj = targets[i];
            var bounds = GetSelectionBounds(obj);
            Vector2[] points = new Vector2[8];
            int pointIndex = 0;
            bool projected = true;
            foreach (var corner in bounds.Corners())
            {
                if (!TryProject(corner, out var local))
                {
                    projected = false;
                    break;
                }
                points[pointIndex++] = viewportContentMin + local;
            }
            if (!projected || pointIndex != 8)
                continue;

            uint color = i == activeIndex ? primary : secondary;
            DrawProjectedBox(drawList, points, color, i == activeIndex ? 1.7f : 1.1f);

            Vector2 screenMin = points[0];
            Vector2 screenMax = points[0];
            for (int p = 1; p < points.Length; p++)
            {
                screenMin = new Vector2(Math.Min(screenMin.X, points[p].X), Math.Min(screenMin.Y, points[p].Y));
                screenMax = new Vector2(Math.Max(screenMax.X, points[p].X), Math.Max(screenMax.Y, points[p].Y));
            }
            screenMin = ClampToViewport(screenMin);
            screenMax = ClampToViewport(screenMax);
            if (screenMax.X > screenMin.X && screenMax.Y > screenMin.Y)
                drawList.AddRectFilled(screenMin, screenMax, fill, 0f);
        }
    }

    private static void DrawProjectedBox(ImDrawListPtr drawList, ReadOnlySpan<Vector2> p, uint color, float thickness)
    {
        ReadOnlySpan<int> edges = stackalloc int[]
        {
            0, 1, 0, 2, 1, 3, 2, 3,
            4, 5, 4, 6, 5, 7, 6, 7,
            0, 4, 1, 5, 2, 6, 3, 7
        };

        for (int i = 0; i < edges.Length; i += 2)
            drawList.AddLine(p[edges[i]], p[edges[i + 1]], color, thickness);
    }

    private Vector2 ClampToViewport(Vector2 point) =>
        new(
            Math.Clamp(point.X, viewportContentMin.X, viewportContentMin.X + viewportContentSize.X),
            Math.Clamp(point.Y, viewportContentMin.Y, viewportContentMin.Y + viewportContentSize.Y));

    private bool IsMouseOverSelectedGizmo(Vector2 mouse)
    {
        if (selected == null || !IsMouseInsideViewport(mouse.X, mouse.Y)) return false;
        if (!TryProject(GetObjectCenter(selected), out var originLocal)) return false;

        var originScreen = viewportContentMin + originLocal;
        float axisLength = Math.Clamp(viewportContentSize.Y * 0.12f, 54f, 112f);
        var axes = new[] { new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1) };
        var center = GetObjectCenter(selected);
        Vector2[][]? rotationRings = null;
        if (currentTool == TransformTool.Rotate)
        {
            rotationRings = new Vector2[3][];
            float radius = GetRotationGizmoRadius(center);
            for (int i = 0; i < axes.Length; i++)
                rotationRings[i] = BuildRotationRing(center, axes[i], radius);
        }

        for (int i = 0; i < axes.Length; i++)
        {
            var end = GetAxisScreenEnd(selected, axes[i], originScreen, axisLength);
            float distance = rotationRings != null
                ? DistancePointToPolyline(mouse, rotationRings[i], closed: true)
                : DistancePointToSegment(mouse, originScreen, end);
            if (distance <= GizmoHitRadius)
                return true;
        }

        return false;
    }

    private bool IsMouseInsideViewport(float x, float y)
    {
        if (!viewportReady) return true;
        return x >= viewportContentMin.X && x <= viewportContentMin.X + viewportContentSize.X &&
               y >= viewportContentMin.Y && y <= viewportContentMin.Y + viewportContentSize.Y;
    }

    private GameObject? PickObjectAt(float mouseX, float mouseY)
    {
        GameObject? best = null;
        float bestHit = float.MaxValue;
        GameObject? bestBox = null;
        float bestBoxHit = float.MaxValue;

        if (!BuildCameraRay(mouseX, mouseY, out var rayOrigin, out var rayDirection))
            return null;

        foreach (var obj in sceneGraph.Flatten())
        {
            if (!obj.IsActive) continue;
            var box = GetSelectionBounds(obj);
            if (!RayIntersectsBox(rayOrigin, rayDirection, box, out float boxHit))
                continue;

            if (boxHit < bestBoxHit)
            {
                bestBoxHit = boxHit;
                bestBox = obj;
            }

            if (TryPickMeshTriangle(obj, rayOrigin, rayDirection, out float meshHit) && meshHit < bestHit)
            {
                bestHit = meshHit;
                best = obj;
            }
        }

        return best ?? bestBox;
    }

    private static bool TryPickMeshTriangle(GameObject obj, Vector3 rayOrigin, Vector3 rayDirection, out float hit)
    {
        hit = float.MaxValue;
        if (obj.GetComponent<MeshFilter>() is not { } mf ||
            string.IsNullOrWhiteSpace(mf.MeshPath) ||
            ObjLoader.Load(mf.MeshPath) is not { } mesh ||
            mesh.Positions.Length < 9)
            return false;

        int vertexStart = 0;
        int vertexEnd = mesh.Positions.Length / 3;
        if (mf.SubmeshIndex >= 0 && mf.SubmeshIndex < mesh.Submeshes.Count)
        {
            var sub = mesh.Submeshes[mf.SubmeshIndex];
            vertexStart = Math.Clamp(sub.VertexStart, 0, vertexEnd);
            vertexEnd = Math.Clamp(sub.VertexStart + sub.VertexCount, vertexStart, vertexEnd);
        }

        // Los meshes del motor están expandidos por triángulo: cada 3 vértices forman una cara.
        vertexStart -= vertexStart % 3;
        vertexEnd -= vertexEnd % 3;
        if (vertexEnd <= vertexStart)
            return false;

        var wm = obj.WorldMatrix;
        float importScale = Math.Max(0.0001f, mf.ImportScale);
        bool found = false;
        for (int vertex = vertexStart; vertex + 2 < vertexEnd; vertex += 3)
        {
            var a = MeshVertexToWorld(mesh.Positions, vertex, importScale, wm);
            var b = MeshVertexToWorld(mesh.Positions, vertex + 1, importScale, wm);
            var c = MeshVertexToWorld(mesh.Positions, vertex + 2, importScale, wm);
            if (RayIntersectsTriangle(rayOrigin, rayDirection, a, b, c, out float t) && t < hit)
            {
                hit = t;
                found = true;
            }
        }

        return found;
    }

    private static Vector3 MeshVertexToWorld(float[] positions, int vertex, float importScale, System.Numerics.Matrix4x4 world)
    {
        int i = vertex * 3;
        var local = new System.Numerics.Vector3(
            positions[i] * importScale,
            positions[i + 1] * importScale,
            positions[i + 2] * importScale);
        var wp = System.Numerics.Vector3.Transform(local, world);
        return new Vector3(wp.X, wp.Y, wp.Z);
    }

    private static bool RayIntersectsTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float hit)
    {
        hit = 0f;
        const float epsilon = 0.000001f;
        var edge1 = b - a;
        var edge2 = c - a;
        var p = Vector3.Cross(direction, edge2);
        float det = Dot(edge1, p);
        if (Math.Abs(det) < epsilon)
            return false;

        float invDet = 1f / det;
        var tvec = origin - a;
        float u = Dot(tvec, p) * invDet;
        if (u < 0f || u > 1f)
            return false;

        var q = Vector3.Cross(tvec, edge1);
        float v = Dot(direction, q) * invDet;
        if (v < 0f || u + v > 1f)
            return false;

        hit = Dot(edge2, q) * invDet;
        return hit > epsilon && float.IsFinite(hit);
    }

    private void SelectObjectsInBox(Vector2 startScreen, Vector2 endScreen, SelectionService.SelectionMode mode)
    {
        var min = new Vector2(Math.Min(startScreen.X, endScreen.X), Math.Min(startScreen.Y, endScreen.Y));
        var max = new Vector2(Math.Max(startScreen.X, endScreen.X), Math.Max(startScreen.Y, endScreen.Y));
        min -= viewportContentMin;
        max -= viewportContentMin;

        var hits = new List<GameObject>();

        foreach (var obj in sceneGraph.Flatten())
        {
            if (!obj.IsActive) continue;
            if (ObjectOverlapsScreenBox(obj, min, max))
                hits.Add(obj);
        }

        selection.SelectMany(hits, mode);
        statusMessage = selection.Selected.Count == 0
            ? "Selection box found nothing"
            : $"{SelectionModeStatus(mode)} {hits.Count} object(s), total {selection.Selected.Count}";
    }

    private static string SelectionModeStatus(SelectionService.SelectionMode mode) => mode switch
    {
        SelectionService.SelectionMode.Add => "Added",
        SelectionService.SelectionMode.Toggle => "Toggled",
        SelectionService.SelectionMode.Remove => "Removed",
        _ => "Selected"
    };

    private bool ObjectOverlapsScreenBox(GameObject obj, Vector2 min, Vector2 max)
    {
        var bounds = GetSelectionBounds(obj);
        foreach (var corner in bounds.Corners())
        {
            if (!TryProject(corner, out var screen)) continue;
            if (screen.X >= min.X && screen.X <= max.X && screen.Y >= min.Y && screen.Y <= max.Y)
                return true;
        }

        if (TryProject(bounds.Center, out var center))
            return center.X >= min.X && center.X <= max.X && center.Y >= min.Y && center.Y <= max.Y;

        return false;
    }

    private bool BuildCameraRay(float mouseX, float mouseY, out Vector3 origin, out Vector3 direction)
    {
        origin = camera.Position;
        direction = camera.Front;
        if (viewportContentSize.X <= 1f || viewportContentSize.Y <= 1f)
            return false;

        float aspect = viewportContentSize.X / Math.Max(1f, viewportContentSize.Y);
        float tan = MathF.Tan(MathHelper.DegreesToRadians(camera.FOV) * 0.5f);
        float ndcX = mouseX / viewportContentSize.X * 2f - 1f;
        float ndcY = 1f - mouseY / viewportContentSize.Y * 2f;
        var right = Vector3.Cross(camera.Front, camera.Up).Normalized();
        direction = (camera.Front + right * (ndcX * aspect * tan) + camera.Up * (ndcY * tan)).Normalized();
        return true;
    }

    private SelectionBounds GetSelectionBounds(GameObject obj)
    {
        Vector3 center = GetObjectCenter(obj);
        Vector3 half;

        if (obj.GetComponent<Collider>() is { } collider)
        {
            var bounds = collider.GetBounds();
            center = (bounds.Min + bounds.Max) * 0.5f;
            half = new Vector3(
                Math.Max(0.05f, Math.Abs(bounds.Max.X - bounds.Min.X) * 0.5f),
                Math.Max(0.05f, Math.Abs(bounds.Max.Y - bounds.Min.Y) * 0.5f),
                Math.Max(0.05f, Math.Abs(bounds.Max.Z - bounds.Min.Z) * 0.5f));
            return new SelectionBounds(center - half, center + half);
        }

        if (obj.GetComponent<MeshFilter>() is { } mf &&
            !string.IsNullOrWhiteSpace(mf.MeshPath) &&
            ObjLoader.Load(mf.MeshPath) is { } mesh)
        {
            float importScale = Math.Max(0.0001f, mf.ImportScale);
            Vector3 size;
            float scX, scY, scZ;
            // Hijo de un FBX "con hijos": usar el tamaño de SU parte y la escala mundial (hereda la de la raíz).
            if (mf.SubmeshIndex >= 0 && mf.SubmeshIndex < mesh.Submeshes.Count)
            {
                var sb = mesh.Submeshes[mf.SubmeshIndex];
                size = new Vector3(sb.MaxX - sb.MinX, sb.MaxY - sb.MinY, sb.MaxZ - sb.MinZ);
                var ws = obj.WorldScale; scX = ws.X; scY = ws.Y; scZ = ws.Z;
            }
            else
            {
                size = mesh.BoundsMax - mesh.BoundsMin;
                scX = obj.ScaleX; scY = obj.ScaleY; scZ = obj.ScaleZ;
            }
            half = new Vector3(
                Math.Max(0.05f, Math.Abs(size.X * importScale * scX) * 0.5f),
                Math.Max(0.05f, Math.Abs(size.Y * importScale * scY) * 0.5f),
                Math.Max(0.05f, Math.Abs(size.Z * importScale * scZ) * 0.5f));
            return new SelectionBounds(center - half, center + half);
        }

        if (obj.Type == 1)
            half = new Vector3(Math.Max(0.05f, Math.Abs(obj.ScaleX) * 0.5f), Math.Max(0.05f, Math.Abs(obj.ScaleY) * 0.5f), Math.Max(0.05f, Math.Abs(obj.ScaleZ) * 0.5f));
        else if (obj.Type == 2)
            half = new Vector3(Math.Max(0.05f, Math.Abs(obj.ScaleX) * 2f), 0.05f, Math.Max(0.05f, Math.Abs(obj.ScaleZ) * 2f));
        else if (obj.Type is 3 or 4 or 5 or 6)
            half = obj.Type switch
            {
                5 => new Vector3(Math.Max(0.05f, Math.Abs(obj.ScaleX) * 0.5f), Math.Max(0.05f, Math.Abs(obj.ScaleY) * 0.5f), 0.05f),
                4 or 6 => new Vector3(Math.Max(0.05f, Math.Abs(obj.ScaleX) * 0.5f), Math.Max(0.05f, Math.Abs(obj.ScaleY) * 1f), Math.Max(0.05f, Math.Abs(obj.ScaleZ) * 0.5f)),
                _ => new Vector3(Math.Max(0.05f, Math.Abs(obj.ScaleX) * 0.5f), Math.Max(0.05f, Math.Abs(obj.ScaleY) * 0.5f), Math.Max(0.05f, Math.Abs(obj.ScaleZ) * 0.5f))
            };
        else
            half = new Vector3(0.28f, 0.28f, 0.28f);

        return new SelectionBounds(center - half, center + half);
    }

    private static bool RayIntersectsBox(Vector3 origin, Vector3 direction, SelectionBounds box, out float hit)
    {
        hit = 0f;
        float tmin = 0f;
        float tmax = 10000f;
        if (!Slab(origin.X, direction.X, box.Min.X, box.Max.X, ref tmin, ref tmax)) return false;
        if (!Slab(origin.Y, direction.Y, box.Min.Y, box.Max.Y, ref tmin, ref tmax)) return false;
        if (!Slab(origin.Z, direction.Z, box.Min.Z, box.Max.Z, ref tmin, ref tmax)) return false;
        hit = tmin;
        return true;
    }

    private static bool Slab(float origin, float direction, float min, float max, ref float tmin, ref float tmax)
    {
        if (Math.Abs(direction) < 0.00001f)
            return origin >= min && origin <= max;

        float inv = 1f / direction;
        float t1 = (min - origin) * inv;
        float t2 = (max - origin) * inv;
        if (t1 > t2) (t1, t2) = (t2, t1);
        tmin = Math.Max(tmin, t1);
        tmax = Math.Min(tmax, t2);
        return tmin <= tmax;
    }

    private bool TryProject(Vector3 world, out Vector2 screen)
    {
        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(camera.FOV),
            viewportContentSize.X / Math.Max(1f, viewportContentSize.Y),
            Math.Max(0.001f, camera.NearClip),
            Math.Max(camera.NearClip + 0.001f, camera.FarClip));

        var eye = ToTk(camera.Position);
        Matrix4 view = Matrix4.LookAt(eye, eye + ToTk(camera.Front), ToTk(camera.Up));
        var clip = Vector4.TransformRow(new Vector4(world.X, world.Y, world.Z, 1f), view * projection);
        if (clip.W <= 0.0001f)
        {
            screen = default;
            return false;
        }

        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        if (ndcX < -1.2f || ndcX > 1.2f || ndcY < -1.2f || ndcY > 1.2f)
        {
            screen = default;
            return false;
        }

        screen = new Vector2(
            (ndcX * 0.5f + 0.5f) * viewportContentSize.X,
            (0.5f - ndcY * 0.5f) * viewportContentSize.Y);
        return true;
    }

    private static Vector3 GetObjectCenter(GameObject obj)
    {
        var worldPos = obj.GlobalPosition;

        if (obj.GetComponent<MeshFilter>() is { } mf &&
            !string.IsNullOrWhiteSpace(mf.MeshPath) &&
            ObjLoader.Load(mf.MeshPath) is { } mesh)
        {
            float importScale = Math.Max(0.0001f, mf.ImportScale);
            // Hijo de un FBX "con hijos": el centro es el de SU parte (con la escala mundial heredada).
            if (mf.SubmeshIndex >= 0 && mf.SubmeshIndex < mesh.Submeshes.Count)
            {
                var sb = mesh.Submeshes[mf.SubmeshIndex];
                var ws = obj.WorldScale;
                return new Vector3(
                    worldPos.X + (sb.MinX + sb.MaxX) * 0.5f * importScale * ws.X,
                    worldPos.Y + (sb.MinY + sb.MaxY) * 0.5f * importScale * ws.Y,
                    worldPos.Z + (sb.MinZ + sb.MaxZ) * 0.5f * importScale * ws.Z);
            }
            var meshCenter = (mesh.BoundsMin + mesh.BoundsMax) * 0.5f * importScale;
            return new Vector3(
                worldPos.X + meshCenter.X * obj.ScaleX,
                worldPos.Y + meshCenter.Y * obj.ScaleY,
                worldPos.Z + meshCenter.Z * obj.ScaleZ);
        }

        return worldPos;
    }

    private static OpenTK.Mathematics.Vector3 ToTk(Vector3 value) =>
        new(value.X, value.Y, value.Z);
}
