# IRONCLAD Hardware Response Module

## What IRONCLAD Does

IRONCLAD is the physical USB port isolation module for RansomGuard-CM. When the USB GUARD module detects a Critical severity threat on a USB device (malware, weaponized documents, bootable attack media), IRONCLAD sends a command to an Arduino-based relay controller that physically cuts power to the USB port. This is a hardware-level defense that cannot be bypassed by software exploits, rootkits, or firmware attacks on the USB device itself.

The module differentiates RansomGuard-CM from CrowdStrike, SentinelOne, and Microsoft Defender, which are limited to software-level USB ejection that can be circumvented by BadUSB/USB Killer attacks. IRONCLAD provides true air-gap isolation by interrupting the electrical connection between the USB device and the host machine.

The action chain follows a graceful fallback pattern: IronClad physical cut is attempted first. If the hardware controller is unavailable (disconnected, faulted, or disabled), the system falls back to the existing software-level BlockAndEject action. This ensures the agent never degrades its protection level due to hardware issues.

## Software-Only Sprint 5 vs Hardware Sprint 8

Sprint 5 implements the complete IronClad software stack using a TCP mock communication layer. The `MockArduinoServer` class simulates the exact behavior of the future Arduino firmware:

- Wire protocol: `CMD:ACTION:PARAM` commands with `ACK/ERR/STATUS/VERSION/PONG` responses
- Relay switching with 50ms delay (matching real relay timing)
- 30-second fail-safe timeout that restores all ports if the agent disconnects
- Single-client model (matching the serial connection constraint of real hardware)

Sprint 8 will replace the TCP socket with `System.IO.Ports.SerialPort` communication to a real Arduino Mega with a 4-channel relay module. The `SerialPortCommunicator` stub is already in place with the correct class structure — Sprint 8 just fills in the method bodies.

## Communication Mode Swap Procedure

In `appsettings.json`, change the `IronClad` section:

```json
{
  "IronClad": {
    "Enabled": true,
    "CommunicationMode": "SerialPort",
    "SerialPortName": "COM3",
    "SerialBaudRate": 9600
  }
}
```

The `IronCladCommunicatorFactory` selects the correct implementation at runtime based on `CommunicationMode`. No code changes required for the swap.

## Drive Letter to Port Mapping

The mapping between Windows drive letters and physical relay ports is configured statically in `UsbActionEngine.MapDriveLetterToPort()`:

| Drive Letter | Relay Port |
|-------------|-----------|
| E: | Port 1 |
| F: | Port 2 |
| G: | Port 3 |
| H: | Port 4 |

In Sprint 8, this mapping will be moved to configuration and matched to the physical USB hub layout.

## CLI Commands Reference

All CLI commands require administrator elevation.

```
--ironclad-status              Print current device status (port states, version, heartbeat)
--ironclad-cut <port>          Cut a specific port (1-4) with mandatory justification
  --justification "<text>"
--ironclad-restore <port>      Restore a specific port with justification
  --justification "<text>"
--ironclad-restore-all         Restore all ports with justification
  --justification "<text>"
```

Each command writes an Ed25519-signed audit log entry before execution, persists the result to the `IronCladEvents` table, and updates the `IronCladDeviceStates` table.

## Safety Defaults

| Setting | Default | Rationale |
|---------|---------|-----------|
| `Enabled` | `false` | Admin must explicitly opt in after whitelist building |
| `FailSafeRestoreOnDisconnect` | `true` | Prevents permanent port lockout on agent crash |
| `RequireDeviceForCriticalActions` | `false` | Allows software fallback when hardware unavailable |
| `HeartbeatTimeoutSeconds` | `30` | Raises Critical alert after 30s without heartbeat |
| `CommandTimeoutSeconds` | `10` | Individual command timeout |
| `RelayCount` | `4` | Standard 4-port USB hub configuration |

The IronClad module follows three safety invariants:
1. **Whitelisted devices NEVER trigger IronClad** (whitelist always takes priority)
2. **Only Strict mode + Critical severity activates physical cut** (3-condition gate)
3. **Audit log written BEFORE command sent** (forensic preservation even on failure)

## Related Components

- USB GUARD Action Engine: `Detection/UsbGuard/Actions/UsbActionEngine.cs`
- Audit Log Signer: `Security/Cryptography/AuditLogSigner.cs` (Ed25519)
- Service Registration: `RansomGuard.Agent.Service/ServiceRegistration.cs`
