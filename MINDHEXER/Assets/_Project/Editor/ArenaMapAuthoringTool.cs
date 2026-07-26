using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Game.Bridge;
using Game.Sim;
using Game.View;

namespace Game.EditorTools
{
    public static class ArenaMapAuthoringTool
    {
        const string ArenaScene = "Assets/_Project/Prefabs/Arenas/Arena_3.unity";
        const string AuthoringObjectName = "[Prediction Map Authoring]";
        const float JumpMinHeight = 0.45f;
        const float JumpMaxHeight = 4.25f;
        const float JumpMinHorizontal = 0.9f;
        const float JumpMaxHorizontal = 8f;
        const int JumpTraversalTicks = 30;
        const int JumpArcSamples = 12;


        [MenuItem("Tools/Prediction/Bake Arena Graph Into Scene")]
        public static void BakeArenaGraphIntoScene()
        {
            var scene = EditorSceneManager.OpenScene(ArenaScene, OpenSceneMode.Single);
            GameObject host = GameObject.Find(AuthoringObjectName);
            if (host == null)
                host = new GameObject(AuthoringObjectName);

            ArenaMapAuthoring authoring = host.GetComponent<ArenaMapAuthoring>();
            if (authoring == null)
                authoring = host.AddComponent<ArenaMapAuthoring>();

            ArenaMapBake bake = GraphPathfinder.CreateArenaBake();
            Undo.RecordObject(authoring, "Bake prediction arena graph");
            authoring.mapVersion = bake.mapVersion;
            authoring.nodes = (ArenaNavNode[])bake.nodes.Clone();
            authoring.links = (ArenaNavLink[])bake.links.Clone();
            EditorUtility.SetDirty(authoring);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException($"Failed to save {ArenaScene}");

            Debug.Log($"[Prediction] Saved mapVersion {bake.mapVersion}, {bake.nodes.Length} nodes, {bake.links.Length} links to {ArenaScene}.");
        }

        [MenuItem("Tools/Prediction/Bake Arena Graph From Scene NavMesh")]
        public static void BakeArenaGraphFromSceneNavMesh()
        {
            var scene = EditorSceneManager.OpenScene(ArenaScene, OpenSceneMode.Single);
            var surfaceHost = new GameObject("[Prediction NavMesh Bake]");
            var surface = surfaceHost.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            if (triangulation.indices == null || triangulation.indices.Length == 0)
                throw new System.InvalidOperationException("Runtime NavMesh triangulation is empty.");

            ArenaMapBake bake = BuildDeterministicBake(triangulation, 5f, 64);
            EnsureConnected(bake);
            Object.DestroyImmediate(surfaceHost);

            GameObject host = GameObject.Find(AuthoringObjectName);
            if (host == null) host = new GameObject(AuthoringObjectName);
            ArenaMapAuthoring authoring = host.GetComponent<ArenaMapAuthoring>();
            if (authoring == null) authoring = host.AddComponent<ArenaMapAuthoring>();

            authoring.mapVersion = Mathf.Max(3, authoring.mapVersion + 1);
            authoring.nodes = bake.nodes;
            authoring.links = bake.links;
            EditorUtility.SetDirty(authoring);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException($"Failed to save {ArenaScene}");

            Debug.Log($"[Prediction] NavMesh bake saved: mapVersion {authoring.mapVersion}, {bake.nodes.Length} nodes, {bake.links.Length} links.");
        }

