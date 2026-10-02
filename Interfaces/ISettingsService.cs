using SkanyxxWeb.Data;

namespace SkanyxxWeb.Interfaces;

public interface ISettingsService
{
    Task<string?> GetAsync(string key);
    Task<Dictionary<string, string>> GetAllAsync();
    Task<Dictionary<string, string>> GetByPrefixAsync(string prefix);
    Task SetAsync(string key, string value);
    Task SetManyAsync(Dictionary<string, string> settings);
    Task DeleteAsync(string key);

    // Connection management
    Task<List<KAgentConnection>> GetConnectionsAsync();
    Task<KAgentConnection?> GetConnectionAsync(int id);
    Task<KAgentConnection?> GetDefaultConnectionAsync();
    Task<KAgentConnection> SaveConnectionAsync(KAgentConnection connection);
    Task DeleteConnectionAsync(int id);
    Task SetDefaultConnectionAsync(int id);

    // Layout management
    Task<List<SavedLayout>> GetLayoutsAsync();
    Task<SavedLayout?> GetLayoutAsync(int id);
    Task<SavedLayout> SaveLayoutAsync(SavedLayout layout);
    Task DeleteLayoutAsync(int id);
}
