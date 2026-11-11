using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

public static class BranchNameGeneration
{
    extension(BranchName)
    {
        /// <summary>
        /// The only place an unclassified issue resolves to <see cref="IssueKind.Feature"/>:
        /// the classification itself stays honest about nothing having decided the kind.
        /// </summary>
        public static BranchName Generate(KindClassification classification, int issueNumber, string title)
        {
            IssueKind kind = classification switch
            {
                KindClassification.Labelled labelled => labelled.Kind,
                KindClassification.Triaged triaged => triaged.Kind,
                _ => IssueKind.Feature,
            };

            return BranchName.Generate(kind.BranchPrefix, issueNumber, title);
        }
    }
}
