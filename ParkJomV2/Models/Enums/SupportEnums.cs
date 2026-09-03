namespace ParkJomV2.Models.Enums;

public enum SupportTicketStatus
{
    New,
    Assigned,
    InProgress,
    WaitingForCustomer,
    WaitingForInternalTeam,
    Resolved,
    Closed,
    Reopened,
    Duplicate,
    Cancelled
}

public enum SupportTicketPriority
{
    P0,
    P1,
    P2,
    P3
}

public enum SupportTicketType
{
    Preset,
    Custom
}

public enum SupportSource
{
    QuickHelp,
    LiveChat,
    Admin,
    System
}

public enum SupportCategory
{
    ParkingAccess,
    Booking,
    Payment,
    Account,
    OwnerSupport,
    General
}

public enum ConversationStatus
{
    Active,
    WaitingCustomer,
    WaitingAdmin,
    Closed,
    ConvertedToTicket
}

public enum IncidentStatus
{
    Open,
    Acknowledged,
    Monitoring,
    Resolved,
    Closed,
    Escalated
}

public enum IncidentPriority
{
    P0,
    P1,
    P2,
    P3
}

public enum DisputeStatus
{
    Opened,
    EvidenceReview,
    FinanceReview,
    DecisionReady,
    MoreInfo,
    Approved,
    Declined
}

public enum DisputeType
{
    DuplicateCharge,
    UnrecognizedCharge,
    Refund,
    OwnerPayout,
    AccountSecurity
}

public enum DisputeDecision
{
    ApproveReversal,
    Decline,
    NeedMoreInfo
}

public enum NotificationChannel
{
    Push,
    SMS,
    Phone,
    Email,
    InternalWebhook
}
