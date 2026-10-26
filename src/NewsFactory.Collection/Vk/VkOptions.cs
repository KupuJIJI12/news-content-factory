namespace NewsFactory.Collection.Vk;

public sealed class VkOptions
{
    public string ApiBaseUrl { get; set; } = "https://api.vk.com/method/";
    public string ApiVersion { get; set; } = "5.199";
    public string ServiceToken { get; set; } = "";
}
