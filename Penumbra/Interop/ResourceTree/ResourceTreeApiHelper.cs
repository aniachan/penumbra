using Dalamud.Game.ClientState.Objects.Types;
using Penumbra.Api;
using Penumbra.Api.Enums;
using Penumbra.String.Classes;
using Penumbra.UI;

namespace Penumbra.Interop.ResourceTree;

internal static class ResourceTreeApiHelper
{
    public static Dictionary<ushort, IReadOnlyDictionary<string, string[]>> GetResourcePathDictionaries(IEnumerable<(Character, ResourceTree)> resourceTrees)
    {
        var pathDictionaries = new Dictionary<ushort, Dictionary<string, HashSet<string>>>(4);

        foreach (var (gameObject, resourceTree) in resourceTrees)
        {
            if (pathDictionaries.ContainsKey(gameObject.ObjectIndex))
                continue;

            var pathDictionary = new Dictionary<string, HashSet<string>>();
            pathDictionaries.Add(gameObject.ObjectIndex, pathDictionary);

            CollectResourcePaths(pathDictionary, resourceTree);
        }

        return pathDictionaries.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyDictionary<string, string[]>)pair.Value.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()).AsReadOnly());
    }

    private static void CollectResourcePaths(Dictionary<string, HashSet<string>> pathDictionary, ResourceTree resourceTree)
    {
        foreach (var node in resourceTree.FlatNodes)
        {
            if (node.PossibleGamePaths.Length == 0)
                continue;

            var fullPath = node.FullPath.ToPath();
            if (!pathDictionary.TryGetValue(fullPath, out var gamePaths))
            {
                gamePaths = new();
                pathDictionary.Add(fullPath, gamePaths);
            }

            foreach (var gamePath in node.PossibleGamePaths)
                gamePaths.Add(gamePath.ToString());
        }
    }

    public static Dictionary<ushort, IReadOnlyDictionary<nint, (string, string, ChangedItemIcon)>> GetResourcesOfType(IEnumerable<(Character, ResourceTree)> resourceTrees,
        ResourceType type)
    {
        var resDictionaries = new Dictionary<ushort, Dictionary<nint, (string, string, ChangedItemIcon)>>(4);
        foreach (var (gameObject, resourceTree) in resourceTrees)
        {
            if (resDictionaries.ContainsKey(gameObject.ObjectIndex))
                continue;

            var resDictionary = new Dictionary<nint, (string, string, ChangedItemIcon)>();
            resDictionaries.Add(gameObject.ObjectIndex, resDictionary);

            foreach (var node in resourceTree.FlatNodes)
            {
                if (node.Type != type)
                    continue;
                if (resDictionary.ContainsKey(node.ResourceHandle))
                    continue;

                var fullPath = node.FullPath.ToPath();
                resDictionary.Add(node.ResourceHandle, (fullPath, node.Name ?? string.Empty, ChangedItemDrawer.ToApiIcon(node.Icon)));
            }
        }

        return resDictionaries.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyDictionary<nint, (string, string, ChangedItemIcon)>)pair.Value.AsReadOnly());
    }

    public static Dictionary<ushort, IEnumerable<Ipc.ResourceNode>> EncapsulateResourceTrees(IEnumerable<(Character, ResourceTree)> resourceTrees)
    {
        static Ipc.ResourceNode GetIpcNode(ResourceNode[] tree, ResourceNode node) =>
            new()
            {
                ChildrenIndices = node.Children.Select(c => Array.IndexOf(tree, c)).ToArray(),
                Type = node.Type,
                Icon = ChangedItemDrawer.ToApiIcon(node.Icon),
                Name = node.Name,
                GamePath = node.GamePath.Equals(Utf8GamePath.Empty) ? null : node.GamePath.ToString(),
                ActualPath = node.FullPath.ToString(),
                ObjectAddress = node.ObjectAddress,
                ResourceHandle = node.ResourceHandle,
            };

        static IEnumerable<Ipc.ResourceNode> GetIpcNodes(ResourceTree tree)
        {
            var nodes = tree.FlatNodes.ToArray();
            return nodes.Select(n => GetIpcNode(nodes, n)).ToArray();
        }

        var resDictionary = new Dictionary<ushort, IEnumerable<Ipc.ResourceNode>>(4);
        foreach (var (gameObject, resourceTree) in resourceTrees)
        {
            if (resDictionary.ContainsKey(gameObject.ObjectIndex))
                continue;

            resDictionary.Add(gameObject.ObjectIndex, GetIpcNodes(resourceTree));
        }

        return resDictionary;
    }
}
