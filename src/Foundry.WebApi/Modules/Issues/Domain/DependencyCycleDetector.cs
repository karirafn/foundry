namespace Foundry.WebApi.Modules.Issues.Domain;

public static class DependencyCycleDetector
{
    private enum VisitState { Unvisited, InStack, Done }

    public static IReadOnlyList<IReadOnlyList<int>> DetectCycles(IReadOnlyList<DependencyEdge> edges)
    {
        Dictionary<int, List<int>> adjacency = BuildAdjacency(edges);
        Dictionary<int, VisitState> state = [];
        List<IReadOnlyList<int>> cycles = [];

        foreach (int node in adjacency.Keys)
        {
            if (state.GetValueOrDefault(node) == VisitState.Unvisited)
            {
                Dfs(node, adjacency, state, [], cycles);
            }
        }

        return cycles;
    }

    private static void Dfs(
        int node,
        Dictionary<int, List<int>> adjacency,
        Dictionary<int, VisitState> state,
        List<int> path,
        List<IReadOnlyList<int>> cycles)
    {
        state[node] = VisitState.InStack;
        path.Add(node);

        foreach (int neighbour in adjacency.GetValueOrDefault(node, []))
        {
            VisitState neighbourState = state.GetValueOrDefault(neighbour);

            if (neighbourState == VisitState.InStack)
            {
                int cycleStart = path.IndexOf(neighbour);
                cycles.Add(path[cycleStart..]);
            }
            else if (neighbourState == VisitState.Unvisited)
            {
                Dfs(neighbour, adjacency, state, path, cycles);
            }
        }

        path.RemoveAt(path.Count - 1);
        state[node] = VisitState.Done;
    }

    private static Dictionary<int, List<int>> BuildAdjacency(IReadOnlyList<DependencyEdge> edges)
    {
        Dictionary<int, List<int>> adjacency = [];

        foreach (DependencyEdge edge in edges)
        {
            if (!adjacency.TryGetValue(edge.IssueNumber, out List<int>? neighbours))
            {
                neighbours = [];
                adjacency[edge.IssueNumber] = neighbours;
            }

            neighbours.Add(edge.BlockedByIssueNumber);

            if (!adjacency.ContainsKey(edge.BlockedByIssueNumber))
            {
                adjacency[edge.BlockedByIssueNumber] = [];
            }
        }

        return adjacency;
    }
}