        static ArenaMapBake BuildDeterministicBake(NavMeshTriangulation triangulation, float cellSize, int maxNodes)
        {
            var cells = new SortedDictionary<string, Vector3>();
            for (int i = 0; i < triangulation.indices.Length; i += 3)
            {
                Vector3 a = triangulation.vertices[triangulation.indices[i]];
                Vector3 b = triangulation.vertices[triangulation.indices[i + 1]];
                Vector3 c = triangulation.vertices[triangulation.indices[i + 2]];
                Vector3 center = (a + b + c) / 3f;
                string key = $"{Mathf.RoundToInt(center.x / cellSize):D6}:" +
                             $"{Mathf.RoundToInt(center.y / 3f):D6}:" +
                             $"{Mathf.RoundToInt(center.z / cellSize):D6}";
                if (!cells.ContainsKey(key)) cells.Add(key, center);
            }

            var candidates = new List<Vector3>(cells.Values);
            var selected = new List<Vector3>();
            Vector3 reference = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            int seed = 0;
            float seedDistance = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                float distance = (candidates[i] - reference).sqrMagnitude;
                if (distance < seedDistance) { seedDistance = distance; seed = i; }
            }
            if (candidates.Count > 0) selected.Add(candidates[seed]);
            while (selected.Count < maxNodes && selected.Count < candidates.Count)
            {
                int best = -1;
                float bestDistance = -1f;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (selected.Contains(candidates[i])) continue;
                    float nearest = float.MaxValue;
                    bool connected = false;
                    for (int j = 0; j < selected.Count; j++)
                    {
                        nearest = Mathf.Min(nearest, (candidates[i] - selected[j]).sqrMagnitude);
                        if (!connected)
                        {
                            bool directWalk = Vector3.Distance(candidates[i], selected[j]) <= cellSize * 6f
                                && !NavMesh.Raycast(selected[j], candidates[i], out _, NavMesh.AllAreas);
                            bool jumpReach = IsDoubleJumpGeometry(selected[j], candidates[i]);
                            connected = directWalk || jumpReach;
                        }
                    }
                    if (!connected) continue;
                    if (nearest > bestDistance)
                    {
                        bestDistance = nearest;
                        best = i;
                    }
                }
                if (best < 0) break;
                selected.Add(candidates[best]);
            }
            selected.Sort(CompareVector);

            var nodes = new ArenaNavNode[selected.Count];
            for (int i = 0; i < selected.Count; i++)
                nodes[i] = new ArenaNavNode
                {
                    nodeId = i,
                    position = selected[i],
                    floorId = Mathf.RoundToInt(selected[i].y / 3f),
                    areaFlags = MapAreaFlags.Playable,
                };

            var links = new List<ArenaNavLink>();
            int linkId = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                var neighbors = new List<int>();
                for (int j = 0; j < nodes.Length; j++)
                    if (i != j) neighbors.Add(j);
                neighbors.Sort((x, y) =>
                {
                    int byDistance = (nodes[x].position - nodes[i].position).sqrMagnitude
                        .CompareTo((nodes[y].position - nodes[i].position).sqrMagnitude);
                    return byDistance != 0 ? byDistance : nodes[x].nodeId.CompareTo(nodes[y].nodeId);
                });

