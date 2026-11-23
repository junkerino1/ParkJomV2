using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services.Notifications;

namespace ParkJomV2.Services.Settlement;

public sealed class SettlementService
{
    private static readonly TimeSpan MalaysiaOffset = TimeSpan.FromHours(8);
    private const decimal MinAutoWithdrawalAmount = 10.00m;

    private readonly ApplicationDbContext _db;
    private readonly WalletService _walletService;
    private readonly TransactionService _transactionService;
    private readonly AccessLogService _accessLogService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SettlementService> _logger;

    public SettlementService(
        ApplicationDbContext db,
        WalletService walletService,
        TransactionService transactionService,
        AccessLogService accessLogService,
        INotificationService notificationService,
        ILogger<SettlementService> logger)
    {
        _db = db;
        _walletService = walletService;
        _transactionService = transactionService;
        _accessLogService = accessLogService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<AdminSettlementSummaryDTO> GetSummaryAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var config = await GetOrDefaultSystemConfigAsync(ct);
        var taxRate = config.TaxRatePercent;

        var platformWallet = await _db.PlatformWallets
            .AsNoTracking()
            .OrderBy(w => w.PlatformWalletId)
            .FirstOrDefaultAsync(ct);

        var ownerWallets = await _db.Wallets
            .AsNoTracking()
            .Where(w => w.User.UserType == UserType.PropertyOwner)
            .Select(w => new { w.UserId, w.Balance, w.OnHold, w.Status })
            .ToListAsync(ct);

        var totalOwnerAvailable = ownerWallets.Sum(w => w.Balance);
        var totalEscrowOnHold = ownerWallets.Sum(w => w.OnHold);

        var unreleasedBookings = await _db.Bookings
            .AsNoTracking()
            .Where(b => b.BookingStatus == BookingStatus.Completed &&
                        b.OwnerPayoutAmount > 0 &&
                        b.PayoutReleasedAt == null)
            .Select(b => b.OwnerPayoutAmount)
            .ToListAsync(ct);

        var withdrawals = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.TransactionType == TransactionType.Withdrawal)
            .Select(t => new { t.TransactionStatus, t.Amount })
            .ToListAsync(ct);

        var pendingWithdrawals = withdrawals.Where(w => w.TransactionStatus == TransactionStatus.Pending).ToList();
        var clearedWithdrawals = withdrawals.Where(w => w.TransactionStatus == TransactionStatus.Completed).ToList();

        var autoWithdrawOwners = await _db.OwnerPayoutAccounts
            .AsNoTracking()
            .Where(a => a.AutoWithdraw)
            .Select(a => a.UserId)
            .ToListAsync(ct);
        var autoWithdrawSet = autoWithdrawOwners.ToHashSet();
        var eligibleAutoWallets = ownerWallets
            .Where(w => w.Status == WalletStatus.Active &&
                        autoWithdrawSet.Contains(w.UserId) &&
                        w.Balance >= MinAutoWithdrawalAmount)
            .ToList();

