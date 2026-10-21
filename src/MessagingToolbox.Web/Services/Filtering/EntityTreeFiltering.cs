using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Web.Services.Filtering;

/// <summary>Pure tree-shape logic for the explorer: search filtering, flattening and node identity.</summary>
public static class EntityTreeFiltering
{
    public static IReadOnlyList<EntityTreeNodeDto> FilteredTree(IReadOnlyList<EntityTreeNodeDto>? tree, string? searchText)
    {
        if (tree is null)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return tree;
        }

        string search = searchText.Trim();
        return tree.SelectMany(node => FilterNode(node, search)).ToList();
    }

    public static IEnumerable<EntityTreeNodeDto> Flatten(IEnumerable<EntityTreeNodeDto> nodes)
    {
        foreach (EntityTreeNodeDto node in nodes)
        {
            yield return node;

            foreach (EntityTreeNodeDto child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    public static string NodeKey(EntityTreeNodeDto node) => $"{node.Path}|{node.SubQueue}|{node.Kind}";

    private static IEnumerable<EntityTreeNodeDto> FilterNode(EntityTreeNodeDto node, string search)
    {
        // Sub-queue pseudo-nodes (Active/Scheduled/DLQ) never match the filter directly —
        // they stay visible through their entity, not as standalone results.
        if (node.IsSubQueueNode)
        {
            yield break;
        }

        if (node.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            yield return node;
            yield break;
        }

        List<EntityTreeNodeDto> children = node.Children
            .Where(child => !child.IsSubQueueNode)
            .SelectMany(child => FilterNode(child, search))
            .ToList();

        if (children.Count > 0)
        {
            yield return node with
            {
                Children = node.Children.Where(child => child.IsSubQueueNode).Concat(children).ToList(),
            };
        }
    }
}
