import { TaskListItem } from '../../utils/agent-activity-format';

/** A task as the list renders it: `fresh` names the change that plays once, null for none. */
export interface TaskListRow extends TaskListItem {
  readonly fresh: 'done' | 'current' | null;
}

/** The rows for `items`, flagging tasks that became done or current since `previous`; null is the first render. */
export function taskListRows(
  items: readonly TaskListItem[],
  previous: readonly TaskListItem[] | null,
): readonly TaskListRow[] {
  const before = new Map(previous?.map((item) => [item.id, item.state]) ?? []);
  return items.map((item) => {
    const was = previous === null ? item.state : before.get(item.id);
    const changed = was !== item.state;
    const fresh =
      changed && (item.state === 'done' || item.state === 'current') ? item.state : null;
    return { ...item, fresh };
  });
}

export function sameTaskItems(a: readonly TaskListItem[], b: readonly TaskListItem[]): boolean {
  return (
    a.length === b.length &&
    a.every(
      (item, i) =>
        item.id === b[i].id &&
        item.text === b[i].text &&
        item.state === b[i].state &&
        item.subagent === b[i].subagent,
    )
  );
}
