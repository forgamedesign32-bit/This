using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TwomAi.EditorTools
{
    // Tools > TWOM AI > Build Prototype Scene
    // Four-floor building cross-section (open front, camera on -Z), TWOM-style side view.
    // Built through Editor / SerializedObject APIs only.
    public static class PrototypeSceneBuilder
    {
        private const string RootFolder = "Assets/TwomAi";
        private const string ScenePath = RootFolder + "/Scenes/TwomAiPrototype.unity";
        private const string ConfigPath = RootFolder + "/Config/AiTuningConfig.asset";
        private const string MaterialFolder = RootFolder + "/Materials";
        private const string AnimationFolder = RootFolder + "/Animations";
        private const string ControllerPath = AnimationFolder + "/TwomCharacter.controller";

        // (trigger / state name, Mixamo FBX file name, loop, seconds before blending back to locomotion)
        private static readonly (string state, string file, bool loop, float seconds)[] ClipTable =
        {
            ("Attack", "Punching", false, 0.8f),
            ("AttackAlt", "Punching Bag", false, 0.9f),
            ("Kick", "Kicking", false, 1.1f),
            ("HitReact", "Center Block", false, 0.6f),
            ("Block", "Block", false, 0.7f),
            ("Dodge", "Dodging", false, 0.9f),
            ("Die", "Dying", false, 0f),
            ("Cower", "Praying", false, 0f),
            ("Stagger", "Stepping Backward", false, 0.7f),
            ("Fire", "Firing Rifle", false, 0.45f),
            ("AimWalk", "Rifle Walk", true, 0f),
        };

        private const float FloorHeight = 3.2f;
        private const float SlabThickness = 0.2f;
        private const float Depth = 3f;
        private const float WallThickness = 0.25f;
        private const float DoorWidth = 1.6f;
        private const float DoorHeight = 2.2f;
        private const float BuildingMinX = -14f;
        private const float BuildingMaxX = 14f;
        private const int FloorCount = 4;
        // Zig-zag stairs: left flight rises to the right (A: 0->1, C: 2->3), right flight rises to the left (B: 1->2).
        private const float StairLeftLowX = -12.5f;
        private const float StairLeftHighX = -7f;
        private const float StairRightLowX = 12.5f;
        private const float StairRightHighX = 7f;

        private static readonly Dictionary<Waypoint, List<Waypoint>> PendingLinks = new Dictionary<Waypoint, List<Waypoint>>();

        private static Material _wallMaterial;
        private static Material _slabMaterial;
        private static Material _fogMaterial;
        private static RuntimeAnimatorController _controller;

        [MenuItem("Tools/TWOM AI/Build Prototype Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolder(RootFolder + "/Scenes");
            EnsureFolder(RootFolder + "/Config");
            EnsureFolder(MaterialFolder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            PendingLinks.Clear();

            var config = LoadOrCreateConfig();
            _controller = BuildAnimatorController(PrepareClips());
            _slabMaterial = GetMaterial("Slab", new Color(0.2f, 0.22f, 0.25f));
            _wallMaterial = GetMaterial("Wall", new Color(0.33f, 0.36f, 0.4f));
            var backMaterial = GetMaterial("BackWall", new Color(0.26f, 0.29f, 0.33f));
            var groundMaterial = GetMaterial("Ground", new Color(0.55f, 0.6f, 0.66f));
            var debrisMaterial = GetMaterial("Debris", new Color(0.3f, 0.26f, 0.2f));
            _fogMaterial = GetUnlitMaterial("Fog");
            var fxMaterial = GetUnlitMaterial("NoiseRing");
            var doorMaterial = GetMaterial("Door", new Color(0.45f, 0.3f, 0.18f));
            var stairMaterial = GetMaterial("Stairs", new Color(0.35f, 0.25f, 0.17f));
            var cabinetMaterial = GetMaterial("Cabinet", new Color(0.22f, 0.18f, 0.14f));
            var fallbackMaterial = GetMaterial("CharacterFallback", new Color(0.7f, 0.7f, 0.7f));

            SetupLighting();
            var environment = new GameObject("Environment").transform;
            BuildBuilding(environment, backMaterial, groundMaterial, stairMaterial);

            // ---- Waypoints: 4 floors, zig-zag stairs (A: 0->1 left, B: 1->2 right, C: 2->3 left) ----
            var graph = new GameObject("WaypointGraph");
            graph.AddComponent<WaypointGraph>();
            var g = graph.transform;
            const float f1 = FloorHeight;
            const float f2 = FloorHeight * 2f;
            const float f3 = FloorHeight * 3f;

            // Floor 0: entrance (0), hall (1), storage (3); outside (5).
            var outside = CreateWaypoint(g, "OUT", -18f, 0f, 5, false);
            var exit = CreateWaypoint(g, "EXIT", -13f, 0f, 0, true);
            var st0A = CreateWaypoint(g, "ST0A", StairLeftLowX, 0f, 0, false);
            var e1 = CreateWaypoint(g, "E1", -10f, 0f, 0, false);
            var e2 = CreateWaypoint(g, "E2", -7.5f, 0f, 0, false);
            var h1 = CreateWaypoint(g, "H1", -4.5f, 0f, 1, false);
            var h2 = CreateWaypoint(g, "H2", -1f, 0f, 1, false);
            var h3 = CreateWaypoint(g, "H3", 2.8f, 0f, 1, false);
            var sb = CreateWaypoint(g, "SB", 5.2f, 0f, 3, false);
            var t1 = CreateWaypoint(g, "T1", 8.5f, 0f, 3, false);
            var t2 = CreateWaypoint(g, "T2", 12f, 0f, 3, false);

            // Floor 1: corridor (4), living room (2, safe room), right corridor (7).
            var st1A = CreateWaypoint(g, "ST1A", StairLeftHighX, f1, 4, true);
            var c1 = CreateWaypoint(g, "C1", -5.2f, f1, 4, false);
            var l1 = CreateWaypoint(g, "L1", -3f, f1, 2, false);
            var l2 = CreateWaypoint(g, "L2", 0.5f, f1, 2, false);
            var l3 = CreateWaypoint(g, "L3", 3.5f, f1, 2, false);
            var r1 = CreateWaypoint(g, "R1", 6.2f, f1, 7, false);
            var r2 = CreateWaypoint(g, "R2", 9.5f, f1, 7, false);
            var st1B = CreateWaypoint(g, "ST1B", StairRightLowX, f1, 7, false);

            // Floor 2: right landing (8), workshop (9), left hall (10).
            var st2B = CreateWaypoint(g, "ST2B", StairRightHighX, f2, 8, true);
            var rh1 = CreateWaypoint(g, "RH1", 5.4f, f2, 8, false);
            var w1 = CreateWaypoint(g, "W1", 2.8f, f2, 9, false);
            var w2 = CreateWaypoint(g, "W2", -0.5f, f2, 9, false);
            var w3 = CreateWaypoint(g, "W3", -3.8f, f2, 9, false);
            var lh1 = CreateWaypoint(g, "LH1", -6.5f, f2, 10, false);
            var lh2 = CreateWaypoint(g, "LH2", -9.5f, f2, 10, false);
            var st2C = CreateWaypoint(g, "ST2C", StairLeftLowX, f2, 10, false);

            // Floor 3: attic (11), bedroom (12, safe room), vault (6).
            var st3C = CreateWaypoint(g, "ST3C", StairLeftHighX, f3, 11, true);
            var a1 = CreateWaypoint(g, "A1", -5f, f3, 11, false);
            var a2 = CreateWaypoint(g, "A2", -1.8f, f3, 11, false);
            var s1 = CreateWaypoint(g, "S1", 1.2f, f3, 12, false);
            var s2 = CreateWaypoint(g, "S2", 3.5f, f3, 12, false);
            var s3 = CreateWaypoint(g, "S3", 5.8f, f3, 12, false);
            var v1 = CreateWaypoint(g, "V1", 9f, f3, 6, false);
            var v2 = CreateWaypoint(g, "V2", 12.5f, f3, 6, false);

            Link(outside, exit); Link(exit, st0A); Link(st0A, e1); Link(e1, e2);
            Link(e2, h1); Link(h1, h2); Link(h2, h3); Link(h3, sb); Link(sb, t1); Link(t1, t2);
            Link(st0A, st1A);
            Link(st1A, c1); Link(c1, l1); Link(l1, l2); Link(l2, l3); Link(l3, r1); Link(r1, r2); Link(r2, st1B);
            Link(st1B, st2B);
            Link(st2B, rh1); Link(rh1, w1); Link(w1, w2); Link(w2, w3); Link(w3, lh1); Link(lh1, lh2); Link(lh2, st2C);
            Link(st2C, st3C);
            Link(st3C, a1); Link(a1, a2); Link(a2, s1); Link(s1, s2); Link(s2, s3); Link(s3, v1); Link(v1, v2);
            ApplyLinks();

            // ---- Doors / hide spots / safe rooms ----
            var doors = new GameObject("Doors").transform;
            var doorStorage = CreateDoor(doors, "Door_Storage", new Vector3(4f, 0f, 0f), h3, sb, doorMaterial, false, false);
            var doorLiving = CreateDoor(doors, "Door_Living", new Vector3(-4f, f1, 0f), c1, l1, doorMaterial, false, false);
            CreateDoor(doors, "Door_Workshop", new Vector3(4f, f2, 0f), w1, rh1, doorMaterial, false, false);
            var doorBedroom = CreateDoor(doors, "Door_Bedroom", new Vector3(0f, f3, 0f), a2, s1, doorMaterial, false, false);
            // Vault: heavy lock, lockpick only (lockpick lies on the storage shelf, ground floor).
            CreateDoor(doors, "Door_Vault", new Vector3(7f, f3, 0f), s3, v1, doorMaterial, true, true);

            var props = new GameObject("HideAndSafe").transform;
            var hideStorage = CreateHideSpot(props, "HideSpot_Storage", new Vector3(12.5f, 0f, 0f), cabinetMaterial);
            var hideLiving = CreateHideSpot(props, "HideSpot_Living", new Vector3(2f, f1, 0f), cabinetMaterial);
            var hideBedroom = CreateHideSpot(props, "HideSpot_Bedroom", new Vector3(4.6f, f3, 0f), cabinetMaterial);
            CreateSafeSpot(props, "SafeSpot_Storage", sb, doorStorage, hideStorage);
            CreateSafeSpot(props, "SafeSpot_Living", l1, doorLiving, hideLiving);
            CreateSafeSpot(props, "SafeSpot_Bedroom", s1, doorBedroom, hideBedroom);
            CreateLootPile(props, "Loot_Entrance", new Vector3(-9f, 0f, 0f), "잔해 더미", "목재", 2, debrisMaterial, false);
            CreateLootPile(props, "Loot_Storage", new Vector3(9.5f, 0f, 0f), "선반", "부품", 3, debrisMaterial, true);
            CreateLootPile(props, "Loot_Living", new Vector3(-1.5f, f1, 0f), "찬장", "통조림", 3, debrisMaterial, false);
            CreateLootPile(props, "Loot_Workshop", new Vector3(0.8f, f2, 0f), "작업대", "부품", 2, debrisMaterial, false);
            CreateLootPile(props, "Loot_Attic", new Vector3(-3.5f, f3, 0f), "상자 더미", "목재", 3, debrisMaterial, false);
            CreateLootPile(props, "Loot_Bedroom", new Vector3(2.4f, f3, 0f), "서랍장", "통조림", 2, debrisMaterial, false);
            CreateLootPile(props, "Loot_Vault", new Vector3(12f, f3, 0f), "금고", "약품", 6, debrisMaterial, false);

            // Ambush alcoves: one per floor, next to a patrol route and a door or stair top.
            var shadowMaterial = GetMaterial("Shadow", new Color(0.03f, 0.03f, 0.04f));
            CreateAmbushSpot(props, "Ambush_Hall", new Vector3(-5.1f, 0f, 0f), shadowMaterial, debrisMaterial);
            CreateAmbushSpot(props, "Ambush_Corridor1", new Vector3(-6.2f, f1, 0f), shadowMaterial, debrisMaterial);
            CreateAmbushSpot(props, "Ambush_Hall2", new Vector3(-6.2f, f2, 0f), shadowMaterial, debrisMaterial);
            CreateAmbushSpot(props, "Ambush_Attic", new Vector3(-4.2f, f3, 0f), shadowMaterial, debrisMaterial);

            // ---- Rooms (fog of war) and lights ----
            var rooms = new GameObject("Rooms").transform;
            CreateRoom(rooms, "Outside", "거리", -26f, BuildingMinX, 0f, FloorHeight * FloorCount, true);
            CreateRoom(rooms, "Room_Entrance", "입구", BuildingMinX, -6f, 0f, FloorHeight, false);
            CreateRoom(rooms, "Room_Hall", "홀", -6f, 4f, 0f, FloorHeight, false);
            CreateRoom(rooms, "Room_Storage", "창고", 4f, BuildingMaxX, 0f, FloorHeight, false);
            CreateRoom(rooms, "Room_Corridor1", "2층 복도", BuildingMinX, -4f, f1, FloorHeight, false);
            CreateRoom(rooms, "Room_Living", "거실", -4f, 5f, f1, FloorHeight, false);
            CreateRoom(rooms, "Room_Corridor1R", "2층 계단참", 5f, BuildingMaxX, f1, FloorHeight, false);
            CreateRoom(rooms, "Room_Landing2", "3층 계단참", 4f, BuildingMaxX, f2, FloorHeight, false);
            CreateRoom(rooms, "Room_Workshop", "작업실", -5f, 4f, f2, FloorHeight, false);
            CreateRoom(rooms, "Room_Hall2", "3층 복도", BuildingMinX, -5f, f2, FloorHeight, false);
            CreateRoom(rooms, "Room_Attic", "다락", BuildingMinX, 0f, f3, FloorHeight, false);
            CreateRoom(rooms, "Room_Bedroom", "침실", 0f, 7f, f3, FloorHeight, false);
            CreateRoom(rooms, "Room_Vault", "금고방", 7f, BuildingMaxX, f3, FloorHeight, false);

            // ---- Systems ----
            var systems = new GameObject("AI Systems");
            var director = systems.AddComponent<AiDirector>();
            SetObject(director, "_config", config);
            systems.AddComponent<SquadCoordinator>();
            systems.AddComponent<VisibilitySystem>();
            var rings = systems.AddComponent<NoiseRingVisualizer>();
            SetObject(rings, "_material", fxMaterial);
            systems.AddComponent<TwomHud>();
            systems.AddComponent<AiDebugOverlay>();

            // ---- Characters (same model for everyone; NPCs get a light tint) ----
            var npcs = new GameObject("NPCs").transform;
            var guardTint = new Color(1f, 0.62f, 0.58f);
            CreateGuard(npcs, "Guard_A", "경비 A", 1, exit, guardTint, fallbackMaterial, new[] { exit, st0A, e1, e2 }, new[] { h1 }, outside);
            CreateGuard(npcs, "Guard_B", "경비 B", 2, h2, guardTint, fallbackMaterial, new[] { h1, h2, h3 }, new[] { e2, sb }, outside);
            CreateGuard(npcs, "Guard_C", "경비 C", 3, c1, guardTint, fallbackMaterial, new[] { st1A, c1, l1, l2, l3, r1 }, new[] { r2 }, outside);
            CreateGuard(npcs, "Guard_D", "경비 D", 4, w2, guardTint, fallbackMaterial, new[] { st2B, rh1, w1, w2, w3, lh1 }, new[] { lh2 }, outside);
            CreateGuard(npcs, "Guard_E", "경비 E", 5, a1, guardTint, fallbackMaterial, new[] { st3C, a1, a2 }, new[] { s1 }, outside);
            var civilianTint = new Color(0.65f, 0.8f, 1f);
            CreateCivilian(npcs, "Civilian_A", "주민 A", 10, l2, civilianTint, fallbackMaterial, new[] { l2, l3, l1, r1 }, outside);
            CreateCivilian(npcs, "Civilian_B", "주민 B", 11, s2, civilianTint, fallbackMaterial, new[] { s2, s3, s1, a2 }, outside);

            var player = CreatePlayer(new Vector3(-19f, 0f, 0f), fallbackMaterial, outside);
            SetupCamera(player.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            Debug.Log($"[TwomAi] Prototype scene built: {ScenePath}");
        }

        // ---------------- Building ----------------

        private static void BuildBuilding(Transform parent, Material backMaterial, Material groundMaterial, Material stairMaterial)
        {
            var building = new GameObject("Building").transform;
            building.SetParent(parent);
            float top = FloorHeight * FloorCount;

            // Street outside (left) and ground floor slab.
            CreateBox(building, "Street", new Vector3(-20f, -SlabThickness * 0.5f, 0f), new Vector3(12f, SlabThickness, Depth + 4f), groundMaterial, true);
            CreateSlab(building, "Floor_0", 0f, BuildingMinX, BuildingMaxX);

            // Upper slabs, each with the hole of the flight arriving on it.
            for (int floor = 1; floor < FloorCount; floor++)
            {
                float y = FloorHeight * floor;
                bool leftHole = floor % 2 == 1;
                float holeMin = leftHole ? StairLeftLowX : StairRightHighX;
                float holeMax = leftHole ? StairLeftHighX : StairRightLowX;
                CreateSlab(building, $"Floor_{floor}_A", y, BuildingMinX, holeMin);
                CreateSlab(building, $"Floor_{floor}_B", y, holeMax, BuildingMaxX);
            }

            CreateSlab(building, "Roof", top, BuildingMinX, BuildingMaxX);

            // Back wall (front is open: cross-section view).
            CreateBox(building, "BackWall", new Vector3(0f, top * 0.5f, Depth * 0.5f + WallThickness * 0.5f),
                new Vector3(BuildingMaxX - BuildingMinX, top, WallThickness), backMaterial, true);

            // Outer walls: entrance opening on the ground floor, left side.
            for (int floor = 0; floor < FloorCount; floor++)
            {
                float y = FloorHeight * floor;
                CreateCrossWall(building, $"Wall_Left_{floor}", BuildingMinX, y, floor == 0);
                CreateCrossWall(building, $"Wall_Right_{floor}", BuildingMaxX, y, false);
            }

            // Interior partitions (all with doorways; doors are placed on some of them).
            CreateCrossWall(building, "Wall_0_Entrance_Hall", -6f, 0f, true);
            CreateCrossWall(building, "Wall_0_Hall_Storage", 4f, 0f, true);
            CreateCrossWall(building, "Wall_1_Corridor_Living", -4f, FloorHeight, true);
            CreateCrossWall(building, "Wall_1_Living_Landing", 5f, FloorHeight, true);
            CreateCrossWall(building, "Wall_2_Hall_Workshop", -5f, FloorHeight * 2f, true);
            CreateCrossWall(building, "Wall_2_Workshop_Landing", 4f, FloorHeight * 2f, true);
            CreateCrossWall(building, "Wall_3_Attic_Bedroom", 0f, FloorHeight * 3f, true);
            CreateCrossWall(building, "Wall_3_Bedroom_Vault", 7f, FloorHeight * 3f, true);

            CreateStairs(building, "Stairs_A", StairLeftLowX, StairLeftHighX, 0f, stairMaterial);
            CreateStairs(building, "Stairs_B", StairRightLowX, StairRightHighX, FloorHeight, stairMaterial);
            CreateStairs(building, "Stairs_C", StairLeftLowX, StairLeftHighX, FloorHeight * 2f, stairMaterial);
        }

        private static void CreateSlab(Transform parent, string name, float topY, float minX, float maxX)
        {
            CreateBox(parent, name, new Vector3((minX + maxX) * 0.5f, topY - SlabThickness * 0.5f, 0f),
                new Vector3(maxX - minX, SlabThickness, Depth), _slabMaterial, true);
        }

        // Wall perpendicular to X spanning the building depth; optional doorway centered on z = 0.
        private static void CreateCrossWall(Transform parent, string name, float x, float baseY, bool doorway)
        {
            float height = FloorHeight - SlabThickness;
            var root = new GameObject(name).transform;
            root.SetParent(parent);
            if (!doorway)
            {
                CreateBox(root, "Solid", new Vector3(x, baseY + height * 0.5f, 0f), new Vector3(WallThickness, height, Depth), _wallMaterial, true);
                return;
            }

            float side = (Depth - DoorWidth) * 0.5f;
            float sideCenter = DoorWidth * 0.5f + side * 0.5f;
            CreateBox(root, "Back", new Vector3(x, baseY + height * 0.5f, sideCenter), new Vector3(WallThickness, height, side), _wallMaterial, true);
            CreateBox(root, "Front", new Vector3(x, baseY + height * 0.5f, -sideCenter), new Vector3(WallThickness, height, side), _wallMaterial, true);
            float lintel = height - DoorHeight;
            CreateBox(root, "Lintel", new Vector3(x, baseY + DoorHeight + lintel * 0.5f, 0f), new Vector3(WallThickness, lintel, DoorWidth), _wallMaterial, true);
        }

        // Visual only: no colliders so clicks and AI rays pass through.
        // One flight from (lowX, baseY) to (highX, baseY + FloorHeight); highX may be left of lowX.
        private static void CreateStairs(Transform parent, string name, float lowX, float highX, float baseY, Material material)
        {
            var stairs = new GameObject(name).transform;
            stairs.SetParent(parent);
            const int steps = 16;
            float run = (highX - lowX) / steps;
            float width = Mathf.Abs(run);
            float rise = FloorHeight / steps;
            // Solid columns whose tread top meets the walking line (stair edge) at each step center,
            // so feet land on the treads instead of half a step below them.
            for (int i = 0; i < steps; i++)
            {
                float top = rise * (i + 0.5f);
                // Behind the walking line (z = 0) so steps never cover the character from the camera.
                float x = lowX + run * (i + 0.5f);
                var step = CreateBox(stairs, $"Step_{i}", new Vector3(x, baseY + top * 0.5f, 0.85f),
                    new Vector3(width, top, 1.2f), material, false);
                Object.DestroyImmediate(step.GetComponent<Collider>());

                // Invisible tread on the walking line for foot IK (trigger: ignored by AI rays and physics).
                var tread = new GameObject($"Tread_{i}");
                tread.transform.SetParent(stairs, false);
                tread.transform.position = new Vector3(x, baseY + top * 0.5f, 0f);
                var box = tread.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(width, top, 0.8f);
                tread.AddComponent<StairStep>();
            }
        }

        private static GameObject CreateBox(Transform parent, string name, Vector3 center, Vector3 size, Material material, bool occluder)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent);
            box.transform.position = center;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
            if (occluder)
                box.AddComponent<Occluder>();
            box.isStatic = true;
            return box;
        }

        private static void SetupLighting()
        {
            var light = Object.FindFirstObjectByType<Light>();
            if (light != null)
            {
                light.transform.rotation = Quaternion.Euler(35f, 25f, 0f);
                light.intensity = 0.35f;
                light.color = new Color(0.6f, 0.72f, 0.9f);
            }

            // Cold, desaturated night palette like the original.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.2f, 0.26f);
        }

        private static void SetupCamera(Transform player)
        {
            var cameraObject = GameObject.Find("Main Camera");
            if (cameraObject == null)
            {
                cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                cameraObject.AddComponent<Camera>();
            }

            var camera = cameraObject.GetComponent<Camera>();
            camera.fieldOfView = 40f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.22f, 0.3f);
            cameraObject.transform.position = player.position + new Vector3(0f, 2f, -12f);
            cameraObject.transform.rotation = Quaternion.Euler(4f, 0f, 0f);
            var follow = cameraObject.AddComponent<SideViewCamera>();
            SetObject(follow, "_target", player);
        }

        // ---------------- Doors / props ----------------

        private static LockableDoor CreateDoor(Transform parent, string name, Vector3 position, Waypoint sideA, Waypoint sideB, Material material,
            bool startLocked, bool reinforced)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent);
            root.transform.position = position;
            // Forward = +X (door normal); local X (width) maps to world Z.
            root.transform.rotation = Quaternion.LookRotation(Vector3.right);

            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(root.transform, false);
            hinge.localPosition = new Vector3(-DoorWidth * 0.5f, 0f, 0f);

            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(hinge, false);
            panel.transform.localPosition = new Vector3(DoorWidth * 0.5f, DoorHeight * 0.5f, 0f);
            panel.transform.localScale = new Vector3(DoorWidth, DoorHeight, 0.1f);
            panel.GetComponent<Renderer>().sharedMaterial = material;
            panel.AddComponent<Occluder>();

            // Trigger used only for mouse picking (AI rays ignore triggers).
            var clickArea = root.AddComponent<BoxCollider>();
            clickArea.isTrigger = true;
            clickArea.center = new Vector3(0f, DoorHeight * 0.5f, 0f);
            clickArea.size = new Vector3(DoorWidth, DoorHeight, 0.6f);

            var door = root.AddComponent<LockableDoor>();
            SetObject(door, "_sideA", sideA);
            SetObject(door, "_sideB", sideB);
            SetObject(door, "_hinge", hinge);
            SetObject(door, "_panelCollider", panel.GetComponent<Collider>());
            SetObject(door, "_panelRenderer", panel.GetComponent<Renderer>());
            SetBool(door, "_startOpen", !startLocked);
            SetBool(door, "_startLocked", startLocked);
            SetBool(door, "_reinforced", reinforced);
            return door;
        }

        private static HideSpot CreateHideSpot(Transform parent, string name, Vector3 position, Material material)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent);
            root.transform.position = position;

            // Wardrobe against the back wall; the hider stands in front of it, darkened.
            var cabinet = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabinet.name = "Wardrobe";
            cabinet.transform.SetParent(root.transform, false);
            cabinet.transform.localPosition = new Vector3(0f, 1f, 0.9f);
            cabinet.transform.localScale = new Vector3(1.2f, 2f, 1f);
            cabinet.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(cabinet.GetComponent<Collider>());

            var clickArea = root.AddComponent<BoxCollider>();
            clickArea.isTrigger = true;
            clickArea.center = new Vector3(0f, 1f, 0.4f);
            clickArea.size = new Vector3(1.2f, 2f, 1.6f);

            return root.AddComponent<HideSpot>();
        }

        private static void CreateSafeSpot(Transform parent, string name, Waypoint inside, LockableDoor door, HideSpot hideSpot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = inside.Position;
            var safe = go.AddComponent<SafeSpot>();
            SetObject(safe, "_insideNode", inside);
            SetObject(safe, "_door", door);
            SetObjectArray(safe, "_hideSpots", new Object[] { hideSpot });
        }

        private static void CreateAmbushSpot(Transform parent, string name, Vector3 position, Material shadow, Material crate)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent);
            root.transform.position = position;

            // Dark recess in the back wall plus a crate giving cover.
            var recess = GameObject.CreatePrimitive(PrimitiveType.Cube);
            recess.name = "Recess";
            recess.transform.SetParent(root.transform, false);
            recess.transform.localPosition = new Vector3(0f, 1.15f, 1.2f);
            recess.transform.localScale = new Vector3(1.2f, 2.3f, 0.3f);
            recess.GetComponent<Renderer>().sharedMaterial = shadow;
            Object.DestroyImmediate(recess.GetComponent<Collider>());

            var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cover.name = "Cover";
            cover.transform.SetParent(root.transform, false);
            cover.transform.localPosition = new Vector3(0.55f, 0.35f, 0.5f);
            cover.transform.localScale = new Vector3(0.5f, 0.7f, 0.6f);
            cover.GetComponent<Renderer>().sharedMaterial = crate;
            Object.DestroyImmediate(cover.GetComponent<Collider>());

            root.AddComponent<PlayerHideSpot>();
        }

        private static void CreateLootPile(Transform parent, string name, Vector3 position, string displayName, string itemKind, int items, Material material, bool lockpick)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent);
            root.transform.position = position;
            var offsets = new[] { new Vector3(-0.3f, 0.2f, 0.6f), new Vector3(0.25f, 0.15f, 0.8f), new Vector3(0f, 0.45f, 0.7f) };
            var sizes = new[] { new Vector3(0.6f, 0.4f, 0.5f), new Vector3(0.5f, 0.3f, 0.6f), new Vector3(0.4f, 0.3f, 0.4f) };
            for (int i = 0; i < offsets.Length; i++)
            {
                var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = "Piece";
                piece.transform.SetParent(root.transform, false);
                piece.transform.localPosition = offsets[i];
                piece.transform.localScale = sizes[i];
                piece.transform.localRotation = Quaternion.Euler(0f, i * 23f, i * 7f);
                piece.GetComponent<Renderer>().sharedMaterial = material;
                Object.DestroyImmediate(piece.GetComponent<Collider>());
            }

            var clickArea = root.AddComponent<BoxCollider>();
            clickArea.isTrigger = true;
            clickArea.center = new Vector3(0f, 0.5f, 0.6f);
            clickArea.size = new Vector3(1.4f, 1f, 1f);

            var pile = root.AddComponent<LootPile>();
            SetString(pile, "_displayName", displayName);
            SetString(pile, "_itemKind", itemKind);
            SetInt(pile, "_items", items);
            SetBool(pile, "_containsLockpick", lockpick);
        }

        private static void CreateRoom(Transform parent, string name, string displayName, float minX, float maxX, float floorY, float height, bool alwaysVisible)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = new Vector3((minX + maxX) * 0.5f, floorY, 0f);
            var room = go.AddComponent<RoomVolume>();
            SetString(room, "_displayName", displayName);
            SetFloat(room, "_minX", minX);
            SetFloat(room, "_maxX", maxX);
            SetFloat(room, "_floorY", floorY);
            SetFloat(room, "_height", height);
            SetBool(room, "_alwaysVisible", alwaysVisible);
            if (alwaysVisible)
                return;

            // Black veil in front of the open cross-section, split into ~0.5 m vertical strips
            // so a peek through a door can clear it progressively from the door outward.
            int stripCount = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / 0.5f));
            float stripWidth = (maxX - minX) / stripCount;
            var strips = new Object[stripCount];
            for (int i = 0; i < stripCount; i++)
            {
                var fog = GameObject.CreatePrimitive(PrimitiveType.Quad);
                fog.name = $"Fog_{i}";
                fog.transform.SetParent(go.transform, false);
                fog.transform.position = new Vector3(minX + stripWidth * (i + 0.5f), floorY + height * 0.5f - SlabThickness * 0.5f, -Depth * 0.5f - 0.1f);
                fog.transform.localScale = new Vector3(stripWidth + 0.02f, height + 0.02f, 1f);
                Object.DestroyImmediate(fog.GetComponent<Collider>());
                var renderer = fog.GetComponent<Renderer>();
                renderer.sharedMaterial = _fogMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                strips[i] = renderer;
            }

            SetObjectArray(room, "_fogStrips", strips);

            // Dim cold room light.
            var lightObject = new GameObject("RoomLight");
            lightObject.transform.SetParent(go.transform, false);
            lightObject.transform.position = new Vector3((minX + maxX) * 0.5f, floorY + height - 0.6f, -0.4f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.55f, 0.72f, 0.85f);
            light.intensity = 2.5f;
            light.range = Mathf.Max(6f, (maxX - minX) * 0.7f);
            light.shadows = LightShadows.None;
        }

        // ---------------- Animation ----------------

        // Mixamo FBX -> Humanoid, root motion baked into pose (movement is driven by NpcMover).
        private static Dictionary<string, AnimationClip> PrepareClips()
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var entry in ClipTable)
            {
                string path = $"{AnimationFolder}/{entry.file}.fbx";
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                {
                    Debug.LogWarning($"[TwomAi] Animation not found: {path}");
                    continue;
                }

                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                var settings = importer.clipAnimations;
                if (settings == null || settings.Length == 0)
                    settings = importer.defaultClipAnimations;

                foreach (var clip in settings)
                {
                    clip.loopTime = entry.loop;
                    clip.lockRootRotation = true;
                    clip.lockRootHeightY = true;
                    clip.lockRootPositionXZ = true;
                    clip.keepOriginalOrientation = true;
                    clip.keepOriginalPositionY = true;
                    clip.keepOriginalPositionXZ = true;
                }

                importer.clipAnimations = settings;
                importer.SaveAndReimport();

                var loaded = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (loaded != null)
                    clips[entry.state] = loaded;
            }

            return clips;
        }

        // Copy of the Starter Assets locomotion controller + action states driven by triggers.
        private static RuntimeAnimatorController BuildAnimatorController(Dictionary<string, AnimationClip> clips)
        {
            string sourceGuid = AssetDatabase.FindAssets("StarterAssetsThirdPerson t:AnimatorController").FirstOrDefault();
            if (string.IsNullOrEmpty(sourceGuid))
            {
                Debug.LogWarning("[TwomAi] StarterAssetsThirdPerson controller not found; combat animations disabled.");
                return null;
            }

            EnsureFolder(AnimationFolder);
            AssetDatabase.DeleteAsset(ControllerPath);
            if (!AssetDatabase.CopyAsset(AssetDatabase.GUIDToAssetPath(sourceGuid), ControllerPath))
                return null;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var baseLayers = controller.layers;
            baseLayers[0].iKPass = true; // StairFootIK
            controller.layers = baseLayers;
            var stateMachine = controller.layers[0].stateMachine;
            var locomotion = stateMachine.defaultState;
            foreach (var entry in ClipTable)
            {
                if (!clips.TryGetValue(entry.state, out var clip))
                    continue;

                if (entry.state == "Fire" || entry.state == "AimWalk")
                    continue; // built by AddRifleStates

                if (entry.state == "Cower")
                {
                    controller.AddParameter("Cower", AnimatorControllerParameterType.Bool);
                    controller.AddParameter(new AnimatorControllerParameter
                    {
                        name = "CowerSpeed",
                        type = AnimatorControllerParameterType.Float,
                        defaultFloat = 1f,
                    });
                    var cower = stateMachine.AddState("Cower");
                    cower.motion = clip;
                    // Played once and frozen in the kneeling pose by CharacterAnimator (CowerSpeed -> 0).
                    cower.speedParameter = "CowerSpeed";
                    cower.speedParameterActive = true;
                    var enter = stateMachine.AddAnyStateTransition(cower);
                    enter.AddCondition(AnimatorConditionMode.If, 0f, "Cower");
                    enter.canTransitionToSelf = false;
                    enter.duration = 0.2f;
                    var leave = cower.AddTransition(locomotion);
                    leave.AddCondition(AnimatorConditionMode.IfNot, 0f, "Cower");
                    leave.hasExitTime = false;
                    leave.duration = 0.2f;
                    continue;
                }

                controller.AddParameter(entry.state, AnimatorControllerParameterType.Trigger);
                var state = stateMachine.AddState(entry.state);
                state.motion = clip;
                var any = stateMachine.AddAnyStateTransition(state);
                any.AddCondition(AnimatorConditionMode.If, 0f, entry.state);
                any.canTransitionToSelf = false;
                any.hasExitTime = false;
                any.duration = 0.08f;
                if (entry.state == "Die")
                    continue; // stays on the last frame

                var back = state.AddTransition(locomotion);
                back.hasExitTime = true;
                back.exitTime = Mathf.Clamp(entry.seconds / Mathf.Max(0.01f, clip.length), 0.2f, 0.95f);
                back.duration = 0.15f;
            }

            AddRifleStates(controller, stateMachine, locomotion, clips);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        // Upper-body "Rifle" layer (arms + head mask, override, weight 1):
        // Empty <-> Aim (Aim bool) and Fire (trigger). Legs keep the base locomotion, so walking/running
        // with the rifle uses the normal walk/run legs while the arms hold the aim pose.
        private static void AddRifleStates(AnimatorController controller, AnimatorStateMachine baseMachine,
            AnimatorState locomotion, Dictionary<string, AnimationClip> clips)
        {
            if (!clips.TryGetValue("Fire", out var fireClip))
                return;

            clips.TryGetValue("AimWalk", out var walkClip);
            controller.AddParameter("Aim", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Fire", AnimatorControllerParameterType.Trigger);

            string maskPath = AnimationFolder + "/RifleUpperBody.mask";
            AssetDatabase.DeleteAsset(maskPath);
            var mask = new AvatarMask();
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
            {
                bool upper = part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                    || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers;
                mask.SetHumanoidBodyPartActive(part, upper);
            }

            AssetDatabase.CreateAsset(mask, maskPath);

            controller.AddLayer("Rifle");
            var layers = controller.layers;
            int layerIndex = layers.Length - 1;
            layers[layerIndex].avatarMask = mask;
            layers[layerIndex].defaultWeight = 1f;
            layers[layerIndex].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;

            var machine = controller.layers[layerIndex].stateMachine;
            var empty = machine.AddState("Empty");
            machine.defaultState = empty;

            // Aim: frozen first frame of "Firing Rifle" when still, rifle-walk arms when moving.
            var aim = controller.CreateBlendTreeInController("Aim", out var tree, layerIndex);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(fireClip, 0f);
            tree.AddChild(walkClip != null ? walkClip : fireClip, 1.5f);
            var children = tree.children;
            children[0].timeScale = 0f;
            tree.children = children;

            var toAim = empty.AddTransition(aim);
            toAim.AddCondition(AnimatorConditionMode.If, 0f, "Aim");
            toAim.hasExitTime = false;
            toAim.duration = 0.15f;
            var toEmpty = aim.AddTransition(empty);
            toEmpty.AddCondition(AnimatorConditionMode.IfNot, 0f, "Aim");
            toEmpty.hasExitTime = false;
            toEmpty.duration = 0.2f;

            var fire = machine.AddState("Fire");
            fire.motion = fireClip;
            var anyFire = machine.AddAnyStateTransition(fire);
            anyFire.AddCondition(AnimatorConditionMode.If, 0f, "Fire");
            anyFire.canTransitionToSelf = false;
            anyFire.hasExitTime = false;
            anyFire.duration = 0.05f;
            float exit = Mathf.Clamp(0.45f / Mathf.Max(0.01f, fireClip.length), 0.2f, 0.95f);
            var fireToAim = fire.AddTransition(aim);
            fireToAim.AddCondition(AnimatorConditionMode.If, 0f, "Aim");
            fireToAim.hasExitTime = true;
            fireToAim.exitTime = exit;
            fireToAim.duration = 0.1f;
            var fireToEmpty = fire.AddTransition(empty);
            fireToEmpty.AddCondition(AnimatorConditionMode.IfNot, 0f, "Aim");
            fireToEmpty.hasExitTime = true;
            fireToEmpty.exitTime = exit;
            fireToEmpty.duration = 0.15f;
        }

        // Low Poly Weapons AK74 in the right hand. Offsets live on WeaponMount (tune in the inspector).
        private static WeaponMount AttachRifle(Transform model)
        {
            var prefab = FindPrefab("AK74", "Low Poly Weapons");
            var animator = model != null ? model.GetComponentInChildren<Animator>() : null;
            if (prefab == null || animator == null || !animator.isHuman)
                return null;

            var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (rightHand == null)
                return null;

            // Parented to the model (not the bone): WeaponMount places it from both hands after animation.
            var rifle = (GameObject)PrefabUtility.InstantiatePrefab(prefab, model);
            PrefabUtility.UnpackPrefabInstance(rifle, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            rifle.name = "Rifle";
            rifle.transform.localPosition = Vector3.zero;
            rifle.transform.localRotation = Quaternion.identity;
            foreach (var collider in rifle.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);

            // The asset ships a Built-in Standard material (magenta in URP): swap for a pipeline-correct one.
            var gunMaterial = GetGunMaterial();
            foreach (var renderer in rifle.GetComponentsInChildren<Renderer>(true))
            {
                var shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                    shared[i] = gunMaterial;
                renderer.sharedMaterials = shared;
            }

            var mount = rifle.AddComponent<WeaponMount>();
            SetObject(mount, "_rightHand", rightHand);
            SetObject(mount, "_rightFinger", animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal));
            SetObject(mount, "_leftHand", leftHand);
            SetObject(mount, "_leftFinger", animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal));
            return mount;
        }

        // ---------------- Waypoints ----------------

        private static Waypoint CreateWaypoint(Transform parent, string name, float x, float y, int region, bool isExit)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(x, y, 0f);
            var waypoint = go.AddComponent<Waypoint>();
            SetInt(waypoint, "_regionId", region);
            SetBool(waypoint, "_isExit", isExit);
            PendingLinks[waypoint] = new List<Waypoint>();
            return waypoint;
        }

        private static void Link(Waypoint a, Waypoint b)
        {
            if (!PendingLinks[a].Contains(b)) PendingLinks[a].Add(b);
            if (!PendingLinks[b].Contains(a)) PendingLinks[b].Add(a);
        }

        private static void ApplyLinks()
        {
            foreach (var pair in PendingLinks)
                SetObjectArray(pair.Key, "_links", pair.Value.ToArray());
        }

        // ---------------- Characters ----------------

        private static GameObject CreateCharacterRoot(Transform parent, string name, Vector3 position, Material fallbackMaterial, out Transform model)
        {
            var root = new GameObject(name);
            if (parent != null)
                root.transform.SetParent(parent);

            root.transform.position = position;
            root.transform.rotation = Quaternion.LookRotation(Vector3.right);

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = 0.3f;

            model = CreateModel(root.transform, fallbackMaterial);
            root.AddComponent<NpcMover>();
            root.AddComponent<CharacterAnimator>();
            return root;
        }

        // Starter Assets PlayerArmature used as a pure visual: controller/input/physics stripped.
        private static Transform CreateModel(Transform parent, Material fallbackMaterial)
        {
            var prefab = FindPrefab("PlayerArmature", "ThirdPerson");
            if (prefab == null)
            {
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.name = "Model (Fallback)";
                capsule.transform.SetParent(parent, false);
                capsule.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                capsule.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
                capsule.GetComponent<Renderer>().sharedMaterial = fallbackMaterial;
                Object.DestroyImmediate(capsule.GetComponent<Collider>());
                return capsule.transform;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "Model";
            instance.tag = "Untagged";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            // Dependents first (ThirdPersonController requires CharacterController / PlayerInput).
            DestroyComponentsNamed(instance, "ThirdPersonController");
            DestroyComponentsNamed(instance, "FirstPersonController");
            DestroyComponentsNamed(instance, "BasicRigidBodyPush");
            DestroyComponentsNamed(instance, "StarterAssetsInputs");
            DestroyComponentsNamed(instance, "PlayerInput");
            foreach (var controller in instance.GetComponentsInChildren<CharacterController>(true))
                Object.DestroyImmediate(controller);
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);

            var cameraRoot = instance.transform.Find("PlayerCameraRoot");
            if (cameraRoot != null)
                Object.DestroyImmediate(cameraRoot.gameObject);

            var animator = instance.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                if (_controller != null)
                    animator.runtimeAnimatorController = _controller;
                animator.gameObject.AddComponent<AnimationEventSink>();
                animator.gameObject.AddComponent<StairFootIK>();
            }

            return instance.transform;
        }

        private static void DestroyComponentsNamed(GameObject root, string typeName)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component != null && component.GetType().Name == typeName)
                    Object.DestroyImmediate(component);
            }
        }

        private static NpcActor AddActor(GameObject go, int id, NpcKind kind, string displayName, Transform model, Color tint)
        {
            var actor = go.AddComponent<NpcActor>();
            SetInt(actor, "_npcId", id);
            SetString(actor, "_displayName", displayName);
            SetEnum(actor, "_kind", (int)kind);
            SetObject(actor, "_modelRoot", model);
            SetColor(actor, "_tint", tint);
            return actor;
        }

        private static void CreateGuard(Transform parent, string name, string displayName, int id, Waypoint start, Color tint, Material fallback, Waypoint[] route, Waypoint[] extras, Waypoint escape)
        {
            var go = CreateCharacterRoot(parent, name, start.Position, fallback, out var model);
            var rifle = AttachRifle(model);
            AddActor(go, id, NpcKind.Guard, displayName, model, tint);
            go.AddComponent<FootstepRingEmitter>();
            var brain = go.AddComponent<GuardBrain>();
            SetObjectArray(brain, "_patrolRoute", route);
            SetObjectArray(brain, "_patrolZoneExtras", extras);
            SetObject(brain, "_rifleModel", rifle);
            SetObject(brain, "_escapeNode", escape);
            SetBool(brain, "_hasRifle", rifle != null);
        }

        private static void CreateCivilian(Transform parent, string name, string displayName, int id, Waypoint start, Color tint, Material fallback, Waypoint[] routine, Waypoint escape)
        {
            var go = CreateCharacterRoot(parent, name, start.Position, fallback, out var model);
            AddActor(go, id, NpcKind.Civilian, displayName, model, tint);
            go.AddComponent<FootstepRingEmitter>();
            var brain = go.AddComponent<CivilianBrain>();
            SetObjectArray(brain, "_routineNodes", routine);
            SetObject(brain, "_escapeNode", escape);
        }

        private static GameObject CreatePlayer(Vector3 position, Material fallback, Waypoint exitNode)
        {
            var player = CreateCharacterRoot(null, "Player", position, fallback, out var model);
            var rifle = AttachRifle(model);
            player.tag = "Player";
            var target = player.AddComponent<PlayerTarget>();
            SetString(target, "_displayName", "파블");
            player.AddComponent<NoiseEmitter>();
            var controller = player.AddComponent<PlayerClickController>();
            SetObject(controller, "_exitNode", exitNode);
            SetObject(controller, "_rifleModel", rifle);
            SetObject(controller, "_crowbarModel", CreateCrowbar(model));
            return player;
        }

        // Low-poly crowbar: one swept mesh (octagonal bar) with a flat chisel end and a J-hook claw.
        private static HandPropMount CreateCrowbar(Transform model)
        {
            var animator = model != null ? model.GetComponentInChildren<Animator>() : null;
            if (animator == null || !animator.isHuman)
                return null;

            const float length = 0.72f;
            var root = new GameObject("Crowbar");
            root.transform.SetParent(model, false);
            root.AddComponent<MeshFilter>().sharedMesh = GetCrowbarMesh(length);
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetMaterial("Crowbar", new Color(0.14f, 0.14f, 0.16f));

            var mount = root.AddComponent<HandPropMount>();
            SetObject(mount, "_hand", animator.GetBoneTransform(HumanBodyBones.RightHand));
            SetObject(mount, "_indexBase", animator.GetBoneTransform(HumanBodyBones.RightIndexProximal));
            SetObject(mount, "_middleBase", animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal));
            SetObject(mount, "_littleBase", animator.GetBoneTransform(HumanBodyBones.RightLittleProximal));
            SetFloat(mount, "_length", length);
            root.SetActive(false);
            return mount;
        }

        // Path in the YZ plane: chisel tip bent slightly up at z < 0, straight shaft to z = length,
        // then a 200-degree J curve (hook) toward +Y. Cross-section: octagon, flattened at both ends.
        private static Mesh GetCrowbarMesh(float length)
        {
            string folder = RootFolder + "/Models";
            string path = folder + "/Crowbar.asset";
            EnsureFolder(folder);
            AssetDatabase.DeleteAsset(path);

            var points = new List<Vector3>();
            var widths = new List<float>();
            var thicknesses = new List<float>();
            void Add(Vector3 p, float w, float t) { points.Add(p); widths.Add(w); thicknesses.Add(t); }

            // Chisel end (flat, bent up a little).
            Add(new Vector3(0f, 0.022f, -0.06f), 0.03f, 0.006f);
            Add(new Vector3(0f, 0.01f, -0.03f), 0.026f, 0.014f);
            Add(new Vector3(0f, 0f, 0f), 0.022f, 0.022f);
            // Shaft.
            const int shaftSegments = 6;
            for (int i = 1; i <= shaftSegments; i++)
                Add(new Vector3(0f, 0f, length * i / shaftSegments), 0.022f, 0.022f);

            // Hook: center (y = R, z = length), offset(theta) = R * (-cos, sin).
            const float radius = 0.05f;
            const int arcSegments = 12;
            for (int i = 1; i <= arcSegments; i++)
            {
                float theta = Mathf.Deg2Rad * (200f * i / arcSegments);
                float flatten = i > arcSegments - 3 ? (arcSegments - i + 1) / 4f : 1f;
                Add(new Vector3(0f, radius - radius * Mathf.Cos(theta), length + radius * Mathf.Sin(theta)),
                    Mathf.Lerp(0.034f, 0.022f, flatten), Mathf.Lerp(0.007f, 0.022f, flatten));
            }

            const int sides = 8;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 prev = points[Mathf.Max(0, i - 1)];
                Vector3 next = points[Mathf.Min(points.Count - 1, i + 1)];
                Vector3 tangent = (next - prev).normalized;
                Vector3 normal = new Vector3(0f, -tangent.z, tangent.y); // in-plane normal
                for (int k = 0; k < sides; k++)
                {
                    float angle = (k + 0.5f) * Mathf.PI * 2f / sides;
                    vertices.Add(points[i] + Vector3.right * (Mathf.Cos(angle) * widths[i] * 0.5f)
                        + normal * (Mathf.Sin(angle) * thicknesses[i] * 0.5f));
                }
            }

            for (int i = 0; i < points.Count - 1; i++)
            {
                for (int k = 0; k < sides; k++)
                {
                    int a0 = i * sides + k;
                    int a1 = i * sides + (k + 1) % sides;
                    int b0 = a0 + sides;
                    int b1 = a1 + sides;
                    triangles.Add(a0); triangles.Add(b0); triangles.Add(a1);
                    triangles.Add(a1); triangles.Add(b0); triangles.Add(b1);
                }
            }

            // End caps.
            AddCap(vertices, triangles, 0, sides, points[0], false);
            AddCap(vertices, triangles, (points.Count - 1) * sides, sides, points[points.Count - 1], true);

            var mesh = new Mesh { name = "Crowbar" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static void AddCap(List<Vector3> vertices, List<int> triangles, int ringStart, int sides, Vector3 center, bool flip)
        {
            int centerIndex = vertices.Count;
            vertices.Add(center);
            for (int k = 0; k < sides; k++)
            {
                int a = ringStart + k;
                int b = ringStart + (k + 1) % sides;
                if (flip) { triangles.Add(centerIndex); triangles.Add(b); triangles.Add(a); }
                else { triangles.Add(centerIndex); triangles.Add(a); triangles.Add(b); }
            }
        }

        private static void AddPart(Transform parent, string name, Vector3 position, Vector3 size, Vector3 euler, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.Euler(euler);
            part.transform.localScale = size;
            part.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(part.GetComponent<Collider>());
        }

        // ---------------- Asset helpers ----------------

        private static GameObject FindPrefab(string prefabName, string preferredPathPart)
        {
            GameObject fallback = null;
            foreach (string guid in AssetDatabase.FindAssets(prefabName + " t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != prefabName)
                    continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (path.Contains(preferredPathPart))
                    return prefab;

                if (fallback == null)
                    fallback = prefab;
            }

            if (fallback == null)
                Debug.LogWarning($"[TwomAi] Prefab '{prefabName}' not found; using capsule models.");

            return fallback;
        }

        private static AiTuningConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<AiTuningConfig>(ConfigPath);
            if (config != null)
                return config;

            config = ScriptableObject.CreateInstance<AiTuningConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        private static Material GetMaterial(string materialName, Color color)
        {
            string path = $"{MaterialFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(FindLitShader());
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetGunMaterial()
        {
            var material = GetMaterial("Gun", Color.white);
            string guid = AssetDatabase.FindAssets("Textures_Guns t:Texture2D").FirstOrDefault();
            if (string.IsNullOrEmpty(guid))
                return material;

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            EditorUtility.SetDirty(material);
            return material;
        }

        // Transparent vertex-colored material (fog veils, noise rings); works in Built-in and URP.
        private static Material GetUnlitMaterial(string materialName)
        {
            string path = $"{MaterialFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = Color.white;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Shader FindLitShader()
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                var urp = Shader.Find("Universal Render Pipeline/Lit");
                if (urp != null)
                    return urp;

                var hdrp = Shader.Find("HDRP/Lit");
                if (hdrp != null)
                    return hdrp;
            }

            return Shader.Find("Standard");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void AddSceneToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == path)
                    return;
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ---------------- SerializedObject helpers ----------------

        private static SerializedProperty Find(SerializedObject serialized, string property)
        {
            var result = serialized.FindProperty(property);
            if (result == null)
                Debug.LogError($"[TwomAi] Property '{property}' not found on {serialized.targetObject.GetType().Name}");

            return result;
        }

        private static void SetObject(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            var p = Find(serialized, property);
            if (p == null) return;
            p.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectArray(Object target, string property, Object[] values)
        {
            var serialized = new SerializedObject(target);
            var p = Find(serialized, property);
            if (p == null) return;
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Object target, string property, int value)
        {
            var serialized = new SerializedObject(target);
            var p = Find(serialized, property);
            if (p == null) return;
            p.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum(Object target, string property, int value)
        {
            var serialized = new SerializedObject(target);
            var p = Find(serialized, property);
            if (p == null) return;
            p.enumValueIndex = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string property, float value)
        {
            var serialized = new SerializedObject(target);
            var p = Find(serialized, property);
            if (p == null) return;
            p.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string property, bool value)
        {
            var serialized = new SerializedObject(target);
            var p = Find(serialized, property);
            if (p == null) return;
            p.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Object target, string property, string value)
        {
            var serialized = new SerializedObject(target);
            var p = Find(serialized, property);
            if (p == null) return;
            p.stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetColor(Object target, string property, Color value)
        {
            var serialized = new SerializedObject(target);
            var p = Find(serialized, property);
            if (p == null) return;
            p.colorValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
