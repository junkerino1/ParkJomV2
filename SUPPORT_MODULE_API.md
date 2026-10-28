# ParkJom V2 — Simplified Support Module API Reference

All Support Module REST endpoints return a standard JSON envelope:

```json
{
  "code": 200,
  "success": true,
  "message": "Success",
  "data": { ... }
}
```

For paginated endpoints (`SupportApiPagedResponse<T>`):

```json
{
  "code": 200,
  "success": true,
  "message": "Success",
  "data": {
    "items": [ ... ],
    "page": 1,
    "pageSize": 25,
    "totalCount": 100,
    "totalPages": 4,
    "hasNextPage": true
  }
}
```

---

## Part 1: Customer APIs (Commuter & PropertyOwner)

**Authentication:** Requires `Authorization: Bearer <JWT_TOKEN>` (`[Authorize]`).

---

### 1. Get Support Context
* **Method & Route:** `GET /api/support/context`
* **Controller:** `SupportContextController.GetContext`
* **Description:** Retrieves the logged-in user's active booking, self-service gate unlock eligibility, role-specific quick issue templates, recent bookings, vehicles, transactions, owned spots, and recent access logs.
* **Query Parameters:**
  * `bookingId` (`int?`, optional): Specific booking ID to inspect. If omitted, automatically detects the user's current active/upcoming booking.
  * `vehicleId` (`int?`, optional): Specific vehicle ID.
* **Response `data` (`SupportContextDto`):**
```json
{
  "userId": 5,
  "userName": "Alex Tan",
  "userEmail": "alex@example.com",
  "userType": "Renter",
  "walletBalance": 45.50,
  "activeBooking": {
    "bookingId": 102,
    "bookingReference": "BK-2026-102",
    "parkingSpotName": "A-12",
    "propertyAddress": " Residensi LRT Subang",
    "vehiclePlate": "WXY 1234",
    "startDate": "2026-10-17T08:00:00Z",
    "endDate": "2026-10-17T18:00:00Z",
    "status": "Confirmed",
    "totalAmount": 12.00,
    "hasIoTDevice": true,
    "ioTDeviceStatus": "Online",
    "lastHeartbeatAt": "2026-10-17T08:25:00Z",
    "canSelfUnlockGate": true
  },
  "canSelfUnlockGate": true,
  "quickIssueTemplates": [
    {
      "issueCode": "GATE_STUCK",
      "title": "道闸打不开 / 无法进出场",
      "description": "道闸未抬起、二维码/车牌未识别或地锁无法降下",
      "category": "ParkingAccess",
      "defaultPriority": "P0",
      "estimatedResponseTime": "立即响应 (5分钟内)",
      "requiresBooking": true,
      "suggestPhotoUpload": false,
      "supportsSelfUnlock": true
    }
  ],
  "recentBookings": [],
  "vehicles": [],
  "recentTransactions": [],
  "ownedSpots": [],
  "recentAccessLogs": []
}
```

---

### 2. Get Quick Issue Templates
* **Method & Route:** `GET /api/support/templates`
* **Controller:** `SupportTicketController.GetQuickIssueTemplates`
* **Description:** Returns 1-click issue cards tailored to the user's role (`Commuter`/`Renter` vs `PropertyOwner`).
* **Query Parameters:** None
* **Supported `IssueCode` Values:**
  * **Commuter (`Renter`):**
    * `GATE_STUCK` (`ParkingAccess`, `P0`, `supportsSelfUnlock: true`)
    * `SPOT_OCCUPIED` (`ParkingAccess`, `P1`, `suggestPhotoUpload: true`)
    * `REFUND_REQUEST` (`Payment`, `P2`)
    * `DUPLICATE_CHARGE` (`Payment`, `P2`)
    * `OTHER` (`General`, `P3`)
  * **PropertyOwner:**
    * `RENTER_OVERSTAY` (`OwnerSupport`, `P1`, `suggestPhotoUpload: true`)
    * `IOT_OFFLINE` (`ParkingAccess`, `P1`)
    * `OWNER_PAYOUT` (`Payment`, `P2`)
    * `OTHER` (`OwnerSupport`, `P3`)
