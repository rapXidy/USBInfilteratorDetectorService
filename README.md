# USB Exfiltration Detector – Windows Worker Service

**.NET 8 Windows Background Worker Service** that runs on monitored endpoints (laptops, desktops, servers).

As a requirement for the course Industrial Research, for the partial fulfilment of an MSc. Cybersecurity Engineering at the University of East London, U.K. It is required that this project be built, tested against established test-cases.

It detects USB / removable storage activity, checks whether required Windows audit settings are enabled, reconstructs USB sessions, applies explainable risk scoring, and securely ships structured telemetry to the central API — even when the host is offline.

---

## Overview

| Component | Role |
|-----------|------|
| **This Worker Service** | Endpoint agent: detect → reconstruct → score → queue → send |
| **Central API** | Ingests payloads, stores data, authenticates auditors, sends notifications |
| **React Frontend** | Auditor dashboard |

The service is designed to be **lightweight**, **least-privilege**, and **offline-resilient**. It never modifies Windows audit policy automatically.

---

## Key Features

- **USB presence detection** – Starts intensive processing only when a removable device is connected
- **Audit-policy awareness** – Checks whether “Audit PNP Activity” and “Audit Removable Storage” are enabled; raises a clear alert if they are missing
- **Session reconstruction** – Groups related events into logical USB sessions (connect → activity → disconnect)
- **Explainable risk scoring** – Weighted rules produce a score plus human-readable reasons that cite Event Record IDs
- **Host context** – Includes hostname, device ID, logged-in user, OS version, and CorpCode in every payload
- **Offline resilience** – Durable local queue (SQLite or file-based); events are never discarded when the network is down
- **Secure outbound communication** – HTTPS batches to the central API with API-key (or client-credentials) authentication
- **Least privilege** – Runs under a dedicated low-privilege account whenever possible

---

## Tech Stack

| Item | Choice |
|------|--------|
| Runtime | .NET 8 |
| Template | Worker Service |
| Installation | Windows Service (`sc.exe` or MSI) |
| Local queue | SQLite (preferred) or append-only files |
| Configuration | `appsettings.json` + environment variables |

---

## Configuration

Required settings (via `appsettings.json`, environment variables, or User Secrets):

```json
{
  "CorpCode": "ACME-UK-001",
  "ApiBaseUrl": "https://api.example.com",
  "ApiKey": "<shared-secret-or-client-credentials>",
  "OfflineQueuePath": "C:\\ProgramData\\UsbDetector\\queue",
  "PollingIntervalSeconds": 5,
  "MaxBatchSize": 50,
  "EnableVerboseLogging": false
}
```

| Key | Purpose |
|-----|---------|
| `CorpCode` | Organisation / tenant identifier (mandatory on every payload) |
| `ApiBaseUrl` | Base URL of the central API |
| `ApiKey` | Shared secret used to authenticate with the API |
| `OfflineQueuePath` | Directory for the durable offline queue |
| `PollingIntervalSeconds` | How often the service checks for USB presence / drains the queue |
| `MaxBatchSize` | Maximum number of payloads sent in one HTTP request |

**Never commit real secrets to source control.**

---

## Core Behaviour

### 1. USB detection
Monitors for removable storage (WMI and/or Event Log channels). Intensive event processing runs only while a USB device is present (or for a short cool-down after removal).

### 2. Audit-policy check (mandatory)
On insertion the service queries:

- Audit PNP Activity  
- Audit Removable Storage  

If either is disabled it emits a high-severity reason:

> “Required audit settings are disabled – file-level detection incomplete”

The service **never** attempts to enable or change Windows audit policy.

### 3. Event log reading
Relevant Event IDs (non-exhaustive):

| Event ID | Meaning |
|----------|---------|
| 6416 | New external device recognised |
| 4663 / 4656 | Object access (file read/write) on removable volumes |
| Partition / Diagnostic | Device arrival / removal |
| DriverFrameworks-UserMode | Additional PnP lifecycle events |

### 4. Session reconstruction & risk scoring
Events are grouped into sessions. A small set of weighted rules is applied, for example:

- Large volume of data written
- Activity outside normal working hours
- Device serial not on the organisation’s Allowed Devices list
- Abrupt removal without clean eject

Every fired rule produces a **reason** containing:

- Rule name  
- Weight  
- Human-readable description  
- List of Event Record IDs that triggered it  

