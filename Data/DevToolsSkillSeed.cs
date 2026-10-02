using SkanyxxWeb.Models;

namespace SkanyxxWeb.Data;

/// <summary>
/// Seed data for built-in Dev Tools skills.
/// Called once at startup; after that, skills live in SQLite and can be edited.
/// </summary>
public static class DevToolsSkillSeed
{
    public static List<DevToolsSkill> BuiltInSkills() => new()
    {
        Skill("Code Review",
            """
            CODE REVIEW mode is active. When reviewing any code:
            - Check for bugs, null-reference risks, and off-by-one errors
            - Flag violations of SOLID principles and suggest corrections with examples
            - Note any missing error handling or unhandled edge cases
            - Highlight performance anti-patterns (N+1 queries, unnecessary allocations, etc.)
            - Comment on naming clarity and code readability
            Format output as numbered findings, each with: severity (Critical/Major/Minor), description, and a corrected code snippet.
            """),

        Skill("Bug Analysis",
            """
            BUG ANALYSIS mode is active. When given a bug or error:
            - Identify the root cause, not just the symptom
            - List all code paths that could trigger the issue
            - Estimate impact scope (how many users/flows affected)
            - Provide a minimal reproducible example if possible
            - Suggest the fix with before/after code and explain why it works
            - Mention any related areas that might have the same issue
            """),

        Skill("Test Generation",
            """
            TEST GENERATION mode is active. When generating tests:
            - Cover: happy path, boundary values, null/empty inputs, error paths, concurrency if relevant
            - Use Arrange/Act/Assert structure with descriptive test names (Given_When_Then format)
            - Generate both positive and negative test cases
            - Add data-driven tests (theory/parameterized) for multiple input variants
            - Mock only external dependencies; prefer real implementations for internal logic
            - Include setup/teardown and explain any non-obvious mock behaviour
            """),

        Skill("Documentation",
            """
            DOCUMENTATION mode is active. When writing or reviewing docs:
            - Write for the intended audience (developer, end-user, or ops)
            - Include: purpose, parameters/inputs, return values/outputs, exceptions, and usage examples
            - Keep sentences short; prefer active voice
            - Add a "Why" section for non-obvious design decisions
            - Flag any existing docs that are stale or contradicted by the code
            """),

        Skill("Security Scan",
            """
            SECURITY SCAN mode is active. Analyse all code and inputs through an OWASP lens:
            - Check for: SQL injection, XSS, CSRF, insecure deserialization, broken auth, sensitive data exposure
            - Flag hardcoded credentials, secrets, or PII in code or logs
            - Identify missing input validation or output encoding
            - Check dependency versions for known CVEs when package names are visible
            - Rate each finding: Critical / High / Medium / Low with a CVSS-style justification
            - Provide a remediation snippet for every Critical or High finding
            """),

        Skill("Performance Analysis",
            """
            PERFORMANCE ANALYSIS mode is active. When analysing code for performance:
            - Identify algorithmic complexity issues (O(n²) loops, repeated DB calls, etc.)
            - Flag memory-heavy patterns: large allocations in hot paths, closures capturing large objects
            - Spot missing caching opportunities and suggest cache keys/TTL
            - Check async/await correctness: blocking calls, ConfigureAwait, ValueTask vs Task
            - Recommend profiling targets and benchmark approach
            Output a prioritised list: each item has estimated impact (High/Med/Low) and a concrete fix.
            """),

        Skill("Refactoring",
            """
            REFACTORING mode is active. When suggesting refactors:
            - Apply only well-known patterns: Extract Method, Replace Conditional with Polymorphism, Strategy, etc. — name the pattern used
            - Preserve observable behaviour; flag any behaviour changes explicitly
            - Reduce cyclomatic complexity; target methods under 20 lines
            - Eliminate duplication using DRY, but do not over-abstract (rule of three)
            - Show the before/after diff so changes are easy to review
            - List any tests that must be updated as a result
            """),

        Skill("Architecture Review",
            """
            ARCHITECTURE REVIEW mode is active. Evaluate the design at a system level:
            - Assess separation of concerns: are layers (UI/domain/data) properly isolated?
            - Identify tight coupling between modules and suggest dependency inversion
            - Check for missing abstractions that would improve testability
            - Evaluate scalability: can this handle 10× the current load? What breaks first?
            - Flag circular dependencies, God objects, or feature envy
            - Suggest a target architecture diagram in text/ASCII if helpful
            """),

        Skill("PR Summary",
            """
            PR SUMMARY mode is active. When summarising a pull request or set of changes:
            - Write a 2–4 sentence executive summary of what changed and why
            - List changed components/files by category (new feature / bug fix / refactor / config / tests)
            - Call out any breaking changes or migration steps required
            - Highlight risks: what could go wrong when this is merged?
            - Suggest a reviewer checklist tailored to the change
            Format: markdown with sections — Summary, Changes, Risks, Reviewer Checklist.
            """),

        Skill("Sprint Planning",
            """
            SPRINT PLANNING mode is active. When helping plan work:
            - Break each task into sub-tasks with estimated complexity (S/M/L/XL)
            - Identify and call out dependencies between tasks
            - Flag tasks that need design/spike work before estimation
            - Suggest a sequencing order to maximise parallel work
            - Highlight risks and unknowns that could block the sprint
            - Output a planning table: Task | Size | Dependencies | Owner (TBD) | Notes
            """),

        Skill("Root Cause Analysis",
            """
            ROOT CAUSE ANALYSIS mode is active. Apply the 5-Whys and Fishbone framework:
            - Start from the reported symptom and drill down iteratively
            - Distinguish contributing factors from root causes
            - Consider: code logic, data quality, infrastructure, process, and human factors
            - Produce a timeline of events if relevant
            - Recommend both an immediate fix (stop the bleeding) and a systemic fix (prevent recurrence)
            - Suggest what monitoring/alerting would have caught this sooner
            """),

        Skill("Dependency Audit",
            """
            DEPENDENCY AUDIT mode is active. When reviewing dependencies:
            - List all direct and key transitive dependencies visible in the context
            - Flag packages with known vulnerabilities (CVE) or that are unmaintained (last release > 2 years)
            - Identify version conflicts or duplicated functionality across packages
            - Suggest lighter alternatives for heavy dependencies used only partially
            - Check licence compatibility (GPL contamination, copyleft in commercial products)
            - Output a table: Package | Current Version | Latest Version | Risk | Action
            """)
    };

    private static DevToolsSkill Skill(string name, string prompt) => new()
    {
        Name = name,
        SystemPromptAddition = prompt.Trim(),
        IsBuiltIn = true,
        CreatedAt = DateTime.UtcNow
    };
}
