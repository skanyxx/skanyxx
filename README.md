# Skanyxx

SRE Platform for Kubernetes management, agent orchestration, and monitoring. Built on a modular plugin architecture where each UI tab (Agents, Alerts, Chat, etc.) is an independently loadable DLL.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- KAgent running locally (default: `http://localhost:8083`) or remote

## Quick Start

```bash
# Build everything (Core + Host + all 13 modules)
dotnet build Skanyxx.sln

# Run the app
dotnet run --project src/Skanyxx.Host
```

The app starts at **http://localhost:5282** (or https://localhost:7219).

Swagger UI is available at http://localhost:5282/swagger in Development mode.

## Project Structure

```
Skanyxx.sln
├── src/
│   ├── Skanyxx.Core/              # Shared contracts, models, interfaces, services
│   ├── Skanyxx.Host/              # Web host, ModuleLoader, DB, static files, Razor pages
│   │   └── modules/               # Module DLLs loaded at runtime
│   └── Modules/
│       ├── Skanyxx.Module.Agents/
│       ├── Skanyxx.Module.Alerts/
│       ├── Skanyxx.Module.Analytics/
│       ├── Skanyxx.Module.Chat/
│       ├── Skanyxx.Module.CloudTools/
│       ├── Skanyxx.Module.Dashboard/
│       ├── Skanyxx.Module.Debug/
│       ├── Skanyxx.Module.Hooks/
│       ├── Skanyxx.Module.Investigate/
│       ├── Skanyxx.Module.Memory/
│       ├── Skanyxx.Module.Sessions/
│       ├── Skanyxx.Module.Settings/
│       └── Skanyxx.Module.ToolServers/
└── SkanyxxWeb.csproj              # Legacy monolith (deprecated)
```

**Dependency rule:** Modules reference only `Skanyxx.Core`. Modules never reference Host or each other. Cross-module communication uses MediatR.

## Modular Architecture

Each module is a self-contained DLL that gets discovered and loaded at startup by the `ModuleLoader`. Every module implements the `IModule` interface:

```csharp
public interface IModule
{
    string ModuleId { get; }
    string DisplayName { get; }
    string Version { get; }
    IReadOnlyList<string> Dependencies { get; }
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
    Task InitializeAsync(IServiceProvider serviceProvider);
}
```

On build, each module DLL is automatically copied to `src/Skanyxx.Host/modules/`. The Host scans that directory, loads assemblies via `PluginLoadContext`, registers controllers with `AddApplicationPart()`, and wires up DI and MediatR.

## Enable / Disable Modules

Edit `src/Skanyxx.Host/appsettings.json`:

```json
{
  "Modules": {
    "Enabled": {
      "agents": true,
      "alerts": true,
      "chat": false
    }
  }
}
```

Set any module to `false` and its API routes will not be registered. You can also simply remove the DLL from the `modules/` directory.

## Adding a New Module

1. Create a new project under `src/Modules/`:
   ```bash
   dotnet new classlib -n Skanyxx.Module.MyFeature -o src/Modules/Skanyxx.Module.MyFeature
   ```

2. Reference Core and add the `CopyToModules` target in the `.csproj`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net8.0</TargetFramework>
     </PropertyGroup>
     <ItemGroup>
       <FrameworkReference Include="Microsoft.AspNetCore.App" />
       <ProjectReference Include="..\..\Skanyxx.Core\Skanyxx.Core.csproj" />
     </ItemGroup>
     <Target Name="CopyToModules" AfterTargets="Build">
       <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(SolutionDir)src/Skanyxx.Host/modules/" />
       <Copy SourceFiles="$(TargetDir)$(TargetName).deps.json" DestinationFolder="$(SolutionDir)src/Skanyxx.Host/modules/" />
     </Target>
   </Project>
   ```

3. Implement `IModule`:
   ```csharp
   public class MyFeatureModule : IModule
   {
       public string ModuleId => "myfeature";
       public string DisplayName => "My Feature";
       public string Version => "1.0.0";
       public IReadOnlyList<string> Dependencies => Array.Empty<string>();

       public void RegisterServices(IServiceCollection services, IConfiguration configuration)
       {
           // Register your services here
       }

       public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
   }
   ```

4. Add your controller(s) with `[ApiController]` and `[Route("api/[controller]")]`.

5. Add the project to `Skanyxx.sln`:
   ```bash
   dotnet sln add src/Modules/Skanyxx.Module.MyFeature
   ```

6. Build and run. The module is automatically discovered.

## Configuration

All configuration is in `src/Skanyxx.Host/appsettings.json`:

| Section | Description |
|---|---|
| `KAgent` | KAgent API connection (BaseUrl, Port, Protocol, Token) |
| `Kubernetes` | Optional kubeconfig path |
| `AWS` | AWS profile and region for cloud tools |
| `Azure` | Azure config directory |
| `Modules` | Plugin directory and enable/disable flags |

## API Endpoints

Each module exposes REST endpoints under `/api/`:

| Module | Routes |
|---|---|
| Agents | `/api/agents` |
| Alerts | `/api/alerts` |
| Analytics | `/api/analytics` |
| Chat | `/api/chat` |
| CloudTools | `/api/cloud`, `/api/cloudtools` |
| Dashboard | `/api/dashboard` |
| Debug | `/api/debug` |
| Hooks | `/api/hooks` |
| Investigate | `/api/investigate` |
| Memory | `/api/memory` |
| Sessions | `/api/sessions` |
| Settings | `/api/settings` |
| ToolServers | `/api/toolservers` |

Health check: `GET /health`
