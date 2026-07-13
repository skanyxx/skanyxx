using Microsoft.EntityFrameworkCore;
using SkanyxxWeb.Data;
using SkanyxxWeb.Interfaces;

namespace SkanyxxWeb.Services;

public class SettingsService : ISettingsService
{
    private readonly AppDbContext _db;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(AppDbContext db, ILogger<SettingsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string?> GetAsync(string key)
    {
        var setting = await _db.Settings.FindAsync(key);
        return setting?.Value;
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        return await _db.Settings.ToDictionaryAsync(s => s.Key, s => s.Value);
    }

    public async Task<Dictionary<string, string>> GetByPrefixAsync(string prefix)
    {
        return await _db.Settings
            .Where(s => s.Key.StartsWith(prefix))
            .ToDictionaryAsync(s => s.Key, s => s.Value);
    }

    public async Task SetAsync(string key, string value)
    {
        var setting = await _db.Settings.FindAsync(key);
        if (setting == null)
        {
            setting = new AppSetting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow };
            _db.Settings.Add(setting);
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
            var setting = await _db.Settings.FindAsync(kvp.Key);
            if (setting == null)
            {
                setting = new AppSetting { Key = kvp.Key, Value = kvp.Value, UpdatedAt = DateTime.UtcNow };
                _db.Settings.Add(setting);
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
        var setting = await _db.Settings.FindAsync(key);
        if (setting != null)
        {
            _db.Settings.Remove(setting);
            await _db.SaveChangesAsync();
        }
    }

    // Connection management
    public async Task<List<KAgentConnection>> GetConnectionsAsync()
    {
        return await _db.Connections.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<KAgentConnection?> GetConnectionAsync(int id)
    {
        return await _db.Connections.FindAsync(id);
    }

    public async Task<KAgentConnection?> GetDefaultConnectionAsync()
    {
        return await _db.Connections.FirstOrDefaultAsync(c => c.IsDefault);
    }

    public async Task<KAgentConnection> SaveConnectionAsync(KAgentConnection connection)
    {
        if (connection.Id == 0)
        {
            _db.Connections.Add(connection);
        }
        else
        {
            _db.Connections.Update(connection);
        }
        await _db.SaveChangesAsync();
        return connection;
    }

    public async Task DeleteConnectionAsync(int id)
    {
        var connection = await _db.Connections.FindAsync(id);
        if (connection != null)
        {
            _db.Connections.Remove(connection);
            await _db.SaveChangesAsync();
        }
    }

    public async Task SetDefaultConnectionAsync(int id)
    {
        // Remove default from all
        await _db.Connections.ExecuteUpdateAsync(c => c.SetProperty(x => x.IsDefault, false));

        // Set new default
        var connection = await _db.Connections.FindAsync(id);
        if (connection != null)
        {
            connection.IsDefault = true;
            await _db.SaveChangesAsync();
        }
    }

    // Layout management
    public async Task<List<SavedLayout>> GetLayoutsAsync()
    {
        return await _db.Layouts.OrderBy(l => l.Name).ToListAsync();
    }

    public async Task<SavedLayout?> GetLayoutAsync(int id)
    {
        return await _db.Layouts.FindAsync(id);
    }

    public async Task<SavedLayout> SaveLayoutAsync(SavedLayout layout)
    {
        layout.UpdatedAt = DateTime.UtcNow;
        if (layout.Id == 0)
        {
            layout.CreatedAt = DateTime.UtcNow;
            _db.Layouts.Add(layout);
        }
        else
        {
            _db.Layouts.Update(layout);
        }
        await _db.SaveChangesAsync();
        return layout;
    }

    public async Task DeleteLayoutAsync(int id)
    {
        var layout = await _db.Layouts.FindAsync(id);
        if (layout != null)
        {
            _db.Layouts.Remove(layout);
            await _db.SaveChangesAsync();
        }
    }
}
