namespace Foundry.WebApi.Modules.Issues.Domain;

public static class DependencyCycleDetector
{
    private enum VisitState { Unvisited, InStack, Done }

    // Frame pushed onto the explicit stack.
    // NeighbourIndex tracks which neighbour to process next, so the loop
    // can resume after a child is pushed without recursion.
    private readonly record struct DfsFrame(int Node, int NeighbourIndex);

    public static IReadOnlyList<IReadOnlyList<int>> DetectCycles(IReadOnlyList<DependencyEdge> edges)
    {
        Dictionary<int, List<int>> adjacency = BuildAdjacency(edges);
        Dictionary<int, VisitState> state = [];
        List<IReadOnlyList<int>> cycles = [];

        foreach (int root in adjacency.Keys)
        {
            if (state.GetValueOrDefault(root) != VisitState.Unvisited)
            {
                continue;
            }

            DfsIterative(root, adjacency, state, cycles);
        }

        return cycles;
    }

    private static void DfsIterative(
        int root,
        Dictionary<int, List<int>> adjacency,
        Dictionary<int, VisitState> state,
        List<IReadOnlyList<int>> cycles)
    {
        // path mirrors the recursive call stack — nodes currently InStack.
        List<int> path = [];
        Stack<DfsFrame> stack = new();

        stack.Push(new DfsFrame(root, 0));
        state[root] = VisitState.InStack;
        path.Add(root);

        while (stack.Count > 0)
        {
            DfsFrame frame = stack.Peek();
            List<int> neighbours = adjacency.GetValueOrDefault(frame.Node, []);

            if (frame.NeighbourIndex >= neighbours.Count)
            {
                // All neighbours processed — backtrack.
                stack.Pop();
                path.RemoveAt(path.Count - 1);
                state[frame.Node] = VisitState.Done;
                continue;
            }

            // Advance the neighbour index on the top frame.
            stack.Pop();
            stack.Push(frame with { NeighbourIndex = frame.NeighbourIndex + 1 });

            int neighbour = neighbours[frame.NeighbourIndex];
            VisitState neighbourState = state.GetValueOrDefault(neighbour);

            if (neighbourState == VisitState.InStack)
            {
                int cycleStart = path.IndexOf(neighbour);
                cycles.Add(path[cycleStart..]);
            }
            else if (neighbourState == VisitState.Unvisited)
            {
                state[neighbour] = VisitState.InStack;
                path.Add(neighbour);
                stack.Push(new DfsFrame(neighbour, 0));
            }
        }
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

            adjacency.TryAdd(edge.BlockedByIssueNumber, []);
        }

        return adjacency;
    }
}
