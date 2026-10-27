using Skanyxx.Core.Models;

namespace Skanyxx.Core.Interfaces;

public interface IToolServerService
{
    Task<List<ToolServer>> GetAllAsync();
}
