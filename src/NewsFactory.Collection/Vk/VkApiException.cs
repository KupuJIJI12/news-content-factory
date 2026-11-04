namespace NewsFactory.Collection.Vk;

public sealed class VkApiException(string method, int code, string message)
    : Exception($"VK API {code} ({method}): {message}")
{
    public string Method { get; } = method;
    public int Code { get; } = code;
}
