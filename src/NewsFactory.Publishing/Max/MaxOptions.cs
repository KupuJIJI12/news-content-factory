namespace NewsFactory.Publishing.Max;

public sealed class MaxOptions
{
    public string BaseUrl { get; set; } = "https://platform-api2.max.ru";
    public string BotToken { get; set; } = "";
    public TimeSpan[] AttachmentRetryDelays { get; set; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16)];
}
