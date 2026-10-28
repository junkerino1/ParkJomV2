using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

[ApiController]
[Authorize]
[Route("api/owner")]
public class OwnerPayoutController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly CurrentUserService _currentUser;
    private readonly WalletService _walletService;
    private readonly TransactionService _transactionService;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<OwnerPayoutController> _logger;

    public OwnerPayoutController(
        ApplicationDbContext context,
        CurrentUserService currentUser,
        WalletService walletService,
        TransactionService transactionService,
        AccessLogService accessLogService,
        ILogger<OwnerPayoutController> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _walletService = walletService;
        _transactionService = transactionService;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/owner/payout-account
    /// Retrieves the authenticated owner's saved payout bank details.
    /// </summary>
    [HttpGet("payout-account")]
    [ProducesResponseType(typeof(OwnerPayoutAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<OwnerPayoutAccountResponse>> GetPayoutAccount()
    {
        var owner = await _currentUser.GetCurrentUserAsync();
        if (owner == null || owner.UserType != UserType.PropertyOwner)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Only property owners can access payout account settings."
            });
        }

        var account = await _context.OwnerPayoutAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == owner.UserId);

        if (account == null)
        {
            return Ok(new OwnerPayoutAccountResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "No payout account configured yet.",
                Data = null
            });
        }

        return Ok(new OwnerPayoutAccountResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Payout account retrieved successfully.",
            Data = new OwnerPayoutAccountDTO
            {
                BankName = account.BankName,
                AccountNumber = account.AccountNumber,
                AccountHolderName = account.AccountHolderName,
                AutoWithdraw = account.AutoWithdraw,
                UpdatedAt = account.UpdatedAt
            }
        });
    }

    /// <summary>
    /// PUT /api/owner/payout-account
    /// Saves or updates the authenticated owner's payout bank details.
    /// </summary>
    [HttpPut("payout-account")]
    [ProducesResponseType(typeof(OwnerPayoutAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<OwnerPayoutAccountResponse>> SavePayoutAccount([FromBody] SaveOwnerPayoutAccountRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BankName) ||
            string.IsNullOrWhiteSpace(request.AccountNumber) ||
            string.IsNullOrWhiteSpace(request.AccountHolderName))
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Bank name, account number, and beneficiary holder name are all required."
            });
        }

        var owner = await _currentUser.GetCurrentUserAsync();
        if (owner == null || owner.UserType != UserType.PropertyOwner)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Only property owners can configure payout credentials."
            });
        }

        var now = DateTime.UtcNow;
        var account = await _context.OwnerPayoutAccounts
            .FirstOrDefaultAsync(a => a.UserId == owner.UserId);

        if (account == null)
        {
            account = new OwnerPayoutAccount
            {
                UserId = owner.UserId,
                BankName = request.BankName.Trim(),
                AccountNumber = request.AccountNumber.Trim(),
                AccountHolderName = request.AccountHolderName.Trim(),
                AutoWithdraw = request.AutoWithdraw,
                CreatedAt = now,
                UpdatedAt = now
            };
            _context.OwnerPayoutAccounts.Add(account);
        }
        else
        {
            account.BankName = request.BankName.Trim();
            account.AccountNumber = request.AccountNumber.Trim();
            account.AccountHolderName = request.AccountHolderName.Trim();
            account.AutoWithdraw = request.AutoWithdraw;
            account.UpdatedAt = now;
        }

        await _context.SaveChangesAsync();
        await _accessLogService.LogAsync(
            User,
            "SaveOwnerPayoutAccount",
            true,
            $"Bank={account.BankName}, Account={account.AccountNumber}");

        return Ok(new OwnerPayoutAccountResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Payout credentials saved successfully.",
            Data = new OwnerPayoutAccountDTO
            {
                BankName = account.BankName,
                AccountNumber = account.AccountNumber,
                AccountHolderName = account.AccountHolderName,
                AutoWithdraw = account.AutoWithdraw,
                UpdatedAt = account.UpdatedAt
            }
        });
    }

    /// <summary>
    /// POST /api/owner/withdrawals
    /// Submits a withdrawal request from the owner's available wallet balance.
    /// </summary>
    [HttpPost("withdrawals")]
    [ProducesResponseType(typeof(OwnerWithdrawalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<OwnerWithdrawalResponse>> RequestWithdrawal([FromBody] OwnerWithdrawalRequest request)
    {
        if (request.Amount < 10.00m)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Minimum withdrawal amount is RM 10.00."
            });
        }

        var owner = await _currentUser.GetCurrentUserAsync();
        if (owner == null || owner.UserType != UserType.PropertyOwner)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Only property owners can request wallet withdrawals."
            });
        }

        var payoutAccount = await _context.OwnerPayoutAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == owner.UserId);

        if (payoutAccount == null || string.IsNullOrWhiteSpace(payoutAccount.AccountNumber))
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Please configure your payout bank account in Settings before requesting a withdrawal."
            });
        }

        var wallet = await _context.Wallets
            .FirstOrDefaultAsync(w => w.UserId == owner.UserId);

        if (wallet == null || wallet.Balance < request.Amount)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = $"Insufficient available balance. You requested RM {request.Amount:F2}, but your current available balance is RM {wallet?.Balance ?? 0m:F2}."
            });
        }

        using var dbTransaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.UtcNow;
            _walletService.Deduct(wallet, request.Amount, now);

            var refNumber = $"WDR-{now:yyyyMMddHHmmss}-{owner.UserId}";
            var transaction = _transactionService.Create(
                wallet.WalletId,
                null,
                null,
                TransactionType.Withdrawal,
                -request.Amount,
                PaymentMethod.Wallet,
                refNumber,
                now);

            await _context.SaveChangesAsync();
            await dbTransaction.CommitAsync();

            await _accessLogService.LogAsync(
                User,
                "RequestOwnerWithdrawal",
                true,
                $"Amount=RM {request.Amount:F2}; Ref={refNumber}; Bank={payoutAccount.BankName}");

            return Ok(new OwnerWithdrawalResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = $"Withdrawal of RM {request.Amount:F2} successfully initiated to {payoutAccount.BankName} ({payoutAccount.AccountNumber}).",
                Data = new OwnerWithdrawalResponseDTO
                {
                    TransactionId = transaction.TransactionId,
                    ReferenceNumber = refNumber,
                    Amount = request.Amount,
                    RemainingBalance = wallet.Balance,
                    BankName = payoutAccount.BankName,
                    AccountNumber = payoutAccount.AccountNumber,
                    AccountHolderName = payoutAccount.AccountHolderName,
                    CreatedAt = now
                }
            });
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync();
            _logger.LogError(ex, "Failed to process owner withdrawal for user {UserId}", owner.UserId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An unexpected error occurred while processing the withdrawal request."
            });
        }
    }
}
