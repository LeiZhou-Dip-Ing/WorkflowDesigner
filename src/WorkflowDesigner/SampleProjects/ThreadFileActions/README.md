# Thread file Actions demo

Import `ThreadFileActionsDemo.json` as a local project, deploy the complete project to Runtime, and run `Main`.

The flow creates a 32 MB source file under `%TEMP%\WorkflowCoreThreadDemo`, starts `CopyFileWorker`, continues executing `Main`, reads the task state with **ThreadInfo**, and joins it with **ThreadWait**. The worker acquires the named resource `thread-file-demo-output`, performs the file work, writes the copied path to the project-global variable `_0copiedFile`, and releases the resource. `ThreadWait` only waits for completion, matching the WinLissy thread model.

Expected log order includes:

1. `Main continues immediately after Thread Start.`
2. `Copy worker running=True`
3. `Thread completed. Copied file=...copied-in-thread.bin`

The output file is intentionally retained so it can be inspected after a Designer run.
