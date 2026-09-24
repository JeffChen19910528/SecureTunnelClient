using System.Text.Json;

namespace SecureTunnel.Client.Core.Client.Ipc;

public static class PipeJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
