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
    /// The shortest cycle closed by the first of <paramref name="blockerNumbers"/>, in the given order, that would
    /// close one when linked as a blocker of <paramref name="issueNumber"/>, as the path issue → blocker → … → issue;
    /// null when no blocker can reach the issue through <paramref name="storedEdges"/> and the other new links.
    /// </summary>
    public static DependencyCycle? FindFirstCycleClosedBy(
        IReadOnlyList<DependencyEdge> storedEdges,
        int issueNumber,
        IReadOnlyList<int> blockerNumbers)
    {
        IReadOnlyList<DependencyEdge> edges =
        [
            .. storedEdges,
            .. blockerNumbers.Select((int blocker) => new DependencyEdge(issueNumber, blocker)),
        ];
        Dictionary<int, List<int>> adjacency = BuildAdjacency(edges);

        foreach (int blockerNumber in blockerNumbers)
        {
            DependencyCycle? cycle = FindCycleClosedBy(adjacency, issueNumber, blockerNumber);

            if (cycle is not null)
            {
                return cycle;
            }
        }

        return null;
    }

    private static DependencyCycle? FindCycleClosedBy(
        Dictionary<int, List<int>> adjacency,
        int issueNumber,
        int blockerNumber)
    {
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

        List<int> pathToBlocker = [issueNumber];

        while (pathToBlocker[^1] != blockerNumber)
        {
            pathToBlocker.Add(predecessors[pathToBlocker[^1]]);
        }

        pathToBlocker.Reverse();

        return DependencyCycle.Create([issueNumber, .. pathToBlocker]).Match<DependencyCycle>(
            (DependencyCycle cycle) => cycle,
            (Error error) => throw new InvalidOperationException(
                $"The detector built an invalid dependency cycle: {error.Message}"));
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
