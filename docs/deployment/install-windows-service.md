# RansomGuard-CM Agent — Windows Service Installation Guide

## Prerequisites

- Windows 10/11 Pro/Enterprise (or Windows 7 SP1 for legacy)
- Administrator privileges
- .NET 8.0 Runtime (included if using self-contained publish)

## Build and Publish

### Self-contained single-file executable (recommended)

```powershell
cd agent\src
dotnet publish RansomGuard.Agent.Service -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output: `agent\src\RansomGuard.Agent.Service\bin\Release\net8.0\win-x64\publish\RansomGuard.Agent.Service.exe`

### Framework-dependent (requires .NET 8 runtime on target)

```powershell
dotnet publish RansomGuard.Agent.Service -c Release -r win-x64
```

## Installation

### Using PowerShell scripts

1. Open PowerShell as Administrator
2. Navigate to the project root

```powershell
# Install the service
.\scripts\install-service.ps1

# Start the service
.\scripts\start-service.ps1

# Verify it's running
Get-Service -Name "RansomGuard-CM"
```

### Manual installation with sc.exe

```powershell
# Create the service
sc.exe create "RansomGuard-CM" binPath= "C:\Path\To\RansomGuard.Agent.Service.exe" start= delayed-auto DisplayName= "RansomGuard-CM Agent"

# Set description
sc.exe description "RansomGuard-CM" "RansomGuard-CM Anti-Ransomware Agent"

# Configure failure recovery
sc.exe failure "RansomGuard-CM" reset= 86400 actions= restart/5000/restart/10000/restart/60000

# Start
sc.exe start "RansomGuard-CM"
```

## Configuration

Configuration file: `appsettings.json` (next to the executable)

Key settings:

| Setting | Default | Description |
|---------|---------|-------------|
| Agent:Identity:Id | auto-generated | Unique agent GUID |
| Agent:Detection:WatchPaths | %USERPROFILE%\Desktop\RansomGuard-TestZone | Paths to monitor |
| Agent:Server:BaseUrl | https://localhost:5001 | Management server URL |
| Agent:Database:ConnectionString | Data Source=%ProgramData%\RansomGuard-CM\data\agent.db | SQLite database path |

## File Locations

| Purpose | Path |
|---------|------|
| Database | %ProgramData%\RansomGuard-CM\data\agent.db |
| Logs | %ProgramData%\RansomGuard-CM\logs\ |
| Configuration | Next to executable (appsettings.json) |

## Service Recovery

The service is configured with automatic recovery:

- **1st failure**: Restart after 5 seconds
- **2nd failure**: Restart after 10 seconds
- **Subsequent failures**: Restart after 60 seconds
- **Reset counter**: After 24 hours

## Management

```powershell
# Start
.\scripts\start-service.ps1

# Stop
.\scripts\stop-service.ps1

# Uninstall (preserves data and logs)
.\scripts\uninstall-service.ps1

# Check status
Get-Service -Name "RansomGuard-CM"

# View logs
Get-Content "$env:ProgramData\RansomGuard-CM\logs\agent-*.log" -Tail 50
```

## Troubleshooting

1. **Service won't start**: Check Windows Event Log (Application) for "RansomGuard-CM" source
2. **Configuration errors**: The agent validates config at startup and logs specific errors
3. **Permission issues**: Ensure the service account has read/write access to ProgramData\RansomGuard-CM
4. **Watch path errors**: Ensure configured watch paths exist or the agent can create them
