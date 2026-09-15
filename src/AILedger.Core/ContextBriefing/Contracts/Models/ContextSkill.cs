namespace AILedger.Core.Contracts;

// One skill as it was served. The id alone would record that a brief carried a skill by that name
// and nothing about what it said, so a skill edited afterwards would leave an event that still
// looks satisfied — the same defect as a verify command that cannot fail. The hash is what makes
// the record falsifiable.
public sealed record ContextSkill(string SkillId, string ContentHash);
