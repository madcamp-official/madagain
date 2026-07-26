using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Scene-owned source of truth for the immutable prediction graph.
    /// Increment mapVersion whenever walkability, heights, links, collision, or LOS changes.
    /// </summary>
    public sealed class ArenaMapAuthoring : MonoBehaviour
    {
        [Min(1)] public int mapVersion = 1;
        public ArenaNavNode[] nodes = System.Array.Empty<ArenaNavNode>();
        public ArenaNavLink[] links = System.Array.Empty<ArenaNavLink>();

        public ArenaMapBake BuildBake()
        {
            return new ArenaMapBake
            {
                mapVersion = mapVersion,
                nodes = nodes ?? System.Array.Empty<ArenaNavNode>(),
                links = links ?? System.Array.Empty<ArenaNavLink>(),
            };
        }
    }
}
