namespace NewsFactory.Publishing;

public sealed class PostTooLongException(int length, int max) : Exception($"Пост длиннее {max} символов ({length})")
{
    public int Length { get; } = length;
    public int Max { get; } = max;
}

/// <summary>The message was definitely not sent; safe to retry.</summary>
public sealed class PublisherTransientException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The platform rejected the request; retrying will not help.</summary>
public sealed class PublisherPermanentException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The send request may have reached the platform; never resend automatically.</summary>
public sealed class PublisherAmbiguousException(string message, Exception? inner = null) : Exception(message, inner);
