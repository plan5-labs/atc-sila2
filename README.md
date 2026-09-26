# atc-sila2

A SiLA2 server for an Automated Thermal Cycler (ATC) lab device, built on C#/.NET with gRPC.

## Tech Stack

| Layer | Technology |
|---|---|
| **Language** | C# |
| **Frameworks** | .NET 10 (Server + Features), .NET Framework 4.8 (CommandExecutor) |
| **RPC** | gRPC over HTTP/2 (ASP.NET Core / Kestrel, TLS + self-signed certs) |
| **Lab standard** | SiLA2 (`SiLA2.Core` / `SiLA2.AspNetCore` v10.1.0), mDNS network discovery |
| **Serialization** | Protocol Buffers (`Google.Protobuf`, `Grpc.Tools`) |
| **Logging** | Serilog (console + file sinks) |
| **Config** | `appsettings.json` (Microsoft.Extensions.Configuration) |

## Architecture (3 projects, `.slnx` solution)

1. **`AutomatedThermalCyclerController.Server`** — ASP.NET Core minimal-hosting web app hosting the SiLA2 server (`ISiLA2Server`), gRPC endpoints, interceptors (logging, metadata/parameter validation), TLS certificate handling, and mDNS service announcement.
2. **`AutomatedThermalCyclerController.Features`** — class library where SiLA features are defined in **FDL XML** (`AutomatedThermalCyclerController-v1_0.sila.xml`), converted to `.proto` via **XSLT** (`sila_base/xslt/fdl2proto.xsl`), then code-generated into C# gRPC stubs at build time.
3. **`AutomatedThermalCyclerCommandExecutor`** — a .NET Framework 4.8 console exe that talks to the physical device. It wraps the vendor's **Java device API** (`atc-api-1.1.5.dll`) through **IKVM** (Java-on-.NET bridge), and exposes its own gRPC service (`Grpc.Core`) that the server consumes.

## Building

### Prerequisites

- **.NET 10 SDK** — the Server and Features projects target `net10.0`
- **A local [sila_base](https://gitlab.com/SiLA2/sila_base) checkout** — every build regenerates code from the FDL XML via `..\..\..\sila_base\xslt\fdl2proto.xsl` (relative to the Features project). With this repo at `~/repo/atc-sila2`, clone it to `~/sila_base`:
  ```bash
  git clone https://gitlab.com/SiLA2/sila_base.git ~/sila_base
  ```
- **Network access to gitlab.com** — the build downloads `SiLAFramework.proto` (cached after the first build)
- **Apple Silicon Macs:** [Rosetta 2](https://support.apple.com/en-us/HT211861) — `Grpc.Tools` ships only x86_64 `protoc`/`grpc_csharp_plugin` binaries:
  ```bash
  softwareupdate --install-rosetta --agree-to-license
  ```

### Build (Server + Features)

```bash
# builds the server and its Features dependency (with codegen: FDL XML → XSLT → .proto → gRPC stubs)
dotnet build AutomatedThermalCyclerController.Server/AutomatedThermalCyclerController.Server.csproj

# or build and run the SiLA2 server (listens on port 5140, per appsettings.json)
dotnet run --project AutomatedThermalCyclerController.Server
```

To build the whole solution, `dotnet build AutomatedThermalCyclerController.slnx` works too — the net10.0 projects build on any platform, but the `AutomatedThermalCyclerCommandExecutor` project fails outside Windows (see below), so on macOS/Linux stick to the Server project build.

### CommandExecutor (Windows only)

`AutomatedThermalCyclerCommandExecutor` is a legacy (non-SDK) csproj targeting .NET Framework 4.8:

- Build it with **Visual Studio / MSBuild on Windows** (`dotnet build` cannot build it)
- It requires the proprietary device DLLs — `atc-api-1.1.5.dll` plus the IKVM 7.2 assemblies — placed in `AutomatedThermalCyclerCommandExecutor/Lib/` (see `Lib/Notice.txt`; these are not included in the repo)

### Build output

Binaries land in the standard `bin/<Configuration>/<TargetFramework>/` folders:

- Server (runnable output, includes `Features/`, `appsettings.json`, licenses): `AutomatedThermalCyclerController.Server/bin/Debug/net10.0/`
- Features library: `AutomatedThermalCyclerController.Features/bin/Debug/net10.0/`

## Tooling

- PowerShell/batch scripts in `Tools/` (protocol file binarization)
- Standard .NET SDK-style projects for the net10.0 apps; legacy-style csproj for the .NET Framework 4.8 executor
