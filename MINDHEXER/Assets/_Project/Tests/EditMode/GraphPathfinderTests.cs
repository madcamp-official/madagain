using NUnit.Framework;
using UnityEngine;
using Game.Bridge;

namespace Game.Sim.Tests
{
    public class GraphPathfinderTests
    {
        static ArenaMapBake BuildBake()
        {
            return new ArenaMapBake
            {
                mapVersion = 7,
                nodes = new[]
                {
                    Node(0, new Vector3(0, 0, 0), 0),
                    Node(1, new Vector3(5, 0, 0), 0),
                    Node(2, new Vector3(5, 5, 0), 1),
                    Node(3, new Vector3(10, 5, 0), 1),
                    Node(4, new Vector3(20, 9, 0), 2), // 고립
                },
                links = new[]
                {
                    Link(0, 0, 1, NavTraversalType.Walk, new Vector3(5,0,0)),
                    Link(1, 1, 0, NavTraversalType.Walk, Vector3.zero),
                    Link(2, 1, 2, NavTraversalType.BoostUp, new Vector3(5,5,0)),
                    Link(3, 2, 3, NavTraversalType.Walk, new Vector3(10,5,0)),
                    Link(4, 3, 2, NavTraversalType.Walk, new Vector3(5,5,0)),
                    Link(5, 3, 1, NavTraversalType.DropDown, new Vector3(5,0,0)),
                }
            };
        }

        [Test]
        public void DirectedTraversal_ReturnsBoostAndDrop_WithoutInventingReverseLinks()
        {
            GraphPathfinder graph = GraphPathfinder.FromBake(BuildBake());
            Assert.AreEqual(MoveKind.Boost, graph.NextStep(new Vector3(5,0,0), new Vector3(5,5,0), -1).kind);
            Assert.AreEqual(MoveKind.Drop, graph.NextStep(new Vector3(10,5,0), Vector3.zero, -1).kind);

            // 2층에서 1층으로는 Drop 경유가 있지만, 2층→부스터 시작점의 역링크를 자동 생성하지 않는다.
            PathStep reverseBoost = graph.NextStep(new Vector3(5,5,0), new Vector3(5,0,0), -1);
            Assert.AreEqual(MoveKind.Walk, reverseBoost.kind); // 2→3 Walk 후 3→1 Drop
        }

        [Test]
        public void UnreachableNode_ReturnsNone_InsteadOfStraightLineFallback()
        {
            GraphPathfinder graph = GraphPathfinder.FromBake(BuildBake());
            Assert.AreEqual(MoveKind.None,
                graph.NextStep(Vector3.zero, new Vector3(20,9,0), -1).kind);
        }

        [Test]
        public void FloorLookup_DistinguishesSameHorizontalAreaByHeight()
        {
            GraphPathfinder graph = GraphPathfinder.FromBake(BuildBake());
            Assert.AreEqual(0, graph.FloorIdAt(new Vector3(5,0.1f,0)));
            Assert.AreEqual(1, graph.FloorIdAt(new Vector3(5,4.9f,0)));
        }

        [Test]
        public void FromBake_PreservesMapVersion()
        {
            GraphPathfinder graph = GraphPathfinder.FromBake(BuildBake());
            Assert.AreEqual(7, graph.MapVersion);
        }

        [Test]
        public void InvalidBake_IsRejectedBeforePrediction()
        {
            ArenaMapBake invalid = BuildBake();
            invalid.links[0].toNodeId = 999;
            Assert.Throws<System.ArgumentException>(() => GraphPathfinder.FromBake(invalid));
        }

        static ArenaNavNode Node(int id, Vector3 p, int floor)
            => new ArenaNavNode { nodeId=id, position=p, floorId=floor, areaFlags=MapAreaFlags.Playable };

        static ArenaNavLink Link(int id, int from, int to, NavTraversalType type, Vector3 landing)
            => new ArenaNavLink { linkId=id, fromNodeId=from, toNodeId=to, traversalType=type,
                traversalTicks=12, agentMask=-1, landingPosition=landing };
    }
}
