using Microsoft.EntityFrameworkCore;
using ParkJomV2.Models;

namespace ParkJomV2.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Property> Properties => Set<Property>();

    public DbSet<Station> Stations => Set<Station>();
    public DbSet<ParkingSpot> ParkingSpots => Set<ParkingSpot>();
    public DbSet<ParkingSpotImage> ParkingSpotImages => Set<ParkingSpotImage>();

    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingQuote> BookingQuotes => Set<BookingQuote>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<PlatformWallet> PlatformWallets => Set<PlatformWallet>();
    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<ParkingVerificationRequest> ParkingVerificationRequests => Set<ParkingVerificationRequest>();
    public DbSet<VerificationDocument> VerificationDocuments => Set<VerificationDocument>();

    public DbSet<IoTDevice> IoTDevices => Set<IoTDevice>();
    public DbSet<IoTStatusLog> IoTStatusLogs => Set<IoTStatusLog>();
    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();

    public DbSet<Availability> Availabilities => Set<Availability>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();

    //public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Review> Reviews => Set<Review>();

    // Support Module
    public DbSet<SupportConversation> SupportConversations => Set<SupportConversation>();
    public DbSet<SupportConversationMessage> SupportConversationMessages => Set<SupportConversationMessage>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportTicketMessage> SupportTicketMessages => Set<SupportTicketMessage>();
    public DbSet<SupportWorkflowRun> SupportWorkflowRuns => Set<SupportWorkflowRun>();
    public DbSet<OperationalIncident> OperationalIncidents => Set<OperationalIncident>();
    public DbSet<IncidentTicket> IncidentTickets => Set<IncidentTicket>();
    public DbSet<DisputeInvestigation> DisputeInvestigations => Set<DisputeInvestigation>();
    public DbSet<DisputeEvidence> DisputeEvidences => Set<DisputeEvidence>();
    public DbSet<SupportAttachment> SupportAttachments => Set<SupportAttachment>();
    public DbSet<SupportAuditEvent> SupportAuditEvents => Set<SupportAuditEvent>();
    public DbSet<SupportNotificationAttempt> SupportNotificationAttempts => Set<SupportNotificationAttempt>();
    public DbSet<SupportOnCallSchedule> SupportOnCallSchedules => Set<SupportOnCallSchedule>();
    public DbSet<SupportOnCallPolicy> SupportOnCallPolicies => Set<SupportOnCallPolicy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // =========================
        // User
        // =========================

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasOne(u => u.Wallet)
            .WithOne(w => w.User)
            .HasForeignKey<Wallet>(w => w.UserId);

        // =========================
        // Vehicle
        // =========================

        modelBuilder.Entity<Vehicle>()
            .HasOne(v => v.User)
            .WithMany(u => u.Vehicles)
            .HasForeignKey(v => v.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        // =========================
        // Parking Spot
        // =========================

        modelBuilder.Entity<ParkingSpot>()
            .HasOne(p => p.Owner)
            .WithMany(u => u.OwnedParkingSpots)
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.NoAction);

        // =========================
        // Booking
        // =========================

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.Renter)
            .WithMany(u => u.Bookings)
            .HasForeignKey(b => b.RenterId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.ParkingSpot)
            .WithMany(p => p.Bookings)
            .HasForeignKey(b => b.ParkingSpotId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.BookingQuote)
            .WithOne(q => q.Booking)
            .HasForeignKey<Booking>(b => b.BookingQuoteId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BookingQuote>()
            .HasOne(q => q.Renter)
            .WithMany()
            .HasForeignKey(q => q.RenterId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<BookingQuote>()
            .HasOne(q => q.ParkingSpot)
            .WithMany(p => p.BookingQuotes)
            .HasForeignKey(q => q.ParkingSpotId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Station>()
            .HasMany(s => s.Properties)
            .WithOne(p => p.Station)
            .HasForeignKey(p => p.NearestStationId)
            .OnDelete(DeleteBehavior.Restrict);

        // =========================
        // Favorite
        // =========================

        modelBuilder.Entity<Favorite>()
            .HasOne(f => f.User)
            .WithMany(u => u.Favorites)
            .HasForeignKey(f => f.UserId);

        modelBuilder.Entity<Favorite>()
            .HasIndex(f => new { f.UserId, f.ParkingSpotId })
            .IsUnique();

        // =========================
        // Review
        // =========================

        modelBuilder.Entity<Review>()
            .HasOne(r => r.Reviewer)
            .WithMany(u => u.Reviews)
            .HasForeignKey(r => r.ReviewerId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Review>()
            .HasIndex(r => r.BookingId)
            .IsUnique();

        // =========================
        // Access Log
        // =========================

        modelBuilder.Entity<AccessLog>()
            .HasOne(a => a.User)
            .WithMany(u => u.AccessLogs)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        // =========================
        // Media File
        // =========================

        modelBuilder.Entity<MediaFile>()
            .HasOne(m => m.UploadedByUser)
            .WithMany()
            .HasForeignKey(m => m.UploadedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // =========================
        // Parking Verification
        // =========================

        modelBuilder.Entity<ParkingVerificationRequest>()
            .HasOne(v => v.SubmittedByUser)
            .WithMany(u => u.SubmittedVerificationRequests)
            .HasForeignKey(v => v.SubmittedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        // =========================
        // Booking Reference
        // =========================

        modelBuilder.Entity<Booking>()
            .HasIndex(b => b.BookingReference)
            .IsUnique();

        modelBuilder.Entity<Booking>()
            .HasIndex(b => new { b.RenterId, b.IdempotencyKey })
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL");

        // =========================
        // Store enums as strings
        // =========================

        modelBuilder.Entity<User>()
            .Property(u => u.UserType)
            .HasConversion<string>();

        modelBuilder.Entity<Property>()
            .Property(p => p.PropertyType)
            .HasConversion<string>();

        modelBuilder.Entity<Property>()
            .HasIndex(p => p.OsmId)
            .IsUnique()
            .HasFilter("[OsmId] IS NOT NULL");

        modelBuilder.Entity<ParkingSpot>()
            .Property(p => p.AvailabilityStatus)
            .HasConversion<string>();

        modelBuilder.Entity<Wallet>()
            .Property(w => w.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Payment>()
            .Property(p => p.Status)
            .HasConversion<string>();

        // =========================
        // Payment
        // =========================

        modelBuilder.Entity<Payment>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Transaction>()
            .HasOne(transaction => transaction.Wallet)
            .WithMany(wallet => wallet.Transactions)
            .HasForeignKey(transaction => transaction.WalletId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Transaction>()
            .HasOne(transaction => transaction.PlatformWallet)
            .WithMany(wallet => wallet.Transactions)
            .HasForeignKey(transaction => transaction.PlatformWalletId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Transaction>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_Transactions_ExactlyOneWallet",
                "(WalletId IS NOT NULL AND PlatformWalletId IS NULL) OR (WalletId IS NULL AND PlatformWalletId IS NOT NULL)"));

        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Wallet)
            .WithMany(w => w.Payments)
            .HasForeignKey(p => p.WalletId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Booking>()
            .Property(b => b.BookingStatus)
            .HasConversion<string>();

        modelBuilder.Entity<Booking>()
            .Property(b => b.RateType)
            .HasConversion<string>();

        modelBuilder.Entity<BookingQuote>()
            .Property(q => q.RateType)
            .HasConversion<string>();

        modelBuilder.Entity<Transaction>()
            .Property(t => t.TransactionType)
            .HasConversion<string>();

        modelBuilder.Entity<Transaction>()
            .Property(t => t.TransactionStatus)
            .HasConversion<string>();

        modelBuilder.Entity<Transaction>()
            .Property(t => t.PaymentMethod)
            .HasConversion<string>();

        modelBuilder.Entity<ParkingVerificationRequest>()
            .Property(v => v.VerificationStatus)
            .HasConversion<string>();

        modelBuilder.Entity<VerificationDocument>()
            .Property(v => v.DocumentType)
            .HasConversion<string>();

        modelBuilder.Entity<IoTDevice>()
            .Property(i => i.DeviceStatus)
            .HasConversion<string>();

        modelBuilder.Entity<IoTStatusLog>()
            .Property(i => i.DeviceStatus)
            .HasConversion<string>();

        // =========================
        // Support Module Configurations
        // =========================

        // SupportConversation
        modelBuilder.Entity<SupportConversation>()
            .HasIndex(c => c.ConversationReference)
            .IsUnique();

        modelBuilder.Entity<SupportConversation>()
            .Property(c => c.Status)
            .HasConversion<string>();

        modelBuilder.Entity<SupportConversation>()
            .HasOne(c => c.CustomerUser)
            .WithMany()
            .HasForeignKey(c => c.CustomerUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportConversation>()
            .HasOne(c => c.AssignedAdminUser)
            .WithMany()
            .HasForeignKey(c => c.AssignedAdminUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportConversation>()
            .HasOne(c => c.CurrentBooking)
            .WithMany()
            .HasForeignKey(c => c.CurrentBookingId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportConversation>()
            .HasOne(c => c.CurrentParkingSpot)
            .WithMany()
            .HasForeignKey(c => c.CurrentParkingSpotId)
            .OnDelete(DeleteBehavior.NoAction);

        // SupportConversationMessage
        modelBuilder.Entity<SupportConversationMessage>()
            .HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SupportConversationMessage>()
            .HasOne(m => m.SenderUser)
            .WithMany()
            .HasForeignKey(m => m.SenderUserId)
            .OnDelete(DeleteBehavior.NoAction);

        // SupportTicket
        modelBuilder.Entity<SupportTicket>()
            .HasIndex(t => t.TicketReference)
            .IsUnique();

        modelBuilder.Entity<SupportTicket>()
            .Property(t => t.TicketType)
            .HasConversion<string>();

        modelBuilder.Entity<SupportTicket>()
            .Property(t => t.Source)
            .HasConversion<string>();

        modelBuilder.Entity<SupportTicket>()
            .Property(t => t.Category)
            .HasConversion<string>();

        modelBuilder.Entity<SupportTicket>()
            .Property(t => t.Priority)
            .HasConversion<string>();

        modelBuilder.Entity<SupportTicket>()
            .Property(t => t.Status)
            .HasConversion<string>();

        modelBuilder.Entity<SupportTicket>()
            .HasOne(t => t.CustomerUser)
            .WithMany()
            .HasForeignKey(t => t.CustomerUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportTicket>()
            .HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportTicket>()
            .HasOne(t => t.AssignedAdminUser)
            .WithMany()
            .HasForeignKey(t => t.AssignedAdminUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportTicket>()
            .HasOne(t => t.Conversation)
            .WithMany(c => c.ConvertedTickets)
            .HasForeignKey(t => t.ConversationId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportTicket>()
            .HasOne(t => t.WorkflowRun)
            .WithMany()
            .HasForeignKey(t => t.WorkflowRunId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportTicket>()
            .HasOne(t => t.Booking)
            .WithMany()
            .HasForeignKey(t => t.BookingId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportTicket>()
            .HasOne(t => t.ParkingSpot)
            .WithMany()
            .HasForeignKey(t => t.ParkingSpotId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportTicket>()
            .HasOne(t => t.Vehicle)
            .WithMany()
            .HasForeignKey(t => t.VehicleId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportTicket>()
            .HasOne(t => t.OperationalIncident)
            .WithMany()
            .HasForeignKey(t => t.OperationalIncidentId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportTicket>()
            .HasOne(t => t.DisputeInvestigation)
            .WithMany()
            .HasForeignKey(t => t.DisputeInvestigationId)
            .OnDelete(DeleteBehavior.NoAction);

        // SupportTicketMessage
        modelBuilder.Entity<SupportTicketMessage>()
            .HasOne(m => m.Ticket)
            .WithMany(t => t.Messages)
            .HasForeignKey(m => m.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SupportTicketMessage>()
            .HasOne(m => m.SenderUser)
            .WithMany()
            .HasForeignKey(m => m.SenderUserId)
            .OnDelete(DeleteBehavior.NoAction);

        // SupportWorkflowRun
        modelBuilder.Entity<SupportWorkflowRun>()
            .HasIndex(r => r.RunReference)
            .IsUnique();

        modelBuilder.Entity<SupportWorkflowRun>()
            .HasOne(r => r.CustomerUser)
            .WithMany()
            .HasForeignKey(r => r.CustomerUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportWorkflowRun>()
            .HasOne(r => r.Ticket)
            .WithMany()
            .HasForeignKey(r => r.TicketId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportWorkflowRun>()
            .HasOne(r => r.Incident)
            .WithMany()
            .HasForeignKey(r => r.IncidentId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportWorkflowRun>()
            .HasOne(r => r.Dispute)
            .WithMany()
            .HasForeignKey(r => r.DisputeId)
            .OnDelete(DeleteBehavior.NoAction);

        // OperationalIncident
        modelBuilder.Entity<OperationalIncident>()
            .HasIndex(i => i.IncidentReference)
            .IsUnique();

        modelBuilder.Entity<OperationalIncident>()
            .Property(i => i.Priority)
            .HasConversion<string>();

        modelBuilder.Entity<OperationalIncident>()
            .Property(i => i.Status)
            .HasConversion<string>();

        modelBuilder.Entity<OperationalIncident>()
            .HasOne(i => i.Property)
            .WithMany()
            .HasForeignKey(i => i.PropertyId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<OperationalIncident>()
            .HasOne(i => i.ParkingSpot)
            .WithMany()
            .HasForeignKey(i => i.ParkingSpotId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<OperationalIncident>()
            .HasOne(i => i.IoTDevice)
            .WithMany()
            .HasForeignKey(i => i.IoTDeviceId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<OperationalIncident>()
            .HasOne(i => i.AssignedUser)
            .WithMany()
            .HasForeignKey(i => i.AssignedUserId)
            .OnDelete(DeleteBehavior.NoAction);

        // IncidentTicket (Many-to-Many Join Table)
        modelBuilder.Entity<IncidentTicket>()
            .HasKey(it => new { it.IncidentId, it.TicketId });

        modelBuilder.Entity<IncidentTicket>()
            .HasOne(it => it.Incident)
            .WithMany(i => i.IncidentTickets)
            .HasForeignKey(it => it.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<IncidentTicket>()
            .HasOne(it => it.Ticket)
            .WithMany(t => t.IncidentTickets)
            .HasForeignKey(it => it.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        // DisputeInvestigation
        modelBuilder.Entity<DisputeInvestigation>()
            .HasIndex(d => d.DisputeReference)
            .IsUnique();

        modelBuilder.Entity<DisputeInvestigation>()
            .Property(d => d.DisputeType)
            .HasConversion<string>();

        modelBuilder.Entity<DisputeInvestigation>()
            .Property(d => d.Status)
            .HasConversion<string>();

        modelBuilder.Entity<DisputeInvestigation>()
            .HasOne(d => d.CustomerUser)
            .WithMany()
            .HasForeignKey(d => d.CustomerUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DisputeInvestigation>()
            .HasOne(d => d.Ticket)
            .WithMany()
            .HasForeignKey(d => d.TicketId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DisputeInvestigation>()
            .HasOne(d => d.Booking)
            .WithMany()
            .HasForeignKey(d => d.BookingId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DisputeInvestigation>()
            .HasOne(d => d.Payment)
            .WithMany()
            .HasForeignKey(d => d.PaymentId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DisputeInvestigation>()
            .HasOne(d => d.Transaction)
            .WithMany()
            .HasForeignKey(d => d.TransactionId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DisputeInvestigation>()
            .HasOne(d => d.AssignedUser)
            .WithMany()
            .HasForeignKey(d => d.AssignedUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DisputeInvestigation>()
            .HasOne(d => d.DecidedByUser)
            .WithMany()
            .HasForeignKey(d => d.DecidedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        // DisputeEvidence
        modelBuilder.Entity<DisputeEvidence>()
            .HasOne(e => e.Dispute)
            .WithMany(d => d.Evidences)
            .HasForeignKey(e => e.DisputeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DisputeEvidence>()
            .HasOne(e => e.MediaFile)
            .WithMany()
            .HasForeignKey(e => e.MediaFileId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DisputeEvidence>()
            .HasOne(e => e.UploadedByUser)
            .WithMany()
            .HasForeignKey(e => e.UploadedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        // SupportAttachment
        modelBuilder.Entity<SupportAttachment>()
            .HasOne(a => a.MediaFile)
            .WithMany()
            .HasForeignKey(a => a.MediaFileId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportAttachment>()
            .HasOne(a => a.Ticket)
            .WithMany(t => t.Attachments)
            .HasForeignKey(a => a.TicketId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportAttachment>()
            .HasOne(a => a.TicketMessage)
            .WithMany(m => m.Attachments)
            .HasForeignKey(a => a.TicketMessageId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportAttachment>()
            .HasOne(a => a.Conversation)
            .WithMany()
            .HasForeignKey(a => a.ConversationId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportAttachment>()
            .HasOne(a => a.ConversationMessage)
            .WithMany(m => m.Attachments)
            .HasForeignKey(a => a.ConversationMessageId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportAttachment>()
            .HasOne(a => a.UploadedByUser)
            .WithMany()
            .HasForeignKey(a => a.UploadedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        // SupportAuditEvent
        modelBuilder.Entity<SupportAuditEvent>()
            .HasIndex(a => new { a.ObjectType, a.ObjectId });

        modelBuilder.Entity<SupportAuditEvent>()
            .HasOne(a => a.ActorUser)
            .WithMany()
            .HasForeignKey(a => a.ActorUserId)
            .OnDelete(DeleteBehavior.NoAction);

        // SupportNotificationAttempt
        modelBuilder.Entity<SupportNotificationAttempt>()
            .Property(n => n.Channel)
            .HasConversion<string>();

        modelBuilder.Entity<SupportNotificationAttempt>()
            .HasOne(n => n.Incident)
            .WithMany(i => i.NotificationAttempts)
            .HasForeignKey(n => n.IncidentId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportNotificationAttempt>()
            .HasOne(n => n.Ticket)
            .WithMany()
            .HasForeignKey(n => n.TicketId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportNotificationAttempt>()
            .HasOne(n => n.RecipientUser)
            .WithMany()
            .HasForeignKey(n => n.RecipientUserId)
            .OnDelete(DeleteBehavior.NoAction);

        // SupportOnCallSchedule
        modelBuilder.Entity<SupportOnCallSchedule>()
            .HasOne(s => s.PrimaryResponder)
            .WithMany()
            .HasForeignKey(s => s.PrimaryResponderId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportOnCallSchedule>()
            .HasOne(s => s.BackupResponder)
            .WithMany()
            .HasForeignKey(s => s.BackupResponderId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportOnCallSchedule>()
            .HasOne(s => s.Supervisor)
            .WithMany()
            .HasForeignKey(s => s.SupervisorId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SupportOnCallSchedule>()
            .HasOne(s => s.OperationsManager)
            .WithMany()
            .HasForeignKey(s => s.OperationsManagerId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
