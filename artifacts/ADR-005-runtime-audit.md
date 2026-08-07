# ADR-005-runtime-audit: Integration Point Discovery

## Existing execution chain
The current call chain from process start is extremely limited and does not include an active runtime loop:
1. `Program.Main(string[] args)` begins execution in `SvitloSk.Publisher.Host`.
2. `Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)` constructs the DI container.
3. `ConfigureServices(...)` registers dependencies, including `SynchronizationEngine`, `EditorialDecisionEngine`, and the new assemblies.
4. `builder.Build()` instantiates the Host.
5. `Console.WriteLine("READY FOR BUSINESS IMPLEMENTATION")` executes sequentially.
6. `host.Run()` is invoked, which blocks the main thread waiting for shutdown signals. Because there are zero `IHostedService` or `BackgroundService` implementations registered, the application sits idle indefinitely doing nothing.

## Candidate integration points

### 1. `SvitloSk.Publisher.Runtime.SynchronizationEngine`
- **File**: `src/SvitloSk.Publisher.Runtime/SynchronizationEngine.cs`
- **Responsibility**: Nominally intended by `SYNCHRONIZATION_ENGINE_SPECIFICATION.md` to coordinate state, input, and outputs.
- **Current implementation status**: Shell class throwing `NotImplementedException()` in `MaintainPublisherState()`.
- **Why it is or is not the correct place**: It is the semantically correct place for orchestration. However, it is fundamentally incomplete as it is completely disconnected from any active execution trigger.

### 2. `SvitloSk.Publisher.Host.Program`
- **File**: `src/SvitloSk.Publisher.Host/Program.cs`
- **Responsibility**: Host configuration and startup.
- **Current implementation status**: Fully implements DI registration and calls `host.Run()`.
- **Why it is or is not the correct place**: Not the correct place for business logic. It lacks a `BackgroundService` to act as the runtime loop required to continuously fetch from `IInputPackageProvider` and invoke the `SituationModel`.

## Verdict
**C — No integration point exists. Runtime orchestration has not yet been implemented in the Publisher.**

There is no existing execution loop, hosted service, or operational pipeline that provides an `InputPackage` or repeatedly invokes the `SynchronizationEngine`.
