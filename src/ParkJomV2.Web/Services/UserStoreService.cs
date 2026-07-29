using System.Text.Json;
using ParkJomV2.Web.Models;

namespace ParkJomV2.Web.Services;

/// <summary>
/// File-based persistent user store.
/// Writes users to a JSON file so data survives app restarts on Azure.
/// Thread-safe via SemaphoreSlim for concurrent requests.
/// </summary>
public class UserStoreService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, StoredUser> _cache = new();
    private bool _loaded;

    public UserStoreService(IWebHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "users.json");
    }

    /// <summary>Load users from disk (cached in memory after first call).</summary>
    private async Task<Dictionary<string, StoredUser>> LoadAsync()
    {
        if (_loaded) return _cache;

        await _lock.WaitAsync();
        try
        {
            if (_loaded) return _cache; // double-check

            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath);
                _cache = JsonSerializer.Deserialize<Dictionary<string, StoredUser>>(json)
                         ?? new Dictionary<string, StoredUser>();
            }

            _loaded = true;
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Save the in-memory dictionary to disk.</summary>
    private async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json);
    }

    // ---- Public API ----

    public async Task<StoredUser?> FindByEmailAsync(string email)
    {
        var users = await LoadAsync();
        users.TryGetValue(email, out var user);
        return user;
    }

    public async Task<StoredUser?> FindByUserIdAsync(int userId)
    {
        var users = await LoadAsync();
        return users.Values.FirstOrDefault(u => u.UserId == userId);
    }

    public async Task SaveUserAsync(StoredUser user)
    {
        await LoadAsync(); // ensure loaded

        await _lock.WaitAsync();
        try
        {
            _cache[user.Email] = user;
            await SaveAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Get the next available UserId.</summary>
    public async Task<int> GetNextUserIdAsync()
    {
        var users = await LoadAsync();
        return users.Count == 0 ? 1 : users.Values.Max(u => u.UserId) + 1;
    }
}
