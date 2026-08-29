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
    }
}
