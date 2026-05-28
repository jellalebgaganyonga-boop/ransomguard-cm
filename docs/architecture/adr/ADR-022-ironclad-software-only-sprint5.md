# ADR-022: IronClad Software-Only Implementation in Sprint 5

## Status

Accepted

## Date

2026-05-28

## Context

RansomGuard-CM targets deployment in Cameroon hospitals where USB-borne malware is a primary attack vector. The Sprint 4 USB GUARD module detects Critical severity threats (weaponized documents, malware executables, bootable attack media) but can only respond with software-level ejection. Software ejection can be circumvented by:

1. **BadUSB attacks**: Firmware-modified USB devices that re-enumerate as HID keyboards and execute commands before software ejection completes.
2. **USB Killer devices**: Hardware attack devices that discharge capacitors into USB ports, potentially damaging the host machine before any software response.
3. **Race conditions**: The window between detection and software ejection allows malware to execute if it runs immediately on insertion.

The IRONCLAD module addresses these threats by physically cutting power to USB ports via an Arduino-controlled relay, providing hardware-level isolation that cannot be bypassed by software exploits.

However, procuring Arduino hardware, relay modules, and USB hub components requires lead time, budget approval, and physical prototyping that would block Sprint 5 delivery. The development team needs to implement and validate the complete software stack without waiting for hardware procurement.

## Decision

Sprint 5 implements IRONCLAD as a **software-only module** using a TCP mock communication layer:

- `MockArduinoServer` simulates the Arduino firmware behavior over TCP (localhost:9999)
- `TcpMockCommunicator` connects via TCP socket, implementing the same `IIronCladCommunicator` interface
- `SerialPortCommunicator` is stubbed with `NotImplementedException` but maintains correct class structure for Sprint 8

The architecture uses **interface-based design** (`IIronCladCommunicator`) with a **factory pattern** (`IronCladCommunicatorFactory`) that selects the communication backend at runtime via configuration:

```json
{ "CommunicationMode": "TcpMock" }   // Sprint 5
{ "CommunicationMode": "SerialPort" } // Sprint 8
```

Sprint 8 activates real hardware by:
1. Filling in `SerialPortCommunicator` method bodies
2. Changing `CommunicationMode` to `SerialPort` in production configuration
3. No changes to action engine, heartbeat monitor, state reconciliation, or test infrastructure

## Consequences

### Positive

- **Development velocity**: Sprint 5 delivers the complete IronClad software stack without hardware blocking.
- **Interface-based swap**: Sprint 8 hardware activation requires implementing one class (SerialPortCommunicator) and changing one config value.
- **Faithful simulation**: MockArduinoServer replicates real firmware behavior (relay delay, fail-safe timeout, error codes) enabling meaningful integration testing.
- **Full test coverage**: 50+ IronClad tests validate protocol, communication, action engine, heartbeat, reconciliation, and USB GUARD integration.
- **Audit trail**: Every IronClad command is Ed25519-signed and persisted regardless of communication backend.

### Negative

- **No physical validation**: Sprint 5 cannot verify actual relay switching, electrical isolation, or real-world timing under load.
- **Mock behavior assumptions**: The MockArduinoServer assumes certain firmware behaviors (50ms relay delay, FIFO command processing) that must be validated against real hardware in Sprint 8.
- **Serial port edge cases**: Real serial communication has issues (buffer overruns, electrical noise, COM port enumeration) that TCP cannot simulate.

### Mitigation

- Sprint 8 will include a hardware validation suite with oscilloscope-verified relay timing measurements.
- The MockArduinoServer behavior contract is documented in XML comments and serves as the firmware specification for the Arduino developer.
- Integration tests use ephemeral TCP ports, ensuring no test conflicts and fast execution.

## Alternatives Considered

1. **Wait for hardware**: Would block Sprint 5 for 2-3 weeks of procurement and prototyping. Rejected — software stack development can proceed independently.
2. **USB P/Invoke only (no hardware)**: Software-only ejection without physical relay. Rejected — does not address BadUSB/USB Killer threats which are the core value proposition.
3. **Named pipes instead of TCP**: Slightly simpler but less portable. Rejected — TCP better simulates the real serial communication model (byte stream, connection lifecycle).
