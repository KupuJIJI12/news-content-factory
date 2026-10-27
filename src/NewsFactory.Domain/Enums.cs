namespace NewsFactory.Domain;

public enum SourceType { Rss, Html, Vk }

public enum RawItemStatus { New, SkippedOld, Irrelevant, Relevant, Clustered, Failed, Duplicate }

public enum StoryStatus
{
    New,
    Processing,
    LowInterest,
    ReadyForReview,
    Scheduled,
    Published,
    Rejected,
    ProcessingFailed,
    PublishFailed,
}

public enum Platform { Max }

public enum PublicationStatus { Scheduled, Sending, Published, Failed, Unknown, Cancelled }

public enum LlmTask { Relevance, Clustering, Scoring, Rewrite, FactCheck, SourceSetup }

public enum LlmProviderKind { OpenAiCompatible, GigaChat }

public enum FactCheckIssueSource { Deterministic, Llm }

public sealed record FactCheckIssue(string Claim, string Problem, FactCheckIssueSource Source);