* **Response `data` (`List<QuickIssueTemplateDto>`):** Array of `QuickIssueTemplateDto`.

---

### 3. Self-Service Emergency Gate Unlock
* **Method & Route:** `POST /api/support/gate-unlock`
* **Controller:** `SupportTicketController.SelfUnlockGate`
* **Content-Type:** `application/json`
* **Description:** Allows a commuter with an active booking (within 15 mins before `StartDate` to 30 mins after `EndDate`) and an `Online` IoT device to remotely trigger the barrier/bollard without opening a ticket. Rate-limited to once every 2 minutes per booking.
* **Request Body (`SelfUnlockGateRequestDto`, optional):**
```json
{
  "bookingId": 102,
  "action": "Enter"
}
```
  * `bookingId` (`int?`, optional): Auto-detected if omitted.
  * `action` (`string`, optional): `"Enter"` (default) or `"Exit"`.
* **Response `data` (`SelfUnlockGateResultDto`):**
```json
{
  "success": true,
  "commandId": "SELF-UNLOCK-9F2A1C3B",
  "message": "远程抬杆指令已下发，道闸正在开启，请缓慢通行。",
  "bookingId": 102,
  "bookingReference": "BK-2026-102",
  "parkingSpotName": "A-12",
  "ioTDeviceId": 7,
  "ioTDeviceStatus": "Online",
  "suggestUrgentTicket": false,
  "executedAt": "2026-10-17T08:30:00Z"
}
```
  * Note: If the IoT device is offline or out of window, `success` is `false` and `suggestUrgentTicket` is `true` so the frontend can prompt 1-click creation of a `GATE_STUCK` ticket.

---

### 4. Get My Tickets
* **Method & Route:** `GET /api/support/tickets/mine`
* **Controller:** `SupportTicketController.GetMyTickets`
* **Description:** Lists all support tickets belonging to the current user.
* **Query Parameters:**
  * `status` (`string?`, optional): Supports customer-friendly display statuses (`Processing`, `ActionRequired`, `Resolved`, `Closed`) or internal enum values (`New`, `Accepted`, `InProgress`, `WaitingForCustomer`, `WaitingForInternalTeam`, `Resolved`, `Reopened`, `Closed`).
  * `search` (`string?`, optional): Searches by `TicketReference` or `Subject`.
* **Response `data` (`List<SupportTicketSummaryDto>`):**
```json
[
  {
    "ticketId": 21,
    "ticketReference": "TKT-2026-48291",
    "issueCode": "REFUND_REQUEST",
    "ticketType": "Preset",
    "source": "QuickHelp",
    "category": "Payment",
    "priority": "P2",
    "status": "WaitingForCustomer",
    "userDisplayStatus": "ActionRequired",
    "userStatusLabel": "待您补充/回复",
    "subject": "申请退款 (BK-2026-102)",
    "customerUserId": 5,
    "customerName": "Alex Tan",
    "customerEmail": "alex@example.com",
    "assignedAdminName": "Sarah Admin",
    "assignedTeam": "Payments",
    "bookingId": 102,
    "bookingReference": "BK-2026-102",
    "parkingSpotName": "A-12",
    "operationalIncidentId": null,
    "disputeInvestigationId": 9,
    "refundSummary": {
      "disputeId": 9,
      "disputeReference": "DSP-2026-19283",
      "disputeType": "Refund",
      "status": "WaitingForCustomerEvidence",
      "claimAmount": 12.00,
      "currency": "MYR",
      "decision": null,
      "decisionReason": null,
      "decidedAt": null
    },
    "createdAt": "2026-10-17T08:35:00Z",
    "updatedAt": "2026-10-17T08:40:00Z",
    "firstResponseDueAt": "2026-10-17T09:05:00Z",
    "resolutionDueAt": "2026-10-17T20:35:00Z",
    "messageCount": 2
  }
]
```

---

