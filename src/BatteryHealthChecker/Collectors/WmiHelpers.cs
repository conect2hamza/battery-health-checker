using System.Globalization;
using System.Management;

namespace BatteryHealthChecker.Collectors;

/// <summary>
/// Defensive readers for WMI properties.
///
/// The <c>root\WMI</c> battery classes are implemented by each vendor's ACPI driver
/// and the property set genuinely varies between machines: a property that exists on
/// one laptop is simply absent on another, and reading it throws
/// <see cref="ManagementException"/>. Every read here returns null instead of
/// throwing, so a missing property degrades to "Not Available" (SRS 2, 19).
/// </summary>
internal static class WmiHelpers
{
    public static object? Get(ManagementBaseObject obj, params string[] names)
    {
        foreach (string name in names)
        {
            try
            {
                object? value = obj[name];
                if (value is not null and not DBNull) return value;
            }
            catch (ManagementException)
            {
                // Property not present on this machine's schema.
            }
            catch (InvalidCastException)
            {
            }
        }
        return null;
    }

    public static string? GetString(ManagementBaseObject obj, params string[] names)
    {
        object? value = Get(obj, names);
        if (value is null) return null;
        string text = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        text = text.Trim().Trim('\0');
        return text.Length == 0 ? null : text;
    }

    public static long? GetInt64(ManagementBaseObject obj, params string[] names)
    {
        object? value = Get(obj, names);
        if (value is null) return null;
        try
        {
            return value switch
            {
                byte b => b,
                sbyte sb => sb,
                short s => s,
                ushort us => us,
                int i => i,
                uint ui => ui,
                long l => l,
                ulong ul => ul <= long.MaxValue ? (long)ul : null,
                string str when long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out long p) => p,
                _ => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    public static int? GetInt32(ManagementBaseObject obj, params string[] names)
    {
        long? value = GetInt64(obj, names);
        if (value is null) return null;
        return value.Value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
    }

    public static bool? GetBool(ManagementBaseObject obj, params string[] names)
    {
        object? value = Get(obj, names);
        return value switch
        {
            bool b => b,
            null => null,
            _ => GetInt64(obj, names) is { } n ? n != 0 : null,
        };
    }

    public static byte[]? GetBytes(ManagementBaseObject obj, params string[] names)
    {
        object? value = Get(obj, names);
        return value switch
        {
            byte[] bytes => bytes,
            ushort[] words => words.Select(w => (byte)w).ToArray(),
            _ => null,
        };
    }

    /// <summary>
    /// Runs a query and yields the results, swallowing the provider-level failures that
    /// mean "this class is not implemented here" rather than "something is wrong".
    /// </summary>
    public static IEnumerable<ManagementBaseObject> Query(
        string scope, string query, Action<Exception>? onError = null)
    {
        ManagementObjectCollection? collection = null;
        ManagementObjectSearcher? searcher = null;
        try
        {
            var options = new ConnectionOptions { Timeout = TimeSpan.FromSeconds(10) };
            var managementScope = new ManagementScope(scope, options);
            managementScope.Connect();
            searcher = new ManagementObjectSearcher(managementScope, new ObjectQuery(query))
            {
                Options = { Timeout = TimeSpan.FromSeconds(10), ReturnImmediately = true, Rewindable = false },
            };
            collection = searcher.Get();
        }
        catch (Exception ex)
        {
            // ManagementException, UnauthorizedAccessException and COMException all mean
            // the same thing to us: this class is not usable here, try the next source.
            onError?.Invoke(ex);
        }

        if (collection is null)
        {
            searcher?.Dispose();
            yield break;
        }

        // The enumerator itself can throw mid-iteration, so step it manually.
        ManagementObjectCollection.ManagementObjectEnumerator? enumerator = null;
        try
        {
            enumerator = collection.GetEnumerator();
            while (true)
            {
                ManagementBaseObject current;
                try
                {
                    if (!enumerator.MoveNext()) break;
                    current = enumerator.Current;
                }
                catch (Exception ex)
                {
                    onError?.Invoke(ex);
                    break;
                }

                yield return current;
                current.Dispose();
            }
        }
        finally
        {
            enumerator?.Dispose();
            collection.Dispose();
            searcher?.Dispose();
        }
    }
}
