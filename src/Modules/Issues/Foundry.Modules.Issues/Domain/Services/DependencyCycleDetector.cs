using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.Services;

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

    /// <summary>
    /// The shortest cycle that linking <paramref name="blockerNumber"/> as a blocker of <paramref name="issueNumber"/>
    /// closes, as the path issue → blocker → … → issue; null when the blocker cannot reach the issue through the
    /// existing edges, or when the blocker is the issue itself.
    /// </summary>
    public static DependencyCycle? FindCycleClosedBy(
        IReadOnlyList<DependencyEdge> edges,
        int issueNumber,
        int blockerNumber)
    {
        Dictionary<int, List<int>> adjacency = BuildAdjacency(edges);
        Dictionary<int, int> predecessors = [];
        Queue<int> queue = new([blockerNumber]);
        HashSet<int> visited = [blockerNumber];

        while (queue.Count > 0 && !visited.Contains(issueNumber))
        {
            int current = queue.Dequeue();

            foreach (int neighbour in adjacency.GetValueOrDefault(current, []))
            {
                if (visited.Add(neighbour))
                {
                    predecessors[neighbour] = current;
                    queue.Enqueue(neighbour);
                }
            }
        }

        if (!visited.Contains(issueNumber))
        {
            return null;
        }

        List<int> pathFromBlocker = [issueNumber];

        while (pathFromBlocker[0] != blockerNumber)
        {
            pathFromBlocker.Insert(0, predecessors[pathFromBlocker[0]]);
        }

        return DependencyCycle.Create([issueNumber, ..pathFromBlocker]).Match<DependencyCycle?>(
            (DependencyCycle cycle) => cycle,
            (Error _) => null);
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
