namespace Skanyxx.Module.Investigate.Contracts;

public sealed record LimitsDto(int MaxDepth, int MaxSteps, int MaxPromptChars, int MaxInputChars, int StepTimeoutSeconds);
