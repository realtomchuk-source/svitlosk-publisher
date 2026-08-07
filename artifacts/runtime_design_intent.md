# Runtime Design Intent Reconstruction

## 1. Existing runtime pieces

- **`Program`** (`src/SvitloSk.Publisher.Host/Program.cs`)
  - **Responsibility**: Configures the generic host and registers DI dependencies.
  - **Implementation State**: Fully implemented.
  - **References**: References `Microsoft.Extensions.Hosting`, Core, Execution, Runtime, Channels, and Adapters.
  - **Callers**: N/A (Entry point).
  - **Callees**: `SynchronizationEngine`, `EditorialDecisionEngine`, `GraphicPublisher`, etc. (via DI registration).

- **`SynchronizationEngine`** (`src/SvitloSk.Publisher.Runtime/SynchronizationEngine.cs`)
  - **Responsibility**: Unknown runtime coordination (implies synchronization state maintenance).
  - **Implementation State**: Shell (Throws `NotImplementedException`).
  - **References**: `ISynchronizationEngine`.
  - **Callers**: Registered in DI by `Program.cs`. Never invoked.
  - **Callees**: None.

- **`IInputPackageProvider`** (`src/SvitloSk.Publisher.Domain/IInputPackageProvider.cs`)
  - **Responsibility**: Provides the latest `InputPackage`.
  - **Implementation State**: Interface only (`GetLatest()`).
  - **References**: `InputPackage`.
  - **Callers**: None.
  - **Callees**: N/A.

- **`SituationModel`** (`src/SvitloSk.Publisher.Core/SituationModel.cs`)
  - **Responsibility**: Detects situations by comparing `Edition`, `InputPackage`, and `InfrastructureState`.
  - **Implementation State**: Minimal slice implemented (S-01, S-11, S-12, S-14, S-15, S-16).
  - **References**: `ISituationModel`, `Edition`, `InputPackage`.
  - **Callers**: None (referenced only in tests).
  - **Callees**: None.

- **`ISynchronizationRegistry`** (`src/SvitloSk.Publisher.Runtime/SynchronizationRegistry.cs`)
  - **Responsibility**: Unknown registry functionality.
  - **Implementation State**: Shell/Interface.
  - **References**: None identified.
  - **Callers**: Registered in DI by `Program.cs`. Never invoked.
  - **Callees**: None.

## 2. Existing execution chain

`Program.Main(args)`
> `Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)`
> `ConfigureServices(...)` (DI bindings created)
> `builder.Build()`
> `Console.WriteLine("READY FOR BUSINESS IMPLEMENTATION")`
> `host.Run()`
> (Thread blocks indefinitely)

## 3. Intended execution chain

Based EXCLUSIVELY on the evidence inside the Publisher repository:

`Host`
> UNKNOWN (No evidence of HostedService, BackgroundService, Timer, or Scheduler connecting Host to any engine).

UNKNOWN
> `SynchronizationEngine.MaintainPublisherState()` (Method exists, but the trigger is completely undocumented in code).

UNKNOWN
> `IInputPackageProvider.GetLatest()` (Interface exists, but callers are unknown).

UNKNOWN
> `SituationModel.Detect()` (Method exists, but callers are unknown).

UNKNOWN
> `EditorialDecisionEngine.Evaluate()` (Method exists, but callers are unknown).

**Note:** While `SynchronizationEngine.cs` contains the comment `// Source: SYNCHRONIZATION_ENGINE_SPECIFICATION.md`, there is zero internal repository evidence (no interfaces, no comments, no DI construction) linking `SynchronizationEngine` to `IInputPackageProvider` or `SituationModel`. Therefore, the intended execution chain inside the implementation repository is UNKNOWN.

## 4. Missing links

To establish a functioning runtime based on the existing fragments, the following exact links are missing:
- A trigger mechanism from `host.Run()` to invoke `SynchronizationEngine.MaintainPublisherState()`.
- An injected dependency inside `SynchronizationEngine` to consume `IInputPackageProvider`.
- An injected dependency inside `SynchronizationEngine` to consume `ISituationModel`.
- A mechanism to convert or map `SituationModel` output (`DetectedSituation`) into inputs for the `EditorialDecisionEngine`.

## 5. Evidence

- **Filename**: `src/SvitloSk.Publisher.Host/Program.cs` | **Method**: `Main` | **Evidence**: Confirms execution chain stops at `host.Run()` without starting any custom workers.
- **Filename**: `src/SvitloSk.Publisher.Runtime/SynchronizationEngine.cs` | **Method**: `MaintainPublisherState` | **Evidence**: Confirms method throws `NotImplementedException` and lacks constructor injection dependencies for `IInputPackageProvider` or `ISituationModel`.
- **Filename**: `src/SvitloSk.Publisher.Domain/IInputPackageProvider.cs` | **Method**: `GetLatest` | **Evidence**: Found using semantic search; zero usages across the entire `src/` directory.

