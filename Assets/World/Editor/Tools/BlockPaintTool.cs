using System;
using System.Collections.Generic;
using MF2.World.Core;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MF2.World.Editor.Tools
{
    [EditorTool("Block Paint Tool")]
    public sealed class BlockPaintTool : EditorTool
    {
        private const int ControlHint = 0x4D463257;
        private const float MaxRayDistance = 512f;

        private readonly HashSet<int3> visited = new HashSet<int3>();
        private readonly List<int3> interpolationBuffer = new List<int3>();
        private GUIContent icon;
        private WorldHost strokeHost;
        private BlockEditMode strokeMode;
        private Block strokeBlock;
        private int3 lastStrokeCell;
        private bool hasLastStrokeCell;
        private bool hasPendingCommands;
        private int activeControlId;
        private int3 hoverCell;
        private bool hasHoverCell;
        private string hoverMessage;

        public override GUIContent toolbarIcon
        {
            get
            {
                if (icon != null)
                    return icon;
                var builtIn = EditorGUIUtility.IconContent("Grid.PaintTool");
                icon = builtIn != null && builtIn.image != null
                    ? new GUIContent(builtIn.image, "Block Paint Tool")
                    : new GUIContent("W", "Block Paint Tool");
                return icon;
            }
        }

        [MenuItem("Tools/World/Activate Block Paint Tool")]
        private static void ActivateFromMenu()
        {
            ToolManager.SetActiveTool<BlockPaintTool>();
        }

        public override void OnActivated()
        {
            BlockToolState.Changed += RepaintLastSceneView;
            RepaintLastSceneView();
        }

        public override void OnWillBeDeactivated()
        {
            BlockToolState.Changed -= RepaintLastSceneView;
            EndStroke();
            ReleaseMouseControl();
            hasHoverCell = false;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView))
                return;

            var currentEvent = Event.current;
            var controlId = GUIUtility.GetControlID(ControlHint, FocusType.Passive);
            var host = ResolveHost();
            var ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
            var validTarget = TryResolveTarget(
                host,
                ray,
                BlockToolState.Mode,
                out var target,
                out var message);

            UpdateHover(sceneView, validTarget, target, message);
            if (currentEvent.type == EventType.Repaint)
                DrawGhostAndStatus(host, validTarget, target, message);

            if (IsNavigationEvent(currentEvent))
            {
                if (activeControlId != 0)
                {
                    EndStroke();
                    ReleaseMouseControl();
                }
                return;
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
            {
                if (!validTarget || host == null)
                    return;

                if (BlockToolState.Mode == BlockEditMode.Pick)
                {
                    Pick(host, target);
                    currentEvent.Use();
                    return;
                }

                BeginStroke(host, target, controlId);
                currentEvent.Use();
                return;
            }

            if (activeControlId == 0 || GUIUtility.hotControl != activeControlId)
                return;

            if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
            {
                if (TryResolveTarget(
                        strokeHost,
                        ray,
                        strokeMode,
                        out var dragTarget,
                        out _))
                {
                    AppendStrokeTo(dragTarget);
                }
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0)
            {
                if (TryResolveTarget(
                        strokeHost,
                        ray,
                        strokeMode,
                        out var mouseUpTarget,
                        out _))
                {
                    AppendStrokeTo(mouseUpTarget);
                }
                EndStroke();
                ReleaseMouseControl();
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseLeaveWindow ||
                currentEvent.type == EventType.Ignore)
            {
                EndStroke();
                ReleaseMouseControl();
                currentEvent.Use();
            }
        }

        internal static bool IsNavigationEvent(Event currentEvent)
        {
            if (currentEvent == null)
                return false;
            if (currentEvent.alt || currentEvent.type == EventType.ScrollWheel)
                return true;
            if (!currentEvent.isMouse)
                return false;
            return currentEvent.button == 1 || currentEvent.button == 2;
        }

        internal static bool TryResolveTarget(
            WorldHost host,
            Ray ray,
            BlockEditMode mode,
            out int3 target,
            out string message)
        {
            target = default;
            if (EditorApplication.isPlaying)
            {
                message = "Block editing is disabled in Play Mode.";
                return false;
            }
            if (host == null)
            {
                message = "No active World Host. Create one from the Block Palette.";
                return false;
            }
            if (!host.IsGridTransformValid)
            {
                message = "World Host rotation must be identity and scale must be (1,1,1).";
                return false;
            }
            if (!host.HasEditorAssets)
            {
                message = "World Host assets are missing. Assign defaults from the Block Palette.";
                return false;
            }
            if (mode == BlockEditMode.Paint && !BlockToolState.HasSelection)
            {
                message = "Select a block in the Block Palette.";
                return false;
            }

            if (VoxelEditingGeometry.TryRaycast(host, ray, MaxRayDistance, out var hit))
            {
                target = mode == BlockEditMode.Paint ? hit.Position + hit.Normal : hit.Position;
                message = mode == BlockEditMode.Paint
                    ? "Paint adjacent to voxel face"
                    : mode == BlockEditMode.Erase ? "Erase voxel" : "Pick voxel";
                return true;
            }

            if (mode == BlockEditMode.Paint &&
                VoxelEditingGeometry.TryIntersectWorkPlane(
                    host,
                    ray,
                    BlockToolState.PlaneAxis,
                    BlockToolState.PlaneOffset,
                    MaxRayDistance,
                    out target))
            {
                message = $"Paint on {BlockToolState.PlaneAxis} plane " +
                          $"at {BlockToolState.PlaneOffset}";
                return true;
            }

            message = mode == BlockEditMode.Paint
                ? "Ray does not intersect the configured work plane."
                : "Point at an existing voxel.";
            return false;
        }

        private static WorldHost ResolveHost()
        {
            var selected = Selection.activeGameObject;
            if (selected != null)
            {
                var selectedHost = selected.GetComponentInParent<WorldHost>();
                if (selectedHost != null && selectedHost.isActiveAndEnabled)
                    return selectedHost;
            }

            return WorldHost.TryGetActiveHost(out var host) ? host : null;
        }

        internal void BeginStroke(
            WorldHost host,
            int3 target,
            int controlId,
            bool captureMouse = true)
        {
            EndStroke();
            strokeHost = host;
            strokeMode = BlockToolState.Mode;
            strokeBlock = BlockToolState.SelectedBlock;
            visited.Clear();
            interpolationBuffer.Clear();
            hasLastStrokeCell = false;
            hasPendingCommands = false;
            activeControlId = captureMouse ? controlId : 0;
            if (captureMouse)
                GUIUtility.hotControl = controlId;
            AppendStrokeTo(target);
        }

        internal void AppendStrokeTo(int3 target)
        {
            if (strokeHost == null)
                return;

            if (!hasLastStrokeCell)
            {
                EnqueueCell(target);
                lastStrokeCell = target;
                hasLastStrokeCell = true;
                return;
            }

            if (!GridStrokeRasterizer.TryFill(lastStrokeCell, target, interpolationBuffer))
            {
                hoverMessage = $"Stroke segment exceeds {GridStrokeRasterizer.MaxCellsPerSegment} cells.";
                return;
            }

            for (var i = 0; i < interpolationBuffer.Count; i++)
                EnqueueCell(interpolationBuffer[i]);
            lastStrokeCell = target;
        }

        private void EnqueueCell(int3 position)
        {
            if (!visited.Add(position))
                return;

            if (strokeMode == BlockEditMode.Paint)
                strokeHost.Enqueue(WorldCommand.SetBlock(position, strokeBlock));
            else
                strokeHost.Enqueue(WorldCommand.RemoveBlock(position));
            hasPendingCommands = true;
        }

        private void EndStroke()
        {
            try
            {
                if (hasPendingCommands && strokeHost != null)
                {
                    var flushResult = strokeHost.Flush();
                    if (flushResult.ChangedCells > 0)
                    {
                        EditorUtility.SetDirty(strokeHost);
                        var scene = strokeHost.gameObject.scene;
                        if (scene.IsValid())
                            EditorSceneManager.MarkSceneDirty(scene);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                strokeHost = null;
                visited.Clear();
                interpolationBuffer.Clear();
                hasLastStrokeCell = false;
                hasPendingCommands = false;
            }
        }

        internal static void Pick(WorldHost host, int3 position)
        {
            var block = host.GetBlockOrAir(position);
            if (block.IsAir)
                return;
            BlockToolState.SelectById(host.BlockRegistryManifest, block.Id);
        }

        private void ReleaseMouseControl()
        {
            if (activeControlId != 0 && GUIUtility.hotControl == activeControlId)
                GUIUtility.hotControl = 0;
            activeControlId = 0;
        }

        private void UpdateHover(
            SceneView sceneView,
            bool valid,
            int3 target,
            string message)
        {
            if (valid == hasHoverCell &&
                (!valid || target.Equals(hoverCell)) &&
                string.Equals(message, hoverMessage, StringComparison.Ordinal))
            {
                return;
            }

            hasHoverCell = valid;
            hoverCell = target;
            hoverMessage = message;
            sceneView.Repaint();
        }

        private static void DrawGhostAndStatus(
            WorldHost host,
            bool valid,
            int3 target,
            string message)
        {
            if (valid && host != null)
            {
                var previousMatrix = Handles.matrix;
                var previousColor = Handles.color;
                var previousZTest = Handles.zTest;
                Handles.matrix = host.transform.localToWorldMatrix;
                Handles.color = BlockToolState.Mode == BlockEditMode.Paint
                    ? new Color(0.2f, 1f, 0.35f, 0.95f)
                    : BlockToolState.Mode == BlockEditMode.Erase
                        ? new Color(1f, 0.25f, 0.2f, 0.95f)
                        : new Color(0.2f, 0.85f, 1f, 0.95f);
                Handles.zTest = CompareFunction.LessEqual;
                Handles.DrawWireCube(
                    new Vector3(target.x, target.y, target.z),
                    Vector3.one);
                Handles.matrix = previousMatrix;
                Handles.color = previousColor;
                Handles.zTest = previousZTest;
            }

            Handles.BeginGUI();
            var status = valid
                ? $"{BlockToolState.Mode}: ({target.x}, {target.y}, {target.z}) — {message}"
                : message;
            var previous = GUI.color;
            if (!valid)
                GUI.color = new Color(1f, 0.65f, 0.65f, 1f);
            GUI.Label(new Rect(8f, 8f, 460f, 22f), status, EditorStyles.helpBox);
            GUI.color = previous;
            Handles.EndGUI();
        }

        private static void RepaintLastSceneView()
        {
            SceneView.lastActiveSceneView?.Repaint();
        }
    }
}
