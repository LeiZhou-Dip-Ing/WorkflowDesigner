# Script library examples

These four scripts exercise the canonical Action property editor and controlled library references.

1. Import `WorkflowRuntime.TestScriptLibrary.dll` through **Manage Script Libraries**. The fixture is built from `tests/Fixtures/WorkflowRuntime.TestScriptLibrary`.
2. Add its exact library identity and version to the current Project.
3. Create scripts in the designer and paste the corresponding `.csx` source. Analyze and run locally before publishing.
4. Publish each script. Its Action then appears in the same Action catalog and method editor as built-in and external-plugin Actions.

For `ScaleNumberScript`, bind `Value` to a variable containing `21`, set `Factor` to `2`, and bind `Result` to a numeric output variable. The result is `42`; a following built-in calculation can produce `43`.

`ExternalLibraryScaleScript` proves controlled managed-DLL resolution. `AsyncScaleScript` verifies awaited execution and output ordering. `PropertyTypesScript` covers string, Boolean, integer, number, enum and explicit picklists, required and optional inputs, defaults, grouping, ordering, validation hints, and multiple outputs.

Project files store only `libraryId` and exact `version`. Machine paths remain in the local cache/runtime catalog and are never serialized into the Project.
