# Local runtime data

`action-catalog.json` is a local snapshot written from the Workflow Runtime Action Catalog API.
It is not an independent WPF Action definition and is intentionally excluded from Git.

`workflows/*.draft.json` contains the editor working copy used for offline editing and crash recovery.
The Runtime workflow remains the published source of truth; drafts are intentionally excluded from Git.