### 5. Get Ticket Details
* **Method & Route:** `GET /api/support/tickets/{ticketId:int}`
* **Controller:** `SupportTicketController.GetTicket`
* **Description:** Retrieves full ticket details, chat messages, uploaded attachments, self-unlock status, and embedded refund summary. (Internal admin notes and audit timeline are automatically hidden for non-admin users).
* **Route Parameters:**
  * `ticketId` (`int`, required)
* **Response `data` (`SupportTicketDto`):**
```json
{
  "ticketId": 21,
  "ticketReference": "TKT-2026-48291",
  "issueCode": "REFUND_REQUEST",
  "ticketType": "Preset",
  "source": "QuickHelp",
  "category": "Payment",
  "priority": "P2",
  "status": "WaitingForCustomer",
  "userDisplayStatus": "ActionRequired",
  "userStatusLabel": "待您补充/回复",
  "subject": "申请退款 (BK-2026-102)",
  "description": "道闸故障未停入，申请全额退款",
  "customerUserId": 5,
  "customerName": "Alex Tan",
  "customerEmail": "alex@example.com",
  "customerRole": "Commuter",
  "assignedAdminUserId": 1,
  "assignedAdminName": "Sarah Admin",
  "assignedTeam": "Payments",
  "bookingId": 102,
  "bookingReference": "BK-2026-102",
  "parkingSpotId": 14,
  "parkingSpotName": "A-12",
  "vehicleId": 3,
  "vehiclePlate": "WXY 1234",
  "canSelfUnlockGate": false,
  "operationalIncidentId": null,
  "incidentReference": null,
  "disputeInvestigationId": 9,
  "disputeReference": "DSP-2026-19283",
  "refundSummary": {
    "disputeId": 9,
    "disputeReference": "DSP-2026-19283",
    "disputeType": "Refund",
    "status": "WaitingForCustomerEvidence",
    "claimAmount": 12.00,
    "currency": "MYR",
    "decision": null,
    "decisionReason": null,
    "decidedAt": null
  },
  "acceptedAt": "2026-10-17T08:36:00Z",
  "firstResponseAt": "2026-10-17T08:40:00Z",
  "firstResponseDueAt": "2026-10-17T09:05:00Z",
  "resolvedAt": null,
  "resolutionDueAt": "2026-10-17T20:35:00Z",
  "closedAt": null,
  "resolutionCode": null,
  "internalSummary": null,
  "createdAt": "2026-10-17T08:35:00Z",
  "updatedAt": "2026-10-17T08:40:00Z",
  "messages": [
    {
      "messageId": 40,
      "ticketId": 21,
      "senderUserId": 5,
      "senderName": "Alex Tan",
      "senderRole": "Customer",
      "body": "道闸故障未停入，申请全额退款",
      "isInternal": false,
      "createdAt": "2026-10-17T08:35:00Z",
      "attachments": []
    }
  ],
  "attachments": [],
  "auditTimeline": []
}
```

---

