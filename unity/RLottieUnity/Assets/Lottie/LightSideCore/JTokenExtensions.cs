using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static partial class JTokenExtensions
{
    private static CultureInfo invariantCulture = CultureInfo.InvariantCulture;
    
    public static int ToInt(this JToken token)
    {
        return Convert.ToInt32(((JValue)token).Value, invariantCulture);
    }
    
    public static JToken FindByPath(string json, string targetPath)
    {
        using var reader = new JsonTextReader(new StringReader(json));

        while (reader.Read())
        {
            if (reader.TokenType == JsonToken.PropertyName)
            {
                if (!reader.Read()) return null;

                if (string.Equals(reader.Path, targetPath, StringComparison.Ordinal))
                {
                    return JToken.ReadFrom(reader);
                }
            }
        }

        return null;
    }

    public static JToken[] FindByPath(string json, params string[] targetPaths)
    {
        using var reader = new JsonTextReader(new StringReader(json));
        int index = 0;
        int length = targetPaths.Length;
        var tokens = new JToken[length];

        while (index < length && reader.Read())
        {
            if (reader.TokenType == JsonToken.PropertyName)
            {
                if (!reader.Read()) return null;

                if (string.Equals(reader.Path, targetPaths[index], StringComparison.Ordinal))
                {
                    tokens[index] = JToken.ReadFrom(reader);
                    index++;
                }
            }
        }

        return tokens;
    }
}