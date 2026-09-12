using Tomlyn;

namespace FaceCaptureAgent.Configuration;

/// <summary>读取 TOML 并在启动前校验参数，配置缺失或无效时直接终止启动。</summary>
public static class TomlOptionsLoader
{
    public static AgentOptions Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Configuration file was not found.", path);
        }

        try
        {
            var text = File.ReadAllText(path);
            var options = TomlSerializer.Deserialize<AgentOptions>(text)
                ?? throw new InvalidOperationException("Configuration file is empty.");
            options.Validate();
            return options;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Invalid configuration file: {path}", exception);
        }
    }
}
