# Local Card Collection

> Historical reference: this name-and-quantity collection belonged to the
> removed legacy server and is not present on this branch. The rewrite
> local domain is the revisioned format-neutral deck store; a future collection
> capability would require its own PLC. See [rewrite-guide.md](rewrite-guide.md).

The legacy server could keep a workstation-local card collection under
`MTGMCP__DATA_DIR/collection`. The first version tracks card name and quantity
only, so it is useful for deck ownership checks without requiring a vendor
account, network access, or printing-specific inventory data.

Use `collection_set` to replace or merge collection rows. It accepts structured
entries, decklist-style text, an existing workspace id, or any combination of
those inputs:

```json
{
  "entries": [
    { "cardName": "Sol Ring", "quantity": 1 },
    { "cardName": "Counterspell", "quantity": 2 }
  ],
  "replace": true
}
```

Set `replace` to `false` to add the submitted quantities to the existing
collection. Decklist text uses the same quantity/name parser as workspace
imports. When `workspaceId` is supplied, included workspace cards are imported
into the collection; excluded categories such as maybeboard are skipped.

Use `collection_get` to inspect the current collection. Use
`collection_diff_workspace` with a `workspaceId` to compare owned quantities with
the workspace's included cards. The diff returns missing copies and known missing
replacement cost when the workspace has cached price snapshots.

Collection writes are local planning-state writes: they are allowed in `plan` and
`apply` operation modes and blocked in `read-only` mode.
