using System.Text;

namespace Skanyxx.Host.Setup;

/// <summary>
/// The "Copy instructions for agent" text: a prompt an AI coding agent (e.g. Claude Code) can act on. Built only from the
/// guide's own text and the setting names in <see cref="SetupState"/>, so it can never carry a configured value.
/// </summary>
public static class AgentPrompt
{
    public static string For(SetupGuide guide, SetupState state)
    {
        var text = new StringBuilder();
        text.AppendLine($"Set up \"{guide.Title}\" in this Skanyxx install. Work in the Skanyxx repository; README section \"{guide.Readme}\" is the reference.");
        text.AppendLine();
        text.AppendLine($"Goal: {guide.What}");
        text.AppendLine();
        text.AppendLine("Detected state (setting names only, read from this install's configuration):");
        if (state.Ready)
            text.AppendLine("- Every required setting is set.");
        foreach (var s in state.Missing)
            text.AppendLine($"- NOT WORKING: {s.Name} is not set. {s.Effect} Value: {s.WhereToGet}");
        foreach (var s in state.OptionalUnset)
            text.AppendLine($"- Optional, not set: {s.Name}. {s.Effect} Value: {s.WhereToGet}");
        foreach (var s in state.Set)
            text.AppendLine($"- Set: {s.Name}");
        text.AppendLine();
        text.AppendLine("Rules:");
        text.AppendLine("- Never print, log, commit or paste a secret value (token, password, key, connection string). Read it from where it lives (a secret store, a 0600 file, a Kubernetes Secret) and pass it by environment variable or Secret reference.");
        text.AppendLine("- Ask before changing a shared cluster; replace every <placeholder> with the real name first.");
        Section(text, "Prerequisites", guide.Prerequisites.Select(p => new SetupStep(p)), numbered: false);
        Section(text, "Steps: local development", guide.Local, numbered: true);
        Section(text, "Steps: Helm on a customer cluster", guide.Helm, numbered: true);
        Section(text, "Verify", guide.Verify, numbered: true);
        text.AppendLine();
        text.AppendLine("Common errors:");
        foreach (var e in guide.Errors)
            text.AppendLine($"- {e.Symptom}: {e.Fix}");
        return text.ToString();
    }

    private static void Section(StringBuilder text, string title, IEnumerable<SetupStep> steps, bool numbered)
    {
        text.AppendLine();
        text.AppendLine($"{title}:");
        var n = 0;
        foreach (var step in steps)
        {
            text.AppendLine(numbered ? $"{++n}. {step.Text}" : $"- {step.Text}");
            if (step.Command is not null)
                text.AppendLine($"   $ {step.Command}");
        }
    }
}
