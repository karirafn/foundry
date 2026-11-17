Engineering principles. The repository's own style, testing and design conventions win where they differ; the safety rules above always apply.

Private package sources restore through Foundry: when the repository declares one, `RestoreConfigFile` points `dotnet restore`, `build`, `test` and `add package` at a config Foundry generated for this run, so never edit `nuget.config` to fix a restore failure; report the failure instead. `dotnet tool restore` from a private package source is not supported in this worker.