### 5. Offline queue
Every generated payload is written to a local durable store **before** network transmission. When connectivity returns, the queue is drained in order.

### 6. Outbound communication
HTTPS POST of JSON batches to the central API (`/api/ingest/batch` or equivalent), authenticated with the configured API key. Retries use exponential back-off.

---

## Payload Contract

The service emits payloads that conform to this shape (consumed by the central API):

```json
{
  "corpCode": "ACME-UK-001",
  "host": {
    "hostName": "LAPTOP-JSMITH",
    "deviceId": "A1B2C3D4-E5F6-7890-ABCD-EF1234567890",
    "loggedInUser": "DOMAIN\\j.smith",
    "osVersion": "10.0.22631"
  },
  "session": {
    "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "deviceSerial": "4C530001A2B3C4D5",
    "vendor": "SanDisk",
    "model": "Cruzer",
    "connectedAt": "2026-11-17T14:22:01Z",
    "disconnectedAt": null,
    "totalBytesWritten": 1876543210,
    "riskScore": 78,
    "reasons": [
      {
        "rule": "LargeVolumeWrite",
        "weight": 30,
        "description": "1.8 GB written to removable volume",
        "eventRecordIds": [412, 413, 414]
      },
      {
        "rule": "UnapprovedDevice",
        "weight": 25,
        "description": "Device serial not in Allowed Devices list",
        "eventRecordIds": [6416]
      }
    ]
  },
  "events": [
    {
      "eventId": 6416,
      "recordId": 10234,
      "timeCreated": "2026-11-17T14:22:01Z",
      "activity": "DeviceConnected",
      "description": "New external device recognised"
    }
  ],
  "auditSettingsStatus": {
    "pnpActivityEnabled": true,
    "removableStorageEnabled": false,
    "detectionComplete": false
  }
}
```

---

## Installation (Windows Service)

### Development

```bash
dotnet run
```

### Production (example)

```bash
# Publish
dotnet publish -c Release -o C:\Services\UsbDetector

# Create the service (run as Administrator)
sc create UsbExfiltrationDetector binPath= "C:\Services\UsbDetector\UsbExfiltrationDetector.Worker.exe" start= auto
sc description UsbExfiltrationDetector "USB Exfiltration Detector Worker Service"
sc start UsbExfiltrationDetector
```

Prefer running under a dedicated low-privilege service account rather than `LocalSystem`.

Protect the offline queue directory with appropriate ACLs.

---

## Security Requirements

- Run under a least-privilege Windows account
- Never store secrets in the binary or in source control
- Protect the offline queue directory
- Do **not** elevate privileges or modify system policy
- Log security-relevant actions without writing sensitive data

---

## Operational Summary

| Situation | Behaviour |
|-----------|-----------|
| USB inserted | Start monitoring, check audit settings, open session |
| Required audit settings missing | Raise high-severity alert, continue best-effort detection |
| File writes occur (audit on) | Capture events, update volume & risk score |
| USB removed | Close session, finalise score, enqueue payload |
| Network unavailable | Persist to local queue, keep collecting |
| Network restored | Drain queue in order, mark delivered |
| Service starts | Recover any undelivered items from the local queue |

---

## Suggested Implementation Order

1. Project scaffolding + Windows Service installation  
2. Configuration loading (`CorpCode`, API URL, queue path)  
3. USB presence detection (WMI or event-based)  
4. Audit-policy status check  
5. Basic event-log reading (connect / disconnect)  
6. Local durable queue (SQLite recommended)  
7. Session reconstruction + explainable risk-scoring engine  
8. HTTPS client + batch upload + retry logic  
9. Full offline ↔ online cycle testing  

---

## Related Components

| Component | Description |
|-----------|-------------|
| Central API | ASP.NET Core API that receives the payloads this service produces |
| React Frontend | Auditor dashboard that displays sessions, risk scores and reasons |

See the companion specification documents:

- `01_PROJECT_OVERVIEW_AND_ARCHITECTURE.md`
- `02_WINDOWS_WORKER_SERVICE.md` (detailed contract for this service)
- `03_BACKEND_API.md`
- `04_FRONTEND_AND_INTEGRATION.md`

---

## Licence

Specify your licence here (e.g. MIT, proprietary, etc.).

---

## Contributing

1. Create a feature branch  
2. Keep the service least-privilege and offline-first  
3. Never add automatic audit-policy changes  
4. Open a pull request with a clear description of the change  
