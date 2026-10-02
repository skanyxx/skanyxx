namespace SkanyxxWeb.Models;

public class AppSettings
{
    public string OrganizationName { get; set; } = "My Organization";
    public string DefaultEnvironment { get; set; } = "Production";
    public string Timezone { get; set; } = "UTC";
    public string Theme { get; set; } = "Dark";
    public bool CompactMode { get; set; } = false;
    public bool EmailNotifications { get; set; } = true;
    public bool PushNotifications { get; set; } = true;
    public bool SlackIntegration { get; set; } = false;
}
