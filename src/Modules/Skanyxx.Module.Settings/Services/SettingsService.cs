using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Data;
using Skanyxx.Core.Interfaces;

namespace Skanyxx.Module.Settings.Services;

public class SettingsService : ISettingsService
{
    private readonly DbContext _db;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(DbContext db, ILogger<SettingsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private DbSet<AppSetting> Settings => _db.Set<AppSetting>();
    private DbSet<KAgentConnection> Connections => _db.Set<KAgentConnection>();
    private DbSet<SavedLayout> Layouts => _db.Set<SavedLayout>();

    public async Task<string?> GetAsync(string key)
    {
        var setting = await Settings.FindAsync(key);
        return setting?.Value;
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        return await Settings.ToDictionaryAsync(s => s.Key, s => s.Value);
    }

    public async Task<Dictionary<string, string>> GetByPrefixAsync(string prefix)
    {
        return await Settings.Where(s => s.Key.StartsWith(prefix)).ToDictionaryAsync(s => s.Key, s => s.Value);
    }

    public async Task SetAsync(string key, string value)
    {
        var setting = await Settings.FindAsync(key);
        if (setting == null)
        {
            setting = new AppSetting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow };
            Settings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        _logger.LogInformation("Setting {Key} updated", key);
    }

    public async Task SetManyAsync(Dictionary<string, string> settings)
    {
        foreach (var kvp in settings)
        {
            var setting = await Settings.FindAsync(kvp.Key);
            if (setting == null)
            {
                setting = new AppSetting { Key = kvp.Key, Value = kvp.Value, UpdatedAt = DateTime.UtcNow };
                Settings.Add(setting);
            }
            else
            {
                setting.Value = kvp.Value;
                setting.UpdatedAt = DateTime.UtcNow;
            }
        }
        await _db.SaveChangesAsync();
        _logger.LogInformation("Updated {Count} settings", settings.Count);
    }

    public async Task DeleteAsync(string key)
    {
        var setting = await Settings.FindAsync(key);
        if (setting != null)
        {
            Settings.Remove(setting);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<List<KAgentConnection>> GetConnectionsAsync()
    {
        return await Connections.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<KAgentConnection?> GetConnectionAsync(int id)
    {
        return await Connections.FindAsync(id);
    }

    public async Task<KAgentConnection?> GetDefaultConnectionAsync()
    {
        return await Connections.FirstOrDefaultAsync(c => c.IsDefault);
    }

    public async Task<KAgentConnection> SaveConnectionAsync(KAgentConnection connection)
    {
        if (connection.Id == 0)
            Connections.Add(connection);
        else
            Connections.Update(connection);
        await _db.SaveChangesAsync();
        return connection;
    }

    public async Task DeleteConnectionAsync(int id)
    {
        var connection = await Connections.FindAsync(id);
        if (connection != null)
        {
            Connections.Remove(connection);
            await _db.SaveChangesAsync();
        }
    }

    public async Task SetDefaultConnectionAsync(int id)
    {
        await Connections.ExecuteUpdateAsync(c => c.SetProperty(x => x.IsDefault, false));
        var connection = await Connections.FindAsync(id);
        if (connection != null)
        {
            connection.IsDefault = true;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<List<SavedLayout>> GetLayoutsAsync()
    {
        return await Layouts.OrderBy(l => l.Name).ToListAsync();
    }

    public async Task<SavedLayout?> GetLayoutAsync(int id)
    {
        return await Layouts.FindAsync(id);
    }

    public async Task<SavedLayout> SaveLayoutAsync(SavedLayout layout)
    {
        layout.UpdatedAt = DateTime.UtcNow;
        if (layout.Id == 0)
        {
            layout.CreatedAt = DateTime.UtcNow;
            Layouts.Add(layout);
        }
        else
            Layouts.Update(layout);
        await _db.SaveChangesAsync();
        return layout;
    }

    public async Task DeleteLayoutAsync(int id)
    {
        var layout = await Layouts.FindAsync(id);
        if (layout != null)
        {
            Layouts.Remove(layout);
            await _db.SaveChangesAsync();
        }
    }
}
