namespace NewsFactory.Collection.Vk;

public sealed class VkApiException(int code, string message) : Exception($"VK API {code}: {message}")
{
    public int Code { get; } = code;
}
