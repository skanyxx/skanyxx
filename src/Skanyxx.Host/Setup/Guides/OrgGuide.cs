namespace Skanyxx.Host.Setup.Guides;

/// <summary>The org tree (D055, D351). Facts: README "Identity and security model" → "Org tree".</summary>
public static class OrgGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "org",
        Title: "Org tree (departments and teams)",
        Readme: "Identity and security model → Org tree",
        What: "Departments contain teams and people belong to teams; memory shares cards along those lines (team:<slug>, department:<slug>). " +
              "The owner creates, renames, moves and deletes them; a slug never changes and is never reused.",
        Prerequisites:
        [
            "The identity module and its Postgres (ConnectionStrings:Identity).",
            "People invited on the People page, so they can be added to teams.",
        ],
        Settings:
        [
            new("ConnectionStrings:Identity", "There is no org tree (the Host does not start without it).",
                "A Postgres connection string from your secret store (Helm: the database Secret)."),
            new("Identity:EntraAvailable", "Teams are only set by hand here; Microsoft groups do not assign them.",
                "Settings → Sign-in → Microsoft sign-in, then map groups to teams on the Microsoft sign-in page.", Optional: true, MustBeTrue: true),
        ],
        Local:
        [
            new("Run the Host and sign in as the owner.", "scripts/dev/first-hour.sh host"),
            new("Open /Org: New department, then New team inside it, then add members from the team's Edit panel."),
            new("Or through the API (owner bearer token).",
                "curl -X POST -H \"Authorization: Bearer $TOKEN\" -H 'Content-Type: application/json' -d '{\"slug\":\"finance\",\"name\":\"Finance\"}' http://localhost:5287/api/identity/org/departments"),
        ],
        Helm:
        [
            new("Nothing to deploy: the tree lives in the identity database."),
        ],
        Verify:
        [
            new("A team member sees the team's cards in the Library (scope team:<slug>)."),
            new("Deleting a team with members asks where they go; /Audit lists org.team_deleted."),
        ],
        Errors:
        [
            new("409 \"The team has N member(s)…\"", "Pick a team to move them to, or remove them first."),
            new("409 \"…belonged to a deleted team\"", "Slugs are never reused; choose another slug."),
            new("409 \"A Microsoft (Entra) group maps to this team\"", "Remove the team from the group map on the Microsoft sign-in page first."),
        ]);
}