### 6. Create Support Ticket (1-Click or Custom)
* **Method & Route:** `POST /api/support/tickets`
* **Controller:** `SupportTicketController.CreateTicket`
* **Content-Type:** `multipart/form-data` (also accepts `application/json` when no files are uploaded)
* **Description:** Creates a unified support ticket.
  * Automatically binds the user's active booking, parking spot, and vehicle if `bookingId` is omitted.
  * Automatically derives `Subject`, `Category`, `Priority`, and `AssignedTeam` from `issueCode`.
  * Silently creates and links an `OperationalIncident` for `GATE_STUCK` / `SPOT_OCCUPIED` / `IOT_OFFLINE`, or a `DisputeInvestigation` for `REFUND_REQUEST` / `DUPLICATE_CHARGE` (using `claimAmount` or the booking's `TotalAmount`).
* **Form Fields (`CreateSupportTicketRequestDto`):**
  * `issueCode` (`string?`, recommended): `GATE_STUCK` | `SPOT_OCCUPIED` | `REFUND_REQUEST` | `DUPLICATE_CHARGE` | `RENTER_OVERSTAY` | `IOT_OFFLINE` | `OWNER_PAYOUT` | `OTHER`
  * `message` (`string?`): User's description of the issue.
  * `bookingId` (`int?`, optional): Auto-detected if omitted.
  * `parkingSpotId` (`int?`, optional): Auto-detected from booking if omitted.
  * `vehicleId` (`int?`, optional): Auto-detected from booking if omitted.
  * `claimAmount` (`decimal?`, optional): Requested refund amount (defaults to booking `TotalAmount`).
  * `subject` (`string?`, optional): Auto-generated from `issueCode` if omitted.
  * `category` (`string?`, optional): Auto-generated from `issueCode` if omitted.
  * `attachments` (`List<IFormFile>?`, optional): Photos/receipts (automatically synced to `DisputeEvidences` if linked to a dispute).
* **Response `data`:** `SupportTicketDto`

---

### 7. Send Ticket Message / Upload Evidence
* **Method & Route:** `POST /api/support/tickets/{ticketId:int}/messages`
* **Controller:** `SupportTicketController.SendTicketMessage`
* **Content-Type:** `multipart/form-data`
* **Description:** Appends a chat message and/or file attachments to the ticket.
  * If a customer replies while the ticket is in `WaitingForCustomer` (`ActionRequired`), the ticket automatically transitions back to `InProgress` (`Processing`).
  * If the ticket is linked to a `DisputeInvestigation`, uploaded attachments are automatically mirrored into `DisputeEvidences`.
* **Form Fields (`SendTicketMessageRequestDto`):**
  * `message` (`string`): Message text (if empty and files are attached, defaults to `"[Uploaded attachment]"`).
  * `isInternal` (`bool`, optional, default `false`): Only takes effect if sender is `Admin`.
  * `attachments` (`List<IFormFile>?`, optional): Uploaded files/photos.
* **Response `data`:** `SupportTicketMessageDto`

---

### 8. Customer Self-Close Ticket
* **Method & Route:** `POST /api/support/tickets/{ticketId:int}/close`
* **Controller:** `SupportTicketController.CloseMyTicket`
* **Content-Type:** `application/json`
* **Description:** Allows the customer to close/resolve their own ticket at any time (e.g. when a blocking car leaves or the gate opens). Also auto-resolves a single-customer linked `OperationalIncident`.
* **Request Body (`CloseTicketRequestDto`, optional):**
```json
{
  "reason": "问题已自行解决"
}
```
* **Response `data`:** `SupportTicketDto`

---

### 9. Reopen Ticket
* **Method & Route:** `POST /api/support/tickets/{ticketId:int}/reopen`
* **Controller:** `SupportTicketController.ReopenTicket`
* **Content-Type:** `application/json`
* **Description:** Reopens a `Resolved` or `Closed` ticket.
* **Request Body (`ReopenTicketRequestDto`):**
```json
{
  "reason": "问题再次出现，需要继续跟进"
}
```
* **Response `data`:** `SupportTicketDto`

---

### 10. Get Attachment Metadata & URL
* **Method & Route:** `GET /api/support/attachments/{attachmentId:int}`
* **Controller:** `SupportTicketController.GetAttachment`
* **Description:** Retrieves attachment metadata and URL after verifying that the requester is either the ticket owner, uploader, or an Admin.
* **Response `data` (`SupportAttachmentDto`):**
```json
{
  "attachmentId": 15,
  "fileName": "spot_occupied.jpg",
  "fileUrl": "https://res.cloudinary.com/...",
  "contentType": "image/jpeg",
  "fileSize": 204800,
  "isPrivate": true,
  "createdAt": "2026-10-17T08:35:00Z"
}
```

---

## Part 2: Admin APIs (`[Authorize(Policy = "AdminOnly")]`)

**Authentication:** Requires `Authorization: Bearer <ADMIN_JWT_TOKEN>`.

---

### A. Dashboard & Audit Logs (`AdminSupportDashboardController`)

#### 11. Get Command Center Dashboard Metrics
* **Method & Route:** `GET /api/admin/support/dashboard`
* **Response `data` (`SupportDashboardDto`):**
```json
{
  "openTicketsCount": 14,
  "actionRequiredTicketsCount": 3,
  "activeIncidentsCount": 2,
  "openDisputesCount": 4,
  "slaRiskTicketsCount": 1,
  "recentTickets": [],
  "recentIncidents": []
}
```

#### 12. Get Support Audit Logs
* **Method & Route:** `GET /api/admin/support/audit`
* **Query Parameters:**
  * `objectType` (`string?`, optional): `"Ticket"` | `"Incident"` | `"Dispute"` | `"OnCall"`
  * `objectId` (`int?`, optional)
  * `page` (`int`, default `1`)
  * `pageSize` (`int`, default `50`)
* **Response:** `SupportApiPagedResponse<SupportAuditEventDto>`

---

### B. Admin Ticket Queue (`AdminSupportTicketController`)

#### 13. List All Support Tickets
* **Method & Route:** `GET /api/admin/support/tickets`
* **Query Parameters:**
  * `status` (`string?`, optional): `Open` (all active), `Processing`, `ActionRequired`, or specific `TicketStatus` (`New`, `Accepted`, `InProgress`, `WaitingForCustomer`, `WaitingForInternalTeam`, `Resolved`, `Reopened`, `Closed`)
  * `priority` (`string?`, optional): `P0` | `P1` | `P2` | `P3`
  * `team` (`string?`, optional): `CustomerSupport` | `ParkingOperations` | `Payments` | `OwnerSupport` | `TrustAndSafety`
  * `assignee` (`int?`, optional): Admin `UserId`
  * `search` (`string?`, optional): Matches `TicketReference`, `Subject`, customer `FullName`, or `Email`
  * `page` (`int`, default `1`)
  * `pageSize` (`int`, default `100`)
* **Response `data`:** `List<SupportTicketSummaryDto>`

#### 14. Get Admin Ticket Detail (by ID or Reference)
* **Method & Route:** `GET /api/admin/support/tickets/{ticketIdentifier}`
* **Route Parameters:**
  * `ticketIdentifier` (`string`): Supports numeric ID (`21`) or reference string (`TKT-2026-48291`).
* **Description:** Returns full `SupportTicketDto` including internal admin notes (`isInternal: true`) and `auditTimeline`.

#### 15. Admin Create Custom Ticket
* **Method & Route:** `POST /api/admin/support/tickets`
* **Content-Type:** `multipart/form-data`
* **Form Fields (`AdminCreateSupportTicketRequestDto`):**
  * `customerUserId` (`int?`) or `customerEmail` (`string?`)
  * `issueCode` (`string?`)
  * `subject` (`string`, required)
  * `message` (`string`, required)
  * `category` (`string`, default `"General"`): `ParkingAccess` | `Booking` | `Payment` | `Account` | `OwnerSupport` | `General`
  * `priority` (`string`, default `"P2"`): `P0` | `P1` | `P2` | `P3`
  * `assignedTeam` (`string`, default `"CustomerSupport"`)
  * `assignedAdminUserId` (`int?`)
  * `bookingId` (`int?`), `parkingSpotId` (`int?`), `vehicleId` (`int?`)
  * `internalSummary` (`string?`)
  * `attachments` (`List<IFormFile>?`)
* **Response `data`:** `SupportTicketDto`

#### 16. Accept Ticket
* **Method & Route:** `POST /api/admin/support/tickets/{ticketId:int}/accept`
* **Description:** Assigns the ticket to the current admin and transitions status to `Accepted`.
* **Response `data`:** `SupportTicketDto`

#### 17. Assign Ticket
* **Method & Route:** `POST /api/admin/support/tickets/{ticketId:int}/assign`
* **Content-Type:** `application/json`
* **Request Body (`AssignTicketRequestDto`):**
```json
{
  "assignedAdminUserId": 1,
  "assignedTeam": "ParkingOperations"
}
```
* **Response `data`:** `SupportTicketDto`

#### 18. Transition Ticket Status
* **Method & Route:** `POST /api/admin/support/tickets/{ticketId:int}/transition`
* **Content-Type:** `application/json`
* **Request Body (`TicketTransitionRequestDto`):**
```json
{
  "toStatus": "Resolved",
  "reason": "已协助用户远程开启道闸",
  "resolutionCode": "RemoteGateOpened"
}
```
  * Valid `toStatus`: `New`, `Accepted`, `InProgress`, `WaitingForCustomer`, `WaitingForInternalTeam`, `Resolved`, `Reopened`, `Duplicate`, `Cancelled`, `Closed`.
* **Response `data`:** `SupportTicketDto`

#### 19. Admin Close Ticket
* **Method & Route:** `POST /api/admin/support/tickets/{ticketId:int}/close`
* **Content-Type:** `application/json`
* **Request Body (`CloseTicketRequestDto`, optional):**
```json
{
  "reason": "Closed by Admin"
}
```
* **Response `data`:** `SupportTicketDto`

#### 20. Link Ticket to Incident
* **Method & Route:** `POST /api/admin/support/tickets/{ticketIdentifier}/link-incident`
* **Content-Type:** `application/json`
* **Request Body (`LinkIncidentRequestDto`):**
```json
{
  "incidentId": 4,
  "incidentReference": "INC-2026-52563"
}
```
* **Response `data`:** `SupportTicketDto`

#### 21. Link Ticket to Dispute
* **Method & Route:** `POST /api/admin/support/tickets/{ticketIdentifier}/link-dispute`
* **Content-Type:** `application/json`
* **Request Body (`LinkDisputeRequestDto`):**
```json
{
  "disputeId": 9,
  "disputeReference": "DSP-2026-19283"
}
```
* **Response `data`:** `SupportTicketDto`

---

### C. Operational Incidents (`AdminSupportIncidentController`)

#### 22. List Operational Incidents
* **Method & Route:** `GET /api/admin/support/incidents`
* **Query Parameters:**
  * `status` (`string?`, optional): `Open` | `Acknowledged` | `Investigating` | `Mitigating` | `Monitoring` | `Resolved` | `Closed`
  * `priority` (`string?`, optional): `P0` | `P1` | `P2` | `P3`
  * `team` (`string?`, optional)
  * `page` (`int`, default `1`), `pageSize` (`int`, default `25`)
* **Response:** `SupportApiPagedResponse<OperationalIncidentSummaryDto>`

#### 23. Get Incident Detail (by ID or Reference)
* **Method & Route:** `GET /api/admin/support/incidents/{incidentIdentifier}`
* **Route Parameters:** `incidentIdentifier` (`4` or `"INC-2026-52563"`)
* **Response `data`:** `OperationalIncidentDto` (includes `linkedTickets`, `auditTimeline`, `notificationAttempts`)

#### 24. Create Operational Incident
* **Method & Route:** `POST /api/admin/support/incidents`
* **Content-Type:** `application/json`
* **Request Body (`CreateIncidentRequestDto`):**
```json
{
  "title": "A-12 道闸控制器离线",
  "description": "ESP32 连续 10 分钟无心跳",
  "incidentType": "IoTOffline",
  "priority": "P1",
  "assignedTeam": "ParkingOperations",
  "assignedUserId": 1,
  "propertyId": 2,
  "parkingSpotId": 14,
  "ioTDeviceId": 7,
  "source": "Admin",
  "initialTicketId": 21
}
```
  * Valid `incidentType`: `GateFailure`, `IoTOffline`, `SpotUnavailable`, `OverstayConflict`, `RepeatedAccessFailure`, `SafetyHazard`, `PlatformOutage`.
* **Response `data`:** `OperationalIncidentDto`

#### 25. Acknowledge Incident
* **Method & Route:** `POST /api/admin/support/incidents/{incidentId:int}/acknowledge`
* **Description:** Marks incident as `Acknowledged` and stops the P0/P1 on-call escalation timer.
* **Response `data`:** `OperationalIncidentDto`

#### 26. Assign Incident
* **Method & Route:** `POST /api/admin/support/incidents/{incidentId:int}/assign`
* **Content-Type:** `application/json`
* **Request Body (`AssignIncidentRequestDto`):**
```json
{
  "assignedUserId": 1,
  "assignedTeam": "ParkingOperations"
}
```
* **Response `data`:** `OperationalIncidentDto`

#### 27. Transition Incident Status
* **Method & Route:** `POST /api/admin/support/incidents/{incidentId:int}/transition`
* **Content-Type:** `application/json`
* **Description:** Transitions incident status. **When transitioned to `Resolved`, all linked open customer tickets are automatically marked `Resolved` with a system notification message posted into each ticket's conversation.**
* **Request Body (`IncidentTransitionRequestDto`):**
```json
{
  "toStatus": "Resolved",
  "reason": "现场道闸已完成重启并恢复正常"
}
```
* **Response `data`:** `OperationalIncidentDto`

#### 28. Execute Remote Access Override (Admin Forced Gate Unlock)
* **Method & Route:** `POST /api/admin/support/incidents/{incidentId:int}/access-override`
* **Content-Type:** `application/json`
* **Request Body (`AccessOverrideRequestDto`):**
```json
{
  "bookingId": 102,
  "reason": "客户在闸口等待，人工核实预约有效后远程抬杆",
  "confirmation": true
}
```
* **Response `data`:** `AccessOverrideResultDto`

#### 29. Link Ticket to Incident
* **Method & Route:** `POST /api/admin/support/incidents/{incidentIdentifier}/link-ticket`
* **Content-Type:** `application/json`
* **Request Body (`LinkTicketRequestDto`):**
```json
{
  "ticketId": 21,
  "ticketReference": "TKT-2026-48291"
}
```
* **Response `data`:** `OperationalIncidentDto`

---

### D. Financial Disputes & Refunds (`AdminSupportDisputeController`)

#### 30. List Dispute Investigations
* **Method & Route:** `GET /api/admin/support/disputes`
* **Query Parameters:**
  * `status` (`string?`, optional): `Opened` | `WaitingForCustomerEvidence` | `WaitingForInternalReview` | `UnderInvestigation` | `Approved` | `Declined` | `PartiallyApproved` | `Closed`
  * `type` (`string?`, optional): `Refund` | `DuplicateCharge` | `UnauthorizedParkingClaim` | `OverstayFeeChallenge` | `OwnerPayoutHold` | `AccountAbuse`
  * `team` (`string?`, optional)
  * `page` (`int`, default `1`), `pageSize` (`int`, default `25`)
* **Response:** `SupportApiPagedResponse<DisputeCustomerSummaryDto>`

#### 31. Get Admin Dispute Detail
* **Method & Route:** `GET /api/admin/support/disputes/{disputeId:int}`
* **Response `data`:** `DisputeAdminDto` (includes `evidences` and `auditTimeline`)

#### 32. Upload Dispute Evidence (Admin)
* **Method & Route:** `POST /api/admin/support/disputes/{disputeId:int}/evidence`
* **Content-Type:** `multipart/form-data`
* **Form Fields (`UploadDisputeEvidenceRequestDto`):**
  * `evidenceType` (`string`, default `"Receipt"`): `Receipt` | `Screenshot` | `Photo` | `AccessLog` | `Telemetry` | `Document`
  * `description` (`string?`)
  * `file` (`IFormFile`, required)
* **Response `data`:** `DisputeEvidenceDto`

#### 33. Request Evidence from Customer
* **Method & Route:** `POST /api/admin/support/disputes/{disputeId:int}/request-evidence`
* **Content-Type:** `application/json`
* **Description:** Marks dispute as `WaitingForCustomerEvidence`, **automatically sets the linked `SupportTicket` to `WaitingForCustomer` (`ActionRequired`), and posts a message into the customer's ticket chat** prompting them to upload photos/files directly in the ticket.
* **Request Body (`RequestDisputeEvidenceRequestDto`):**
```json
{
  "requiredEvidence": "扣款截图或现场照片",
  "customerMessage": "请在此对话中直接上传您的银行扣款截图，以便我们为您办理退款。",
  "deadline": "2026-10-19T12:00:00Z"
}
```

#### 34. Execute Dispute Decision (Atomic Wallet Refund)
* **Method & Route:** `POST /api/admin/support/disputes/{disputeId:int}/decision`
* **Content-Type:** `application/json`
* **Description:** Executes the financial decision:
  * `ApproveReversal`: Credits the customer's `Wallet`, records a completed `Refund` `Transaction`, marks the linked `SupportTicket` as `Resolved`, and posts a refund confirmation message in the ticket chat.
  * `Decline`: Marks the dispute `Declined`, resolves the linked ticket, and posts the decline explanation in the ticket chat.
  * `NeedMoreInfo`: Sets the linked ticket to `WaitingForCustomer` (`ActionRequired`) and posts the request in the ticket chat.
* **Request Body (`DisputeDecisionRequestDto`):**
```json
{
  "decision": "ApproveReversal",
  "reason": "核实道闸离线导致无法入场，批准全额退还至钱包",
  "amount": 12.00
}
```
* **Response `data`:** `DisputeAdminDto`

#### 35. Assign Dispute
* **Method & Route:** `POST /api/admin/support/disputes/{disputeId:int}/assign`
* **Content-Type:** `application/json`
* **Request Body (`AssignDisputeRequestDto`):**
```json
{
  "assignedUserId": 1,
  "assignedTeam": "Payments",
  "status": "UnderInvestigation"
}
```
* **Response `data`:** `DisputeAdminDto`

---

### E. 24/7 On-Call Schedule & Escalation Policy (`AdminSupportOnCallController`)

#### 36. Get On-Call Status & Policy
* **Method & Route:** `GET /api/admin/support/on-call`
* **Response `data`:** `SupportOnCallStatusDto`

#### 37. Test On-Call Notification Channel
* **Method & Route:** `POST /api/admin/support/on-call/test`
* **Content-Type:** `application/json`
* **Request Body (`TestOnCallNotificationRequestDto`):**
```json
{
  "channel": "Push",
  "targetRecipient": "admin@parkjom.com",
  "message": "On-call alert test"
}
```
* **Response `data`:** `TestNotificationResultDto`

#### 38. Update On-Call Escalation Policy
* **Method & Route:** `PUT /api/admin/support/on-call/policy`
* **Content-Type:** `application/json`
* **Request Body (`UpdateOnCallPolicyRequestDto`):**
```json
{
  "p0BackupDelayMinutes": 2,
  "p0SupervisorDelayMinutes": 5,
  "p0ManagerDelayMinutes": 15,
  "p1BackupDelayMinutes": 5,
  "p1SupervisorDelayMinutes": 15,
  "p1ManagerDelayMinutes": 30,
  "notificationChannels": "Push,SMS,Phone,Email",
  "autoEscalateEnabled": true
}
```
* **Response `data`:** `SupportOnCallPolicyDto`

---

## Part 3: Realtime Channels (SignalR & Native WebSocket)

1. **SignalR Hub:** `/hubs/support`
   * **Client -> Server Hub Methods:**
     * `JoinTicket(ticketId: int)`
     * `LeaveTicket(ticketId: int)`
2. **Native WebSocket:** `ws(s)://<host>/api/support/ws?access_token=<JWT_TOKEN>`
3. **Server -> Client Event Types:**
   * `TicketCreated` (`SupportTicketDto`)
   * `TicketMessageReceived` (`SupportTicketMessageDto`)
   * `TicketUpdated` (`SupportTicketDto`)
   * `SelfGateUnlockExecuted` (`SelfUnlockGateResultDto`)
   * `IncidentCreated` / `IncidentAcknowledged` / `IncidentUpdated` (`OperationalIncidentDto`)
   * `AccessOverrideExecuted` (`AccessOverrideResultDto`)
   * `DisputeEvidenceUploaded` (`DisputeEvidenceDto`)
   * `DisputeEvidenceRequested` (`{ disputeId, requiredEvidence, customerMessage, deadline }`)
   * `DisputeDecided` (`DisputeAdminDto`)
