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

## Tooling

- PowerShell/batch scripts in `Tools/` (protocol file binarization)
- Standard .NET SDK-style projects for the net10.0 apps; legacy-style csproj for the .NET Framework 4.8 executor
