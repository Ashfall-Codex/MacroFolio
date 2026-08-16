using System.Text.Json;

namespace MacroFolio.Services;
internal static class SafeIo
{
    public static Exception? Run(Action operation)
    {
        try
        {
            operation();
            return null;
        }
        catch (IOException ex) { return ex; }
        catch (UnauthorizedAccessException ex) { return ex; }
        catch (JsonException ex) { return ex; }
        catch (NotSupportedException ex) { return ex; }
    }
    
    public static Exception? Run<T>(Func<T> operation, out T? result)
    {
        result = default;
        try
        {
            result = operation();
            return null;
        }
        catch (IOException ex) { return ex; }
        catch (UnauthorizedAccessException ex) { return ex; }
        catch (JsonException ex) { return ex; }
        catch (NotSupportedException ex) { return ex; }
    }
}