        var totalGrossCommissions = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.TransactionType == TransactionType.PlatformCommission && t.TransactionStatus == TransactionStatus.Completed)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var totalCommissionTaxSst = CalculateInclusiveTax(totalGrossCommissions, taxRate);

        var totalOverstayPenaltiesCollected = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.TransactionType == TransactionType.OverstayPenalty &&
                        t.TransactionStatus == TransactionStatus.Completed &&
                        t.Amount > 0)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var totalStripeTopUps = await _db.Payments
            .AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Completed)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        var latestBatchEntity = await _db.SettlementBatches
            .AsNoTracking()
            .Include(b => b.CreatedByUser)
            .OrderByDescending(b => b.SettlementBatchId)
            .FirstOrDefaultAsync(ct);

        var reconciliation = await RunReconciliationAsync(null, null, null, ct, writeAuditLog: false);

        return new AdminSettlementSummaryDTO
        {
            CommissionRate = config.CommissionRate,
            TaxRatePercent = taxRate,
            PlatformWalletBalance = platformWallet?.Balance ?? 0m,
            PlatformTotalRevenue = platformWallet?.TotalRevenue ?? 0m,
            PlatformTotalRefunded = platformWallet?.TotalRefunded ?? 0m,
            TotalOwnerAvailable = totalOwnerAvailable,
            TotalEscrowOnHold = totalEscrowOnHold,
            UnreleasedCompletedBookingsCount = unreleasedBookings.Count,
            UnreleasedCompletedBookingsAmount = unreleasedBookings.Sum(),
            PendingWithdrawalsCount = pendingWithdrawals.Count,
            PendingWithdrawalsAmount = pendingWithdrawals.Sum(w => Math.Abs(w.Amount)),
            ClearedWithdrawalsCount = clearedWithdrawals.Count,
            ClearedWithdrawalsAmount = clearedWithdrawals.Sum(w => Math.Abs(w.Amount)),
            EligibleAutoWithdrawOwnersCount = eligibleAutoWallets.Count,
            EligibleAutoWithdrawAmount = eligibleAutoWallets.Sum(w => w.Balance),
            TotalGrossCommissions = totalGrossCommissions,
            TotalCommissionTaxSst = totalCommissionTaxSst,
            TotalOverstayPenaltiesCollected = totalOverstayPenaltiesCollected,
            TotalStripeTopUps = totalStripeTopUps,
            NextScheduledIbgCutoffUtc = GetNextScheduledIbgCutoffUtc(now),
            LatestBatch = latestBatchEntity is null ? null : MapBatchSummary(latestBatchEntity),
            ReconciliationHealth = reconciliation.OverallStatus,
            GeneratedAtUtc = now
        };
    }

    public async Task<AdminWithdrawalListResponse> GetWithdrawalsAsync(
        string? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.Transactions
            .AsNoTracking()
            .Include(t => t.Wallet!)
                .ThenInclude(w => w.User)
            .Where(t => t.TransactionType == TransactionType.Withdrawal && t.WalletId != null);

        if (!string.IsNullOrWhiteSpace(status) &&
            !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse<TransactionStatus>(status.Trim(), true, out var parsedStatus))
        {
            query = query.Where(t => t.TransactionStatus == parsedStatus);
        }

        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.TransactionId)
            .ToListAsync(ct);

        var ownerIds = rows
            .Select(r => r.Wallet!.UserId)
            .Distinct()
            .ToList();

        var payoutAccounts = await _db.OwnerPayoutAccounts
            .AsNoTracking()
            .Where(a => ownerIds.Contains(a.UserId))
            .ToDictionaryAsync(a => a.UserId, ct);

        var txIds = rows.Select(r => r.TransactionId).ToList();
        var batchItems = await _db.SettlementBatchItems
            .AsNoTracking()
            .Include(i => i.SettlementBatch)
            .Where(i => txIds.Contains(i.TransactionId))
            .OrderByDescending(i => i.SettlementBatchItemId)
            .ToListAsync(ct);

        var latestBatchByTx = batchItems
            .GroupBy(i => i.TransactionId)
            .ToDictionary(g => g.Key, g => g.First());

        var mapped = rows.Select(tx =>
        {
            var user = tx.Wallet!.User;
            payoutAccounts.TryGetValue(user.UserId, out var payoutAccount);
            latestBatchByTx.TryGetValue(tx.TransactionId, out var batchItem);

            var bankName = batchItem?.BankName ?? payoutAccount?.BankName ?? "Unconfigured";
            var bankCode = batchItem?.BankCode ?? MalaysianBankDirectory.ResolveBic(bankName);
            var accountNumber = batchItem?.AccountNumber ?? payoutAccount?.AccountNumber ?? "—";
            var holderName = batchItem?.AccountHolderName
                             ?? payoutAccount?.AccountHolderName
                             ?? FormatFullName(user);

            return new AdminWithdrawalItemDTO
            {
                TransactionId = tx.TransactionId,
                ReferenceNumber = tx.ReferenceNumber,
                WalletId = tx.WalletId!.Value,
                OwnerUserId = user.UserId,
                OwnerName = FormatFullName(user),
                OwnerEmail = user.Email,
                Amount = Math.Abs(tx.Amount),
                Status = tx.TransactionStatus.ToString(),
                BankName = bankName,
                BankCode = bankCode,
                AccountNumber = accountNumber,
                AccountHolderName = holderName,
                AutoWithdraw = payoutAccount?.AutoWithdraw ?? false,
                BatchId = batchItem?.SettlementBatchId,
                BatchReference = batchItem?.SettlementBatch?.BatchReference,
                BatchItemStatus = batchItem?.Status,
                ClearingReference = batchItem?.ClearingReference,
                FailureReason = batchItem?.FailureReason,
                CreatedAt = tx.CreatedAt,
                UpdatedAt = tx.UpdatedAt
            };
        });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            mapped = mapped.Where(item =>
                item.OwnerName.ToLowerInvariant().Contains(term, StringComparison.Ordinal) ||
                item.OwnerEmail.ToLowerInvariant().Contains(term, StringComparison.Ordinal) ||
                item.ReferenceNumber.ToLowerInvariant().Contains(term, StringComparison.Ordinal) ||
                item.BankName.ToLowerInvariant().Contains(term, StringComparison.Ordinal) ||
                item.AccountNumber.ToLowerInvariant().Contains(term, StringComparison.Ordinal) ||
                (item.BatchReference?.ToLowerInvariant().Contains(term, StringComparison.Ordinal) ?? false));
        }

        var filtered = mapped.ToList();
        var totalCount = filtered.Count;
        var pageItems = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new AdminWithdrawalListResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = pageItems.Count > 0 ? "Owner withdrawal requests retrieved." : "No matching withdrawal requests found.",
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
            Data = pageItems
        };
    }

    public async Task<AdminWithdrawalItemDTO> ProcessWithdrawalDecisionAsync(
        int transactionId,
        WithdrawalDecisionRequest request,
        int? adminUserId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await using var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var tx = await _db.Transactions
            .Include(t => t.Wallet!)
                .ThenInclude(w => w.User)
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId && t.TransactionType == TransactionType.Withdrawal, ct);

        if (tx is null || tx.Wallet is null)
            throw new KeyNotFoundException($"Withdrawal transaction #{transactionId} was not found.");

        if (tx.TransactionStatus != TransactionStatus.Pending)
            throw new InvalidOperationException($"Withdrawal {tx.ReferenceNumber} is already {tx.TransactionStatus} and cannot be modified.");

        var amount = Math.Abs(tx.Amount);
        var owner = tx.Wallet.User;
        var linkedItems = await _db.SettlementBatchItems
            .Include(i => i.SettlementBatch)
                .ThenInclude(b => b.Items)
            .Where(i => i.TransactionId == tx.TransactionId && i.Status != "Cleared" && i.Status != "Rejected")
            .ToListAsync(ct);

        if (request.Approve)
        {
            tx.TransactionStatus = TransactionStatus.Completed;
            tx.UpdatedAt = now;

            var clearingRef = string.IsNullOrWhiteSpace(request.BankReference)
                ? $"IBG-CLR-{now:yyyyMMddHHmmss}-{tx.TransactionId}"
                : request.BankReference.Trim();

            foreach (var item in linkedItems)
            {
                item.Status = "Cleared";
                item.ClearingReference = clearingRef;
                item.ProcessedAt = now;
                item.UpdatedAt = now;
                RecalculateBatchStatus(item.SettlementBatch, now);
            }

            await _db.SaveChangesAsync(ct);
            await dbTx.CommitAsync(ct);

            await _accessLogService.LogAsync(
                adminUserId,
                "AdminClearWithdrawal",
                true,
                $"TransactionId={tx.TransactionId}; Ref={tx.ReferenceNumber}; Amount=RM {amount:F2}; BankRef={clearingRef}");

            await TryNotifyWithdrawalStatusAsync(owner.UserId, tx.TransactionId, "Completed", amount, ct);
        }
        else
        {
            var reason = string.IsNullOrWhiteSpace(request.Reason)
                ? "Rejected during administrator clearing review."
                : request.Reason.Trim();

            tx.TransactionStatus = TransactionStatus.Failed;
            tx.UpdatedAt = now;

            _walletService.Refund(tx.Wallet, amount, now);
            _transactionService.Create(
                tx.Wallet.WalletId,
                null,
                null,
                TransactionType.Refund,
                amount,
                PaymentMethod.Wallet,
                $"REV-{tx.ReferenceNumber}",
                now,
                TransactionStatus.Completed);

            foreach (var item in linkedItems)
            {
                item.Status = "Rejected";
                item.FailureReason = reason;
                item.ProcessedAt = now;
                item.UpdatedAt = now;
                RecalculateBatchStatus(item.SettlementBatch, now);
            }

            await _db.SaveChangesAsync(ct);
            await dbTx.CommitAsync(ct);

            await _accessLogService.LogAsync(
                adminUserId,
                "AdminRejectWithdrawal",
                true,
                $"TransactionId={tx.TransactionId}; Ref={tx.ReferenceNumber}; Refunded=RM {amount:F2}; Reason={reason}");

            await TryNotifyWithdrawalStatusAsync(owner.UserId, tx.TransactionId, "Failed", amount, ct);
        }

        var refreshed = await GetWithdrawalsAsync("all", tx.ReferenceNumber, 1, 10, ct);
        return refreshed.Data.First(w => w.TransactionId == tx.TransactionId);
    }

    public async Task<SettlementBatchDetailDTO> GenerateBatchAsync(
        GenerateSettlementBatchRequest request,
        int? adminUserId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var rail = string.Equals(request.Rail?.Trim(), "DuitNow", StringComparison.OrdinalIgnoreCase)
            ? "DuitNow"
            : "IBG";

        await using var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        if (request.IncludeAutoWithdrawOwners)
        {
            var autoAccounts = await _db.OwnerPayoutAccounts
                .Where(a => a.AutoWithdraw && !string.IsNullOrWhiteSpace(a.AccountNumber))
                .ToListAsync(ct);

            if (autoAccounts.Count > 0)
            {
                var autoUserIds = autoAccounts.Select(a => a.UserId).Distinct().ToList();
                var wallets = await _db.Wallets
                    .Where(w => autoUserIds.Contains(w.UserId) &&
                                w.Status == WalletStatus.Active &&
                                w.Balance >= MinAutoWithdrawalAmount)
                    .ToListAsync(ct);

                foreach (var wallet in wallets)
                {
                    var sweepAmount = wallet.Balance;
                    _walletService.Deduct(wallet, sweepAmount, now);
                    var refNum = $"WDR-AUTO-{now:yyyyMMddHHmmss}-{wallet.UserId}";
                    _transactionService.Create(
                        wallet.WalletId,
                        null,
                        null,
                        TransactionType.Withdrawal,
                        -sweepAmount,
                        PaymentMethod.Wallet,
                        refNum,
                        now,
                        TransactionStatus.Pending);
                }

                if (wallets.Count > 0)
                {
                    await _db.SaveChangesAsync(ct);
                }
            }
        }

        var activeBatchTxIds = await _db.SettlementBatchItems
            .AsNoTracking()
            .Where(i => i.Status == "Queued" || i.Status == "Submitted")
            .Select(i => i.TransactionId)
            .Distinct()
            .ToListAsync(ct);
        var activeTxSet = activeBatchTxIds.ToHashSet();

        var pendingQuery = _db.Transactions
            .Include(t => t.Wallet!)
                .ThenInclude(w => w.User)
            .Where(t => t.TransactionType == TransactionType.Withdrawal &&
                        t.TransactionStatus == TransactionStatus.Pending &&
                        t.WalletId != null);

        if (request.TransactionIds is { Count: > 0 })
        {
            var selectedIds = request.TransactionIds.Distinct().ToList();
            pendingQuery = pendingQuery.Where(t => selectedIds.Contains(t.TransactionId));
        }

        var candidates = await pendingQuery
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);

        var eligibleTransactions = candidates
            .Where(t => !activeTxSet.Contains(t.TransactionId))
            .ToList();

        if (eligibleTransactions.Count == 0)
        {
            await dbTx.RollbackAsync(ct);
            throw new InvalidOperationException("No eligible pending owner withdrawal requests are available to batch.");
        }

        var ownerIds = eligibleTransactions.Select(t => t.Wallet!.UserId).Distinct().ToList();
        var payoutAccounts = await _db.OwnerPayoutAccounts
            .AsNoTracking()
            .Where(a => ownerIds.Contains(a.UserId))
            .ToDictionaryAsync(a => a.UserId, ct);

        var missingPayoutOwner = eligibleTransactions
            .FirstOrDefault(t => !payoutAccounts.ContainsKey(t.Wallet!.UserId));
        if (missingPayoutOwner is not null)
        {
            await dbTx.RollbackAsync(ct);
            throw new InvalidOperationException(
                $"Owner {missingPayoutOwner.Wallet!.User.Email} has no payout bank account configured.");
        }

        var malaysiaNow = now.Add(MalaysiaOffset);
        var existingTodayCount = await _db.SettlementBatches
            .CountAsync(b => b.CreatedAt >= now.Date && b.CreatedAt < now.Date.AddDays(1), ct);
        var batchRef = $"{rail.ToUpperInvariant()}-{malaysiaNow:yyyyMMdd}-{(existingTodayCount + 1):D3}";

        var batch = new SettlementBatch
        {
            BatchReference = batchRef,
            Rail = rail,
            ScheduleWindow = string.IsNullOrWhiteSpace(request.ScheduleWindow)
                ? "Twice Weekly (Tue & Fri, 14:00 MYT)"
                : request.ScheduleWindow.Trim(),
            Status = "Generated",
            ItemCount = eligibleTransactions.Count,
            TotalAmount = eligibleTransactions.Sum(t => Math.Abs(t.Amount)),
            Currency = "MYR",
            BnmFormatVersion = "BNM-IBG-v2.1",
            CreatedByUserId = adminUserId,
            CreatedAt = now,
            UpdatedAt = now,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };

        foreach (var tx in eligibleTransactions)
        {
            var owner = tx.Wallet!.User;
            var account = payoutAccounts[owner.UserId];
            var batchItem = new SettlementBatchItem
            {
                TransactionId = tx.TransactionId,
                Transaction = tx,
                OwnerUserId = owner.UserId,
                OwnerUser = owner,
                BankName = account.BankName,
                BankCode = MalaysianBankDirectory.ResolveBic(account.BankName),
                AccountNumber = account.AccountNumber,
                AccountHolderName = account.AccountHolderName,
                Amount = Math.Abs(tx.Amount),
                Status = "Queued",
                CreatedAt = now,
                UpdatedAt = now
            };
            batch.Items.Add(batchItem);
        }

        var (_, checksum) = MalaysianBankDirectory.GenerateBnmIbgPayload(batch, batch.Items);
        batch.FileChecksum = checksum;

        _db.SettlementBatches.Add(batch);
        await _db.SaveChangesAsync(ct);
        await dbTx.CommitAsync(ct);

        await _accessLogService.LogAsync(
            adminUserId,
            "GenerateSettlementBatch",
            true,
            $"BatchRef={batch.BatchReference}; Rail={batch.Rail}; Items={batch.ItemCount}; Total=RM {batch.TotalAmount:F2}; SHA256={batch.FileChecksum[..12]}");

        return (await GetBatchDetailAsync(batch.SettlementBatchId, ct))!;
    }

    public async Task<SettlementBatchListResponse> GetBatchesAsync(int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.SettlementBatches
            .AsNoTracking()
            .Include(b => b.CreatedByUser)
            .OrderByDescending(b => b.SettlementBatchId);

        var totalCount = await query.CountAsync(ct);
        var batches = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new SettlementBatchListResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = batches.Count > 0 ? "Settlement batches retrieved." : "No settlement batches generated yet.",
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
            Data = batches.Select(MapBatchSummary).ToList()
        };
    }

    public async Task<SettlementBatchDetailDTO?> GetBatchDetailAsync(int batchId, CancellationToken ct = default)
    {
        var batch = await _db.SettlementBatches
            .AsNoTracking()
            .Include(b => b.CreatedByUser)
            .Include(b => b.Items)
                .ThenInclude(i => i.Transaction)
            .Include(b => b.Items)
                .ThenInclude(i => i.OwnerUser)
            .FirstOrDefaultAsync(b => b.SettlementBatchId == batchId, ct);

        if (batch is null) return null;

        var (payload, _) = MalaysianBankDirectory.GenerateBnmIbgPayload(batch, batch.Items);
        return new SettlementBatchDetailDTO
        {
            Batch = MapBatchSummary(batch),
            Items = batch.Items
                .OrderBy(i => i.SettlementBatchItemId)
                .Select(i => new SettlementBatchItemDTO
                {
                    SettlementBatchItemId = i.SettlementBatchItemId,
                    TransactionId = i.TransactionId,
                    TransactionReference = i.Transaction.ReferenceNumber,
                    OwnerUserId = i.OwnerUserId,
                    OwnerName = FormatFullName(i.OwnerUser),
                    OwnerEmail = i.OwnerUser.Email,
                    BankName = i.BankName,
                    BankCode = i.BankCode,
                    AccountNumber = i.AccountNumber,
                    AccountHolderName = i.AccountHolderName,
                    Amount = i.Amount,
                    Status = i.Status,
                    ClearingReference = i.ClearingReference,
                    FailureReason = i.FailureReason,
                    ProcessedAt = i.ProcessedAt
                })
                .ToList(),
            IbgPreview = payload
        };
    }

    public async Task<SettlementBatchDetailDTO> SubmitBatchAsync(int batchId, int? adminUserId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var batch = await _db.SettlementBatches
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.SettlementBatchId == batchId, ct);

        if (batch is null)
            throw new KeyNotFoundException($"Settlement batch #{batchId} was not found.");

        if (!string.Equals(batch.Status, "Generated", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Batch {batch.BatchReference} is {batch.Status} and cannot be submitted again.");

        batch.Status = "Submitted";
        batch.SubmittedAt = now;
        batch.UpdatedAt = now;
        foreach (var item in batch.Items.Where(i => i.Status == "Queued"))
        {
            item.Status = "Submitted";
            item.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        await _accessLogService.LogAsync(
            adminUserId,
            "SubmitSettlementBatch",
            true,
            $"BatchRef={batch.BatchReference}; Rail={batch.Rail}; ItemCount={batch.ItemCount}; Amount=RM {batch.TotalAmount:F2}");

        return (await GetBatchDetailAsync(batchId, ct))!;
    }

    public async Task<SettlementBatchDetailDTO> ApplyBankClearingUpdateAsync(
        int? batchId,
        string? batchReference,
        string? bankReference,
        IReadOnlyList<BankClearingItemUpdateRequest>? itemUpdates,
        int? actorUserId,
        string source,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var notificationsToSend = new List<(int UserId, int TransactionId, string Status, decimal Amount)>();

        await using var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var batch = await _db.SettlementBatches
            .Include(b => b.Items)
                .ThenInclude(i => i.Transaction)
                    .ThenInclude(t => t.Wallet)
            .FirstOrDefaultAsync(b =>
                (batchId.HasValue && b.SettlementBatchId == batchId.Value) ||
                (!string.IsNullOrWhiteSpace(batchReference) && b.BatchReference == batchReference.Trim()),
                ct);

        if (batch is null)
            throw new KeyNotFoundException("Target settlement batch was not found.");

        if (!string.IsNullOrWhiteSpace(bankReference))
        {
            batch.BankReference = bankReference.Trim();
        }
        else if (string.IsNullOrWhiteSpace(batch.BankReference))
        {
            batch.BankReference = $"PAYNET-{now:yyyyMMddHHmmss}-{batch.SettlementBatchId}";
        }

        batch.SubmittedAt ??= now;

        var targetItems = new List<(SettlementBatchItem Item, bool Cleared, string? ClearingRef, string? FailureReason)>();
        if (itemUpdates is { Count: > 0 })
        {
            foreach (var update in itemUpdates)
            {
                var match = batch.Items.FirstOrDefault(i =>
                    (update.SettlementBatchItemId.HasValue && i.SettlementBatchItemId == update.SettlementBatchItemId.Value) ||
                    (update.TransactionId.HasValue && i.TransactionId == update.TransactionId.Value) ||
                    (!string.IsNullOrWhiteSpace(update.TransactionReference) &&
                     string.Equals(i.Transaction.ReferenceNumber, update.TransactionReference.Trim(), StringComparison.OrdinalIgnoreCase)));

                if (match is null) continue;
                var isCleared = !string.Equals(update.Status?.Trim(), "Rejected", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(update.Status?.Trim(), "Failed", StringComparison.OrdinalIgnoreCase);
                targetItems.Add((match, isCleared, update.ClearingReference, update.FailureReason));
            }
        }
        else
        {
            foreach (var item in batch.Items.Where(i => i.Status is "Queued" or "Submitted"))
            {
                targetItems.Add((item, true, $"{batch.BankReference}-{item.SettlementBatchItemId}", null));
            }
        }

        if (targetItems.Count == 0)
            throw new InvalidOperationException($"Batch {batch.BatchReference} has no pending items matching the clearing update.");

        foreach (var (item, isCleared, clearingRef, failureReason) in targetItems)
        {
            if (item.Status is "Cleared" or "Rejected") continue;

            if (isCleared)
            {
                item.Status = "Cleared";
                item.ClearingReference = string.IsNullOrWhiteSpace(clearingRef)
                    ? $"{batch.BankReference}-{item.SettlementBatchItemId}"
                    : clearingRef.Trim();
                item.ProcessedAt = now;
                item.UpdatedAt = now;

                if (item.Transaction.TransactionStatus == TransactionStatus.Pending)
                {
                    item.Transaction.TransactionStatus = TransactionStatus.Completed;
                    item.Transaction.UpdatedAt = now;
                    notificationsToSend.Add((item.OwnerUserId, item.TransactionId, "Completed", item.Amount));
                }
            }
            else
            {
                var reason = string.IsNullOrWhiteSpace(failureReason)
                    ? "Bank clearing rejected beneficiary account."
                    : failureReason.Trim();

                item.Status = "Rejected";
                item.FailureReason = reason;
                item.ProcessedAt = now;
                item.UpdatedAt = now;

                if (item.Transaction.TransactionStatus == TransactionStatus.Pending)
                {
                    item.Transaction.TransactionStatus = TransactionStatus.Failed;
                    item.Transaction.UpdatedAt = now;

                    if (item.Transaction.Wallet is not null)
                    {
                        _walletService.Refund(item.Transaction.Wallet, item.Amount, now);
                        _transactionService.Create(
                            item.Transaction.Wallet.WalletId,
                            null,
                            null,
                            TransactionType.Refund,
                            item.Amount,
                            PaymentMethod.Wallet,
                            $"REV-{item.Transaction.ReferenceNumber}",
                            now,
                            TransactionStatus.Completed);
                    }

                    notificationsToSend.Add((item.OwnerUserId, item.TransactionId, "Failed", item.Amount));
                }
            }
        }

        RecalculateBatchStatus(batch, now);
        await _db.SaveChangesAsync(ct);
        await dbTx.CommitAsync(ct);

        await _accessLogService.LogAsync(
            actorUserId,
            "BankClearingSync",
            true,
            $"Source={source}; BatchRef={batch.BatchReference}; BankRef={batch.BankReference}; Status={batch.Status}");

        foreach (var note in notificationsToSend)
        {
            await TryNotifyWithdrawalStatusAsync(note.UserId, note.TransactionId, note.Status, note.Amount, ct);
        }

        return (await GetBatchDetailAsync(batch.SettlementBatchId, ct))!;
    }

    public async Task<(byte[] Bytes, string FileName)> BuildIbgFileAsync(int batchId, CancellationToken ct = default)
    {
        var batch = await _db.SettlementBatches
            .AsNoTracking()
            .Include(b => b.Items)
                .ThenInclude(i => i.Transaction)
            .Include(b => b.Items)
                .ThenInclude(i => i.OwnerUser)
            .FirstOrDefaultAsync(b => b.SettlementBatchId == batchId, ct);

        if (batch is null)
            throw new KeyNotFoundException($"Settlement batch #{batchId} was not found.");

        var (payload, _) = MalaysianBankDirectory.GenerateBnmIbgPayload(batch, batch.Items);
        var bytes = Encoding.UTF8.GetBytes(payload);
        var fileName = $"{batch.BatchReference}-bnm.ibg.txt";
        return (bytes, fileName);
    }

    public async Task<EscrowSyncResultDTO> SyncEscrowLedgerAsync(int? adminUserId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var releasedBookings = new List<(int OwnerId, decimal Amount, int BookingId)>();
        var skipped = 0;
        var flagged = 0;

        await using var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var candidates = await _db.Bookings
            .Include(b => b.ParkingSpot)
            .Where(b => b.BookingStatus == BookingStatus.Completed &&
                        b.OwnerPayoutAmount > 0 &&
                        b.PayoutReleasedAt == null)
            .ToListAsync(ct);

        if (candidates.Count > 0)
        {
            var ownerIds = candidates.Select(b => b.ParkingSpot.OwnerId).Distinct().ToList();
            var ownerWallets = await _db.Wallets
                .Where(w => ownerIds.Contains(w.UserId) && w.Status == WalletStatus.Active)
                .ToDictionaryAsync(w => w.UserId, ct);

            foreach (var booking in candidates)
            {
                if (!ownerWallets.TryGetValue(booking.ParkingSpot.OwnerId, out var ownerWallet))
                {
                    flagged++;
                    continue;
                }

                if (ownerWallet.OnHold >= booking.OwnerPayoutAmount)
                {
                    _walletService.SettleOwnerPayout(ownerWallet, booking.OwnerPayoutAmount, now);
                    booking.PayoutReleasedAt = now;
                    booking.UpdatedAt = now;
                    releasedBookings.Add((booking.ParkingSpot.OwnerId, booking.OwnerPayoutAmount, booking.BookingId));
                }
                else
                {
                    // Historical pre-migration bookings may already have had their held balance settled.
                    // Mark released when wallet OnHold is zero so the sync stays idempotent without over-crediting.
                    if (ownerWallet.OnHold == 0m)
                    {
                        booking.PayoutReleasedAt = booking.UpdatedAt;
                        skipped++;
                    }
                    else
                    {
                        flagged++;
                    }
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        await dbTx.CommitAsync(ct);

        foreach (var item in releasedBookings)
        {
            try
            {
                await _notificationService.PublishAsync(new NotificationPublishRequest(
                    item.OwnerId,
                    "payout.released",
                    new Dictionary<string, object?>
                    {
                        ["amount"] = $"RM {item.Amount:F2}",
                        ["bookingId"] = item.BookingId
                    },
                    entityType: "Booking",
                    entityId: item.BookingId.ToString(),
                    dedupeKey: $"escrow.released:{item.BookingId}"), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to notify owner {OwnerId} for escrow release on booking {BookingId}.", item.OwnerId, item.BookingId);
            }
        }

        var result = new EscrowSyncResultDTO
        {
            ReleasedBookingsCount = releasedBookings.Count,
            ReleasedAmount = releasedBookings.Sum(x => x.Amount),
            SkippedOrAlreadySettledCount = skipped,
            FlaggedMismatchCount = flagged,
            SyncedAtUtc = now
        };

        await _accessLogService.LogAsync(
            adminUserId,
            "SyncEscrowLedger",
            true,
            $"Released={result.ReleasedBookingsCount} (RM {result.ReleasedAmount:F2}); Skipped={skipped}; Flagged={flagged}");

        return result;
    }

    public async Task<SettlementLedgerResponse> GetLedgerAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        string? type,
        string? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var config = await GetOrDefaultSystemConfigAsync(ct);
        var taxRate = config.TaxRatePercent;

        var query = BuildFilteredLedgerQuery(fromUtc, toUtc, type, status, search);

        var allFiltered = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.TransactionId)
            .ToListAsync(ct);

        var totalCount = allFiltered.Count;
        var pageItems = allFiltered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => MapLedgerEntry(t, taxRate))
            .ToList();

        var totalGrossVolume = allFiltered
            .Where(t => t.TransactionStatus == TransactionStatus.Completed)
            .Sum(t => Math.Abs(t.Amount));

        var totalPlatformCommission = allFiltered
            .Where(t => t.TransactionType == TransactionType.PlatformCommission && t.TransactionStatus == TransactionStatus.Completed)
            .Sum(t => t.Amount);

        var totalTaxAmount = CalculateInclusiveTax(totalPlatformCommission, taxRate);
        var totalNetCommission = totalPlatformCommission - totalTaxAmount;

        return new SettlementLedgerResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = pageItems.Count > 0 ? "Financial journal retrieved." : "No matching journal entries found.",
            TaxRatePercent = taxRate,
            TotalGrossVolume = totalGrossVolume,
            TotalPlatformCommission = totalPlatformCommission,
            TotalTaxAmount = totalTaxAmount,
            TotalNetCommission = totalNetCommission,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
            Data = pageItems
        };
    }

    public async Task<(byte[] Content, string ContentType, string FileName)> BuildExportAsync(
        string format,
        DateTime? fromUtc,
        DateTime? toUtc,
        string? type,
        string? status,
        string? search,
        int? adminUserId,
        CancellationToken ct = default)
    {
        var normalizedFormat = string.Equals(format?.Trim(), "xlsx", StringComparison.OrdinalIgnoreCase)
            ? "xlsx"
            : "csv";

        var config = await GetOrDefaultSystemConfigAsync(ct);
        var taxRate = config.TaxRatePercent;

        var transactions = await BuildFilteredLedgerQuery(fromUtc, toUtc, type, status, search)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.TransactionId)
            .Take(10_000)
            .ToListAsync(ct);

        var ledgerEntries = transactions.Select(t => MapLedgerEntry(t, taxRate)).ToList();
        var stamp = DateTime.UtcNow.Add(MalaysiaOffset).ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);

        if (normalizedFormat == "csv")
        {
            var sb = new StringBuilder();
            var headers = new[]
            {
                "Transaction ID", "Reference Number", "Type", "Status", "Payment Method",
                "Ledger Account", "User Name", "User Email", "Booking Reference", "Spot",
                "Gross Amount (MYR)", "SST Rate (%)", "SST Tax (MYR)", "Net Amount (MYR)", "Timestamp (UTC)"
            };
            sb.AppendLine(string.Join(',', headers.Select(EscapeCsv)));

            foreach (var entry in ledgerEntries)
            {
                sb.AppendLine(string.Join(',', new[]
                {
                    EscapeCsv(entry.TransactionId),
                    EscapeCsv(entry.ReferenceNumber),
                    EscapeCsv(entry.Type),
                    EscapeCsv(entry.Status),
                    EscapeCsv(entry.PaymentMethod),
                    EscapeCsv(entry.LedgerAccount),
                    EscapeCsv(entry.UserName ?? "Platform"),
                    EscapeCsv(entry.UserEmail ?? "-"),
                    EscapeCsv(entry.BookingReference ?? "-"),
                    EscapeCsv(entry.SpotLabel ?? "-"),
                    EscapeCsv(entry.Amount.ToString("0.00", CultureInfo.InvariantCulture)),
                    EscapeCsv(entry.TaxRatePercent.ToString("0.00", CultureInfo.InvariantCulture)),
                    EscapeCsv(entry.TaxAmount.ToString("0.00", CultureInfo.InvariantCulture)),
                    EscapeCsv(entry.NetAmount.ToString("0.00", CultureInfo.InvariantCulture)),
                    EscapeCsv(entry.CreatedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture))
                }));
            }

            var bom = Encoding.UTF8.GetPreamble();
            var payload = Encoding.UTF8.GetBytes(sb.ToString());
            var resultBytes = new byte[bom.Length + payload.Length];
            Buffer.BlockCopy(bom, 0, resultBytes, 0, bom.Length);
            Buffer.BlockCopy(payload, 0, resultBytes, bom.Length, payload.Length);

            await _accessLogService.LogAsync(adminUserId, "ExportSettlementLedger", true,
                $"Format=CSV; Rows={ledgerEntries.Count}");

            return (resultBytes, "text/csv; charset=utf-8", $"parkjom-settlement-journal-{stamp}.csv");
        }

        // XLSX multi-sheet export
        var summary = await GetSummaryAsync(ct);
        var withdrawals = await GetWithdrawalsAsync("all", null, 1, 50, ct);
        var reconciliation = await RunReconciliationAsync(fromUtc, toUtc, adminUserId, ct, writeAuditLog: false);

        var summarySheet = new WorkbookSheet(
            "Summary & Tax",
            new[] { "Metric", "Value (MYR / Count)", "Notes" },
            new List<IReadOnlyList<object?>>
            {
                new object?[] { "Platform Commission Rate (%)", summary.CommissionRate, "Configured in SystemConfiguration" },
                new object?[] { "Malaysian SST Rate (%)", summary.TaxRatePercent, "Inclusive service tax on platform commission" },
                new object?[] { "Platform Wallet Balance (MYR)", summary.PlatformWalletBalance, "Available retained commission" },
                new object?[] { "Gross Platform Commissions (MYR)", summary.TotalGrossCommissions, "All completed commission entries" },
                new object?[] { "Calculated SST Tax Liability (MYR)", summary.TotalCommissionTaxSst, $"Extracted at {summary.TaxRatePercent:0.00}% inclusive SST" },
                new object?[] { "Net Platform Commission Ex-Tax (MYR)", summary.TotalGrossCommissions - summary.TotalCommissionTaxSst, "Net fee retained post-SST" },
                new object?[] { "Owner Escrow On Hold (MYR)", summary.TotalEscrowOnHold, "Held until booking exit/completion" },
                new object?[] { "Owner Available Wallet Balance (MYR)", summary.TotalOwnerAvailable, "Ready for withdrawal" },
                new object?[] { "Pending Withdrawals (MYR)", summary.PendingWithdrawalsAmount, $"{summary.PendingWithdrawalsCount} requests queued" },
                new object?[] { "Cleared Withdrawals (MYR)", summary.ClearedWithdrawalsAmount, $"{summary.ClearedWithdrawalsCount} payouts completed" },
                new object?[] { "Stripe Top-Ups Collected (MYR)", summary.TotalStripeTopUps, "Completed Stripe checkout receipts" },
                new object?[] { "Reconciliation Status", reconciliation.OverallStatus, "Automated ledger vs Stripe cross-check" }
            });

        var journalSheet = new WorkbookSheet(
            "Financial Journal",
            new[]
            {
                "Transaction ID", "Reference", "Type", "Status", "Payment Method",
                "Ledger Account", "User", "Email", "Booking Ref", "Spot",
                "Amount (MYR)", "SST %", "SST Tax (MYR)", "Net (MYR)", "Timestamp"
            },
            ledgerEntries.Select(e => (IReadOnlyList<object?>)new object?[]
            {
                e.TransactionId, e.ReferenceNumber, e.Type, e.Status, e.PaymentMethod,
                e.LedgerAccount, e.UserName ?? "Platform", e.UserEmail ?? "-", e.BookingReference ?? "-", e.SpotLabel ?? "-",
                e.Amount, e.TaxRatePercent, e.TaxAmount, e.NetAmount, e.CreatedAt
            }).ToList());

        var withdrawalsSheet = new WorkbookSheet(
            "Owner Withdrawals",
            new[]
            {
                "Tx ID", "Reference", "Owner", "Email", "Bank", "BIC",
                "Account Number", "Holder Name", "Amount (MYR)", "Status", "Batch Ref", "Clearing Ref", "Created"
            },
            withdrawals.Data.Select(w => (IReadOnlyList<object?>)new object?[]
            {
                w.TransactionId, w.ReferenceNumber, w.OwnerName, w.OwnerEmail, w.BankName, w.BankCode,
                w.AccountNumber, w.AccountHolderName, w.Amount, w.Status, w.BatchReference ?? "-", w.ClearingReference ?? "-", w.CreatedAt
            }).ToList());

        var reconSheet = new WorkbookSheet(
            "Stripe Reconciliation",
            new[] { "Section / Source", "Reference", "Account", "Expected / Stripe (MYR)", "Recorded / Ledger (MYR)", "Variance (MYR)", "Status", "Detail" },
            reconciliation.Sections.Select(s => (IReadOnlyList<object?>)new object?[]
            {
                s.Title, s.Key, "Aggregate", s.ExpectedAmount, s.RecordedAmount, s.VarianceAmount, s.Status, s.Notes
            }).Concat(reconciliation.ExceptionsAndRecentReceipts.Select(r => (IReadOnlyList<object?>)new object?[]
            {
                r.Source, r.Reference, r.UserEmail, r.StripeOrBookingAmount, r.LedgerAmount, r.Variance, r.MatchStatus, r.Detail
            })).ToList());

        var xlsxBytes = OpenXmlXlsxWriter.BuildWorkbook(new[] { summarySheet, journalSheet, withdrawalsSheet, reconSheet });

        await _accessLogService.LogAsync(adminUserId, "ExportSettlementLedger", true,
            $"Format=XLSX; Rows={ledgerEntries.Count}");

        return (
            xlsxBytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"parkjom-settlement-journal-{stamp}.xlsx");
    }

    public async Task<ReconciliationReportDTO> RunReconciliationAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        int? adminUserId,
        CancellationToken ct = default,
        bool writeAuditLog = true)
    {
        var now = DateTime.UtcNow;

        var stripePaymentsQuery = _db.Payments
            .AsNoTracking()
            .Include(p => p.User)
            .Where(p => p.Status == PaymentStatus.Completed);

        var stripeTopUpsQuery = _db.Transactions
            .AsNoTracking()
            .Include(t => t.Wallet!)
                .ThenInclude(w => w.User)
            .Where(t => t.TransactionType == TransactionType.TopUp &&
                        t.PaymentMethod == PaymentMethod.Stripe &&
                        t.TransactionStatus == TransactionStatus.Completed);

        var publicStripeQuery = _db.PublicParkingSessions
            .AsNoTracking()
            .Where(s => s.Status == PublicParkingSessionStatus.Paid && s.PaymentMethod == "Stripe");

        if (fromUtc.HasValue)
        {
            stripePaymentsQuery = stripePaymentsQuery.Where(p => (p.CompletedAt ?? p.CreatedAt) >= fromUtc.Value);
            stripeTopUpsQuery = stripeTopUpsQuery.Where(t => t.CreatedAt >= fromUtc.Value);
            publicStripeQuery = publicStripeQuery.Where(s => (s.PaidAtUtc ?? s.CreatedAt) >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            stripePaymentsQuery = stripePaymentsQuery.Where(p => (p.CompletedAt ?? p.CreatedAt) <= toUtc.Value);
            stripeTopUpsQuery = stripeTopUpsQuery.Where(t => t.CreatedAt <= toUtc.Value);
            publicStripeQuery = publicStripeQuery.Where(s => (s.PaidAtUtc ?? s.CreatedAt) <= toUtc.Value);
        }

        var stripePayments = await stripePaymentsQuery.ToListAsync(ct);
        var stripeTopUps = await stripeTopUpsQuery.ToListAsync(ct);
        var publicStripeSessions = await publicStripeQuery.ToListAsync(ct);

        var stripePaymentsTotal = stripePayments.Sum(p => p.Amount);
        var stripeTopUpsTotal = stripeTopUps.Sum(t => t.Amount);
        var stripeTopUpVariance = stripePaymentsTotal - stripeTopUpsTotal;

        var topUpByRef = stripeTopUps
            .Where(t => !string.IsNullOrWhiteSpace(t.ReferenceNumber))
            .GroupBy(t => t.ReferenceNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount), StringComparer.OrdinalIgnoreCase);

        var receiptRows = new List<ReconciliationReceiptRowDTO>();
        foreach (var p in stripePayments.OrderByDescending(p => p.CompletedAt ?? p.CreatedAt).Take(100))
        {
            var intentId = p.StripePaymentIntentId ?? string.Empty;
            decimal matchedAmount = 0m;
            var hasLedgerMatch = !string.IsNullOrEmpty(intentId) && topUpByRef.TryGetValue(intentId, out matchedAmount);
            var diff = p.Amount - matchedAmount;
            var statusLabel = hasLedgerMatch && diff == 0m ? "Matched" : "Discrepancy";

            receiptRows.Add(new ReconciliationReceiptRowDTO
            {
                Source = "Stripe Wallet Top-Up",
                Reference = string.IsNullOrEmpty(intentId) ? (p.StripeSessionId ?? $"PAY-{p.PaymentId}") : intentId,
                UserEmail = p.User.Email,
                StripeOrBookingAmount = p.Amount,
                LedgerAmount = matchedAmount,
                Variance = diff,
                MatchStatus = statusLabel,
                Detail = statusLabel == "Matched"
                    ? "Stripe PaymentIntent matched to Wallet TopUp transaction."
                    : "Completed Stripe payment lacks an equal Wallet TopUp ledger credit.",
                TimestampUtc = p.CompletedAt ?? p.CreatedAt
            });
        }

        foreach (var ps in publicStripeSessions.OrderByDescending(s => s.PaidAtUtc ?? s.CreatedAt).Take(30))
        {
            receiptRows.Add(new ReconciliationReceiptRowDTO
            {
                Source = "Public Drive-In Stripe",
                Reference = ps.StripePaymentIntentId ?? ps.SessionCode,
                UserEmail = ps.CarPlateNumber,
                StripeOrBookingAmount = ps.TotalAmount,
                LedgerAmount = ps.TotalAmount,
                Variance = 0m,
                MatchStatus = "Matched",
                Detail = $"Drive-in session {ps.SessionCode} settled via Stripe Checkout.",
                TimestampUtc = ps.PaidAtUtc ?? ps.CreatedAt
            });
        }

        // Booking split integrity check
        var bookingQuery = _db.Bookings
            .AsNoTracking()
            .Include(b => b.Renter)
            .Where(b => b.BookingStatus == BookingStatus.Confirmed ||
                        b.BookingStatus == BookingStatus.Active ||
                        b.BookingStatus == BookingStatus.Completed);

        if (fromUtc.HasValue) bookingQuery = bookingQuery.Where(b => b.CreatedAt >= fromUtc.Value);
        if (toUtc.HasValue) bookingQuery = bookingQuery.Where(b => b.CreatedAt <= toUtc.Value);

        var bookings = await bookingQuery.ToListAsync(ct);
        var bookingSplitExceptions = bookings
            .Where(b => Math.Abs(b.TotalAmount - (b.OwnerPayoutAmount + b.PlatformCommissionAmount)) > 0.01m)
            .ToList();

        foreach (var badBooking in bookingSplitExceptions)
        {
            var splitSum = badBooking.OwnerPayoutAmount + badBooking.PlatformCommissionAmount;
            receiptRows.Add(new ReconciliationReceiptRowDTO
            {
                Source = "Booking Split Check",
                Reference = badBooking.BookingReference,
                UserEmail = badBooking.Renter.Email,
                StripeOrBookingAmount = badBooking.TotalAmount,
                LedgerAmount = splitSum,
                Variance = badBooking.TotalAmount - splitSum,
                MatchStatus = "Discrepancy",
                Detail = "Booking TotalAmount != OwnerPayoutAmount + PlatformCommissionAmount.",
                TimestampUtc = badBooking.CreatedAt
            });
        }

        // Platform wallet ledger reconciliation
        var platformWallet = await _db.PlatformWallets
            .AsNoTracking()
            .OrderBy(p => p.PlatformWalletId)
            .FirstOrDefaultAsync(ct);

        var platformRecordedBalance = platformWallet?.Balance ?? 0m;
        var platformTotalCommissionCredits = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.PlatformWalletId != null && t.TransactionStatus == TransactionStatus.Completed)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var platformExpectedBalance = platformTotalCommissionCredits - (platformWallet?.TotalRefunded ?? 0m);
        var platformVariance = platformRecordedBalance - platformExpectedBalance;

        // Owner escrow & payout reconciliation
        var ownerWallets = await _db.Wallets
            .AsNoTracking()
            .Where(w => w.User.UserType == UserType.PropertyOwner)
            .ToListAsync(ct);

        var totalOwnerHeldEscrow = ownerWallets.Sum(w => w.OnHold);
        var totalOwnerAvailable = ownerWallets.Sum(w => w.Balance);

        var totalClearedWithdrawals = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.TransactionType == TransactionType.Withdrawal && t.TransactionStatus == TransactionStatus.Completed)
            .SumAsync(t => (decimal?)(-t.Amount), ct) ?? 0m;

        var totalPendingWithdrawals = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.TransactionType == TransactionType.Withdrawal && t.TransactionStatus == TransactionStatus.Pending)
            .SumAsync(t => (decimal?)(-t.Amount), ct) ?? 0m;

        var unreleasedEscrowBookings = await _db.Bookings
            .AsNoTracking()
            .CountAsync(b => b.BookingStatus == BookingStatus.Completed &&
                             b.OwnerPayoutAmount > 0 &&
                             b.PayoutReleasedAt == null, ct);

        var ownerBookingEarned = await _db.Bookings
            .AsNoTracking()
            .Where(b => b.BookingStatus == BookingStatus.Confirmed ||
                        b.BookingStatus == BookingStatus.Active ||
                        b.BookingStatus == BookingStatus.Completed)
            .SumAsync(b => (decimal?)b.OwnerPayoutAmount, ct) ?? 0m;

        var ownerPenaltiesCredited = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.TransactionType == TransactionType.OverstayPenalty &&
                        t.TransactionStatus == TransactionStatus.Completed &&
                        t.Amount > 0)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var ownerRecordedTotal = totalOwnerHeldEscrow + totalOwnerAvailable + totalClearedWithdrawals + totalPendingWithdrawals;
        var ownerExpectedTotal = ownerBookingEarned + ownerPenaltiesCredited;

        var sections = new List<ReconciliationSectionDTO>
        {
            new()
            {
                Key = "stripe-topups",
                Title = "Stripe Receipts vs Wallet Top-Ups",
                ExpectedAmount = stripePaymentsTotal,
                RecordedAmount = stripeTopUpsTotal,
                VarianceAmount = stripeTopUpVariance,
                Status = Math.Abs(stripeTopUpVariance) <= 0.01m && receiptRows.All(r => r.Source != "Stripe Wallet Top-Up" || r.MatchStatus == "Matched")
                    ? "Matched"
                    : "Discrepancy",
                Notes = $"{stripePayments.Count} completed Stripe wallet checkouts matched against {stripeTopUps.Count} Wallet TopUp ledger credits."
            },
            new()
            {
                Key = "booking-splits",
                Title = "Booking Commission Split Integrity",
                ExpectedAmount = bookings.Sum(b => b.TotalAmount),
                RecordedAmount = bookings.Sum(b => b.OwnerPayoutAmount + b.PlatformCommissionAmount),
                VarianceAmount = bookings.Sum(b => b.TotalAmount - (b.OwnerPayoutAmount + b.PlatformCommissionAmount)),
                Status = bookingSplitExceptions.Count == 0 ? "Matched" : "Discrepancy",
                Notes = bookingSplitExceptions.Count == 0
                    ? $"All {bookings.Count} active/confirmed/completed bookings balance Total = OwnerPayout + Commission."
                    : $"{bookingSplitExceptions.Count} booking(s) exhibit commission split rounding variance."
            },
            new()
            {
                Key = "owner-escrow-payouts",
                Title = "Owner Escrow & Bank Payout Pool",
                ExpectedAmount = ownerExpectedTotal,
                RecordedAmount = ownerRecordedTotal,
                VarianceAmount = ownerExpectedTotal - ownerRecordedTotal,
                Status = unreleasedEscrowBookings > 0 ? "AttentionRequired" : "Matched",
                Notes = unreleasedEscrowBookings > 0
                    ? $"{unreleasedEscrowBookings} completed booking(s) await escrow release sync into owner available balance."
                    : "All completed bookings have synchronized escrow releases; withdrawals reconcile with bank queues."
            },
            new()
            {
                Key = "platform-wallet",
                Title = "Platform Commission Wallet Ledger",
                ExpectedAmount = platformExpectedBalance,
                RecordedAmount = platformRecordedBalance,
                VarianceAmount = platformVariance,
                Status = Math.Abs(platformVariance) <= 0.01m ? "Matched" : "AttentionRequired",
                Notes = $"Recorded balance RM {platformRecordedBalance:F2} vs net ledger commissions minus refunds RM {platformExpectedBalance:F2}."
            }
        };

        var overallStatus = sections.Any(s => s.Status == "Discrepancy")
            ? "Discrepancy"
            : sections.Any(s => s.Status == "AttentionRequired")
                ? "AttentionRequired"
                : "Matched";

        var orderedRows = receiptRows
            .OrderByDescending(r => r.MatchStatus != "Matched")
            .ThenByDescending(r => r.TimestampUtc)
            .Take(60)
            .ToList();

        if (writeAuditLog)
        {
            await _accessLogService.LogAsync(adminUserId, "RunFinancialReconciliation", true,
                $"Status={overallStatus}; StripePayments={stripePayments.Count}; SplitExceptions={bookingSplitExceptions.Count}");
        }

        return new ReconciliationReportDTO
        {
            OverallStatus = overallStatus,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            StripeCompletedPaymentsCount = stripePayments.Count,
            StripeCompletedPaymentsTotal = stripePaymentsTotal,
            StripeTopUpTransactionsCount = stripeTopUps.Count,
            StripeTopUpTransactionsTotal = stripeTopUpsTotal,
            PublicParkingStripeCount = publicStripeSessions.Count,
            PublicParkingStripeTotal = publicStripeSessions.Sum(s => s.TotalAmount),
            BookingSplitExceptionsCount = bookingSplitExceptions.Count,
            PlatformWalletRecordedBalance = platformRecordedBalance,
            PlatformWalletExpectedBalance = platformExpectedBalance,
            TotalOwnerHeldEscrow = totalOwnerHeldEscrow,
            TotalOwnerAvailable = totalOwnerAvailable,
            TotalClearedWithdrawals = totalClearedWithdrawals,
            TotalPendingWithdrawals = totalPendingWithdrawals,
            Sections = sections,
            ExceptionsAndRecentReceipts = orderedRows,
            GeneratedAtUtc = now
        };
    }

    private IQueryable<Transaction> BuildFilteredLedgerQuery(
        DateTime? fromUtc,
        DateTime? toUtc,
        string? type,
        string? status,
        string? search)
    {
        var query = _db.Transactions
            .AsNoTracking()
            .Include(t => t.Wallet!)
                .ThenInclude(w => w.User)
            .Include(t => t.PlatformWallet)
            .Include(t => t.Booking!)
                .ThenInclude(b => b.ParkingSpot)
            .AsQueryable();

        if (fromUtc.HasValue)
            query = query.Where(t => t.CreatedAt >= fromUtc.Value);

        if (toUtc.HasValue)
            query = query.Where(t => t.CreatedAt <= toUtc.Value);

        if (!string.IsNullOrWhiteSpace(type) &&
            !string.Equals(type, "all", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse<TransactionType>(type.Trim(), true, out var parsedType))
        {
            query = query.Where(t => t.TransactionType == parsedType);
        }

        if (!string.IsNullOrWhiteSpace(status) &&
            !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse<TransactionStatus>(status.Trim(), true, out var parsedStatus))
        {
            query = query.Where(t => t.TransactionStatus == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(t =>
                t.ReferenceNumber.Contains(term) ||
                (t.Booking != null && t.Booking.BookingReference.Contains(term)) ||
                (t.Wallet != null && (
                    t.Wallet.User.Email.Contains(term) ||
                    (t.Wallet.User.FirstName != null && t.Wallet.User.FirstName.Contains(term)) ||
                    (t.Wallet.User.LastName != null && t.Wallet.User.LastName.Contains(term)))));
        }

        return query;
    }

    private static SettlementLedgerEntryDTO MapLedgerEntry(Transaction t, decimal taxRatePercent)
    {
        var isCommission = t.TransactionType == TransactionType.PlatformCommission && t.Amount > 0;
        var appliedRate = isCommission ? taxRatePercent : 0m;
        var taxAmount = isCommission ? CalculateInclusiveTax(t.Amount, taxRatePercent) : 0m;
        var netAmount = t.Amount - taxAmount;

        return new SettlementLedgerEntryDTO
        {
            TransactionId = t.TransactionId,
            ReferenceNumber = t.ReferenceNumber,
            Type = t.TransactionType.ToString(),
            Status = t.TransactionStatus.ToString(),
            PaymentMethod = t.PaymentMethod.ToString(),
            LedgerAccount = t.PlatformWalletId.HasValue ? "Platform Escrow & Revenue" : $"User Wallet #{t.WalletId}",
            UserId = t.Wallet?.UserId,
            UserName = t.Wallet?.User is not null ? FormatFullName(t.Wallet.User) : "ParkJom Platform",
            UserEmail = t.Wallet?.User?.Email,
            BookingId = t.BookingId,
            BookingReference = t.Booking?.BookingReference,
            SpotLabel = t.Booking?.ParkingSpot?.ParkingLabel,
            Amount = t.Amount,
            TaxRatePercent = appliedRate,
            TaxAmount = taxAmount,
            NetAmount = netAmount,
            CreatedAt = t.CreatedAt
        };
    }

    private async Task<SystemConfiguration> GetOrDefaultSystemConfigAsync(CancellationToken ct)
    {
        return await _db.SystemConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == 1, ct)
            ?? new SystemConfiguration
            {
                Id = 1,
                CommissionRate = 10.00m,
                GracePeriodMinutes = 15,
                OverstayPenaltyMultiplier = 1.50m,
                OverstayMaxDailyPenalty = 30.00m,
                HardwareExitLockEnabled = true,
                TaxRatePercent = 8.00m
            };
    }

    private async Task TryNotifyWithdrawalStatusAsync(
        int userId,
        int transactionId,
        string status,
        decimal amount,
        CancellationToken ct)
    {
        try
        {
            await _notificationService.PublishAsync(new NotificationPublishRequest(
                userId,
                "withdrawal.status_changed",
                new Dictionary<string, object?>
                {
                    ["status"] = status,
                    ["amount"] = $"RM {amount:F2}"
                },
                entityType: "Transaction",
                entityId: transactionId.ToString(),
                dedupeKey: $"withdrawal:{transactionId}:{status.ToLowerInvariant()}"), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish withdrawal {Status} notification for TransactionId {TransactionId}", status, transactionId);
        }
    }

    private static void RecalculateBatchStatus(SettlementBatch batch, DateTime now)
    {
        if (batch.Items.Count == 0) return;
        var clearedCount = batch.Items.Count(i => i.Status == "Cleared");
        var rejectedCount = batch.Items.Count(i => i.Status == "Rejected");
        var pendingCount = batch.Items.Count - clearedCount - rejectedCount;

        if (pendingCount > 0) return;

        batch.ClearedAt = now;
        batch.UpdatedAt = now;
        if (clearedCount == batch.Items.Count)
            batch.Status = "Cleared";
        else if (rejectedCount == batch.Items.Count)
            batch.Status = "Failed";
        else
            batch.Status = "PartiallyCleared";
    }

    private static SettlementBatchSummaryDTO MapBatchSummary(SettlementBatch batch)
    {
        return new SettlementBatchSummaryDTO
        {
            SettlementBatchId = batch.SettlementBatchId,
            BatchReference = batch.BatchReference,
            Rail = batch.Rail,
            ScheduleWindow = batch.ScheduleWindow,
            Status = batch.Status,
            ItemCount = batch.ItemCount,
            TotalAmount = batch.TotalAmount,
            Currency = batch.Currency,
            BnmFormatVersion = batch.BnmFormatVersion,
            FileChecksum = batch.FileChecksum,
            BankReference = batch.BankReference,
            CreatedByUserId = batch.CreatedByUserId,
            CreatedByName = batch.CreatedByUser is not null ? FormatFullName(batch.CreatedByUser) : "Automated Batch Scheduler",
            CreatedAt = batch.CreatedAt,
            SubmittedAt = batch.SubmittedAt,
            ClearedAt = batch.ClearedAt,
            Notes = batch.Notes
        };
    }

    private static DateTime GetNextScheduledIbgCutoffUtc(DateTime utcNow)
    {
        var mytNow = utcNow.Add(MalaysiaOffset);
        for (var offset = 0; offset <= 7; offset++)
        {
            var candidateDate = mytNow.Date.AddDays(offset);
            if (candidateDate.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Friday)
            {
                var cutoffMyt = candidateDate.AddHours(14);
                if (cutoffMyt > mytNow)
                {
                    return DateTime.SpecifyKind(cutoffMyt - MalaysiaOffset, DateTimeKind.Utc);
                }
            }
        }

        return DateTime.SpecifyKind(mytNow.Date.AddDays(3).AddHours(14) - MalaysiaOffset, DateTimeKind.Utc);
    }

    private static decimal CalculateInclusiveTax(decimal grossAmount, decimal taxRatePercent)
    {
        if (grossAmount <= 0m || taxRatePercent <= 0m) return 0m;
        return Math.Round(grossAmount * taxRatePercent / (100m + taxRatePercent), 2, MidpointRounding.AwayFromZero);
    }

    private static string FormatFullName(User user)
    {
        var full = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrEmpty(full) ? user.Email : full;
    }

    private static string EscapeCsv(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