                for (int n = 0; n < neighbors.Count; n++)
                {
                    int j = neighbors[n];
                    if (Vector3.Distance(nodes[i].position, nodes[j].position) > cellSize * 6f) break;
                    if (NavMesh.Raycast(nodes[i].position, nodes[j].position, out _, NavMesh.AllAreas)) continue;
                    float heightDelta = nodes[j].position.y - nodes[i].position.y;
                    NavTraversalType type = heightDelta > 0.75f ? NavTraversalType.RampUp
                        : heightDelta < -0.75f ? NavTraversalType.RampDown
                        : NavTraversalType.Walk;
                    links.Add(new ArenaNavLink
                    {
                        linkId = linkId++,
                        fromNodeId = i,
                        toNodeId = j,
                        traversalType = type,
                        traversalTicks = 30,
                        heightDelta = heightDelta,
                        agentMask = -1,
                    });
                }
            }

            int jumpCount = AddValidatedJumpLinks(nodes, links, ref linkId);
            if (jumpCount > 0)
            {
                var hasIncomingJump = new bool[nodes.Length];
                for (int i = 0; i < links.Count; i++)
                    if (links[i].traversalType == NavTraversalType.JumpUp)
                        hasIncomingJump[links[i].toNodeId] = true;
                for (int i = 0; i < nodes.Length; i++)
                    if (hasIncomingJump[i])
                        nodes[i].areaFlags |= MapAreaFlags.EscapeCandidate;
            }
            Debug.Log($"[Prediction] Auto-authored {jumpCount} validated JumpUp links.");
            return new ArenaMapBake { nodes = nodes, links = links.ToArray() };
        }

        static int AddValidatedJumpLinks(
            ArenaNavNode[] nodes, List<ArenaNavLink> links, ref int linkId)
        {
            int added = 0;
            for (int from = 0; from < nodes.Length; from++)
            {
                for (int to = 0; to < nodes.Length; to++)
                {
                    if (from == to || HasLink(links, from, to)) continue;
                    Vector3 start = nodes[from].position;
                    Vector3 landing = nodes[to].position;
                    if (!IsDoubleJumpGeometry(start, landing)) continue;

                    // Direct NavMesh visibility means this is a ramp/stair/walk edge, not a jump.
                    if (!NavMesh.Raycast(start, landing, out _, NavMesh.AllAreas)) continue;
                    if (!HasClearJumpArc(start, landing)) continue;

                    links.Add(new ArenaNavLink
                    {
                        linkId = linkId++,
                        fromNodeId = from,
                        toNodeId = to,
                        traversalType = NavTraversalType.JumpUp,
                        traversalTicks = JumpTraversalTicks,
                        heightDelta = landing.y - start.y,
                        agentMask = -1,
                        landingPosition = landing,
                        landingSlotCount = 1,
                        landingSpread = 0f,
                    });
                    added++;
                }
            }
            return added;
        }

        static bool HasLink(List<ArenaNavLink> links, int from, int to)
        {
            for (int i = 0; i < links.Count; i++)
                if (links[i].fromNodeId == from && links[i].toNodeId == to)
                    return true;
            return false;
        }

        static bool IsDoubleJumpGeometry(Vector3 start, Vector3 landing)
        {
            float height = landing.y - start.y;
            Vector2 flat = new Vector2(landing.x - start.x, landing.z - start.z);
            float horizontal = flat.magnitude;
            return height >= JumpMinHeight && height <= JumpMaxHeight
                && horizontal >= JumpMinHorizontal && horizontal <= JumpMaxHorizontal;
        }

        static bool HasClearJumpArc(Vector3 start, Vector3 landing)
        {
            // Conservative double-jump envelope. The actual candidate is still replayed by SimStep;
            // this bake-time test only rejects walls, ceilings, and occupied landing capsules.
            float heightDelta = landing.y - start.y;
            float arcHeight = Mathf.Max(2.1f, heightDelta + 0.8f);
            float radius = SimConfig.PlayerRadius * 0.9f;
            for (int sample = 1; sample < JumpArcSamples; sample++)
            {
                float t = (float)sample / JumpArcSamples;
                Vector3 feet = Vector3.Lerp(start, landing, t);
                feet.y += 4f * arcHeight * t * (1f - t);
                Vector3 bottom = feet + Vector3.up * (radius + 0.08f);
                Vector3 top = feet + Vector3.up * (SimConfig.PlayerHeight - radius);
                if (Physics.CheckCapsule(bottom, top, radius, Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore))
                    return false;
            }

            Vector3 landingBottom = landing + Vector3.up * (radius + 0.08f);
            Vector3 landingTop = landing + Vector3.up * (SimConfig.PlayerHeight - radius);
            return !Physics.CheckCapsule(landingBottom, landingTop, radius,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        static int CompareVector(Vector3 a, Vector3 b)
        {
            int x = a.x.CompareTo(b.x);
            if (x != 0) return x;
            int y = a.y.CompareTo(b.y);
            return y != 0 ? y : a.z.CompareTo(b.z);
        }

        static void EnsureConnected(ArenaMapBake bake)
        {
            if (bake.nodes.Length == 0) throw new System.InvalidOperationException("Generated graph is empty.");
            var reached = new bool[bake.nodes.Length];
            var queue = new Queue<int>();
            reached[0] = true;
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                for (int i = 0; i < bake.links.Length; i++)
                {
                    if (bake.links[i].fromNodeId != node) continue;
                    int next = bake.links[i].toNodeId;
                    if (next < 0 || next >= reached.Length || reached[next]) continue;
                    reached[next] = true;
                    queue.Enqueue(next);
                }
            }
            int count = 0;
            for (int i = 0; i < reached.Length; i++) if (reached[i]) count++;
            if (count != bake.nodes.Length)
                throw new System.InvalidOperationException(
                    $"Generated graph is disconnected: {count}/{bake.nodes.Length} nodes reachable.");
        }

        [MenuItem("Tools/Prediction/Validate Arena Graph Against NavMesh")]
        public static void ValidateArenaGraphAgainstNavMesh()
        {
            EditorSceneManager.OpenScene(ArenaScene, OpenSceneMode.Single);
            ArenaMapAuthoring authoring = Object.FindFirstObjectByType<ArenaMapAuthoring>();
            if (authoring == null)
                throw new System.InvalidOperationException("ArenaMapAuthoring is missing.");

            var surfaceHost = new GameObject("[Prediction NavMesh Validation]");
            var surface = surfaceHost.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            int mismatches = 0;
            int jumpLinks = 0;
            for (int i = 0; i < authoring.nodes.Length; i++)
            {
                Vector3 node = authoring.nodes[i].position;
                bool found = NavMesh.SamplePosition(node, out NavMeshHit hit, 12f, NavMesh.AllAreas);
                float distance = found ? Vector3.Distance(node, hit.position) : float.PositiveInfinity;
                if (!found || distance > 1.5f)
                {
                    mismatches++;
                    Debug.LogWarning($"[Prediction] node {authoring.nodes[i].nodeId}: authored={node}, nearest={(found ? hit.position.ToString() : "none")}, distance={distance:0.00}");
                }
            }

            for (int i = 0; i < authoring.links.Length; i++)
                if (authoring.links[i].traversalType == NavTraversalType.JumpUp)
                    jumpLinks++;

            Object.DestroyImmediate(surfaceHost);
            Debug.Log($"[Prediction] NavMesh validation: {authoring.nodes.Length - mismatches}/{authoring.nodes.Length} matched, " +
                      $"{jumpLinks} JumpUp links, mapVersion {authoring.mapVersion}.");
        }

        [MenuItem("Tools/Gameplay/Configure Arena Monster Test Spawns")]
        public static void ConfigureArenaMonsterTestSpawns()
        {
            var scene = EditorSceneManager.OpenScene(ArenaScene, OpenSceneMode.Single);
            MapSpawnConfig config = Object.FindFirstObjectByType<MapSpawnConfig>();
            if (config == null)
                throw new System.InvalidOperationException("MapSpawnConfig is missing from Arena_test.");
            if (config.entries == null || config.entries.Length == 0 || config.entries[0].point == null)
                throw new System.InvalidOperationException("MapSpawnConfig needs at least one spawn point.");

            Transform point = config.entries[0].point;
            config.autoSpawnOnStart = true;
            config.intervalTicks = 45;
            config.cap = 10;
            config.entries = new[]
            {
                new SpawnEntry { point = point, kind = MobKind.Grunt },
                new SpawnEntry { point = point, kind = MobKind.Pinky },
                new SpawnEntry { point = point, kind = MobKind.Soldier },
                new SpawnEntry { point = point, kind = MobKind.Caco },
                new SpawnEntry { point = point, kind = MobKind.Large },
            };
            EditorUtility.SetDirty(config);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException($"Failed to save {ArenaScene}");
            Debug.Log("[Gameplay] Arena monster test spawns configured: 5 kinds, cap 10, interval 45 ticks.");
        }
    }
}
